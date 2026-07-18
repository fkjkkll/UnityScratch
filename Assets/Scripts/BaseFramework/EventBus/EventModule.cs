using System;
using System.Collections.Generic;
using BaseFramework.Module;
using BaseFramework.System;

namespace BaseFramework.EventBus
{
    /// <summary>
    /// 延迟事件进入的 Unity 生命周期阶段。
    /// </summary>
    public enum EventDispatchPhase
    {
        /// <summary>在 ModuleRepository.Update 中派发。（常规业务逻辑下，一般是下一帧）</summary>
        Update,

        /// <summary>在 ModuleRepository.LateUpdate 中派发。（常规业务逻辑下，一般是当前帧）</summary>
        LateUpdate,
    }

    /// <summary>
    /// 持有全部事件频道、订阅关系和延迟队列，并通过静态门面向业务代码提供事件 API。
    /// </summary>
    public sealed class EventModule : IModule, IUpdatableModule, ILateUpdatableModule
    {
        private readonly Dictionary<Type, IEventChannel> _channels = new(8);
        private readonly Queue<IDeferredEventDispatch> _updateDispatchQueue = new(8);
        private readonly Queue<IDeferredEventDispatch> _lateUpdateDispatchQueue = new(8);

        private bool _isDisposed;

        private static EventModule GetOrCreateInstance()
        {
            // 只有确实需要保存状态的操作才创建模块；普通 Fire 不会隐式创建。
            return ModuleRepository.Get<EventModule>();
        }

        /// <summary>
        /// 订阅指定类型的事件。同一个回调不能重复订阅。
        /// </summary>
        /// <exception cref="ArgumentNullException">回调为 null。</exception>
        /// <exception cref="InvalidOperationException">回调已经订阅，或模块仓库已经开始关闭。</exception>
        public static void Subscribe<T>(EventCallback<T> callback) where T : IGameEvent
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            GetOrCreateInstance().GetOrCreateChannel<T>().Subscribe(callback);
        }

        /// <summary>
        /// 取消订阅。模块或频道不存在时不产生效果，也不会隐式创建事件系统。
        /// </summary>
        public static void Unsubscribe<T>(EventCallback<T> callback) where T : IGameEvent
        {
            if (ModuleRepository.HasShutdownStarted)
                return;
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (!ModuleRepository.TryGet<EventModule>(out var module) || module._isDisposed)
                return;
            if (module.TryGetChannel<T>(out var channel))
                channel.Unsubscribe(callback);
        }

        /// <summary>
        /// 立即派发事件。没有对应频道时不产生效果，也不会隐式创建事件系统。
        /// </summary>
        public static void Fire<T>(T eventData) where T : IGameEvent
        {
            if (ModuleRepository.HasShutdownStarted)
                return;
            if (!ModuleRepository.TryGet<EventModule>(out var module) || module._isDisposed)
                return;
            if (module.TryGetChannel<T>(out var channel))
                channel.Fire(eventData);
        }

        /// <summary>
        /// 将事件加入指定阶段的延迟派发队列，并保持同一阶段内的全局 FIFO 顺序。
        /// </summary>
        public static void FireDeferred<T>(T eventData, EventDispatchPhase phase) where T : IGameEvent
        {
            if (ModuleRepository.HasShutdownStarted)
                return;

            GetOrCreateInstance().Enqueue(eventData, phase);
        }

        private void Enqueue<T>(T eventData, EventDispatchPhase phase) where T : IGameEvent
        {
            EnsureNotDisposed();

            Queue<IDeferredEventDispatch> queue;
            switch (phase)
            {
                case EventDispatchPhase.Update:
                    queue = _updateDispatchQueue;
                    break;
                case EventDispatchPhase.LateUpdate:
                    queue = _lateUpdateDispatchQueue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown event phase.");
            }

            // 入队时保存强类型频道，出队时无需再次查询字典或恢复泛型类型。
            queue.Enqueue(new DeferredEventDispatch<T>(GetOrCreateChannel<T>(), eventData));
        }

        // 显式实现生命周期接口，避免业务代码直接驱动或释放 EventModule 实例。
        void IUpdatableModule.Update()
        {
            EnsureNotDisposed();
            DispatchCurrentBatch(_updateDispatchQueue);
        }

        void ILateUpdatableModule.LateUpdate()
        {
            EnsureNotDisposed();
            DispatchCurrentBatch(_lateUpdateDispatchQueue);
        }

        void IDisposable.Dispose()
        {
            DisposeCore();
        }

        private void DisposeCore()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _updateDispatchQueue.Clear();
            _lateUpdateDispatchQueue.Clear();

            // 频道由 EventModule 实例持有，释放模块即可一次性清除所有订阅关系。
            foreach (var channel in _channels.Values)
                channel.Clear();

            _channels.Clear();
        }

        private EventChannel<T> GetOrCreateChannel<T>() where T : IGameEvent
        {
            EnsureNotDisposed();

            // 字典维持频道的实例所有权，并允许 Dispose 枚举全部频道统一清理。
            var eventType = typeof(T);
            if (_channels.TryGetValue(eventType, out var channel))
                return (EventChannel<T>)channel;

            var createdChannel = new EventChannel<T>();
            _channels.Add(eventType, createdChannel);
            return createdChannel;
        }

        private bool TryGetChannel<T>(out EventChannel<T> channel) where T : IGameEvent
        {
            if (_channels.TryGetValue(typeof(T), out var registeredChannel))
            {
                channel = (EventChannel<T>)registeredChannel;
                return true;
            }

            channel = null;
            return false;
        }

        private void DispatchCurrentBatch(Queue<IDeferredEventDispatch> queue)
        {
            // 固定本阶段开始时的数量；回调向同一阶段新入队的事件留到下一帧。
            var count = queue.Count;
            for (var i = 0; i < count && !_isDisposed && queue.Count > 0; i++)
                queue.Dequeue().Dispatch();
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(EventModule));
        }
    }
}
