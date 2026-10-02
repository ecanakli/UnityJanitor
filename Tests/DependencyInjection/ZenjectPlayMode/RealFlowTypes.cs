using Zenject;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // Creates plain objects that inject a Lifetime.
    internal sealed class AlphaFactory : PlaceholderFactory<AlphaService>
    {
    }

    // Creates a MonoBehaviour that injects its Lifetime, from a prefab built in code.
    internal sealed class InjectedBehaviourFactory : PlaceholderFactory<InjectedBehaviour>
    {
    }

    // The root of a facade: the context it lives in and a plain service that injects a Lifetime.
    internal sealed class FacadeRoot
    {
        public readonly Context Context;
        public readonly AlphaService Service;

        public FacadeRoot(Context context, AlphaService service)
        {
            Context = context;
            Service = service;
        }
    }

    // Creates a facade: a GameObject with a GameObjectContext and its own container.
    internal sealed class FacadeRootFactory : PlaceholderFactory<FacadeRoot>
    {
    }
}
