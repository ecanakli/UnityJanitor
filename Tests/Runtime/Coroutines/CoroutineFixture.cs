using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // One isolated tree, one capturing error handler and the scene objects of a single test; Complete() tears it all down.
    internal sealed class CoroutineFixture
    {
        private const float DeadlineSeconds = 10f;

        private readonly List<GameObject> _objects = new List<GameObject>();

        // makeDefault is needed by the tests that resolve component or active lifetimes (GetLifetime, GetActiveLifetime).
        internal CoroutineFixture(bool makeDefault = false)
        {
            Scope = new TestScope();
            Area = Scope.App.CreateChild("area");
            if (makeDefault)
            {
                Scope.Tree.MakeDefault();
            }
        }

        internal TestScope Scope { get; }

        // A plain area under the tree's App; most tests use it as the coroutine owner.
        internal Lifetime Area { get; }

        internal CapturingErrorHandler Errors => Scope.Errors;

        internal CallLog Log => Scope.Log;

        internal CoroutineTestHost NewHost(string name = "Host", bool active = true, Transform parent = null)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            var host = gameObject.AddComponent<CoroutineTestHost>();
            if (!active)
            {
                gameObject.SetActive(false);
            }

            return host;
        }

        internal GameObject NewObject(string name, bool active = true)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            if (!active)
            {
                gameObject.SetActive(false);
            }

            return gameObject;
        }

        // Call from TearDown: lifetimes first, so nothing reacts to the destruction; errors are checked last.
        internal void Complete()
        {
            try
            {
                try
                {
                    Scope.Tree.Shutdown();
                }
                finally
                {
                    DestroyAll();
                }
            }
            finally
            {
                Scope.Complete();
            }
        }

        // The wrapper behind the newest entry of a lifetime; the entry list is the only way to reach it.
        internal static LifetimeCoroutine WrapperOf(Lifetime lifetime)
        {
            Assert.That(lifetime.EntryCount, Is.GreaterThan(0), "the lifetime has no entry to read");
            var entry = lifetime.EntryAt(lifetime.NewestEntryId);
            Assert.That(entry.A, Is.InstanceOf<LifetimeCoroutine>());
            return (LifetimeCoroutine)entry.A;
        }

        // How many live entries of a lifetime belong to coroutines, told apart by their Terminate identity.
        internal static int CountCoroutineEntries(Lifetime lifetime)
        {
            var count = 0;
            for (var id = lifetime.NewestEntryId; id != 0; id = lifetime.EntryAt(id).Next)
            {
                if (ReferenceEquals(lifetime.EntryAt(id).Terminate, LifetimeCoroutine.Terminate))
                {
                    count++;
                }
            }

            return count;
        }

        internal static bool Has(CallLog log, string entry)
        {
            return Array.IndexOf(log.ToArray(), entry) >= 0;
        }

        internal static int CountOf(CallLog log, string entry)
        {
            var count = 0;
            var all = log.ToArray();
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] == entry)
                {
                    count++;
                }
            }

            return count;
        }

        // Waits a fixed number of frames; the test runner resumes once per frame.
        internal static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        // Waits until the condition holds, bounded by a real-time deadline so a broken routine fails the test instead of hanging it.
        internal static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        // Development warnings are compiled out of release builds, so the expectation is too.
        internal static void ExpectWarning(string pattern)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogAssert.Expect(LogType.Warning, new Regex(pattern));
#endif
        }

        private void DestroyAll()
        {
            for (var i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null)
                {
                    Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
        }
    }
}
