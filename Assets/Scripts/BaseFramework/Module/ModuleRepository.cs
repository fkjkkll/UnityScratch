using System;
using System.Collections.Generic;
using UnityEngine;
using BaseFramework.System;

namespace BaseFramework.Module
{
    /// <summary>
    /// 以具体类型为键保存游戏全生命周期模块，并统一驱动更新和关闭流程。
    /// 这是轻量级模块仓库，不负责接口映射、构造函数注入或作用域管理。
    /// </summary>
    public static class ModuleRepository
    {
        private const string ShutdownMessage = "The module repository has been shut down.";
        private const string ShutdownRequestedMessage = "The module repository is shutting down.";

        private static readonly List<IModuleRegistration> _registrations = new(8);
        private static readonly List<IModuleRegistration> _updateRegistrations = new(8);
        private static readonly List<IModuleRegistration> _lateUpdateRegistrations = new(8);
        private static readonly List<IModuleRegistration> _pendingActivations = new(8);

        private static bool _isTicking;
        private static bool _shutdownRequested;
        private static bool _isShutdown;

        /// <summary>
        /// 仓库是否已经请求或完成关闭。该状态一旦开始，在本次游戏生命周期内不会恢复。
        /// </summary>
        public static bool HasShutdownStarted => _shutdownRequested || _isShutdown;

        /// <summary>
        /// 注册一个已有模块实例。同一种具体模块类型只能注册一次。
        /// </summary>
        /// <exception cref="ArgumentNullException">模块实例为 null。</exception>
        /// <exception cref="InvalidOperationException">仓库正在关闭，或该类型已经注册、正在创建。</exception>
        public static void Register<T>(T module) where T : class, IModule, new()
        {
            EnsureRunning();

            if (module == null)
                throw new ArgumentNullException(nameof(module));
            if (ModuleSlot<T>.Instance != null)
                throw new InvalidOperationException($"Module {typeof(T).FullName} is already registered.");
            if (ModuleSlot<T>.IsCreationInProgress)
                throw new InvalidOperationException($"Module {typeof(T).FullName} is currently being created.");

            RegisterCore(module);
        }

        /// <summary>
        /// 获取指定类型的模块；尚未注册时通过公共无参构造函数创建并注册。
        /// </summary>
        /// <exception cref="InvalidOperationException">仓库正在关闭，或检测到模块循环创建。</exception>
        public static T Get<T>() where T : class, IModule, new()
        {
            EnsureRunning();

            var module = ModuleSlot<T>.Instance;
            if (module != null)
                return module;

            if (ModuleSlot<T>.IsCreationInProgress)
                throw new InvalidOperationException($"Circular creation detected for module {typeof(T).FullName}.");

            // 创建标记按具体 T 独立保存，可以检测 A -> B -> A 形式的循环创建。
            ModuleSlot<T>.IsCreationInProgress = true;
            try
            {
                module = new T();
                RegisterCore(module);
                return module;
            }
            finally
            {
                ModuleSlot<T>.IsCreationInProgress = false;
            }
        }

        /// <summary>
        /// 查询指定类型的已注册模块，不会隐式创建；仓库关闭后的清理代码也可以安全调用。
        /// </summary>
        public static bool TryGet<T>(out T module) where T : class, IModule, new()
        {
            module = ModuleSlot<T>.Instance;
            return module != null;
        }

        /// <summary>
        /// 激活上一阶段新注册的模块，并按注册顺序执行 Update。
        /// </summary>
        public static void Update()
        {
            EnsureRunning();
            BeginTick();
            try
            {
                ActivatePendingModules();
                ExecuteUpdates();
            }
            finally
            {
                EndTick();
            }
        }

        /// <summary>
        /// 按注册顺序执行 LateUpdate。新模块必须先由一次仓库 Update 激活才会进入该阶段。
        /// </summary>
        public static void LateUpdate()
        {
            EnsureRunning();
            BeginTick();
            try
            {
                ExecuteLateUpdates();
            }
            finally
            {
                EndTick();
            }
        }

        /// <summary>
        /// 逆注册顺序释放所有模块。关闭具有幂等性，并且在本次游戏生命周期内不可逆。
        /// </summary>
        public static void Shutdown()
        {
            if (_isShutdown || _shutdownRequested)
                return;

            if (_isTicking)
            {
                // 允许当前模块正常返回，再由 EndTick 的 finally 路径完成关闭。
                _shutdownRequested = true;
                return;
            }

            ShutdownCore();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            // Domain Reload 关闭时静态字段不会自动重置，因此每次播放会话都主动清理。
            ResetState();
        }

        internal static void ResetForTests()
        {
            ResetState();
        }

        private static void RegisterCore<T>(T module) where T : class, IModule, new()
        {
            var registration = new ModuleRegistration<T>(module);
            ModuleSlot<T>.Instance = module;

            try
            {
                _registrations.Add(registration);
                // 更新列表在遍历期间保持稳定，新模块统一在下一次 Update 开始时激活。
                if (registration.UpdatableModule != null || registration.LateUpdatableModule != null)
                    _pendingActivations.Add(registration);
            }
            catch
            {
                _pendingActivations.Remove(registration);
                _registrations.Remove(registration);
                ModuleSlot<T>.Instance = null;
                throw;
            }
        }

