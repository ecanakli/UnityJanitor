using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // Work started from the host's own OnDestroy: the call must not throw, Unity must log no error, and the entry must not outlive the host.
    [TestFixture]
    public sealed class LifetimeCoroutineDestroyHostTests
    {
        private CoroutineFixture _f;

        [SetUp]
        public void SetUp()
        {
            _f = new CoroutineFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _f.Complete();
        }

        [UnityTest]
        public IEnumerator StartCoroutine_FromTheHostsOwnOnDestroy_ThrowsNothingLogsNoErrorAndLeavesNothingRegistered()
        {
            var messages = new List<string>();
            var errors = new List<string>();
            Application.LogCallback capture = (condition, stackTrace, type) =>
            {
                messages.Add(type + ": " + condition);
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    errors.Add(type + ": " + condition);
                }
            };
            Application.logMessageReceived += capture;

            // Unity may print a plain line when it refuses to add the package's trigger to an object that is being destroyed.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var counter = new Counter();
                var go = _f.NewObject("DestroyHost");
                var host = go.AddComponent<CoroutineDestroyHost>();
                Exception thrown = null;
                var calls = 0;
                host.OnDestroyAction = self =>
                {
                    calls++;
                    try
                    {
                        _f.Area.StartCoroutine(self, CoroutineRoutines.Forever(counter));
                    }
                    catch (Exception exception)
                    {
                        thrown = exception;
                    }
                };

                Object.Destroy(go);
                yield return CoroutineFixture.Frames(3);

                var entriesAfterDestroy = _f.Area.EntryCount;
                var atEnd = counter.Value;
                yield return CoroutineFixture.Frames(3);
                TestContext.WriteLine("StartCoroutine in OnDestroy: first step ran=" + (atEnd > 0) + ", entries after the destroy=" + entriesAfterDestroy
                    + ", engine messages=[" + string.Join(" | ", messages) + "]");

                Assert.That(calls, Is.EqualTo(1), "OnDestroy ran");
                Assert.That(thrown, Is.Null, "nothing escapes: " + thrown);
                Assert.That(errors, Is.Empty, "Unity logged no error: " + string.Join(" | ", errors));
                Assert.That(counter.Value, Is.EqualTo(atEnd), "the routine does not run after its host is gone");

                if (entriesAfterDestroy > 0)
                {
                    // Unity accepted the start and then destroyed the host: the probe reports the entry finished, so the next sweep drops it.
                    Assert.That(LifetimeCoroutine.IsFinishedProbe(CoroutineFixture.WrapperOf(_f.Area)), Is.True, "the entry that stayed belongs to a destroyed host");
                }

                // The first sweep runs at 16 entries.
                for (var i = 0; i < 20; i++)
                {
                    _f.Area.OnCancel(() => { });
                }

                Assert.That(CoroutineFixture.CountCoroutineEntries(_f.Area), Is.Zero, "nothing stays registered");
                Assert.DoesNotThrow(() => _f.Area.Cancel());
                Assert.That(_f.Area.EntryCount, Is.Zero);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Application.logMessageReceived -= capture;
            }
        }
    }
}
