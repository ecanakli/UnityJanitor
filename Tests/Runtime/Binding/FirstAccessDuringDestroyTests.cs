using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks.Triggers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // The first access to a lifetime from the teardown code of an object that is being destroyed (its own or a child's)
    // must never throw. Unity may refuse AddComponent or dispose a token source at that moment; the caller gets a
    // disposed lifetime. The outcomes are printed so a run shows what the engine did.
    [TestFixture]
    public sealed class FirstAccessDuringDestroyTests
    {
        private enum Accessor
        {
            Component,
            GameObject,
            Active,
        }

        private BindingSession _s;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        // The owner of a disposed token source

        [Test]
        public void GameObjectLifetime_WhenTheDestroyTriggerWasAlreadyDisposed_ReturnsADisposedLifetimeInsteadOfThrowing()
        {
            var go = _s.NewObject();
            var trigger = go.GetAsyncDestroyTrigger();
            Assert.That(trigger.CancellationToken.CanBeCanceled, Is.True, "premise: the trigger holds a token source");
            var field = typeof(AsyncDestroyTrigger).GetField("cancellationTokenSource", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "UniTask keeps its token source in this field");
            ((CancellationTokenSource)field.GetValue(trigger)).Dispose();
            Lifetime lifetime = null;

            try
            {
                Assert.That(() => lifetime = go.GetLifetime(), Throws.Nothing);
            }
            finally
            {
                // The trigger's own OnDestroy would cancel the disposed source at teardown.
                field.SetValue(trigger, null);
            }

            Assert.That(lifetime, Is.Not.Null);
            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_s.Tree.Owners.Count, Is.Zero, "nothing is indexed for an owner that is going away");
        }

        // From OnDisable, on the object itself

        [UnityTest]
        public IEnumerator ComponentLifetime_FirstAccessInOnDisable_OfTheObjectBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.Component, false, false);
        }

        [UnityTest]
        public IEnumerator GameObjectLifetime_FirstAccessInOnDisable_OfTheObjectBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.GameObject, false, false);
        }

        [UnityTest]
        public IEnumerator ActiveLifetime_FirstAccessInOnDisable_OfTheObjectBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.Active, false, false);
        }

        // From OnDestroy, on the object itself

        [UnityTest]
        public IEnumerator ComponentLifetime_FirstAccessInOnDestroy_OfTheObjectBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.Component, true, false);
        }

        [UnityTest]
        public IEnumerator GameObjectLifetime_FirstAccessInOnDestroy_OfTheObjectBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.GameObject, true, false);
        }

        [UnityTest]
        public IEnumerator ActiveLifetime_FirstAccessInOnDestroy_OfTheObjectBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.Active, true, false);
        }

        // From the parent's OnDisable, on a child that is destroyed with it

        [UnityTest]
        public IEnumerator ComponentLifetime_FirstAccessInOnDisable_OfAChildBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.Component, false, true);
        }

        [UnityTest]
        public IEnumerator GameObjectLifetime_FirstAccessInOnDisable_OfAChildBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.GameObject, false, true);
        }

        [UnityTest]
        public IEnumerator ActiveLifetime_FirstAccessInOnDisable_OfAChildBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.Active, false, true);
        }

        // From the parent's OnDestroy, on a child that is destroyed with it

        [UnityTest]
        public IEnumerator ComponentLifetime_FirstAccessInOnDestroy_OfAChildBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.Component, true, true);
        }

        [UnityTest]
        public IEnumerator GameObjectLifetime_FirstAccessInOnDestroy_OfAChildBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.GameObject, true, true);
        }

        [UnityTest]
        public IEnumerator ActiveLifetime_FirstAccessInOnDestroy_OfAChildBeingDestroyed_DoesNotThrow()
        {
            return Scenario(Accessor.Active, true, true);
        }

        // Destroys a parent whose probe makes the first access, once, from OnDisable or OnDestroy.
        private IEnumerator Scenario(Accessor accessor, bool inOnDestroy, bool onChild)
        {
            var messages = new List<string>();
            Application.LogCallback capture = (condition, stackTrace, type) => messages.Add(type + ": " + condition);
            Application.logMessageReceived += capture;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var parent = _s.NewObject("Parent");
                var parentProbe = parent.AddComponent<BindingProbe>();
                var target = parentProbe;
                if (onChild)
                {
                    var child = _s.NewObject("Child");
                    child.transform.SetParent(parent.transform, false);
                    target = child.AddComponent<BindingProbe>();
                }

                var targetObject = target.gameObject;
                Exception thrown = null;
                Lifetime got = null;
                var calls = 0;
                Action<BindingProbe> hook = probe =>
                {
                    if (!ReferenceEquals(probe, parentProbe) || calls > 0)
                    {
                        return;
                    }

                    calls++;
                    try
                    {
                        got = Access(accessor, target, targetObject);
                    }
                    catch (Exception exception)
                    {
                        thrown = exception;
                    }
                };

                if (inOnDestroy)
                {
                    BindingProbe.DestroyHook = hook;
                }
                else
                {
                    BindingProbe.DisableHook = hook;
                }

                Object.Destroy(parent);
                yield return BindingScenes.Frames(2);

                TestContext.WriteLine(accessor + " in " + (inOnDestroy ? "OnDestroy" : "OnDisable") + (onChild ? " of a child" : " of the object itself")
                    + ": thrown=" + (thrown == null ? "none" : thrown.GetType().Name)
                    + ", disposed afterwards=" + (got != null && got.IsDisposed)
                    + ", indexed lifetimes left=" + _s.Tree.Owners.Count
                    + ", engine messages=[" + string.Join(" | ", messages) + "]");
                Assert.That(calls, Is.EqualTo(1), "the hook ran");
                Assert.That(thrown, Is.Null, "a registration call from teardown code must not throw: " + thrown);
                Assert.That(got, Is.Not.Null);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Application.logMessageReceived -= capture;
            }
        }

        private static Lifetime Access(Accessor accessor, BindingProbe target, GameObject targetObject)
        {
            switch (accessor)
            {
                case Accessor.Component:
                    return target.GetLifetime();
                case Accessor.GameObject:
                    return targetObject.GetLifetime();
                default:
                    return target.GetActiveLifetime();
            }
        }
    }
}
