using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Core;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for EventBus - the frame-coherent, zero-allocation event bus.
    ///
    /// WHY THIS MATTERS: systems communicate through this bus rather than direct
    /// references, so a delivery failure does not throw - listeners simply stop being
    /// called and the symptom shows up somewhere unrelated. Delivery guarantees are
    /// pinned here so they cannot erode silently.
    /// </summary>
    public class EventBusTests
    {
        private struct TestEvent : IGameEvent
        {
            public float TimeStamp { get; set; }
            public int Value;
        }

        private struct OtherEvent : IGameEvent
        {
            public float TimeStamp { get; set; }
            public int Value;
        }

        private EventBus bus;

        [SetUp]
        public void SetUp() => bus = new EventBus();

        [TearDown]
        public void TearDown() => bus?.Dispose();

        /// <summary>
        /// The bus catches handler exceptions and reports them through
        /// ArchonLogger.LogError, which routes to Debug.LogError. NUnit fails any test
        /// that logs an error unless it is declared expected, so tests that deliberately
        /// throw from a handler must call this first.
        /// </summary>
        private static void ExpectHandlerFailureLog()
        {
            LogAssert.Expect(LogType.Error, new Regex("^Error processing event TestEvent:"));
        }

        // ===== Delivery =====

        [Test]
        public void Emit_DoesNotDeliverUntilProcessEvents()
        {
            int received = 0;
            bus.Subscribe<TestEvent>(_ => received++);

            bus.Emit(new TestEvent { Value = 1 });

            Assert.AreEqual(0, received,
                "Delivery must be frame-coherent - Emit queues, ProcessEvents delivers");

            bus.ProcessEvents();

            Assert.AreEqual(1, received, "ProcessEvents must deliver the queued event");
        }

        [Test]
        public void ProcessEvents_DeliversPayloadIntact()
        {
            int captured = 0;
            bus.Subscribe<TestEvent>(e => captured = e.Value);

            bus.Emit(new TestEvent { Value = 42 });
            bus.ProcessEvents();

            Assert.AreEqual(42, captured);
        }

        [Test]
        public void ProcessEvents_PreservesEmissionOrderWithinAType()
        {
            var received = new List<int>();
            bus.Subscribe<TestEvent>(e => received.Add(e.Value));

            for (int i = 1; i <= 5; i++)
                bus.Emit(new TestEvent { Value = i });

            bus.ProcessEvents();

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, received,
                "Events of one type must be delivered FIFO");
        }

        [Test]
        public void ProcessEvents_RoutesByEventType()
        {
            int testCount = 0;
            int otherCount = 0;
            bus.Subscribe<TestEvent>(_ => testCount++);
            bus.Subscribe<OtherEvent>(_ => otherCount++);

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(1, testCount, "The matching handler must fire");
            Assert.AreEqual(0, otherCount, "A handler for a different type must not fire");
        }

        [Test]
        public void ProcessEvents_DeliversToAllSubscribers()
        {
            int first = 0;
            int second = 0;
            bus.Subscribe<TestEvent>(_ => first++);
            bus.Subscribe<TestEvent>(_ => second++);

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second);
        }

        [Test]
        public void ProcessEvents_OnEmptyBus_IsSafe()
        {
            Assert.DoesNotThrow(() => bus.ProcessEvents());
        }

        [Test]
        public void ProcessEvents_DrainsTheQueue()
        {
            int received = 0;
            bus.Subscribe<TestEvent>(_ => received++);

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();
            bus.ProcessEvents();

            Assert.AreEqual(1, received, "A processed event must not be delivered twice");
        }

        [Test]
        public void Emit_WithNoSubscribers_IsSafe()
        {
            bus.Emit(new TestEvent { Value = 1 });

            Assert.DoesNotThrow(() => bus.ProcessEvents());
        }

        /// <summary>
        /// Events emitted from inside a handler land in the next processing pass, not the
        /// current one. This bounds a single ProcessEvents call - without it, a handler
        /// that emits its own trigger would spin forever inside one frame.
        /// </summary>
        [Test]
        public void EventsEmittedDuringProcessing_DeferToNextPass()
        {
            var order = new List<int>();
            bus.Subscribe<TestEvent>(e =>
            {
                order.Add(e.Value);
                if (e.Value < 3)
                    bus.Emit(new TestEvent { Value = e.Value + 1 });
            });

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            CollectionAssert.AreEqual(new[] { 1 }, order,
                "A re-entrant emit must not be processed in the same pass");

            bus.ProcessEvents();

            CollectionAssert.AreEqual(new[] { 1, 2 }, order,
                "The deferred event arrives on the next pass");
        }

        // ===== Unsubscribe =====

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            int received = 0;
            Action<TestEvent> handler = _ => received++;

            bus.Subscribe(handler);
            bus.Unsubscribe(handler);

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(0, received);
        }

        [Test]
        public void Unsubscribe_LeavesOtherSubscribersConnected()
        {
            int removed = 0;
            int kept = 0;
            Action<TestEvent> toRemove = _ => removed++;

            bus.Subscribe(toRemove);
            bus.Subscribe<TestEvent>(_ => kept++);
            bus.Unsubscribe(toRemove);

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(0, removed);
            Assert.AreEqual(1, kept, "Removing one handler must not detach the others");
        }

        [Test]
        public void Unsubscribe_UnknownHandler_IsSafe()
        {
            Assert.DoesNotThrow(() => bus.Unsubscribe<TestEvent>(_ => { }));
        }

        /// <summary>
        /// Handlers are invoked from a cached invocation list that is invalidated on any
        /// subscription change. A handler that unsubscribes while running therefore
        /// mutates the cache mid-pass - the pass must work from a stable snapshot rather
        /// than re-reading the field, or it dereferences null.
        /// </summary>
        [Test]
        public void UnsubscribeDuringProcessing_DoesNotThrow()
        {
            Action<TestEvent> toRemove = _ => { };
            bus.Subscribe<TestEvent>(_ => bus.Unsubscribe(toRemove));
            bus.Subscribe(toRemove);

            bus.Emit(new TestEvent { Value = 1 });
            bus.Emit(new TestEvent { Value = 2 });

            Assert.DoesNotThrow(() => bus.ProcessEvents(),
                "Unsubscribing from inside a handler must not corrupt the running pass");
        }

        [Test]
        public void UnsubscribeDuringProcessing_TakesEffectOnNextPass()
        {
            int removedCalls = 0;
            Action<TestEvent> toRemove = _ => removedCalls++;

            bus.Subscribe<TestEvent>(_ => bus.Unsubscribe(toRemove));
            bus.Subscribe(toRemove);

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            int callsAfterFirstPass = removedCalls;

            bus.Emit(new TestEvent { Value = 2 });
            bus.ProcessEvents();

            Assert.AreEqual(callsAfterFirstPass, removedCalls,
                "Once the pass ends, the unsubscribed handler must stop receiving events");
        }

        [Test]
        public void SubscribeDuringProcessing_DefersToNextPass()
        {
            int lateSubscriberCalls = 0;
            bool hasSubscribed = false;

            bus.Subscribe<TestEvent>(_ =>
            {
                if (hasSubscribed) return;
                hasSubscribed = true;
                bus.Subscribe<TestEvent>(__ => lateSubscriberCalls++);
            });

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(0, lateSubscriberCalls,
                "A handler added mid-pass must not receive the event being processed");

            bus.Emit(new TestEvent { Value = 2 });
            bus.ProcessEvents();

            Assert.AreEqual(1, lateSubscriberCalls,
                "It must start receiving events on the next pass");
        }

        [Test]
        public void SubscriptionToken_UnsubscribesOnDispose()
        {
            int received = 0;
            var token = bus.Subscribe<TestEvent>(_ => received++);

            token.Dispose();

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(0, received, "Disposing the token must unsubscribe");
        }

        [Test]
        public void SubscriptionToken_DoubleDispose_IsSafe()
        {
            var token = bus.Subscribe<TestEvent>(_ => { });

            token.Dispose();

            Assert.DoesNotThrow(() => token.Dispose());
        }

        /// <summary>
        /// Subscribing the same handler twice registers it twice (multicast delegate),
        /// and one Unsubscribe removes only one registration. Pinned because it is
        /// surprising: a double-subscribed handler needs two unsubscribes to go quiet.
        /// </summary>
        [Test]
        public void DoubleSubscribe_RequiresDoubleUnsubscribe()
        {
            int received = 0;
            Action<TestEvent> handler = _ => received++;

            bus.Subscribe(handler);
            bus.Subscribe(handler);

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();
            Assert.AreEqual(2, received, "A handler subscribed twice is invoked twice");

            received = 0;
            bus.Unsubscribe(handler);
            bus.Emit(new TestEvent { Value = 2 });
            bus.ProcessEvents();
            Assert.AreEqual(1, received, "One Unsubscribe removes only one registration");
        }

        // ===== Lifecycle =====

        [Test]
        public void Emit_AfterDispose_IsIgnored()
        {
            int received = 0;
            bus.Subscribe<TestEvent>(_ => received++);

            bus.Dispose();
            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(0, received, "A disposed bus must not deliver");
        }

        [Test]
        public void IsActive_ReflectsDisposal()
        {
            Assert.IsTrue(bus.IsActive);

            bus.Dispose();

            Assert.IsFalse(bus.IsActive);
        }

        /// <summary>
        /// KNOWN DEFECT: Clear() drops the EventQueue objects, and each queue owns its
        /// listener delegate - so every existing subscription is silently discarded. The
        /// subscriber is never told. A later Emit creates a fresh empty queue, the event
        /// is "processed" with zero listeners, and nothing reports a problem.
        ///
        /// This bites anything that outlives a Clear (a UI panel across a scene change):
        /// it keeps running, keeps holding a token that now unsubscribes nothing, and
        /// simply stops receiving events.
        ///
        /// Pinned as-is. If Clear is fixed to preserve listeners, this test should start
        /// failing - update it rather than reverting the fix.
        /// </summary>
        [Test]
        public void Clear_SilentlyDiscardsSubscriptions_KnownDefect()
        {
            int received = 0;
            bus.Subscribe<TestEvent>(_ => received++);

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();
            Assert.AreEqual(1, received, "Precondition: delivery works before Clear");

            bus.Clear();

            bus.Emit(new TestEvent { Value = 2 });
            bus.ProcessEvents();

            Assert.AreEqual(1, received,
                "Clear() discards listeners without notifying them. If this now reads 2, " +
                "Clear has been fixed to preserve subscriptions - update this test.");
        }

        [Test]
        public void Clear_ThenResubscribe_RestoresDelivery()
        {
            int received = 0;
            bus.Subscribe<TestEvent>(_ => received++);
            bus.Clear();

            bus.Subscribe<TestEvent>(_ => received++);
            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(1, received,
                "Re-subscribing after Clear must work - the bus itself stays usable");
        }

        [Test]
        public void Clear_DiscardsPendingEvents()
        {
            int received = 0;
            bus.Subscribe<TestEvent>(_ => received++);

            bus.Emit(new TestEvent { Value = 1 });
            bus.Clear();
            bus.ProcessEvents();

            Assert.AreEqual(0, received, "Events queued before Clear must not be delivered");
        }

        // ===== Exception handling =====

        /// <summary>
        /// REGRESSION: handlers used to be invoked through a single listeners?.Invoke()
        /// on the multicast delegate. A try/catch around that call cannot resume the
        /// chain, so one throwing handler silently starved every subscriber registered
        /// after it - and the catch made it look handled.
        ///
        /// Handlers are now invoked individually from a cached invocation list, each in
        /// its own try.
        /// </summary>
        [Test]
        public void ThrowingHandler_DoesNotStarveLaterHandlers()
        {
            int laterHandlerCalls = 0;

            bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("handler failure"));
            bus.Subscribe<TestEvent>(_ => laterHandlerCalls++);

            bus.Emit(new TestEvent { Value = 1 });

            // The bus logs the failure via Debug.LogError, which NUnit treats as a test
            // failure unless it is declared expected.
            ExpectHandlerFailureLog();

            Assert.DoesNotThrow(() => bus.ProcessEvents(),
                "A throwing handler must not propagate out of ProcessEvents");

            Assert.AreEqual(1, laterHandlerCalls,
                "A handler registered after a throwing one must still receive the event");
        }

        [Test]
        public void ThrowingHandler_DoesNotStarveEarlierHandlers()
        {
            int earlierHandlerCalls = 0;

            bus.Subscribe<TestEvent>(_ => earlierHandlerCalls++);
            bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("handler failure"));

            bus.Emit(new TestEvent { Value = 1 });
            ExpectHandlerFailureLog();
            bus.ProcessEvents();

            Assert.AreEqual(1, earlierHandlerCalls);
        }

        [Test]
        public void MultipleThrowingHandlers_AreAllIsolated()
        {
            int survivors = 0;

            bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("handler failure"));
            bus.Subscribe<TestEvent>(_ => survivors++);
            bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("handler failure"));
            bus.Subscribe<TestEvent>(_ => survivors++);

            bus.Emit(new TestEvent { Value = 1 });
            ExpectHandlerFailureLog();
            ExpectHandlerFailureLog();
            bus.ProcessEvents();

            Assert.AreEqual(2, survivors,
                "Every non-throwing handler must run regardless of how many others fail");
        }

        [Test]
        public void ThrowingHandler_DoesNotBreakSubsequentFrames()
        {
            int goodCalls = 0;

            bus.Subscribe<TestEvent>(e =>
            {
                if (e.Value == 1) throw new InvalidOperationException("one-off failure");
                goodCalls++;
            });

            bus.Emit(new TestEvent { Value = 1 });
            ExpectHandlerFailureLog();
            bus.ProcessEvents();

            bus.Emit(new TestEvent { Value = 2 });
            bus.ProcessEvents();

            Assert.AreEqual(1, goodCalls,
                "A failure on one event must not disable the bus for later events");
        }

        // ===== Counters =====

        [Test]
        public void EventsInQueue_TracksPendingEvents()
        {
            bus.Subscribe<TestEvent>(_ => { });

            Assert.AreEqual(0, bus.EventsInQueue);

            bus.Emit(new TestEvent { Value = 1 });
            bus.Emit(new TestEvent { Value = 2 });

            Assert.AreEqual(2, bus.EventsInQueue);

            bus.ProcessEvents();

            Assert.AreEqual(0, bus.EventsInQueue, "Processing must drain the queue");
        }

        [Test]
        public void EventsInQueue_SpansMultipleTypes()
        {
            bus.Emit(new TestEvent { Value = 1 });
            bus.Emit(new OtherEvent { Value = 2 });

            Assert.AreEqual(2, bus.EventsInQueue,
                "The count must aggregate across every event type");
        }

        [Test]
        public void EventsProcessedTotal_AccumulatesAcrossFrames()
        {
            bus.Subscribe<TestEvent>(_ => { });

            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();
            bus.Emit(new TestEvent { Value = 2 });
            bus.ProcessEvents();

            Assert.AreEqual(2, bus.EventsProcessedTotal);
        }

        [Test]
        public void EventsProcessedTotal_CountsEventsWithoutSubscribers()
        {
            bus.Emit(new TestEvent { Value = 1 });
            bus.ProcessEvents();

            Assert.AreEqual(1, bus.EventsProcessedTotal,
                "An event with no listeners is still consumed and counted");
        }

        /// <summary>
        /// The counter used to sit inside the try block, so a throwing handler meant the
        /// event was consumed but never counted - EventsProcessedTotal under-reported
        /// exactly when something was going wrong. It now counts consumption, which is
        /// independent of whether the handlers succeeded.
        /// </summary>
        [Test]
        public void EventsProcessedTotal_CountsEventsWhoseHandlerThrew()
        {
            bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("failure"));

            bus.Emit(new TestEvent { Value = 1 });
            ExpectHandlerFailureLog();
            bus.ProcessEvents();

            Assert.AreEqual(1, bus.EventsProcessedTotal,
                "The event was consumed, so it must be counted even though its handler failed");
        }
    }
}
