using Core.Data;
using Core.Systems;

namespace Core.Resources
{
    /// <summary>
    /// ENGINE: Event fired when a resource amount changes for a country.
    /// Subscribe via EventBus for reactive UI updates.
    /// </summary>
    public struct ResourceChangedEvent : IGameEvent
    {
        /// <summary>The country whose resource changed</summary>
        public ushort CountryId;

        /// <summary>The resource type that changed</summary>
        public ushort ResourceId;

        /// <summary>Previous amount</summary>
        public FixedPoint64 OldAmount;

        /// <summary>New amount</summary>
        public FixedPoint64 NewAmount;

        /// <summary>Event timestamp (auto-set by EventBus)</summary>
        public float TimeStamp { get; set; }

        /// <summary>The difference (NewAmount - OldAmount)</summary>
        public FixedPoint64 Delta => NewAmount - OldAmount;

        /// <summary>True if resource increased</summary>
        public bool IsIncrease => NewAmount > OldAmount;

        /// <summary>True if resource decreased</summary>
        public bool IsDecrease => NewAmount < OldAmount;
    }

    /// <summary>
    /// ENGINE: Event fired when resources are transferred between countries.
    /// </summary>
    public struct ResourceTransferredEvent : IGameEvent
    {
        /// <summary>The country that sent the resource</summary>
        public ushort FromCountryId;

        /// <summary>The country that received the resource</summary>
        public ushort ToCountryId;

        /// <summary>The resource type transferred</summary>
        public ushort ResourceId;

        /// <summary>Amount transferred</summary>
        public FixedPoint64 Amount;

        /// <summary>Event timestamp (auto-set by EventBus)</summary>
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// ENGINE: Event fired when batch mode ends (single event instead of many individual changes).
    /// Contains summary information about what changed during the batch.
    /// </summary>
    public struct ResourceBatchCompletedEvent : IGameEvent
    {
        /// <summary>Number of individual changes that occurred during the batch</summary>
        public int ChangeCount;

        /// <summary>Event timestamp (auto-set by EventBus)</summary>
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// ENGINE: Event fired when ResourceSystem is initialized.
    /// </summary>
    public struct ResourceSystemInitializedEvent : IGameEvent
    {
        /// <summary>Number of countries supported</summary>
        public int MaxCountries;

        /// <summary>Event timestamp (auto-set by EventBus)</summary>
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// ENGINE: Event fired when a new resource type is registered.
    /// </summary>
    public struct ResourceRegisteredEvent : IGameEvent
    {
        /// <summary>The resource ID that was registered</summary>
        public ushort ResourceId;

        /// <summary>The resource string identifier</summary>
        public string ResourceStringId;

        /// <summary>Event timestamp (auto-set by EventBus)</summary>
        public float TimeStamp { get; set; }
    }
}
