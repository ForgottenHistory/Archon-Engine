using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// High-performance event bus for decoupled system communication
    /// Features: Type-safe events, zero-allocation processing, frame-coherent batching
    /// Performance: ZERO allocations during gameplay (no boxing), batch event processing
    ///
    /// Architecture: Uses typed EventQueue<T> wrapper to avoid boxing and reflection
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
            ArchonLogger.LogCoreEvents("EventBus initialized (zero-allocation mode)");
        }

        /// <summary>
        /// Subscribe to events of a specific type
        /// </summary>
        public void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (!eventQueues.TryGetValue(eventType, out var queue))
            {
                queue = new EventQueue<T>();
                eventQueues[eventType] = queue;
            }

            ((EventQueue<T>)queue).AddListener(handler);
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
        /// ZERO ALLOCATION: Event stays as struct T in EventQueue<T>
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
            if (eventsProcessedThisFrame > 1000)
            {
                ArchonLogger.LogCoreEventsWarning($"EventBus processed {eventsProcessedThisFrame} events this frame");
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
            ArchonLogger.LogCoreEvents("EventBus cleared");
        }

        public void Dispose()
        {
            IsActive = false;
            Clear();
            ArchonLogger.LogCoreEvents("EventBus disposed");
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Debug information for editor
        /// </summary>
        public void LogDebugInfo()
        {
            ArchonLogger.LogDataLinking($"EventBus Status:\n" +
                      $"- Active: {IsActive}\n" +
                      $"- Events in queue: {EventsInQueue}\n" +
                      $"- Event types registered: {eventQueues.Count}\n" +
                      $"- Total events processed: {totalEventsProcessed}\n" +
                      $"- Events this frame: {eventsProcessedThisFrame}");
        }
        #endif

        /// <summary>
        /// Internal interface for type-erased event queue storage
        /// Virtual method calls don't box value types
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
            }

            public void RemoveListener(Action<T> handler)
            {
                listeners = (Action<T>)Delegate.Remove(listeners, handler);

                if (listeners == null && eventQueue.Count == 0)
                {
                    // Queue is now unused, could be cleaned up
                }
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

                // Process all events
                while (processingQueue.Count > 0)
                {
                    var gameEvent = processingQueue.Dequeue();  // NO BOXING - T stays T

                    try
                    {
                        // Invoke listeners - NO BOXING - direct Action<T> call
                        listeners?.Invoke(gameEvent);
                        processed++;
                    }
                    catch (Exception e)
                    {
                        ArchonLogger.LogCoreEventsError($"Error processing event {typeof(T).Name}: {e.Message}\n{e.StackTrace}");
                    }
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
    /// Base interface for all game events
    /// Events MUST be structs to avoid allocations
    /// </summary>
    public interface IGameEvent
    {
        float TimeStamp { get; set; }
    }
}
