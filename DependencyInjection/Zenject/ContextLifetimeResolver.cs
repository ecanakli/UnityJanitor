using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection
{
    // Backs the Lifetime binding of one context: a MonoBehaviour injectee gets its component lifetime, anything else
    // gets a new child of the context lifetime that ends with the context. Main thread only.
    internal sealed class ContextLifetimeResolver
    {
        private readonly DiContainer _container;
        private readonly Lifetime _context;

        // Scenes already reported with the missing-install warning, so a scene with many services logs once.
        private HashSet<int> _reportedScenes;

        // GameObjectContext names already reported, so a facade prefab spawned many times (same clone name) logs once.
        private HashSet<string> _reportedContexts;

        // The context lifetime is null only while the container validates; Zenject never calls Resolve then.
        internal ContextLifetimeResolver(DiContainer container, Lifetime context)
        {
            _container = container;
            _context = context;
        }

        internal Lifetime ContextLifetime => _context;

        internal Lifetime Resolve(InjectContext injectContext)
        {
            // A MonoBehaviour injectee: its component lifetime. This is its first access, so it fixes the default parent.
            if (injectContext.ObjectInstance is MonoBehaviour behaviour)
            {
                return behaviour.GetLifetime();
            }

            if (_context == null)
            {
                throw new InvalidOperationException("Janitor: the Lifetime binding was installed while the container was validating, so it has no context lifetime.");
            }

            CheckContextInstall(injectContext);
            return _context.CreateChild(LabelOf(injectContext));
        }

        private static string LabelOf(InjectContext injectContext)
        {
            var type = injectContext.ObjectType;
            return type != null ? type.Name : "Injected";
        }

        // The service is injected from a SceneContext or GameObjectContext below this resolver's container, and that context
        // never called LifetimeInstaller.Install, so the lifetime came from an outer context.
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void CheckContextInstall(InjectContext injectContext)
        {
            try
            {
                var context = FindContextWithoutInstall(injectContext.Container);
                if (context is SceneContext sceneContext)
                {
                    if (_reportedScenes == null)
                    {
                        _reportedScenes = new HashSet<int>();
                    }

                    if (_reportedScenes.Add(sceneContext.gameObject.scene.handle))
                    {
                        ZenjectDiagnostics.MissingSceneInstall(injectContext.ObjectType, sceneContext, _context);
                    }
                }
                else if (context is GameObjectContext objectContext)
                {
                    if (_reportedContexts == null)
                    {
                        _reportedContexts = new HashSet<string>();
                    }

                    if (_reportedContexts.Add(objectContext.gameObject.name))
                    {
                        ZenjectDiagnostics.MissingContextInstall(injectContext.ObjectType, objectContext, _context);
                    }
                }
            }
            catch (Exception)
            {
                // A diagnostic must never break injection.
            }
        }

        // The nearest SceneContext or GameObjectContext between the injecting container and this resolver's own container, if any.
        private Context FindContextWithoutInstall(DiContainer injecting)
        {
            if (injecting == null || ReferenceEquals(injecting, _container))
            {
                return null;
            }

            var own = ContextLookup.FindLocal(injecting);
            if (NeedsOwnInstall(own))
            {
                return own;
            }

            var ancestors = injecting.AncestorContainers;
            for (var i = 0; i < ancestors.Length; i++)
            {
                if (ReferenceEquals(ancestors[i], _container))
                {
                    return null;
                }

                var context = ContextLookup.FindLocal(ancestors[i]);
                if (NeedsOwnInstall(context))
                {
                    return context;
                }
            }

            return null;
        }

        // Scene and GameObject contexts answer their plain services with their own binding or none; the project context is the outermost.
        private static bool NeedsOwnInstall(Context context)
        {
            return context is SceneContext || context is GameObjectContext;
        }
    }
}
