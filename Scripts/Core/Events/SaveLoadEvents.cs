namespace Core.Systems
{
    /// <summary>
    /// Event emitted when a game is successfully loaded from a save file
    /// UI systems should listen to this and refresh their displays
    /// </summary>
    public struct GameLoadedEvent : IGameEvent
    {
        public string SaveName;
        public ulong CurrentTick;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Event emitted when a game is successfully saved to a file
    /// </summary>
    public struct GameSavedEvent : IGameEvent
    {
        public string SaveName;
        public ulong CurrentTick;
        public float TimeStamp { get; set; }
    }
}
