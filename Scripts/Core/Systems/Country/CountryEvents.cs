using UnityEngine;

namespace Core.Systems
{
    /// <summary>
    /// Country-related events for EventBus
    /// Extracted from CountrySystem.cs for better organization
    /// </summary>

    public struct CountrySystemInitializedEvent : IGameEvent
    {
        public int CountryCount;
        public float TimeStamp { get; set; }
    }

    public struct CountryColorChangedEvent : IGameEvent
    {
        public ushort CountryId;
        public Color32 OldColor;
        public Color32 NewColor;
        public float TimeStamp { get; set; }
    }
}
