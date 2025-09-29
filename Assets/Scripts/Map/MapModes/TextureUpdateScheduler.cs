using UnityEngine;
using System.Collections.Generic;

namespace Map.MapModes
{
    /// <summary>
    /// Schedules texture updates for map mode handlers based on their update frequency
    /// Prevents unnecessary texture updates and optimizes performance
    /// </summary>
    public class TextureUpdateScheduler : System.IDisposable
    {
        private struct UpdateRequest
        {
            public IMapModeHandler handler;
            public System.Action<IMapModeHandler> updateAction;
            public UpdateFrequency frequency;
            public float lastUpdate;
            public float nextUpdate;
        }

        private List<UpdateRequest> updateRequests = new List<UpdateRequest>();
        private float currentTime;

        /// <summary>
        /// Register a handler for scheduled updates
        /// </summary>
        public void RegisterHandler(IMapModeHandler handler, UpdateFrequency frequency, System.Action<IMapModeHandler> updateAction)
        {
            if (handler == null || updateAction == null) return;

            var request = new UpdateRequest
            {
                handler = handler,
                updateAction = updateAction,
                frequency = frequency,
                lastUpdate = 0f,
                nextUpdate = 0f
            };

            updateRequests.Add(request);
        }

        /// <summary>
        /// Update all scheduled handlers
        /// </summary>
        public void Update()
        {
            currentTime = Time.time;

            for (int i = 0; i < updateRequests.Count; i++)
            {
                var request = updateRequests[i];

                if (ShouldUpdate(request))
                {
                    request.updateAction(request.handler);
                    request.lastUpdate = currentTime;
                    request.nextUpdate = currentTime + GetUpdateInterval(request.frequency);
                    updateRequests[i] = request;
                }
            }
        }

        /// <summary>
        /// Check if a handler should be updated
        /// </summary>
        private bool ShouldUpdate(UpdateRequest request)
        {
            return currentTime >= request.nextUpdate;
        }

        /// <summary>
        /// Get update interval for a frequency (in real-time seconds)
        /// </summary>
        private float GetUpdateInterval(UpdateFrequency frequency)
        {
            return frequency switch
            {
                UpdateFrequency.Never => float.MaxValue,
                UpdateFrequency.Yearly => 60f,    // Update yearly data every minute
                UpdateFrequency.Monthly => 10f,   // Update monthly data every 10 seconds
                UpdateFrequency.Weekly => 5f,     // Update weekly data every 5 seconds
                UpdateFrequency.Daily => 2f,      // Update daily data every 2 seconds
                UpdateFrequency.PerConquest => 0f, // Event-driven (immediate)
                UpdateFrequency.RealTime => 0.5f,  // Real-time data every 0.5 seconds
                _ => 5f
            };
        }

        /// <summary>
        /// Notify when map mode changes (for immediate updates)
        /// </summary>
        public void OnModeChanged(IMapModeHandler newHandler)
        {
            // Force immediate update for the new handler
            for (int i = 0; i < updateRequests.Count; i++)
            {
                var request = updateRequests[i];
                if (request.handler == newHandler)
                {
                    request.updateAction(request.handler);
                    request.lastUpdate = Time.time;
                    request.nextUpdate = Time.time + GetUpdateInterval(request.frequency);
                    updateRequests[i] = request;
                    break;
                }
            }
        }

        /// <summary>
        /// Force update for a specific handler
        /// </summary>
        public void ForceUpdate(IMapModeHandler handler)
        {
            for (int i = 0; i < updateRequests.Count; i++)
            {
                var request = updateRequests[i];
                if (request.handler == handler)
                {
                    request.updateAction(request.handler);
                    request.lastUpdate = Time.time;
                    request.nextUpdate = Time.time + GetUpdateInterval(request.frequency);
                    updateRequests[i] = request;
                    break;
                }
            }
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            updateRequests?.Clear();
        }
    }
}