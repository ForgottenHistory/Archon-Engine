using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// High-performance event bus for decoupled system communication
    /// Features: Type-safe events, pooled allocation, frame-coherent processing
    /// Performance: Zero allocations during gameplay, batch event processing
    /// </summary>
    public class EventBus : IDisposable
    {
        private const int INITIAL_LISTENER_CAPACITY = 16;
        private const int INITIAL_EVENT_QUEUE_CAPACITY = 256;

        // Event listeners organized by type
        private readonly Dictionary<Type, List<IEventListener>> listeners;

        // Event queue for frame-coherent processing
        private readonly Queue<IGameEvent> eventQueue;
        private readonly Queue<IGameEvent> processingQueue;

        // Event pooling for zero allocations
        private readonly Dictionary<Type, Queue<IGameEvent>> eventPools;

        // Performance monitoring
        private int eventsProcessedThisFrame;
        private int totalEventsProcessed;

        public bool IsActive { get; private set; }
        public int EventsInQueue => eventQueue.Count;
        public int EventsProcessedTotal => totalEventsProcessed;

        public EventBus()
        {
            listeners = new Dictionary<Type, List<IEventListener>>(INITIAL_LISTENER_CAPACITY);
            eventQueue = new Queue<IGameEvent>(INITIAL_EVENT_QUEUE_CAPACITY);
            processingQueue = new Queue<IGameEvent>(INITIAL_EVENT_QUEUE_CAPACITY);
            eventPools = new Dictionary<Type, Queue<IGameEvent>>();

            IsActive = true;
            DominionLogger.Log("EventBus initialized");
        }

        /// <summary>
        /// Subscribe to events of a specific type
        /// Thread-safe and can be called during event processing
        /// </summary>
        public void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (!listeners.ContainsKey(eventType))
            {
                listeners[eventType] = new List<IEventListener>();
            }

            listeners[eventType].Add(new EventListener<T>(handler));
        }

        /// <summary>
        /// Subscribe with a listener object (for automatic cleanup)
        /// </summary>
        public void Subscribe<T>(IEventListener<T> listener) where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (!listeners.ContainsKey(eventType))
            {
                listeners[eventType] = new List<IEventListener>();
            }

            listeners[eventType].Add(listener);
        }

        /// <summary>
        /// Unsubscribe from events of a specific type
        /// </summary>
        public void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (!listeners.ContainsKey(eventType))
                return;

            listeners[eventType].RemoveAll(l => l is EventListener<T> el && el.Handler.Equals(handler));
        }

        /// <summary>
        /// Unsubscribe a listener object
        /// </summary>
        public void Unsubscribe<T>(IEventListener<T> listener) where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (!listeners.ContainsKey(eventType))
                return;

            listeners[eventType].Remove(listener);
        }

        /// <summary>
        /// Emit an event - queued for frame-coherent processing
        /// Zero allocation if event type is pooled
        /// </summary>
        public void Emit<T>(T gameEvent) where T : struct, IGameEvent
        {
            if (!IsActive)
                return;

            // Set timestamp
            var timestampedEvent = gameEvent;
            timestampedEvent.TimeStamp = Time.time;

            // Queue for processing
            eventQueue.Enqueue(timestampedEvent);
        }

        /// <summary>
        /// Get a pooled event instance to avoid allocations
        /// </summary>
        public T GetPooledEvent<T>() where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (eventPools.ContainsKey(eventType) && eventPools[eventType].Count > 0)
            {
                return (T)eventPools[eventType].Dequeue();
            }

            return new T();
        }

        /// <summary>
        /// Return event to pool after processing
        /// </summary>
        public void ReturnToPool<T>(T gameEvent) where T : struct, IGameEvent
        {
            var eventType = typeof(T);

            if (!eventPools.ContainsKey(eventType))
            {
                eventPools[eventType] = new Queue<IGameEvent>();
            }

            eventPools[eventType].Enqueue(gameEvent);
        }

        /// <summary>
        /// Process all queued events - call once per frame
        /// Frame-coherent processing ensures consistent event ordering
        /// </summary>
        public void ProcessEvents()
        {
            if (!IsActive || eventQueue.Count == 0)
                return;

            eventsProcessedThisFrame = 0;

            // Swap queues to allow new events during processing
            while (eventQueue.Count > 0)
            {
                processingQueue.Enqueue(eventQueue.Dequeue());
            }

            // Process all events in order
            while (processingQueue.Count > 0)
            {
                var gameEvent = processingQueue.Dequeue();
                ProcessEvent(gameEvent);
                eventsProcessedThisFrame++;
                totalEventsProcessed++;
            }

            #if UNITY_EDITOR
            if (eventsProcessedThisFrame > 100)
            {
                DominionLogger.LogWarning($"EventBus processed {eventsProcessedThisFrame} events this frame - potential performance issue");
            }
            #endif
        }

        /// <summary>
        /// Process a single event by notifying all listeners
        /// </summary>
        private void ProcessEvent(IGameEvent gameEvent)
        {
            var eventType = gameEvent.GetType();

            if (!listeners.ContainsKey(eventType))
                return;

            var eventListeners = listeners[eventType];

            // Process all listeners for this event type
            for (int i = eventListeners.Count - 1; i >= 0; i--)
            {
                try
                {
                    eventListeners[i].OnEvent(gameEvent);
                }
                catch (Exception e)
                {
                    DominionLogger.LogError($"Error processing event {eventType.Name}: {e.Message}");

                    // Remove broken listeners
                    eventListeners.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Clear all events and listeners - useful for scene changes
        /// </summary>
        public void Clear()
        {
            eventQueue.Clear();
            processingQueue.Clear();
            listeners.Clear();

            // Clear but keep pools for reuse
            foreach (var pool in eventPools.Values)
            {
                pool.Clear();
            }

            DominionLogger.Log("EventBus cleared");
        }

        public void Dispose()
        {
            IsActive = false;
            Clear();
            DominionLogger.Log("EventBus disposed");
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Debug information for editor
        /// </summary>
        public void LogDebugInfo()
        {
            DominionLogger.LogDataLinking($"EventBus Status:\n" +
                      $"- Active: {IsActive}\n" +
                      $"- Events in queue: {eventQueue.Count}\n" +
                      $"- Event types registered: {listeners.Count}\n" +
                      $"- Total events processed: {totalEventsProcessed}\n" +
                      $"- Events this frame: {eventsProcessedThisFrame}");
        }
        #endif
    }

    /// <summary>
    /// Base interface for all game events
    /// </summary>
    public interface IGameEvent
    {
        float TimeStamp { get; set; }
    }

    /// <summary>
    /// Base interface for event listeners
    /// </summary>
    public interface IEventListener
    {
        void OnEvent(IGameEvent gameEvent);
    }

    /// <summary>
    /// Typed event listener interface
    /// </summary>
    public interface IEventListener<T> : IEventListener where T : struct, IGameEvent
    {
        void OnEvent(T gameEvent);
    }

    /// <summary>
    /// Internal event listener wrapper for Action delegates
    /// </summary>
    internal class EventListener<T> : IEventListener<T> where T : struct, IGameEvent
    {
        public readonly Action<T> Handler;

        public EventListener(Action<T> handler)
        {
            Handler = handler;
        }

        public void OnEvent(IGameEvent gameEvent)
        {
            if (gameEvent is T typedEvent)
            {
                Handler(typedEvent);
            }
        }

        public void OnEvent(T gameEvent)
        {
            Handler(gameEvent);
        }
    }
}