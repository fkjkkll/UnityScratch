using BaseFramework.Module;
using UnityEngine;

namespace GameLogic.Logic.ModuleDriver
{
    /// <summary>
    /// 将 Unity 帧循环转发给模块仓库，并确保整个游戏生命周期内只有一个跨场景驱动器。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class ModuleRepositoryDriver : MonoBehaviour
    {
        private static ModuleRepositoryDriver _primaryInstance;

        private bool _hasInitiatedShutdown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPrimaryInstance()
        {
            // Player 启动和编辑器播放会话开始时执行；主要用于兼容关闭 Domain Reload 的情况。
            _primaryInstance = null;
        }

        private void Awake()
        {
            if (_primaryInstance != null && _primaryInstance != this)
            {
                // 重复驱动器从未拥有仓库，因此销毁时不能触发全局 Shutdown。
                Destroy(gameObject);
                return;
            }

            _primaryInstance = this;
            // 模块生命周期跟随游戏进程，而不是跟随创建驱动器的场景。
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (_primaryInstance == this && !_hasInitiatedShutdown && !ModuleRepository.HasShutdownStarted)
                ModuleRepository.Update();
        }

        private void LateUpdate()
        {
            if (_primaryInstance == this && !_hasInitiatedShutdown && !ModuleRepository.HasShutdownStarted)
                ModuleRepository.LateUpdate();
        }

        private void OnApplicationQuit()
        {
            // 正常退出路径会先收到该回调，随后销毁对象时通常还会收到 OnDestroy。
            if (_primaryInstance == this)
                Shutdown();
        }

        private void OnDestroy()
        {
            // 同时覆盖编辑器停止播放或主驱动器被意外销毁等非正常退出路径。
            if (_primaryInstance != this)
                return;

            _primaryInstance = null;
            Shutdown();
        }

        private void Shutdown()
        {
            // OnApplicationQuit 与 OnDestroy 可以连续到达，只允许主驱动器发起一次关闭。
            if (_hasInitiatedShutdown)
                return;

            _hasInitiatedShutdown = true;
            ModuleRepository.Shutdown();
        }
    }
}
