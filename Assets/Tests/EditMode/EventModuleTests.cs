using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BaseFramework.EventBus;
using BaseFramework.Module;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BaseFramework.Tests
{
    public sealed class EventModuleTests
    {
        [SetUp]
        public void SetUp()
        {
            ModuleRepository.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ModuleRepository.ResetForTests();
        }

        [Test]
        public void FireWithoutSubscriber_DoesNotCreateEventModule()
        {
            Assert.DoesNotThrow(() => EventModule.Fire(new FirstEvent(1)));
            Assert.That(ModuleRepository.TryGet<EventModule>(out _), Is.False);
        }

        [Test]
        public void Subscribe_RejectsNullAndDuplicateCallbacks()
        {
            EventCallback<FirstEvent> callback = _ => { };

            Assert.Throws<ArgumentNullException>(
                () => EventModule.Subscribe<FirstEvent>(null));
            EventModule.Subscribe(callback);
            Assert.Throws<InvalidOperationException>(() => EventModule.Subscribe(callback));
        }

        [Test]
        public void Dispatch_DefersUnsubscribeUntilCurrentFireCompletes()
        {
            var received = new List<string>();
            EventCallback<FirstEvent> second = _ => received.Add("second");
            EventCallback<FirstEvent> first = _ =>
            {
                received.Add("first");
                EventModule.Unsubscribe(second);
            };
            EventModule.Subscribe(first);
            EventModule.Subscribe(second);

            EventModule.Fire(new FirstEvent(1));
            EventModule.Fire(new FirstEvent(2));

            CollectionAssert.AreEqual(new[] { "first", "second", "first" }, received);
        }

        [Test]
        public void NestedDispatch_AppliesChangesAfterOutermostFireCompletes()
        {
            var received = new List<string>();
            EventCallback<FirstEvent> second = eventData => received.Add($"second:{eventData.Value}");
            EventCallback<FirstEvent> first = eventData =>
            {
                received.Add($"first:{eventData.Value}");
                if (eventData.Value != 1)
                    return;

                EventModule.Unsubscribe(second);
                EventModule.Fire(new FirstEvent(2));
            };
            EventModule.Subscribe(first);
            EventModule.Subscribe(second);

            EventModule.Fire(new FirstEvent(1));
            EventModule.Fire(new FirstEvent(3));

            CollectionAssert.AreEqual(
                new[] { "first:1", "first:2", "second:2", "second:1", "first:3" },
                received);
        }

        [Test]
        public void PendingChanges_PreserveOrderAndDuplicateRules()
        {
            var received = new List<string>();
            var duplicateWasRejected = false;
            EventCallback<FirstEvent> second = eventData => received.Add($"second:{eventData.Value}");
            EventCallback<FirstEvent> first = eventData =>
            {
                received.Add($"first:{eventData.Value}");
                if (eventData.Value != 1)
                    return;

                EventModule.Unsubscribe(second);
                EventModule.Subscribe(second);
                try
                {
                    EventModule.Subscribe(second);
                }
                catch (InvalidOperationException)
                {
                    duplicateWasRejected = true;
                }

                EventModule.Unsubscribe(second);
                EventModule.Unsubscribe(second);
            };
            EventModule.Subscribe(first);
            EventModule.Subscribe(second);

            EventModule.Fire(new FirstEvent(1));
            EventModule.Fire(new FirstEvent(2));

            Assert.That(duplicateWasRejected, Is.True);
            CollectionAssert.AreEqual(
                new[] { "first:1", "second:1", "first:2" },
                received);
        }

        [Test]
        public void CallbackException_DoesNotBlockOtherSubscribers()
        {
            var callCount = 0;
            EventModule.Subscribe<FirstEvent>(_ => throw new InvalidOperationException("callback failure"));
            EventModule.Subscribe<FirstEvent>(_ => callCount++);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: callback failure"));

            EventModule.Fire(new FirstEvent(1));

            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public void DeferredEvents_PreserveGlobalFifoOrderAcrossTypes()
        {
            var received = new List<string>();
            EventModule.Subscribe<FirstEvent>(eventData => received.Add($"first:{eventData.Value}"));
            EventModule.Subscribe<SecondEvent>(eventData => received.Add($"second:{eventData.Value}"));

            EventModule.FireDeferred(new FirstEvent(1), EventDispatchPhase.Update);
            EventModule.FireDeferred(new SecondEvent(2), EventDispatchPhase.Update);
            EventModule.FireDeferred(new FirstEvent(3), EventDispatchPhase.Update);
            ModuleRepository.Update();

            CollectionAssert.AreEqual(
                new[] { "first:1", "second:2", "first:3" }, received);
        }

        [Test]
        public void DeferredEvents_KeepUpdateAndLateUpdateSeparate()
        {
            var received = new List<int>();
            EventModule.Subscribe<FirstEvent>(eventData => received.Add(eventData.Value));

            EventModule.FireDeferred(new FirstEvent(1), EventDispatchPhase.LateUpdate);
            EventModule.FireDeferred(new FirstEvent(2), EventDispatchPhase.Update);

            ModuleRepository.Update();
            CollectionAssert.AreEqual(new[] { 2 }, received);

            ModuleRepository.LateUpdate();
            CollectionAssert.AreEqual(new[] { 2, 1 }, received);
        }

        [Test]
        public void SamePhaseReentrantEvent_WaitsUntilNextFrame()
        {
            var received = new List<int>();
            EventModule.Subscribe<FirstEvent>(eventData =>
            {
                received.Add(eventData.Value);
                if (eventData.Value == 1)
                    EventModule.FireDeferred(new FirstEvent(2), EventDispatchPhase.Update);
            });
            EventModule.FireDeferred(new FirstEvent(1), EventDispatchPhase.Update);

            ModuleRepository.Update();
            CollectionAssert.AreEqual(new[] { 1 }, received);

            ModuleRepository.Update();
            CollectionAssert.AreEqual(new[] { 1, 2 }, received);
        }

        [Test]
        public void UpdateCallback_CanQueueLateUpdateForCurrentFrame()
        {
            var received = new List<string>();
            EventModule.Subscribe<FirstEvent>(_ =>
            {
                received.Add("update");
                EventModule.FireDeferred(new SecondEvent(2), EventDispatchPhase.LateUpdate);
            });
            EventModule.Subscribe<SecondEvent>(_ => received.Add("late"));
            EventModule.FireDeferred(new FirstEvent(1), EventDispatchPhase.Update);

            ModuleRepository.Update();
            ModuleRepository.LateUpdate();

            CollectionAssert.AreEqual(new[] { "update", "late" }, received);
        }

        [Test]
        public void Shutdown_ClearsStateAndTeardownDispatchIsNoOp()
        {
            var callCount = 0;
            EventCallback<FirstEvent> callback = _ => callCount++;
            EventModule.Subscribe(callback);
            EventModule.FireDeferred(new FirstEvent(1), EventDispatchPhase.Update);

            ModuleRepository.Shutdown();

            Assert.DoesNotThrow(() => EventModule.Fire(new FirstEvent(2)));
            Assert.DoesNotThrow(
                () => EventModule.FireDeferred(new FirstEvent(3), EventDispatchPhase.Update));
            Assert.DoesNotThrow(() => EventModule.Unsubscribe(callback));
            Assert.That(callCount, Is.Zero);
            Assert.That(ModuleRepository.TryGet<EventModule>(out _), Is.False);
        }

        [Test]
        public void SubscribeAfterShutdown_IsRejected()
        {
            ModuleRepository.Shutdown();

            Assert.Throws<InvalidOperationException>(
                () => EventModule.Subscribe<FirstEvent>(_ => { }));
        }

        [Test]
        public void FireDeferred_RejectsUnknownPhase()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => EventModule.FireDeferred(new FirstEvent(1), (EventDispatchPhase)99));
        }

        [Test]
        public void InstanceAndLifecycleMethods_AreNotPublicBusinessApi()
        {
            var publicStatic = BindingFlags.Public | BindingFlags.Static;
            var publicInstance = BindingFlags.Public | BindingFlags.Instance;

            Assert.That(typeof(EventModule).GetProperty("Instance", publicStatic), Is.Null);
            Assert.That(typeof(EventModule).GetMethod("Update", publicInstance), Is.Null);
            Assert.That(typeof(EventModule).GetMethod("LateUpdate", publicInstance), Is.Null);
            Assert.That(typeof(EventModule).GetMethod("Dispose", publicInstance), Is.Null);
        }

        public readonly struct FirstEvent : IGameEvent
        {
            public FirstEvent(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        public readonly struct SecondEvent : IGameEvent
        {
            public SecondEvent(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }
    }
}
