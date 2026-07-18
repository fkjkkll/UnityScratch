using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BaseFramework.Module;
using BaseFramework.System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BaseFramework.Tests
{
    public sealed class ModuleRepositoryTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetWithoutLogFailure();
            CountingModule.Reset();
            DisposeOrder.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ResetWithoutLogFailure();
        }

        [Test]
        public void Get_CreatesOneModulePerType()
        {
            var first = ModuleRepository.Get<CountingModule>();
            var second = ModuleRepository.Get<CountingModule>();

            Assert.That(second, Is.SameAs(first));
            Assert.That(ModuleRepository.TryGet<CountingModule>(out var found), Is.True);
            Assert.That(found, Is.SameAs(first));
        }

        [Test]
        public void Register_RejectsNullAndDuplicateModules()
        {
            Assert.Throws<ArgumentNullException>(() => ModuleRepository.Register<CountingModule>(null));

            var original = new CountingModule();
            ModuleRepository.Register(original);

            Assert.Throws<InvalidOperationException>(
                () => ModuleRepository.Register(new CountingModule()));
            Assert.That(ModuleRepository.Get<CountingModule>(), Is.SameAs(original));
        }

        [Test]
        public void PendingModule_StartsWithUpdateBeforeLateUpdate()
        {
            ModuleRepository.Get<CountingModule>();

            ModuleRepository.LateUpdate();
            Assert.That(CountingModule.LateUpdateCount, Is.Zero);

            ModuleRepository.Update();
            ModuleRepository.LateUpdate();

            Assert.That(CountingModule.UpdateCount, Is.EqualTo(1));
            Assert.That(CountingModule.LateUpdateCount, Is.EqualTo(1));
        }

        [Test]
        public void ModuleCreatedDuringUpdate_StartsOnNextFrame()
        {
            ModuleRepository.Get<RegisteringModule>();

            ModuleRepository.Update();
            ModuleRepository.LateUpdate();
            Assert.That(CountingModule.UpdateCount, Is.Zero);
            Assert.That(CountingModule.LateUpdateCount, Is.Zero);

            ModuleRepository.Update();
            ModuleRepository.LateUpdate();
            Assert.That(CountingModule.UpdateCount, Is.EqualTo(1));
            Assert.That(CountingModule.LateUpdateCount, Is.EqualTo(1));
        }

        [Test]
        public void FaultedModule_IsRemovedFromBothPhasesAndDisposedAtShutdown()
        {
            var throwing = ModuleRepository.Get<ThrowingUpdatableModule>();
            ModuleRepository.Get<CountingModule>();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: update failure"));

            ModuleRepository.Update();
            ModuleRepository.LateUpdate();
            ModuleRepository.Update();
            ModuleRepository.LateUpdate();

            Assert.That(throwing.UpdateCalls, Is.EqualTo(1));
            Assert.That(throwing.LateUpdateCalls, Is.Zero);
            Assert.That(CountingModule.UpdateCount, Is.EqualTo(2));
            Assert.That(CountingModule.LateUpdateCount, Is.EqualTo(2));

            ModuleRepository.Shutdown();
            Assert.That(throwing.IsDisposed, Is.True);
        }

        [Test]
        public void ShutdownRequestedDuringTick_WaitsForCurrentModuleAndStopsLaterModules()
        {
            var requester = ModuleRepository.Get<ShutdownRequestingModule>();
            ModuleRepository.Get<CountingModule>();

            ModuleRepository.Update();

            Assert.That(requester.WasDisposedBeforeUpdateReturned, Is.False);
            Assert.That(requester.ContinuedAfterRequest, Is.True);
            Assert.That(requester.IsDisposed, Is.True);
            Assert.That(CountingModule.UpdateCount, Is.Zero);
            Assert.That(ModuleRepository.HasShutdownStarted, Is.True);
        }

        [Test]
        public void Shutdown_IsTerminalAndIdempotent()
        {
            ModuleRepository.Get<CountingModule>();

            ModuleRepository.Shutdown();

            Assert.That(ModuleRepository.HasShutdownStarted, Is.True);
            Assert.That(ModuleRepository.TryGet<CountingModule>(out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => ModuleRepository.Get<CountingModule>());
            Assert.Throws<InvalidOperationException>(
                () => ModuleRepository.Register(new CountingModule()));
            Assert.Throws<InvalidOperationException>(ModuleRepository.Update);
            Assert.Throws<InvalidOperationException>(ModuleRepository.LateUpdate);
            Assert.DoesNotThrow(ModuleRepository.Shutdown);
        }

        [Test]
        public void Shutdown_UsesReverseOrderAndContinuesAfterDisposeException()
        {
            ModuleRepository.Get<FirstDisposeModule>();
            ModuleRepository.Get<ThrowingDisposeModule>();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: dispose failure"));

            ModuleRepository.Shutdown();

            CollectionAssert.AreEqual(new[] { "throwing", "first" }, DisposeOrder);
        }

        [Test]
        public void ResetForTests_ReopensRepositoryWithFreshModules()
        {
            var original = ModuleRepository.Get<CountingModule>();
            ModuleRepository.Shutdown();

            ModuleRepository.ResetForTests();
            var replacement = ModuleRepository.Get<CountingModule>();

            Assert.That(ModuleRepository.HasShutdownStarted, Is.False);
            Assert.That(replacement, Is.Not.SameAs(original));
        }

        private static readonly List<string> DisposeOrder = new();

        private static void ResetWithoutLogFailure()
        {
            var previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                ModuleRepository.ResetForTests();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        public sealed class CountingModule : IModule, IUpdatableModule, ILateUpdatableModule
        {
            public static int UpdateCount { get; private set; }
            public static int LateUpdateCount { get; private set; }

            public static void Reset()
            {
                UpdateCount = 0;
                LateUpdateCount = 0;
            }

            public void Update()
            {
                UpdateCount++;
            }

            public void LateUpdate()
            {
                LateUpdateCount++;
            }

            public void Dispose()
            {
            }
        }

        public sealed class RegisteringModule : IModule, IUpdatableModule
        {
            public void Update()
            {
                ModuleRepository.Get<CountingModule>();
            }

            public void Dispose()
            {
            }
        }

        public sealed class ThrowingUpdatableModule : IModule, IUpdatableModule, ILateUpdatableModule
        {
            public int UpdateCalls { get; private set; }
            public int LateUpdateCalls { get; private set; }
            public bool IsDisposed { get; private set; }

            public void Update()
            {
                UpdateCalls++;
                throw new InvalidOperationException("update failure");
            }

            public void LateUpdate()
            {
                LateUpdateCalls++;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        public sealed class ShutdownRequestingModule : IModule, IUpdatableModule
        {
            public bool IsDisposed { get; private set; }
            public bool WasDisposedBeforeUpdateReturned { get; private set; }
            public bool ContinuedAfterRequest { get; private set; }

            public void Update()
            {
                ModuleRepository.Shutdown();
                WasDisposedBeforeUpdateReturned = IsDisposed;
                ContinuedAfterRequest = true;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        public sealed class FirstDisposeModule : IModule
        {
            public void Dispose()
            {
                DisposeOrder.Add("first");
            }
        }

        public sealed class ThrowingDisposeModule : IModule
        {
            public void Dispose()
            {
                DisposeOrder.Add("throwing");
                throw new InvalidOperationException("dispose failure");
            }
        }
    }
}