        private static void ActivatePendingModules()
        {
            if (_pendingActivations.Count == 0)
                return;

            foreach (var registration in _pendingActivations)
            {
                // 接口视角已在注册时缓存，帧循环中无需重复进行类型判断和转换。
                if (registration.UpdatableModule != null)
                    _updateRegistrations.Add(registration);
                if (registration.LateUpdatableModule != null)
                    _lateUpdateRegistrations.Add(registration);
            }

            _pendingActivations.Clear();
        }

        private static void ExecuteUpdates()
        {
            for (var i = 0; i < _updateRegistrations.Count && !_shutdownRequested; i++)
            {
                var registration = _updateRegistrations[i];
                try
                {
                    registration.UpdatableModule.Update();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    UnscheduleFaultedModule(registration);
                    // Remove 会让后续元素左移，回退索引以免跳过下一个模块。
                    i--;
                }
            }
        }

        private static void ExecuteLateUpdates()
        {
            for (var i = 0; i < _lateUpdateRegistrations.Count && !_shutdownRequested; i++)
            {
                var registration = _lateUpdateRegistrations[i];
                try
                {
                    registration.LateUpdatableModule.LateUpdate();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    UnscheduleFaultedModule(registration);
                    // Remove 会让后续元素左移，回退索引以免跳过下一个模块。
                    i--;
                }
            }
        }

        private static void UnscheduleFaultedModule(IModuleRegistration registration)
        {
            // 只停止后续更新，实例仍保持注册，以便关闭时统一释放资源。
            _updateRegistrations.Remove(registration);
            _lateUpdateRegistrations.Remove(registration);
            _pendingActivations.Remove(registration);
        }

        private static void BeginTick()
        {
            if (_isTicking)
                throw new InvalidOperationException("The module repository cannot be ticked recursively.");

            _isTicking = true;
        }

        private static void EndTick()
        {
            _isTicking = false;
            if (_shutdownRequested)
                // Shutdown 可能由模块回调发起，必须等当前调用栈退出后再释放模块。
                ShutdownCore();
        }

        private static void ShutdownCore()
        {
            // 先进入关闭状态，防止 Dispose 期间的业务代码重新创建模块。
            _isShutdown = true;
            _shutdownRequested = false;
            DisposeModules();
        }

        private static void ResetState()
        {
            _isTicking = false;
            // 重置期间同样拒绝创建模块，避免 Dispose 回调污染下一次播放会话。
            _isShutdown = true;
            _shutdownRequested = false;
            try
            {
                DisposeModules();
            }
            finally
            {
                _isTicking = false;
                _isShutdown = false;
            }
        }

        private static void DisposeModules()
        {
            try
            {
                // 依赖通常按照注册顺序建立，逆序释放可以先销毁后注册的上层模块。
                for (var i = _registrations.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _registrations[i].Module.Dispose();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                // 即使某个 Dispose 抛出异常，也必须清空所有泛型槽位和调度集合。
                for (var i = _registrations.Count - 1; i >= 0; i--)
                    _registrations[i].Clear();

                _pendingActivations.Clear();
                _updateRegistrations.Clear();
                _lateUpdateRegistrations.Clear();
                _registrations.Clear();
            }
        }

        private static void EnsureRunning()
        {
            if (_isShutdown)
                throw new InvalidOperationException(ShutdownMessage);
            if (_shutdownRequested)
                throw new InvalidOperationException(ShutdownRequestedMessage);
        }

        private interface IModuleRegistration
        {
            IModule Module { get; }
            IUpdatableModule UpdatableModule { get; }
            ILateUpdatableModule LateUpdatableModule { get; }
            void Clear();
        }

        /// <summary>
        /// 把不同具体类型的模块适配到统一集合，并保留 T 以便关闭时清空对应泛型槽位。
        /// </summary>
        private sealed class ModuleRegistration<T> : IModuleRegistration
            where T : class, IModule, new()
        {
            public ModuleRegistration(T module)
            {
                Module = module;
                UpdatableModule = module as IUpdatableModule;
                LateUpdatableModule = module as ILateUpdatableModule;
            }

            public IModule Module { get; }
            public IUpdatableModule UpdatableModule { get; }
            public ILateUpdatableModule LateUpdatableModule { get; }

            public void Clear()
            {
                ModuleSlot<T>.Instance = null;
                ModuleSlot<T>.IsCreationInProgress = false;
            }
        }

        /// <summary>
        /// 每个封闭泛型类型都拥有独立静态字段，用于无字典查询模块并记录创建状态。
        /// </summary>
        private static class ModuleSlot<T> where T : class, IModule, new()
        {
            public static T Instance;
            public static bool IsCreationInProgress;
        }
    }
}
