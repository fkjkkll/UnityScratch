using System;
using System.Collections.Generic;
using UnityEngine;
using BaseFramework.System;

namespace BaseFramework.Module
{
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

        public static bool HasShutdownStarted => _shutdownRequested || _isShutdown;

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

        public static T Get<T>() where T : class, IModule, new()
        {
            EnsureRunning();

            var module = ModuleSlot<T>.Instance;
            if (module != null)
                return module;

            if (ModuleSlot<T>.IsCreationInProgress)
                throw new InvalidOperationException($"Circular creation detected for module {typeof(T).FullName}.");

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

        public static bool TryGet<T>(out T module) where T : class, IModule, new()
        {
            module = ModuleSlot<T>.Instance;
            return module != null;
        }

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

        public static void Shutdown()
        {
            if (_isShutdown || _shutdownRequested)
                return;

            if (_isTicking)
            {
                _shutdownRequested = true;
                return;
            }

            ShutdownCore();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
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
                    i--;
                }
            }
        }

        private static void UnscheduleFaultedModule(IModuleRegistration registration)
        {
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
                ShutdownCore();
        }

        private static void ShutdownCore()
        {
            _isShutdown = true;
            _shutdownRequested = false;
            DisposeModules();
        }

        private static void ResetState()
        {
            _isTicking = false;
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

        private static class ModuleSlot<T> where T : class, IModule, new()
        {
            public static T Instance;
            public static bool IsCreationInProgress;
        }
    }
}
