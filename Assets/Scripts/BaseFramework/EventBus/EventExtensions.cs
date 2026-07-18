namespace BaseFramework.EventBus
{
    /// <summary>
    /// 为事件数据提供便捷派发语法；扩展方法本身不保存任何事件状态。
    /// 泛型接口约束拓展方法：过程中不会产生装箱（不会真把结构体转为接口
    /// </summary>
    public static class EventExtensions
    {
        /// <summary>
        /// 立即派发事件。
        /// </summary>
        public static void Fire<T>(this T eventData) where T : IGameEvent
        {
            EventModule.Fire(eventData);
        }

        /// <summary>
        /// 将事件加入指定生命周期阶段的延迟派发队列。
        /// </summary>
        public static void FireDeferred<T>(this T eventData, EventDispatchPhase phase) where T : IGameEvent
        {
            EventModule.FireDeferred(eventData, phase);
        }
    }
}
