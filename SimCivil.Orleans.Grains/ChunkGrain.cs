﻿// Copyright (c) 2017 TPDT
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// SimCivil - SimCivil.Orleans.Grains - ChunkGrain.cs
// Create Date: 2018/03/27
// Update Date: 2018/05/12

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Orleans;
using Orleans.Runtime;

using SimCivil.Orleans.Interfaces;
using SimCivil.Orleans.Interfaces.Components;

using static System.Math;

namespace SimCivil.Orleans.Grains
{
    public class ChunkGrain : Grain, IChunk
    {
        public Dictionary<Guid, Position> Entities;

        public ILogger<ChunkGrain> Logger { get; }

        /// <summary>
        /// This constructor should never be invoked. We expose it so that client code (subclasses of Grain) do not have to add a constructor.
        /// Client code should use the GrainFactory property to get a reference to a Grain.
        /// </summary>
        public ChunkGrain(ILogger<ChunkGrain> logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task OnEntityMoved(Guid entityGuid, Position previousPos, Position currentPos)
        {
            Logger.Debug($"{entityGuid} entity have moved");

            var movedEntity = GrainFactory.GetGrain<IEntity>(entityGuid);
            var movedObserver = await movedEntity.Get<IObserver>();
            foreach (var entity in Entities)
            {
                if (entityGuid == entity.Key) continue;

                float prevDistance = Max(
                    Abs(previousPos.X - entity.Value.X),
                    Abs(previousPos.Y - entity.Value.Y));
                float currentDistance = Max(Abs(currentPos.X - entity.Value.X), Abs(currentPos.Y - entity.Value.Y));
                var effectedObserver = await GrainFactory.GetGrain<IEntity>(entity.Key).Get<IObserver>();
                if (movedObserver != null)
                {
                    uint range = await movedObserver.GetNotifyRange();

                    if (currentDistance < range)
                    {
                        await movedObserver.OnEntityEntered(entity.Key);
                    }
                    else if (prevDistance < range)
                    {
                        await movedObserver.OnEntityLeft(entity.Key);
                    }
                }

                if (effectedObserver != null)
                {
                    uint range = await effectedObserver.GetNotifyRange();

                    if (currentDistance < range)
                    {
                        await effectedObserver.OnEntityEntered(entityGuid);
                    }
                    else if (prevDistance < range)
                    {
                        await effectedObserver.OnEntityLeft(entityGuid);
                    }
                }
            }

            if (currentPos.Tile.DivDown(Config.ChunkSize) == this.GetPrimaryKeyXY())
            {
                Entities[entityGuid] = currentPos;
            }
            else
            {
                Entities.Remove(entityGuid);
            }
        }

        public override Task OnActivateAsync()
        {
            Entities = new Dictionary<Guid, Position>();

            return base.OnActivateAsync();
        }
    }
}