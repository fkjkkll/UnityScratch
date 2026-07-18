using System;

namespace BaseFramework.EventBus
{
    /// <summary>
    /// 强类型事件回调。逆变参数允许接收更宽泛事件类型的回调参与订阅。
    /// </summary>
    public delegate void EventCallback<in T>(T eventData) where T : IGameEvent;
}
