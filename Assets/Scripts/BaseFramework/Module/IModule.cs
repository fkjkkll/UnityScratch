using System;

namespace BaseFramework.Module
{
    /// <summary>
    /// 模块仓库管理的最小生命周期契约。仓库关闭时会统一调用 <see cref="IDisposable.Dispose"/>。
    /// </summary>
    public interface IModule : IDisposable
    {
    }
}
