using Core;

namespace StarterKit
{
    /// <summary>
    /// Fired when player selects a country and clicks Start.
    /// Game layer can subscribe to this to initialize game-specific systems.
    /// </summary>
    public struct PlayerCountrySelectedEvent : IGameEvent
    {
        public ushort CountryId;
        public float TimeStamp { get; set; }
    }
}
