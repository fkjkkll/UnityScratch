namespace BaseFramework.EventBus
{
    /// <summary>
    /// 为不同事件类型提供统一的延迟派发入口，使它们能够进入同一个 FIFO 队列。
    /// </summary>
    internal interface IDeferredEventDispatch
    {
        void Dispatch();
    }

    /// <summary>
    /// 同时保存强类型频道与事件数据，出队时无需反射、dynamic 或再次查询频道。
    /// </summary>
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
