using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // Finds or creates the lifetime of a component, a GameObject or an active GameObject. Public entry points validate
    // their arguments and the tree first; everything here runs on the main thread. A destroyed owner, a prefab asset
    // and a scene that is going away all get the shared disposed sentinel, so their registrations terminate at once.
    internal static class ComponentLifetimes
    {
        private static readonly Action<object> DisposeOnDestroy = DisposeOnDestroyCore;
        private static readonly Action<object> DisposeOnEitherDestroy = DisposeOnEitherDestroyCore;

        internal static Lifetime ForBehaviour(LifetimeTree tree, MonoBehaviour behaviour, Lifetime parent, bool hasParent)
        {
            if (behaviour == null)
            {
                return tree.Sentinel;
            }

            var key = behaviour.GetInstanceID();
            if (tree.Owners.TryGet(key, out var existing))
            {
                CheckParent(existing, parent, hasParent);
                return existing;
            }

            var gameObject = behaviour.gameObject;
            var structural = ResolveParent(tree, gameObject, behaviour, parent, hasParent, out var scene, out var placed);
            if (structural == null)
            {
                return tree.Sentinel;
            }

            // An inactive object may never run Awake, so its GameObject is watched as well as the component.
            bool active;
            CancellationToken token;
            CancellationToken objectToken = default;
            try
            {
                active = gameObject.activeInHierarchy;
                token = behaviour.destroyCancellationToken;
                if (!active)
                {
                    objectToken = gameObject.GetCancellationTokenOnDestroy();
                }
            }
            catch (Exception exception)
            {
                return Refused(tree, exception);
            }

            var lifetime = structural.CreateChildCore(behaviour.GetType().Name, LifetimeKind.Component, true, behaviour, null, 0);
            Index(tree, lifetime, key, scene, structural, placed);
            if (active)
            {
                BindDestroy(lifetime, token);
            }
            else
            {
                BindDestroy(lifetime, token, objectToken);
            }

            return lifetime;
        }

        internal static Lifetime ForGameObject(LifetimeTree tree, GameObject gameObject)
        {
            if (gameObject == null)
            {
                return tree.Sentinel;
            }

            var key = gameObject.GetInstanceID();
            if (tree.Owners.TryGet(key, out var existing))
            {
                return existing;
            }

            var structural = ResolveParent(tree, gameObject, gameObject, null, false, out var scene, out var placed);
            if (structural == null)
            {
                return tree.Sentinel;
            }

            CancellationToken token;
            try
            {
                token = gameObject.GetCancellationTokenOnDestroy();
            }
            catch (Exception exception)
            {
                return Refused(tree, exception);
            }

            var lifetime = structural.CreateChildCore(gameObject.name, LifetimeKind.GameObject, true, gameObject, null, 0);
            Index(tree, lifetime, key, scene, structural, placed);
            BindDestroy(lifetime, token);
            return lifetime;
        }

        internal static Lifetime ForActive(LifetimeTree tree, Component component, Lifetime parent, bool hasParent)
        {
            if (component == null)
            {
                return tree.Sentinel;
            }

            var gameObject = component.gameObject;
            // The trigger may exist unbound: a lifetime-bound coroutine adds it to count deactivations.
            var hasTrigger = gameObject.TryGetComponent(out ActiveLifetimeTrigger trigger);
            if (hasTrigger)
            {
                trigger.EnableAgainChecked();
                var existing = trigger.BoundLifetime;
                if (existing != null)
                {
                    CheckParent(existing, parent, hasParent);
                    return existing;
                }
            }

            var structural = ResolveParent(tree, gameObject, gameObject, parent, hasParent, out var scene, out var placed);
            if (structural == null)
            {
                return tree.Sentinel;
            }

            if (!hasTrigger)
            {
                trigger = ActiveLifetimeTrigger.Add(gameObject);

                // Unity refuses to add a component to a GameObject that is being destroyed.
                if (trigger == null)
                {
                    return tree.Sentinel;
                }
            }

            CancellationToken token;
            try
            {
                token = gameObject.activeInHierarchy ? trigger.destroyCancellationToken : gameObject.GetCancellationTokenOnDestroy();
            }
            catch (Exception exception)
            {
                return Refused(tree, exception);
            }

            var lifetime = structural.CreateChildCore(gameObject.name, LifetimeKind.Active, true, trigger, null, 0);
            trigger.Bind(lifetime);
            Index(tree, lifetime, trigger.GetInstanceID(), scene, structural, placed);
            BindDestroy(lifetime, token);
            return lifetime;
        }

        // The owner is going away, so the registration is refused. A destroyed object, a disposed token source and a refused
        // AddComponent are the expected causes and stay silent; any other failure is reported.
        private static Lifetime Refused(LifetimeTree tree, Exception exception)
        {
            if (!(exception is MissingReferenceException || exception is ObjectDisposedException || exception is NullReferenceException))
            {
                tree.ReportError(exception, LifetimeErrorSource.CancelAction, null, null, 0);
            }

            return tree.Sentinel;
        }

        // The structural parent for a new object lifetime, or null when the object gets the sentinel: it is not in
        // a scene (a prefab asset), or its scene lifetime is going away. The first access decides the parent.
        private static Lifetime ResolveParent(LifetimeTree tree, GameObject gameObject, UnityEngine.Object owner, Lifetime requested, bool hasParent, out Lifetime scene, out bool placed)
        {
            placed = false;
            scene = SceneBinding.Resolve(tree, gameObject.scene);
            if (scene == null)
            {
                return null;
            }

            var state = scene.State;
            if (state == LifetimeState.Disposed)
            {
                SceneBinding.WarnDisposedScene(scene);
                return null;
            }

            if (state == LifetimeState.Disposing)
            {
                return null;
            }

            if (!hasParent || ReferenceEquals(requested, scene))
            {
                return scene;
            }

            if (requested.State == LifetimeState.Disposing || requested.State == LifetimeState.Disposed)
            {
                DevWarnings.ParentDisposed(owner, requested);
                return scene;
            }

            placed = true;
            return requested;
        }

        // A later call with a different parent returns the existing lifetime unchanged and warns; it never reparents.
        private static void CheckParent(Lifetime existing, Lifetime requested, bool hasParent)
        {
            if (hasParent && existing.State != LifetimeState.Disposed && !ReferenceEquals(existing.Parent, requested))
            {
                DevWarnings.ParentMismatch(existing, requested);
            }
        }

        // Owner index entry, plus the dispose-only membership when the category lies outside the scene's own subtree.
        private static void Index(LifetimeTree tree, Lifetime lifetime, int key, Lifetime scene, Lifetime structural, bool placed)
        {
            lifetime.OwnerKey = key;
            tree.Owners.Add(key, lifetime);
            if (!placed)
            {
                return;
            }

            lifetime.Placed = true;
            if (scene.Kind == LifetimeKind.Scene && !structural.IsDescendantOrSelfOf(scene))
            {
                scene.AddMember(lifetime);
            }
        }

        // The registration belongs to the lifetime object, not to a generation, so Cancel never removes it.
        private static void BindDestroy(Lifetime lifetime, CancellationToken token)
        {
            lifetime.OwnerTokenRegistration = token.RegisterWithoutCaptureExecutionContext(DisposeOnDestroy, lifetime);
        }

        // Two destroy signals, one disposal: whichever fires first disposes the lifetime and drops the other registration.
        private static void BindDestroy(Lifetime lifetime, CancellationToken componentToken, CancellationToken objectToken)
        {
            var pair = new DestroyPair(lifetime);
            pair.ComponentRegistration = componentToken.RegisterWithoutCaptureExecutionContext(DisposeOnEitherDestroy, pair);
            if (lifetime.State == LifetimeState.Disposed)
            {
                return;
            }

            pair.ObjectRegistration = objectToken.RegisterWithoutCaptureExecutionContext(DisposeOnEitherDestroy, pair);
            if (lifetime.State != LifetimeState.Disposed)
            {
                lifetime.OwnerTokenRegistration = pair.ComponentRegistration;
            }
        }

        private static void DisposeOnDestroyCore(object state)
        {
            ((Lifetime)state).DisposeFromOwner();
        }

        private static void DisposeOnEitherDestroyCore(object state)
        {
            var pair = (DestroyPair)state;
            pair.ComponentRegistration.Dispose();
            pair.ObjectRegistration.Dispose();
            pair.Lifetime.DisposeFromOwner();
        }

        private sealed class DestroyPair
        {
            internal readonly Lifetime Lifetime;
            internal CancellationTokenRegistration ComponentRegistration;
            internal CancellationTokenRegistration ObjectRegistration;

            internal DestroyPair(Lifetime lifetime)
            {
                Lifetime = lifetime;
            }
        }
    }
}
