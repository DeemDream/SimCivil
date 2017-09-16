﻿using log4net;
using SimCivil.Net.Packets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SimCivil.Config;

namespace SimCivil.Net
{
    /// <summary>
    /// An implement of IServerListener with better preformence.
    /// </summary>
    /// <seealso cref="IServerListener" />
    /// <seealso cref="ITicker" />
    public class MatrixServer : IServerListener, ITicker
    {
        private static readonly ILog logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Gets the clients.
        /// </summary>
        /// <value>
        /// The clients.
        /// </value>
        public ConcurrentDictionary<EndPoint, IServerConnection> Clients { get; }

        private ConcurrentDictionary<PacketType, PacketCallBack> _callbackDict;

        /// <summary>
        /// The event triggered when a new ServerClient created
        /// </summary>
        public event EventHandler<IServerConnection> OnConnected;

        /// <summary>
        /// The event triggered when a connection closed
        /// </summary>
        public event EventHandler<IServerConnection> OnDisconnected;

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        /// <summary>
        /// Server host
        /// </summary>
        public IPAddress Host { get; }

        /// <summary>
        /// The port this listener listen to
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Ticker's priority, larger number has high priorty.
        /// If system is busy, low priorty ticker may be skip.
        /// </summary>
        public int Priority { get; } = 900;

        private readonly Socket _socket;
        private readonly BlockingCollection<Packet> _packetSendQueue;
        private readonly BlockingCollection<Packet> _packetReadQueue;

        /// <summary>
        /// Construct a serverlistener
        /// </summary>
        /// <param name="ip">ip to start listener</param>
        /// <param name="port">port to start listener</param>
        public MatrixServer(string ip, int port)
        {
            Host = IPAddress.Parse(ip);
            Port = port;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _packetSendQueue = new BlockingCollection<Packet>();
            _packetReadQueue = new BlockingCollection<Packet>();

            Clients = new ConcurrentDictionary<EndPoint, IServerConnection>();

            _callbackDict = new ConcurrentDictionary<PacketType, PacketCallBack>(
                PacketFactory.LegalPackets.Select(lp => new KeyValuePair<PacketType, PacketCallBack>(lp.Key, null))
            );
        }

        /// <summary>
        /// Sends the packet.
        /// </summary>
        /// <param name="pkt">The PKT.</param>
        public void SendPacket(Packet pkt)
        {
            pkt.Client.SendPacket(pkt);
        }

        /// <summary>
        /// Starts this server.
        /// </summary>
        public void Start()
        {
            Task.Factory.StartNew(
                () => LisenteningLoop(_cancellation.Token),
                _cancellation.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);
        }

        private void LisenteningLoop(CancellationToken token)
        {
            Thread.CurrentThread.Name = nameof(LisenteningLoop);
            TcpListener listener = new TcpListener(Host, Port);
            listener.Start();
            logger.Info($"{nameof(MatrixServer)} start at {Host}:{Port}");
            while (!token.IsCancellationRequested)
            {
                var socket = listener.AcceptSocket();
                var con = new MatrixConnection(this, socket);
                AttachClient(con);
                var _ = ReadPacketAsync(con);
            }
            listener.Stop();
        }

        private async Task ReadPacketAsync(MatrixConnection con)
        {
            byte[] buffer = new byte[Packet.MaxSize];
            int lengthOfBody = 0;
            try
            {
                while (!_cancellation.Token.IsCancellationRequested)
                {
                    int lengthOfHead = await con.Stream.ReadAsync(buffer, 0, Head.HeadLength);
                    Head head = Head.FromBytes(buffer);
                    lengthOfBody = await con.Stream.ReadAsync(buffer, 0, head.Length);

                    Packet pkt = PacketFactory.Create(con, head, buffer);

                    logger.Debug($"received packet {pkt}");
                    _packetReadQueue.Add(pkt);
                }
            }
            catch (Exception e)
            {
                byte[] body = new byte[lengthOfBody];
                Array.Copy(buffer, body, lengthOfBody);
                logger.Error($"{nameof(ReadPacketAsync)} read error packet {BitConverter.ToString(body)}", e);
            }
            finally
            {
                con.Close();
            }
        }

        /// <summary>
        /// Stops this server.
        /// </summary>
        public void Stop()
        {
            _cancellation.Cancel();
            foreach (var c in Clients.Values)
            {
                c.Close();
            }
        }

        /// <summary>
        /// Attaches the client.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void AttachClient(IServerConnection client)
        {
            MatrixConnection connection = client as MatrixConnection ?? throw new NotSupportedException();
            if (!Clients.TryAdd(connection.Socket.RemoteEndPoint, client))
                throw new ArgumentException();
            OnConnected?.Invoke(this, client);
            logger.Info($"Connection established {connection.Socket.RemoteEndPoint}");
        }

        /// <summary>
        /// Detaches the client.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <exception cref="NotSupportedException"></exception>
        public void DetachClient(IServerConnection client)
        {
            MatrixConnection connection = client as MatrixConnection ?? throw new NotSupportedException();
            Clients.Remove(connection.Socket.RemoteEndPoint, out client);
            OnDisconnected?.Invoke(this, client);
            logger.Info($"Detached Connection {connection.Socket.RemoteEndPoint}");
        }

        /// <summary>
        /// Check vaildition, callback and handle
        /// </summary>
        /// <param name="tickCount"></param>
        public void Update(int tickCount)
        {
            while (_packetReadQueue.TryTake(out Packet pkt))
            {
                bool isVaild = pkt.Verify(out string error);
                if (isVaild)
                {
                    pkt.ReplyError(desc: error);
                    continue;
                }
                _callbackDict[pkt.PacketHead.Type]?.Invoke(pkt, ref isVaild);
                if (isVaild)
                    pkt.Handle();
            }
        }

        /// <summary>
        /// Registers the packet.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="callBack">The call back.</param>
        public void RegisterPacket(PacketType type, PacketCallBack callBack)
        {
            _callbackDict[type] += callBack;
        }

        /// <summary>
        /// Unregisters the packet.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="callBack">The call back.</param>
        public void UnregisterPacket(PacketType type, PacketCallBack callBack)
        {
            if (_callbackDict != null)
            {
                Debug.Assert(callBack != null, nameof(callBack) + " != null");
                _callbackDict[type] -= callBack;
            }
        }
    }
}