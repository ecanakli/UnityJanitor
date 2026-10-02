using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // A ProjectContext that starts the way a game's does: it is created on first access, its installers run inside InstallBindings,
    // and one of them calls LifetimeInstaller.Install. It replaces the shared plain ProjectContext for one test, and the next
    // session creates a plain one again. No asset is involved, so the branch that loads Resources/ProjectContext is not reached.
    internal sealed class RealProjectContext
    {
        private readonly Action<DiContainer> _projectBindings;

        private RealProjectContext(Action<DiContainer> projectBindings)
        {
            _projectBindings = projectBindings;
        }

        internal ProjectContext Instance { get; private set; }

        internal DiContainer Container => Instance.Container;

        // Call after ZenjectPlaySession.Begin, so LifetimeInstaller captures the App lifetime of this test's session.
        internal static RealProjectContext Begin(Action<DiContainer> projectBindings = null)
        {
            if (ProjectContext.HasInstance)
            {
                Object.DestroyImmediate(ProjectContext.Instance.gameObject);
            }

            var real = new RealProjectContext(projectBindings);
            ProjectContext.PreInstall += real.AddInstaller;
            try
            {
                real.Instance = ProjectContext.Instance;
            }
            finally
            {
                ProjectContext.PreInstall -= real.AddInstaller;
            }

            return real;
        }

        internal void Dispose()
        {
            if (Instance != null)
            {
                Object.DestroyImmediate(Instance.gameObject);
            }
        }

        // Runs while the ProjectContext initialises, before its installers: the same moment a game adds a project installer.
        private void AddInstaller()
        {
            ProjectContext.Instance.AddNormalInstaller(new ActionInstaller(container =>
            {
                LifetimeInstaller.Install(container);
                _projectBindings?.Invoke(container);
            }));
        }
    }

    // Prefabs built in code. A prefab is an active object under an inactive holder: it never runs Awake or OnEnable, and Zenject
    // treats it like an asset (clone it, deactivate the clone, inject, activate). The holder lives in the active scene, so no
    // SceneContext of a test scene injects it.
    internal sealed class CodePrefabs
    {
        private readonly List<GameObject> _owned = new List<GameObject>();

        internal GameObject New<T>(string name)
            where T : Component
        {
            var holder = new GameObject(name + "Holder");
            holder.SetActive(false);
            _owned.Add(holder);
            var prefab = new GameObject(name);
            prefab.transform.SetParent(holder.transform, false);
            prefab.AddComponent<T>();
            return prefab;
        }

        internal void Dispose()
        {
            for (var i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] != null)
                {
                    Object.DestroyImmediate(_owned[i]);
                }
            }

            _owned.Clear();
        }
    }
}
