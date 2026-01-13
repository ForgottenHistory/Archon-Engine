using System;

namespace Core.Events
{
    /// <summary>
    /// Disposable token representing an event subscription.
    /// Dispose to unsubscribe from the event.
    ///
    /// Usage:
    /// var token = eventBus.Subscribe<MyEvent>(HandleEvent);
    /// // Later...
    /// token.Dispose(); // Unsubscribes
    /// </summary>
    public sealed class SubscriptionToken<T> : IDisposable where T : struct, IGameEvent
    {
        private EventBus eventBus;
        private Action<T> handler;
        private bool disposed;

        internal SubscriptionToken(EventBus eventBus, Action<T> handler)
        {
            this.eventBus = eventBus;
            this.handler = handler;
            this.disposed = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            eventBus?.Unsubscribe(handler);
            eventBus = null;
            handler = null;
        }
    }

    /// <summary>
    /// Non-generic interface for subscription tokens.
    /// Allows storing mixed token types in collections.
    /// </summary>
    public interface ISubscriptionToken : IDisposable
    {
    }

    /// <summary>
    /// Generic subscription token implementing non-generic interface.
    /// </summary>
    public sealed class SubscriptionTokenGeneric<T> : ISubscriptionToken where T : struct, IGameEvent
    {
        private EventBus eventBus;
        private Action<T> handler;
        private bool disposed;

        internal SubscriptionTokenGeneric(EventBus eventBus, Action<T> handler)
        {
            this.eventBus = eventBus;
            this.handler = handler;
            this.disposed = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            eventBus?.Unsubscribe(handler);
            eventBus = null;
            handler = null;
        }
    }
}
