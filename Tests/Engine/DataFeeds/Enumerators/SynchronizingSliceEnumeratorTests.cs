/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Tests.Engine.DataFeeds.Enumerators
{
    [TestFixture]
    public class SynchronizingSliceEnumeratorTests
    {
        [Test]
        public void SynchronizesData()
        {
            var time = new DateTime(2016, 03, 03, 12, 05, 00);
            var stream1 = Enumerable.Range(0, 10).Select(x => new Slice(time.AddSeconds(x * 1), new List<BaseData>())).GetEnumerator();
            var stream2 = Enumerable.Range(0, 5).Select(x => new Slice(time.AddSeconds(x * 2), new List<BaseData>())).GetEnumerator();
            var stream3 = Enumerable.Range(0, 20).Select(x => new Slice(time.AddSeconds(x * 0.5), new List<BaseData>())).GetEnumerator();

            var previous = DateTime.MinValue;
            var synchronizer = new SynchronizingSliceEnumerator(stream1, stream2, stream3);
            while (synchronizer.MoveNext())
            {
                Assert.That(synchronizer.Current.Time, Is.GreaterThanOrEqualTo(previous));
                previous = synchronizer.Current.Time;
            }
            synchronizer.Dispose();
        }

        [Test]
        public void WontRemoveEnumeratorsReturningTrueWithCurrentNull()
        {
            var time = new DateTime(2016, 03, 03, 12, 05, 00);
            var tradeBar1 = new TradeBar { Symbol = Symbols.SPY, Time = time };
            var tradeBar2 = new TradeBar { Symbol = Symbols.AAPL, Time = time, Open = 23 };
            var stream1 = Enumerable.Range(0, 20)
                // return null except the last value and check if its emitted
                .Select(x => x == 19 ? new Slice(time.AddSeconds(x * 1), new BaseData[] { tradeBar1, tradeBar2 }) : null
            ).GetEnumerator();
            var stream2 = Enumerable.Range(0, 5).Select(x => new Slice(time.AddSeconds(x * 2), new List<BaseData>())).GetEnumerator();
            var stream3 = Enumerable.Range(0, 20).Select(x => new Slice(time.AddSeconds(x * 0.5), new List<BaseData>())).GetEnumerator();

            var previous = new Slice(DateTime.MinValue, new List<BaseData>());
            var synchronizer = new SynchronizingSliceEnumerator(stream1, stream2, stream3);
            while (synchronizer.MoveNext())
            {
                Assert.That(synchronizer.Current.Time, Is.GreaterThanOrEqualTo(previous.Time));
                previous = synchronizer.Current;
            }
            Assert.AreEqual(2, previous.Bars.Count);
            synchronizer.Dispose();
        }

        [Test]
        public void UseBinarySearchMethodForSync()
        {
            // Uses BinraySearch when stream count >= 500
            var len = 500;
            var time = new DateTime(2016, 03, 03, 12, 05, 00);
            var tradeBar1 = new TradeBar { Symbol = Symbols.SPY, Time = time };
            var tradeBar2 = new TradeBar { Symbol = Symbols.MSFT, Time = time };
            var tradeBar3 = new TradeBar { Symbol = Symbols.AAPL, Time = time };
            var stream1 = Enumerable.Range(0, 20)
                // return null except the last value and check if its emitted
                .Select(x => x == 19 ? new Slice(time.AddHours(x * 1), new BaseData[] { tradeBar1, tradeBar2, tradeBar3 }) : null
            ).GetEnumerator();
            IEnumerator<Slice>[] enumerators = new IEnumerator<Slice>[len];
            enumerators[0] = stream1;
            for (var i=1; i<len; i++)
            {
                var stream = Enumerable.Range(0, 5).Select(x => new Slice(time.AddSeconds(x * i), new List<BaseData>())).GetEnumerator();
                enumerators[i] = stream;
            }
            var previous = new Slice(DateTime.MinValue, new List<BaseData>());
            var synchronizer = new SynchronizingSliceEnumerator(enumerators);
            while (synchronizer.MoveNext())
            {
                Assert.That(synchronizer.Current.Time, Is.GreaterThanOrEqualTo(previous.Time));
                previous = synchronizer.Current;
            }
            Assert.AreEqual(3, previous.Bars.Count);
            synchronizer.Dispose();
        }

        [Test]
        public void WillRemoveEnumeratorsReturningFalse()
        {
            var time = new DateTime(2016, 03, 03, 12, 05, 00);
            var stream1 = new TestEnumerator { MoveNextReturnValue = false };
            var stream2 = Enumerable.Range(0, 10).Select(x => new Slice(time.AddSeconds(x * 0.5), new List<BaseData>())).GetEnumerator();
            var synchronizer = new SynchronizingSliceEnumerator(stream1, stream2);
            var emitted = false;
            while (synchronizer.MoveNext())
            {
                emitted = true;
            }
            Assert.IsTrue(emitted);
            Assert.IsTrue(stream1.MoveNextWasCalled);
            Assert.AreEqual(1, stream1.MoveNextCallCount);
            synchronizer.Dispose();
        }

        [Test]
        public void WillStopIfAllEnumeratorsCurrentIsNullAndReturningTrue()
        {
            var stream1 = new TestEnumerator { MoveNextReturnValue = true };
            var synchronizer = new SynchronizingSliceEnumerator(stream1);
            while (synchronizer.MoveNext())
            {
                Assert.Fail();
            }
            Assert.IsTrue(stream1.MoveNextWasCalled);
            synchronizer.Dispose();
        }

        [Test]
        public void WillStopIfAllEnumeratorsCurrentIsNullAndReturningFalse()
        {
            var stream1 = new TestEnumerator { MoveNextReturnValue = false };
            var synchronizer = new SynchronizingSliceEnumerator(stream1);
            while (synchronizer.MoveNext())
            {
                Assert.Fail();
            }
            Assert.IsTrue(stream1.MoveNextWasCalled);
            synchronizer.Dispose();
        }

        private class TestEnumerator : IEnumerator<Slice>
        {
            public int MoveNextCallCount { get; set; }
            public bool MoveNextReturnValue { get; set; }
            public bool MoveNextWasCalled { get; set; }

            public TestEnumerator()
            {
                MoveNextWasCalled = false;
            }

            public bool MoveNext()
            {
                MoveNextCallCount++;
                MoveNextWasCalled = true;
                return MoveNextReturnValue;
            }

            public Slice Current { get; }

            object IEnumerator.Current => Current;

            public void Dispose()
            { }

            public void Reset()
            { }
        }
    }
}
