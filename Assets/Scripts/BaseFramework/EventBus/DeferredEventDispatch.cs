namespace BaseFramework.EventBus
{
    internal interface IDeferredEventDispatch
    {
        void Dispatch();
    }

    internal sealed class DeferredEventDispatch<T> : IDeferredEventDispatch where T : IGameEvent
    {
        private readonly EventChannel<T> _channel;
        private readonly T _eventData;

        public DeferredEventDispatch(EventChannel<T> channel, T eventData)
        {
            _channel = channel;
            _eventData = eventData;
        }

        public void Dispatch()
        {
            _channel.Fire(_eventData);
        }
    }
}
