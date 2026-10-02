using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ecanakli.Janitor
{
    // Maps scenes to scene lifetimes, decides re-homing and runs the scene reparent pre-pass and the sceneUnloaded fallback.
    // Every method is main thread only and runs no user code except where noted.
    internal static class SceneBinding
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Scene lifetimes already reported with JANITOR109. Weak: it never keeps a lifetime alive.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Lifetime, object> DisposedSceneWarned = new System.Runtime.CompilerServices.ConditionalWeakTable<Lifetime, object>();
        private static readonly object WarnedMarker = new object();
#endif

        // Lookup only. Null when the scene is invalid or has no lifetime yet; App for a persistent scene
        // (DontDestroyOnLoad, preview scenes), which is decided once per handle against the loaded scenes.
        internal static Lifetime Find(LifetimeTree tree, Scene scene)
        {
            if (!scene.IsValid())
            {
                return null;
            }

            var handle = scene.handle;
            if (tree.Scenes.TryGet(handle, out var existing))
            {
                return existing;
            }

            if (tree.Scenes.IsPersistent(handle))
            {
                return tree.App;
            }

            // A scene that is still loading is an ordinary scene even if the manager does not list it yet.
            if (scene.isLoaded && !IsEnumerated(handle))
            {
                tree.Scenes.MarkPersistent(handle);
                return tree.App;
            }

            return null;
        }

        // The scene lifetime (created on first use), App for a persistent scene, null for an invalid scene.
        internal static Lifetime Resolve(LifetimeTree tree, Scene scene)
        {
            if (!scene.IsValid())
            {
                return null;
            }

            return Find(tree, scene) ?? Create(tree, scene);
        }

        // Where a placed object lifetime goes when its category is disposed; null when its owner is gone.
        internal static Lifetime ResolveHome(LifetimeTree tree, Lifetime node)
        {
            var gameObject = node.OwnerGameObject;
            return gameObject == null ? null : Resolve(tree, gameObject.scene);
        }

        // Mark-pass decision: a placed object lifetime is cancelled and re-homed, not disposed, unless its own
        // scene or App is what is being disposed (or is going away).
        internal static bool ShouldRehome(Lifetime node, Lifetime cause)
        {
            if (!node.Placed || cause.Kind == LifetimeKind.App)
            {
                return false;
            }

            var gameObject = node.OwnerGameObject;
            if (gameObject == null)
            {
                return false;
            }

            var home = Find(node.Tree, gameObject.scene);
            if (cause.Kind == LifetimeKind.Scene && ReferenceEquals(home, cause))
            {
                return false;
            }

            return home == null || (home.State != LifetimeState.Disposing && home.State != LifetimeState.Disposed);
        }

        // Only a scene lifetime's own operation can touch objects that already left or joined the scene.
        internal static bool NeedsPrePass(Lifetime root)
        {
            return root.Kind == LifetimeKind.Scene;
        }

        // Runs before a scene lifetime's Cancel or Dispose. Objects that moved to another scene or to
        // DontDestroyOnLoad are taken out of this scene's reach, and objects that moved into this scene are brought in;
        // a category-placed object keeps its category and only its membership entry moves.
        // User code can run here only through a nested Dispose of a moved object.
        internal static void PrePass(LifetimeTree tree, Lifetime scene)
        {
            if (scene.FirstChild == null && scene.Members == null && tree.Owners.Count == 0)
            {
                return;
            }

            var buffer = tree.Buffers.Rent(0);
            try
            {
                for (var child = scene.FirstChild; child != null; child = child.NextSibling)
                {
                    if (child.IsObjectKind && !child.Placed)
                    {
                        buffer.Add(child);
                    }
                }

                for (var i = 0; i < buffer.Count; i++)
                {
                    ReparentIfMoved(tree, scene, buffer.Items[i]);
                }

                buffer.Reset();
                var members = scene.Members;
                if (members != null)
                {
                    for (var i = 0; i < members.Count; i++)
                    {
                        buffer.Add(members[i]);
                    }

                    for (var i = 0; i < buffer.Count; i++)
                    {
                        MoveMembershipIfMoved(tree, scene, buffer.Items[i]);
                    }
                }

                // The user code above may have ended this scene; nothing is adopted into a lifetime that is gone.
                var state = scene.State;
                if (state != LifetimeState.Active && state != LifetimeState.Cancelling)
                {
                    return;
                }

                buffer.Reset();
                CollectIncoming(tree, scene, buffer);
                for (var i = 0; i < buffer.Count; i++)
                {
                    AdoptIncoming(scene, buffer.Items[i]);
                }
            }
            catch (Exception exception)
            {
                tree.ReportError(exception, LifetimeErrorSource.CancelAction, scene, null, 0);
            }
            finally
            {
                tree.Buffers.Return(buffer);
            }
        }

        // The sceneUnloaded fallback: a scene lifetime that is still alive is disposed late, with a hint.
        // A disposed scene lifetime is dropped from the index here, so the hint appears once per scene.
        internal static void OnSceneUnloaded(LifetimeTree tree, Scene scene, bool exiting)
        {
            if (tree == null)
            {
                return;
            }

            var handle = scene.handle;
            tree.Scenes.ClearPersistent(handle);
            if (!tree.Scenes.TryGet(handle, out var lifetime))
            {
                return;
            }

            try
            {
                var state = lifetime.State;
                if (state == LifetimeState.Active || state == LifetimeState.Cancelling)
                {
                    if (!exiting)
                    {
                        DevWarnings.SceneUnloadedWithoutDispose(lifetime);
                    }

                    lifetime.DisposeFromOwner();
                }
            }
            finally
            {
                tree.Scenes.Remove(handle, lifetime);
            }
        }

        // JANITOR109, once per scene lifetime and never while the session is exiting: a disposed scene is asked again every frame.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        internal static void WarnDisposedScene(Lifetime scene)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PlayModeBootstrap.IsExiting || DisposedSceneWarned.TryGetValue(scene, out _))
            {
                return;
            }

            DisposedSceneWarned.Add(scene, WarnedMarker);
            DevWarnings.RegistrationOnDisposedScene(scene);
#endif
        }

        // True while the scene with this handle is in the scene manager and loaded.
        internal static bool IsLoaded(int handle)
        {
            var count = SceneManager.sceneCount;
            for (var i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.handle == handle)
                {
                    return scene.isLoaded;
                }
            }

            return false;
        }

        internal const string UntitledSceneName = "Untitled";

        // A scene that was never saved has an empty name; the editor calls it Untitled.
        internal static string LifetimeNameFor(string sceneName)
        {
            return string.IsNullOrEmpty(sceneName) ? UntitledSceneName : sceneName;
        }

        private static Lifetime Create(LifetimeTree tree, Scene scene)
        {
            var lifetime = tree.App.CreateChildCore(LifetimeNameFor(scene.name), LifetimeKind.Scene, true, null, null, 0);
            lifetime.SceneKey = scene.handle;
            tree.Scenes.Add(scene.handle, lifetime);
            return lifetime;
        }

        // DontDestroyOnLoad and editor preview scenes are not part of the scene manager's list.
        private static bool IsEnumerated(int handle)
        {
            var count = SceneManager.sceneCount;
            for (var i = 0; i < count; i++)
            {
                if (SceneManager.GetSceneAt(i).handle == handle)
                {
                    return true;
                }
            }

            return false;
        }

        // One scan of the owner index: object lifetimes whose GameObject is in this scene but that hang elsewhere.
        private static void CollectIncoming(LifetimeTree tree, Lifetime scene, SnapshotBuffer buffer)
        {
            var handle = scene.SceneKey;
            foreach (var pair in tree.Owners)
            {
                var node = pair.Value;
                var state = node.State;
                if ((state != LifetimeState.Active && state != LifetimeState.Cancelling)
                    || ReferenceEquals(node.Membership, scene)
                    || node.IsDescendantOrSelfOf(scene))
                {
                    continue;
                }

                var gameObject = node.OwnerGameObject;
                if (gameObject != null && gameObject.scene.handle == handle)
                {
                    buffer.Add(node);
                }
            }
        }

        // A plain object lifetime becomes a child of this scene; a placed one keeps its category and only its membership moves.
        private static void AdoptIncoming(Lifetime scene, Lifetime node)
        {
            var state = node.State;
            if (state != LifetimeState.Active && state != LifetimeState.Cancelling)
            {
                return;
            }

            if (node.Placed)
            {
                node.DetachMembership();
                scene.AddMember(node);
                return;
            }

            node.Unlink();
            scene.AttachChild(node);
        }

        private static void ReparentIfMoved(LifetimeTree tree, Lifetime scene, Lifetime node)
        {
            var state = node.State;
            if ((state != LifetimeState.Active && state != LifetimeState.Cancelling) || !ReferenceEquals(node.Parent, scene))
            {
                return;
            }

            var gameObject = node.OwnerGameObject;
            if (gameObject == null)
            {
                return;
            }

            var home = Resolve(tree, gameObject.scene);
            if (home == null || ReferenceEquals(home, scene))
            {
                return;
            }

            if (home.State == LifetimeState.Disposing || home.State == LifetimeState.Disposed)
            {
                // It moved into a scene that is already going away, so it ends with that scene.
                node.DisposeFromOwner();
                return;
            }

            node.Unlink();
            home.AttachChild(node);
        }

        private static void MoveMembershipIfMoved(LifetimeTree tree, Lifetime scene, Lifetime member)
        {
            if (!ReferenceEquals(member.Membership, scene))
            {
                return;
            }

            var gameObject = member.OwnerGameObject;
            if (gameObject == null)
            {
                return;
            }

            var home = Resolve(tree, gameObject.scene);
            if (home == null || ReferenceEquals(home, scene))
            {
                return;
            }

            scene.RemoveMember(member);
            if (home.Kind != LifetimeKind.Scene)
            {
                // DontDestroyOnLoad: the category stays and the membership is dropped.
                return;
            }

            if (home.State == LifetimeState.Disposing || home.State == LifetimeState.Disposed)
            {
                member.DisposeFromOwner();
                return;
            }

            if (member.Parent == null || !member.Parent.IsDescendantOrSelfOf(home))
            {
                home.AddMember(member);
            }
        }
    }
}
