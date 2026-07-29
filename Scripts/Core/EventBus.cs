using System;
using System.Collections.Generic;
using Core.Events;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// High-performance event bus for decoupled system communication
    /// Features: Type-safe events, zero-allocation processing, frame-coherent batching
    /// Performance: ZERO allocations during gameplay (no boxing), batch event processing
    ///
    /// Architecture: Uses typed EventQueue{T} wrapper to avoid boxing and reflection
    /// See: Assets/Archon-Engine/Docs/Engine/data-flow-architecture.md
    /// </summary>
    public class EventBus : IDisposable
    {
        private const int INITIAL_CAPACITY = 16;

        // Type-specific event queues - uses IEventQueue interface for polymorphism without boxing
        private readonly Dictionary<Type, IEventQueue> eventQueues;

        // Performance monitoring
        private int eventsProcessedThisFrame;
        private int totalEventsProcessed;

        public bool IsActive { get; private set; }
        public int EventsProcessedTotal => totalEventsProcessed;

        public int EventsInQueue
        {
            get
            {
                int total = 0;
                foreach (var queue in eventQueues.Values)
                {
                    total += queue.Count;
                }
                return total;
            }
        }

        public EventBus()
        {
            eventQueues = new Dictionary<Type, IEventQueue>(INITIAL_CAPACITY);
            IsActive = true;
            ArchonLogger.Log("EventBus initialized (zero-allocation mode)", "core_events");
        }

        /// <summary>
        /// Subscribe to events of a specific type.
        /// Returns a token that can be disposed to unsubscribe.
        /// </summary>
        public IDisposable Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (!eventQueues.TryGetValue(eventType, out var queue))
            {
                queue = new EventQueue<T>();
                eventQueues[eventType] = queue;
            }

            ((EventQueue<T>)queue).AddListener(handler);

            return new SubscriptionTokenGeneric<T>(this, handler);
        }

        /// <summary>
        /// Unsubscribe from events of a specific type
        /// </summary>
        public void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (!eventQueues.TryGetValue(eventType, out var queue))
                return;

            ((EventQueue<T>)queue).RemoveListener(handler);
        }

        /// <summary>
        /// Emit an event - queued for frame-coherent processing
        /// ZERO ALLOCATION: Event stays as struct T in EventQueue{T}
        /// </summary>
        public void Emit<T>(T gameEvent) where T : struct, IGameEvent
        {
            if (!IsActive)
                return;

            var eventType = typeof(T);

            // Set timestamp
            gameEvent.TimeStamp = Time.time;

            // Get or create type-specific queue
            if (!eventQueues.TryGetValue(eventType, out var queue))
            {
                queue = new EventQueue<T>();
                eventQueues[eventType] = queue;
            }

            // Enqueue - NO BOXING (cast is safe, EventQueue<T> is what we created)
            ((EventQueue<T>)queue).Enqueue(gameEvent);
        }

        // Cached list for processing (reused to avoid allocations)
        private readonly List<IEventQueue> queuesToProcess = new List<IEventQueue>(16);

        /// <summary>
        /// Process all queued events - call once per frame
        /// Frame-coherent processing ensures consistent event ordering
        /// ZERO ALLOCATION: No boxing, events stay as struct T throughout
        /// </summary>
        public void ProcessEvents()
        {
            if (!IsActive || eventQueues.Count == 0)
                return;

            eventsProcessedThisFrame = 0;

            // Copy queues to list (prevents concurrent modification if new event types emitted during processing)
            queuesToProcess.Clear();
            foreach (var queue in eventQueues.Values)
            {
                queuesToProcess.Add(queue);
            }

            // Process all type-specific queues
            for (int i = 0; i < queuesToProcess.Count; i++)
            {
                int processed = queuesToProcess[i].ProcessEvents();
                eventsProcessedThisFrame += processed;
                totalEventsProcessed += processed;
            }

            #if UNITY_EDITOR
            if (eventsProcessedThisFrame > 10000)
            {
                ArchonLogger.LogWarning($"EventBus processed {eventsProcessedThisFrame} events this frame", "core_events");
            }
            #endif
        }

        /// <summary>
        /// Clear all events and listeners - useful for scene changes
        /// </summary>
        public void Clear()
        {
            foreach (var queue in eventQueues.Values)
            {
                queue.Clear();
            }

            eventQueues.Clear();
            ArchonLogger.Log("EventBus cleared", "core_events");
        }

        public void Dispose()
        {
            IsActive = false;
            Clear();
            ArchonLogger.Log("EventBus disposed", "core_events");
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Debug information for editor
        /// </summary>
        public void LogDebugInfo()
        {
            ArchonLogger.Log($"EventBus Status:\n" +
                      $"- Active: {IsActive}\n" +
                      $"- Events in queue: {EventsInQueue}\n" +
                      $"- Event types registered: {eventQueues.Count}\n" +
                      $"- Total events processed: {totalEventsProcessed}\n" +
                      $"- Events this frame: {eventsProcessedThisFrame}", "core_data_linking");
        }
        #endif

        /// <summary>
        /// Internal interface for type-erased event queue storage.
        /// Enables storing different EventQueue&lt;T&gt; instances in one dictionary.
        /// Virtual method calls on this interface do NOT box value types because
        /// the actual event processing happens inside the typed EventQueue&lt;T&gt;.
        /// </summary>
        private interface IEventQueue
        {
            int ProcessEvents();
            void Clear();
            int Count { get; }
        }

        /// <summary>
        /// Type-specific event queue that processes events without boxing
        /// This is the key to zero-allocation: Queue<T> stays as T throughout
        /// </summary>
        private class EventQueue<T> : IEventQueue where T : struct, IGameEvent
        {
            private const int INITIAL_QUEUE_CAPACITY = 256;

            private readonly Queue<T> eventQueue;
            private readonly Queue<T> processingQueue;
            private Action<T> listeners;

            // Cached invocation list. Handlers must be invoked individually so that one
            // throwing handler cannot abort the rest of the chain, but GetInvocationList
            // allocates a new array on every call - so it is cached here and rebuilt only
            // when the listener set actually changes.
            private Delegate[] invocationCache;

            public int Count => eventQueue.Count;

            public EventQueue()
            {
                eventQueue = new Queue<T>(INITIAL_QUEUE_CAPACITY);
                processingQueue = new Queue<T>(INITIAL_QUEUE_CAPACITY);
            }

            public void Enqueue(T gameEvent)
            {
                eventQueue.Enqueue(gameEvent);  // NO BOXING - T stays T
            }

            public void AddListener(Action<T> handler)
            {
                listeners = (Action<T>)Delegate.Combine(listeners, handler);
                invocationCache = null;
            }

            public void RemoveListener(Action<T> handler)
            {
                listeners = (Action<T>)Delegate.Remove(listeners, handler);
                invocationCache = null;
            }

            public int ProcessEvents()
            {
                if (eventQueue.Count == 0)
                    return 0;

                int processed = 0;

                // Swap queues to allow new events during processing
                while (eventQueue.Count > 0)
                {
                    processingQueue.Enqueue(eventQueue.Dequeue());  // NO BOXING - T to T
                }

                // Rebuild the invocation list only when listeners changed, then snapshot
                // it into a local. A handler may subscribe or unsubscribe while it runs,
                // which nulls invocationCache - reading the field inside the loop would
                // then throw. The snapshot also gives the pass a stable listener set;
                // subscription changes take effect on the next pass.
                if (listeners != null && invocationCache == null)
                    invocationCache = listeners.GetInvocationList();

                var handlers = invocationCache;

                // Process all events
                while (processingQueue.Count > 0)
                {
                    var gameEvent = processingQueue.Dequeue();  // NO BOXING - T stays T

                    // Each handler is invoked inside its own try so that a failure in one
                    // subscriber cannot starve the subscribers registered after it.
                    // The event counts as processed regardless - it was consumed.
                    if (handlers != null)
                    {
                        for (int i = 0; i < handlers.Length; i++)
                        {
                            try
                            {
                                // NO BOXING - direct Action<T> call
                                ((Action<T>)handlers[i]).Invoke(gameEvent);
                            }
                            catch (Exception e)
                            {
                                ArchonLogger.LogError($"Error processing event {typeof(T).Name}: {e.Message}\n{e.StackTrace}", "core_events");
                            }
                        }
                    }

                    processed++;
                }

                return processed;
            }

            public void Clear()
            {
                eventQueue.Clear();
                processingQueue.Clear();
            }
        }
    }

    /// <summary>
    /// Base interface for all game events.
    /// Events MUST be structs (not classes) to avoid heap allocations during gameplay.
    /// The EventBus uses generic EventQueue&lt;T&gt; which keeps events as value types throughout,
    /// ensuring zero boxing and zero GC pressure.
    /// </summary>
    public interface IGameEvent
    {
        /// <summary>
        /// Timestamp when the event was emitted (set automatically by EventBus).
        /// </summary>
        float TimeStamp { get; set; }
    }
}
