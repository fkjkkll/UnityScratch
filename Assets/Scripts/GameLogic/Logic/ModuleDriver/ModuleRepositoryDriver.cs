using BaseFramework.Module;
using UnityEngine;

namespace GameLogic.Logic.ModuleDriver
{
    [DefaultExecutionOrder(-10000)]
    public sealed class ModuleRepositoryDriver : MonoBehaviour
    {
        private static ModuleRepositoryDriver _primaryInstance;

        private bool _hasInitiatedShutdown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPrimaryInstance()
        {
            _primaryInstance = null;
        }

        private void Awake()
        {
            if (_primaryInstance != null && _primaryInstance != this)
            {
                Destroy(gameObject);
                return;
            }

            _primaryInstance = this;
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
            if (_primaryInstance == this)
                Shutdown();
        }

        private void OnDestroy()
        {
            if (_primaryInstance != this)
                return;

            _primaryInstance = null;
            Shutdown();
        }

        private void Shutdown()
        {
            if (_hasInitiatedShutdown)
                return;

            _hasInitiatedShutdown = true;
            ModuleRepository.Shutdown();
        }
    }
}
