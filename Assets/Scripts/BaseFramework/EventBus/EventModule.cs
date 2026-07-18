using System;
using System.Collections.Generic;
using BaseFramework.Module;
using BaseFramework.System;

namespace BaseFramework.EventBus
{
    public enum EventDispatchPhase
    {
        Update,
        LateUpdate,
    }

    public sealed class EventModule : IModule, IUpdatableModule, ILateUpdatableModule
    {
        private readonly Dictionary<Type, IEventChannel> _channels = new(8);
        private readonly Queue<IDeferredEventDispatch> _updateDispatchQueue = new(8);
        private readonly Queue<IDeferredEventDispatch> _lateUpdateDispatchQueue = new(8);

        private bool _isDisposed;

        private static EventModule GetOrCreateInstance()
        {
            return ModuleRepository.Get<EventModule>();
        }

        public static void Subscribe<T>(EventCallback<T> callback) where T : IGameEvent
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            GetOrCreateInstance().GetOrCreateChannel<T>().Subscribe(callback);
        }

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

        public static void Fire<T>(T eventData) where T : IGameEvent
        {
            if (ModuleRepository.HasShutdownStarted)
                return;
            if (!ModuleRepository.TryGet<EventModule>(out var module) || module._isDisposed)
                return;
            if (module.TryGetChannel<T>(out var channel))
                channel.Fire(eventData);
        }

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

            queue.Enqueue(new DeferredEventDispatch<T>(GetOrCreateChannel<T>(), eventData));
        }

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

            foreach (var channel in _channels.Values)
                channel.Clear();

            _channels.Clear();
        }

        private EventChannel<T> GetOrCreateChannel<T>() where T : IGameEvent
        {
            EnsureNotDisposed();

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
