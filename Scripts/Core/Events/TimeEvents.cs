using Core.Systems;

namespace Core.Systems
{
    // Time-related events (all include tick for command synchronization)
    public struct HourlyTickEvent : IGameEvent
    {
        public GameTime GameTime;
        public ulong Tick;
        public float TimeStamp { get; set; }
    }

    public struct DailyTickEvent : IGameEvent
    {
        public GameTime GameTime;
        public ulong Tick;
        public float TimeStamp { get; set; }
    }

    public struct WeeklyTickEvent : IGameEvent
    {
        public GameTime GameTime;
        public ulong Tick;
        public float TimeStamp { get; set; }
    }

    public struct MonthlyTickEvent : IGameEvent
    {
        public GameTime GameTime;
        public ulong Tick;
        public float TimeStamp { get; set; }
    }

    public struct YearlyTickEvent : IGameEvent
    {
        public GameTime GameTime;
        public ulong Tick;
        public float TimeStamp { get; set; }
    }

    public struct TimeStateChangedEvent : IGameEvent
    {
        public bool IsPaused;
        public int GameSpeed;
        public float TimeStamp { get; set; }
    }

    public struct TimeChangedEvent : IGameEvent
    {
        public GameTime GameTime;
        public float TimeStamp { get; set; }
    }
}
