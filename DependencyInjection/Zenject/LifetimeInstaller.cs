using System;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection
{
    /// <summary>
    /// Binds <see cref="Lifetime"/> in a Zenject container, so services and MonoBehaviours can inject their lifetime.
    /// Call <see cref="Install"/> from an installer of the ProjectContext, of every SceneContext and of every
    /// GameObjectContext whose plain services inject a <see cref="Lifetime"/>. The context lifetime is captured at install
    /// time: <see cref="Lifetime.App"/> for the ProjectContext, the scene lifetime for a SceneContext, the component
    /// lifetime of the context for a GameObjectContext.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A MonoBehaviour injectee receives its component lifetime (<c>GetLifetime()</c>). That counts as its first access,
    /// so it fixes the default parent. Any other injectee receives a new child of the context lifetime, named after the
    /// injected type. The child ends with the context, and the service may cancel it. It stays until then, so dispose it
    /// when an object created at run time is done with its work.
    /// </para>
    /// <para>
    /// A GameObjectContext that does not call <see cref="Install"/> hands its plain services the lifetime of an outer
    /// context, so their work outlives the GameObjectContext; the development warning JANITOR112 reports it once per GameObject name.
    /// </para>
    /// <para>
    /// A SceneContext also gets a disposable that ends the scene lifetime first among the container's disposables. This
    /// is the second fallback when the game skips <c>SceneLifetimes.Dispose</c>. No SignalBus binding is added here.
    /// </para>
    /// </remarks>
    public static class LifetimeInstaller
    {
        // DisposableManager orders by priority and then reverses the list, so the highest priority is disposed first.
        private const int DisposeFirst = int.MaxValue;

        /// <summary>
        /// Binds <see cref="Lifetime"/> in <paramref name="container"/> and, for a SceneContext, binds the disposer that ends
        /// the scene lifetime first. Installing the same container twice does nothing. The context is found through the
        /// container's own <c>Context</c> binding; a container that is not a context (a sub-container built by a binder
        /// method, or a bare container in a test) uses its nearest enclosing context, or <see cref="Lifetime.App"/> when there is none.
        /// While the container validates nothing is captured, so scene validation works outside Play Mode.
        /// </summary>
        /// <param name="container">The container of the context being installed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="container"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode (no lifetime tree exists) or off the main thread.</exception>
        /// <exception cref="ArgumentException">The SceneContext's scene is not valid.</exception>
        public static void Install(DiContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            // A second Lifetime binding in the same container would be ambiguous to Zenject.
            if (container.HasBindingId(typeof(Lifetime), null, InjectSources.Local))
            {
                return;
            }

            var local = ContextLookup.FindLocal(container);
            var context = local != null ? local : ContextLookup.FindInAncestors(container);
            var contextLifetime = container.IsValidating ? null : CaptureLifetime(context);

            var resolver = new ContextLifetimeResolver(container, contextLifetime);
            container.Bind<Lifetime>().FromMethod(resolver.Resolve).AsTransient();

            if (local is SceneContext)
            {
                container.BindInterfacesTo<SceneContextLifetimeDisposer>().AsSingle();
                container.BindDisposableExecutionOrder<SceneContextLifetimeDisposer>(DisposeFirst);
            }
        }

        // ProjectContext and a container without a context: App. SceneContext: its scene lifetime.
        // GameObjectContext, or any other context: the component lifetime of the context itself.
        private static Lifetime CaptureLifetime(Context context)
        {
            if (context == null || context is ProjectContext)
            {
                return Lifetime.App;
            }

            if (context is SceneContext)
            {
                return SceneLifetimes.Get(context.gameObject.scene);
            }

            return context.GetLifetime();
        }
    }
}
