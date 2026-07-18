using System;
using System.Collections.Generic;
using UnityEngine;

namespace BaseFramework.EventBus
{
    /// <summary>
    /// 为 EventModule 提供不依赖事件具体类型的统一清理入口。
    /// </summary>
    internal interface IEventChannel
    {
        void Clear();
    }

    /// <summary>
    /// 保存单一事件类型的订阅关系，并保证同步嵌套派发期间订阅视图稳定。
    /// </summary>
    internal sealed class EventChannel<T> : IEventChannel where T : IGameEvent
    {
        private readonly List<EventCallback<T>> _subscribers = new();
        // 派发期间不直接修改订阅者列表，避免索引错位以及每次派发创建快照数组。
        private readonly List<SubscriptionChange> _pendingChanges = new();

        private int _dispatchDepth;

        public void Subscribe(EventCallback<T> subscriber)
        {
            if (IsSubscribedAfterPendingChanges(subscriber))
                throw new InvalidOperationException($"Callback is already subscribed to event {typeof(T).FullName}.");

            if (_dispatchDepth == 0)
                _subscribers.Add(subscriber);
            else
                // 整条同步嵌套派发链共享同一份订阅视图。
                _pendingChanges.Add(SubscriptionChange.Subscribe(subscriber));
        }

        public void Unsubscribe(EventCallback<T> subscriber)
        {
            if (!IsSubscribedAfterPendingChanges(subscriber))
                return;

            if (_dispatchDepth == 0)
                _subscribers.Remove(subscriber);
            else
                _pendingChanges.Add(SubscriptionChange.Unsubscribe(subscriber));
        }

        public void Fire(T eventData)
        {
            if (_subscribers.Count == 0)
                return;

            // 深度计数覆盖同步嵌套 Fire，只有最外层退出时才能应用订阅变更。
            _dispatchDepth++;
            try
            {
                // 订阅变更已经延迟，派发期间该列表的数量和顺序保持稳定。
                var count = _subscribers.Count;
                for (var i = 0; i < count; i++)
                {
                    try
                    {
                        _subscribers[i].Invoke(eventData);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0)
                    // 即使回调抛出异常，也通过 finally 保证待处理变更最终生效。
                    ApplyPendingChanges();
            }
        }

        public void Clear()
        {
            if (_dispatchDepth == 0)
            {
                _subscribers.Clear();
                _pendingChanges.Clear();
                return;
            }

            // Dispose 理论上不会与回调并发；仍保留延迟清理语义以维护列表遍历安全。
            _pendingChanges.Add(SubscriptionChange.Clear());
        }

        private bool IsSubscribedAfterPendingChanges(EventCallback<T> subscriber)
        {
            // 计算所有待处理操作应用后的最终状态，使重复订阅能够立即报错。
            var isSubscribed = _subscribers.Contains(subscriber);
            foreach (var change in _pendingChanges)
            {
                switch (change.Kind)
                {
                    case SubscriptionChangeKind.Subscribe when change.Subscriber == subscriber:
                        isSubscribed = true;
                        break;
                    case SubscriptionChangeKind.Unsubscribe when change.Subscriber == subscriber:
                    case SubscriptionChangeKind.Clear:
                        isSubscribed = false;
                        break;
                }
            }

            return isSubscribed;
        }

        private void ApplyPendingChanges()
        {
            // 必须保持调用顺序，例如“取消后重新订阅”与“重新订阅后取消”结果不同。
            foreach (var change in _pendingChanges)
            {
                switch (change.Kind)
                {
                    case SubscriptionChangeKind.Subscribe:
                        _subscribers.Add(change.Subscriber);
                        break;
                    case SubscriptionChangeKind.Unsubscribe:
                        _subscribers.Remove(change.Subscriber);
                        break;
                    case SubscriptionChangeKind.Clear:
                        _subscribers.Clear();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            _pendingChanges.Clear();
        }

        private enum SubscriptionChangeKind
        {
            Subscribe,
            Unsubscribe,
            Clear,
        }

        private readonly struct SubscriptionChange
        {
            private SubscriptionChange(SubscriptionChangeKind kind, EventCallback<T> subscriber)
            {
                Kind = kind;
                Subscriber = subscriber;
            }

            public SubscriptionChangeKind Kind { get; }
            public EventCallback<T> Subscriber { get; }

            public static SubscriptionChange Subscribe(EventCallback<T> subscriber)
            {
                return new SubscriptionChange(SubscriptionChangeKind.Subscribe, subscriber);
            }

            public static SubscriptionChange Unsubscribe(EventCallback<T> subscriber)
            {
                return new SubscriptionChange(SubscriptionChangeKind.Unsubscribe, subscriber);
            }

            public static SubscriptionChange Clear()
            {
                return new SubscriptionChange(SubscriptionChangeKind.Clear, null);
            }
        }
    }
}
