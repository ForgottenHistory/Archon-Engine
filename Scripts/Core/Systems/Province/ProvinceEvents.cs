namespace Core.Systems
{
    /// <summary>
    /// Province-related events for EventBus
    /// Extracted from ProvinceSystem.cs for better organization
    /// </summary>

    public struct ProvinceSystemInitializedEvent : IGameEvent
    {
        public int ProvinceCount;
        public bool HasDefinitions;
        public float TimeStamp { get; set; }
    }

    public struct ProvinceOwnershipChangedEvent : IGameEvent
    {
        public ushort ProvinceId;
        public ushort OldOwner;
        public ushort NewOwner;
        public float TimeStamp { get; set; }
    }

    public struct ProvinceDevelopmentChangedEvent : IGameEvent
    {
        public ushort ProvinceId;
        public byte OldDevelopment;
        public byte NewDevelopment;
        public float TimeStamp { get; set; }
    }

    public struct ProvinceInitialStatesLoadedEvent : IGameEvent
    {
        public int LoadedCount;
        public int FailedCount;
        public float TimeStamp { get; set; }
    }
}
