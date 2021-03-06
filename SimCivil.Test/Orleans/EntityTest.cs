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
// SimCivil - SimCivil.Test - EntityTest.cs
// Create Date: 2018/05/14
// Update Date: 2018/05/17

using System;
using System.Linq;
using System.Text;

using Orleans;
using Orleans.TestingHost;

using SimCivil.Orleans.Interfaces;

using Xunit;

namespace SimCivil.Test.Orleans
{
    public class EntityTest
    {
        public EntityTest()
        {
            Cluster = OrleansFixture.Single.Cluster;
        }

        public TestCluster Cluster { get; }

        [Fact]
        public void ComponentManageTest()
        {
            var entity = Cluster.GrainFactory.GetGrain<IEntity>(Guid.NewGuid());
            entity.Enable().Wait();
            Assert.True(entity.IsEnabled().Result);
            var entities = Cluster.GrainFactory.GetGrain<IEntityGroup>(0).GetEntities().Result.ToArray();
            Assert.NotEmpty(entities);
            Assert.Contains(entity.GetPrimaryKey(), entities);
        }
    }
}