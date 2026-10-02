using System;
using UnityEngine;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Finds the lifetime of a component or a GameObject. A component lifetime is disposed when the component is
    /// destroyed, a GameObject lifetime when the GameObject is destroyed, and an active lifetime is cancelled every
    /// time its GameObject is deactivated and disposed when it is destroyed. The first call creates the lifetime and
    /// may allocate; later calls allocate nothing. All members must be called on the main thread in Play Mode.
    /// A destroyed owner, an owner whose destruction is under way, and an object whose scene lifetime is already
    /// disposed, get a lifetime that is already disposed: every registration on it is terminated at once. These calls do
    /// not throw when made from <c>OnDisable</c> or <c>OnDestroy</c> of the object being destroyed.
    /// </summary>
    public static class LifetimeComponentExtensions
    {
        /// <summary>
        /// Returns the lifetime of this component, disposed when the component is destroyed. It is a child of the
        /// component's scene lifetime, or of <see cref="Lifetime.App"/> for a DontDestroyOnLoad object.
        /// A component whose GameObject was never activated never receives Unity's destroy signal; its lifetime
        /// ends when the GameObject is destroyed.
        /// </summary>
        /// <param name="behaviour">The component.</param>
        /// <returns>The component lifetime, or a disposed lifetime when the component was destroyed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static Lifetime GetLifetime(this MonoBehaviour behaviour)
        {
            if (ReferenceEquals(behaviour, null))
            {
                throw new ArgumentNullException(nameof(behaviour));
            }

            var tree = RequireTree("GetLifetime");
            return ComponentLifetimes.ForBehaviour(tree, behaviour, null, false);
        }

        /// <summary>
        /// Like <see cref="GetLifetime(MonoBehaviour)"/>, and places the component lifetime in a category: it
        /// becomes a child of <paramref name="parent"/>, so <c>parent.Cancel()</c> reaches it. The first access
        /// decides the parent: a later call with a different parent returns the existing lifetime unchanged and
        /// raises the development warning JANITOR113. A disposed parent falls back to the scene lifetime
        /// (also JANITOR113). Disposing the category cancels this lifetime and re-homes it under its scene lifetime
        /// instead of disposing it. Call this before anything else touches the component's lifetime.
        /// </summary>
        /// <param name="behaviour">The component.</param>
        /// <param name="parent">The category.</param>
        /// <returns>The component lifetime, or a disposed lifetime when the component was destroyed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="behaviour"/> or <paramref name="parent"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static Lifetime GetLifetime(this MonoBehaviour behaviour, Lifetime parent)
        {
            if (ReferenceEquals(behaviour, null))
            {
                throw new ArgumentNullException(nameof(behaviour));
            }

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var tree = RequireTree("GetLifetime");
            return ComponentLifetimes.ForBehaviour(tree, behaviour, parent, true);
        }

        /// <summary>
        /// Returns the lifetime of this GameObject, disposed when the GameObject is destroyed. Destroying only one of
        /// its components does not dispose it. It is a child of the scene lifetime, or of <see cref="Lifetime.App"/>
        /// for a DontDestroyOnLoad object. The first call adds UniTask's destroy trigger to the GameObject.
        /// </summary>
        /// <param name="gameObject">The GameObject.</param>
        /// <returns>The GameObject lifetime, or a disposed lifetime when the GameObject was destroyed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static Lifetime GetLifetime(this GameObject gameObject)
        {
            if (ReferenceEquals(gameObject, null))
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            var tree = RequireTree("GetLifetime");
            return ComponentLifetimes.ForGameObject(tree, gameObject);
        }

        /// <summary>
        /// Returns the active lifetime of this component's GameObject. It is cancelled every time the GameObject is
        /// deactivated, and the next activation starts a fresh generation; it is disposed when the GameObject is
        /// destroyed. The first call adds a hidden <see cref="ActiveLifetimeTrigger"/> to the GameObject, or binds the one
        /// a lifetime-bound coroutine already added there. Work registered while the GameObject is inactive is
        /// terminated at once, with the development warning JANITOR108.
        /// It follows GameObject activation, not the <c>enabled</c> flag of a component. Disabling the hidden trigger
        /// itself (a loop that disables every behaviour reaches it) is reported by Unity like a destruction, so the
        /// work registered on the active lifetime is cancelled once; the trigger enables itself again the next time
        /// the lifetime is used, so later deactivations are seen.
        /// </summary>
        /// <param name="component">Any component on the GameObject.</param>
        /// <returns>The active lifetime, or a disposed lifetime when the owner was destroyed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static Lifetime GetActiveLifetime(this Component component)
        {
            if (ReferenceEquals(component, null))
            {
                throw new ArgumentNullException(nameof(component));
            }

            var tree = RequireTree("GetActiveLifetime");
            return ComponentLifetimes.ForActive(tree, component, null, false);
        }

        /// <summary>
        /// Like <see cref="GetActiveLifetime(Component)"/>, and places the active lifetime in a category, with the
        /// same rules as <see cref="GetLifetime(MonoBehaviour, Lifetime)"/>: the first access decides the parent, a
        /// mismatch raises JANITOR113, and disposing the category cancels and re-homes the lifetime.
        /// </summary>
        /// <param name="component">Any component on the GameObject.</param>
        /// <param name="parent">The category.</param>
        /// <returns>The active lifetime, or a disposed lifetime when the owner was destroyed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="component"/> or <paramref name="parent"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static Lifetime GetActiveLifetime(this Component component, Lifetime parent)
        {
            if (ReferenceEquals(component, null))
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var tree = RequireTree("GetActiveLifetime");
            return ComponentLifetimes.ForActive(tree, component, parent, true);
        }

        // The lifetime a component owner resolves to: its own for a MonoBehaviour, otherwise its GameObject's.
        // Null for a C# null owner, so the caller's own argument check reports it.
        internal static Lifetime ResolveOwnerOrNull(Component owner)
        {
            if (ReferenceEquals(owner, null))
            {
                return null;
            }

            return ResolveOwner(owner);
        }

        internal static Lifetime ResolveOwner(Component owner)
        {
            var tree = RequireTree("Component owner lookup");
            if (owner == null)
            {
                return tree.Sentinel;
            }

            var behaviour = owner as MonoBehaviour;
            return behaviour != null
                ? ComponentLifetimes.ForBehaviour(tree, behaviour, null, false)
                : ComponentLifetimes.ForGameObject(tree, owner.gameObject);
        }

        internal static Lifetime ResolveOwner(GameObject owner)
        {
            var tree = RequireTree("GameObject owner lookup");
            return ComponentLifetimes.ForGameObject(tree, owner);
        }

        // Play Mode and the main thread are required before any Unity object is touched.
        private static LifetimeTree RequireTree(string api)
        {
            var tree = LifetimeTree.RequireDefault(api);
            tree.Guard.EnsureMainThread(api);
            return tree;
        }
    }
}
