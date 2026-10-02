using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Events
{
    // One isolated tree (made the default, so component and active lifetimes resolve in it), its error handler and the scene objects of one test.
    internal sealed class EventFixture
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        internal EventFixture()
        {
            Scope = new TestScope();
            Scope.Tree.MakeDefault();
            Area = Scope.App.CreateChild("area");
        }

        internal TestScope Scope { get; }

        // A plain area under the tree's App; the owners of most tests are children of it.
        internal Lifetime Area { get; }

        internal CapturingErrorHandler Errors => Scope.Errors;

        internal CallLog Log => Scope.Log;

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

        internal EventTestListener NewListener(string name = "Listener")
        {
            return NewObject(name).AddComponent<EventTestListener>();
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
