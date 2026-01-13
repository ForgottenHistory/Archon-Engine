using System;
using System.Collections.Generic;

namespace Core.Events
{
    /// <summary>
    /// Groups multiple IDisposable objects for batch disposal.
    /// Useful for managing multiple event subscriptions.
    ///
    /// Usage:
    /// var subscriptions = new CompositeDisposable();
    /// subscriptions.Add(eventBus.Subscribe<EventA>(HandleA));
    /// subscriptions.Add(eventBus.Subscribe<EventB>(HandleB));
    /// // Later...
    /// subscriptions.Dispose(); // Unsubscribes all
    /// </summary>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> disposables;
        private bool disposed;

        public CompositeDisposable()
        {
            disposables = new List<IDisposable>(4);
        }

        public CompositeDisposable(int capacity)
        {
            disposables = new List<IDisposable>(capacity);
        }

        /// <summary>
        /// Add a disposable to the group.
        /// </summary>
        public void Add(IDisposable disposable)
        {
            if (disposed)
            {
                // Already disposed - dispose immediately
                disposable?.Dispose();
                return;
            }

            if (disposable != null)
            {
                disposables.Add(disposable);
            }
        }

        /// <summary>
        /// Remove a disposable from the group without disposing it.
        /// </summary>
        public bool Remove(IDisposable disposable)
        {
            if (disposed) return false;
            return disposables.Remove(disposable);
        }

        /// <summary>
        /// Number of disposables in the group.
        /// </summary>
        public int Count => disposables.Count;

        /// <summary>
        /// Clear all disposables without disposing them.
        /// </summary>
        public void Clear()
        {
            disposables.Clear();
        }

        /// <summary>
        /// Dispose all contained disposables.
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            for (int i = disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    disposables[i]?.Dispose();
                }
                catch (Exception)
                {
                    // Swallow exceptions during disposal
                }
            }

            disposables.Clear();
        }
    }
}
