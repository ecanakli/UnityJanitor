using Zenject;

namespace Ecanakli.Janitor.DependencyInjection
{
    // Finds the Zenject Context behind a container. Every context binds itself as Context in its own container
    // (ProjectContext, SceneContext and GameObjectContext do), so a Local lookup tells a context's container from a sub-container.
    internal static class ContextLookup
    {
        // The Context bound in this container itself; parent containers are not searched. Null for a container that is not a context.
        internal static Context FindLocal(DiContainer container)
        {
            if (!container.HasBindingId(typeof(Context), null, InjectSources.Local))
            {
                return null;
            }

            // Resolving while a context installs logs a Zenject warning in development builds; the flag is only used for that.
            var wasInstalling = container.IsInstalling;
            container.IsInstalling = false;
            try
            {
                var injectContext = new InjectContext(container, typeof(Context))
                {
                    SourceType = InjectSources.Local,
                    Optional = true,
                };
                return container.Resolve(injectContext) as Context;
            }
            finally
            {
                container.IsInstalling = wasInstalling;
            }
        }

        // The nearest enclosing context of a container that has none of its own (a sub-container built by a binder method).
        internal static Context FindInAncestors(DiContainer container)
        {
            var ancestors = container.AncestorContainers;
            for (var i = 0; i < ancestors.Length; i++)
            {
                var context = FindLocal(ancestors[i]);
                if (context != null)
                {
                    return context;
                }
            }

            return null;
        }
    }
}
