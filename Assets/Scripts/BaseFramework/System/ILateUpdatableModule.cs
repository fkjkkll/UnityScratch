namespace BaseFramework.System
{
    /// <summary>
    /// 表示模块需要参与由 <c>ModuleRepository</c> 驱动的 LateUpdate 阶段。
    /// </summary>
    public interface ILateUpdatableModule
    {
        void LateUpdate();
    }
}
