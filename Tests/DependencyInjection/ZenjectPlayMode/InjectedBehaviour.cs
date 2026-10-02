using UnityEngine;
using Zenject;

#pragma warning disable CS0649 // Assigned by Zenject.

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // A MonoBehaviour that asks for its lifetime through field injection, the way a scene object does.
    [AddComponentMenu("")]
    internal sealed class InjectedBehaviour : MonoBehaviour
    {
        [Inject]
        private Lifetime _lifetime;

        internal Lifetime Injected => _lifetime;
    }
}
