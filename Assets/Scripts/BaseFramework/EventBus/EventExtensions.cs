namespace BaseFramework.EventBus
{
    public static class EventExtensions
    {
        public static void Fire<T>(this T eventData) where T : IGameEvent
        {
            EventModule.Fire(eventData);
        }

        public static void FireDeferred<T>(this T eventData, EventDispatchPhase phase) where T : IGameEvent
        {
            EventModule.FireDeferred(eventData, phase);
        }
    }
}
