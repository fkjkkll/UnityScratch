using System;
using System.Collections.Generic;
using UnityEngine;

namespace BaseFramework.EventBus
{
    internal interface IEventChannel
    {
        void Clear();
    }

    internal sealed class EventChannel<T> : IEventChannel where T : IGameEvent
    {
        private readonly List<EventCallback<T>> _subscribers = new();
        private readonly List<SubscriptionChange> _pendingChanges = new();

        private int _dispatchDepth;

        public void Subscribe(EventCallback<T> subscriber)
        {
            if (IsSubscribedAfterPendingChanges(subscriber))
                throw new InvalidOperationException($"Callback is already subscribed to event {typeof(T).FullName}.");

            if (_dispatchDepth == 0)
                _subscribers.Add(subscriber);
            else
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

            _dispatchDepth++;
            try
            {
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

            _pendingChanges.Add(SubscriptionChange.Clear());
        }

        private bool IsSubscribedAfterPendingChanges(EventCallback<T> subscriber)
        {
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
