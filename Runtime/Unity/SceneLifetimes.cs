using System;
using UnityEngine.SceneManagement;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// The lifetime of each loaded scene, and the one-line call that ends a scene's work before Unity destroys it.
    /// Call <see cref="DisposeAll"/> right before a Single-mode load and <see cref="Dispose"/> right before unloading
    /// one scene; without the call, cleanup still happens, but late (see the development warning JANITOR104).
    /// Scene lifetimes are children of <see cref="Lifetime.App"/>. A DontDestroyOnLoad scene maps to
    /// <see cref="Lifetime.App"/> and is never disposed by these calls. Play Mode and the main thread only.
    /// </summary>
    public static class SceneLifetimes
    {
        /// <summary>
        /// Returns the lifetime of <paramref name="scene"/>, created on first use. <see cref="Lifetime.Cancel"/> on it
        /// stops the scene's work, including that of objects moved into the scene after their lifetimes were
        /// created, and keeps the scene usable. Disposal is terminal: after
        /// <see cref="Dispose"/> this returns the disposed lifetime until the scene is unloaded, and registrations on
        /// it are terminated at once (JANITOR109 while the scene is still loaded). A DontDestroyOnLoad scene returns
        /// <see cref="Lifetime.App"/>.
        /// </summary>
        /// <param name="scene">A valid scene.</param>
        /// <returns>The scene lifetime.</returns>
        /// <exception cref="ArgumentException"><paramref name="scene"/> is not valid.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static Lifetime Get(Scene scene)
        {
            var tree = RequireTree("SceneLifetimes.Get");
            RequireValid(scene);
            return SceneBinding.Resolve(tree, scene);
        }

        /// <summary>
        /// Disposes the lifetime of <paramref name="scene"/>: every token is cancelled, then every item is terminated,
        /// including those of objects placed in categories outside the scene and of objects moved into the scene
        /// after their lifetimes were created. Call it right before unloading the
        /// scene. Idempotent, and it never throws for user code: failures are routed to
        /// <see cref="LifetimeErrors.Handler"/>. A DontDestroyOnLoad scene is left alone.
        /// </summary>
        /// <param name="scene">A valid scene.</param>
        /// <exception cref="ArgumentException"><paramref name="scene"/> is not valid.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static void Dispose(Scene scene)
        {
            var tree = RequireTree("SceneLifetimes.Dispose");
            RequireValid(scene);
            DisposeScene(tree, scene);
        }

        /// <summary>
        /// Disposes the lifetime of every loaded scene, the last loaded scene first; a scene that is still loading is
        /// skipped. Call it right before any Single-mode load. <see cref="Lifetime.App"/> and DontDestroyOnLoad
        /// lifetimes are never touched.
        /// Never throws for user code: failures are routed to <see cref="LifetimeErrors.Handler"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static void DisposeAll()
        {
            var tree = RequireTree("SceneLifetimes.DisposeAll");
            var count = SceneManager.sceneCount;
            if (count == 0)
            {
                return;
            }

            // A snapshot, because a dispose callback may load or unload scenes.
            var scenes = new Scene[count];
            for (var i = 0; i < count; i++)
            {
                scenes[i] = SceneManager.GetSceneAt(i);
            }

            // A scene that is still loading is not part of the outgoing set.
            for (var i = count - 1; i >= 0; i--)
            {
                if (scenes[i].IsValid() && scenes[i].isLoaded)
                {
                    DisposeScene(tree, scenes[i]);
                }
            }
        }

        // Disposal is terminal, so a scene without a lifetime gets one that is disposed at once: later
        // registrations from its objects are then refused instead of starting work in a scene that is going away.
        private static void DisposeScene(LifetimeTree tree, Scene scene)
        {
            var lifetime = SceneBinding.Resolve(tree, scene);
            if (lifetime != null && lifetime.Kind == LifetimeKind.Scene)
            {
                lifetime.DisposeFromOwner();
            }
        }

        private static LifetimeTree RequireTree(string api)
        {
            var tree = LifetimeTree.RequireDefault(api);
            tree.Guard.EnsureMainThread(api);
            return tree;
        }

        private static void RequireValid(Scene scene)
        {
            if (!scene.IsValid())
            {
                throw new ArgumentException("The scene is not valid.", nameof(scene));
            }
        }
    }
}
