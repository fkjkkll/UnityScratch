using System;

namespace BaseFramework.EventBus
{
    public delegate void EventCallback<in T>(T eventData) where T : IGameEvent;
}
