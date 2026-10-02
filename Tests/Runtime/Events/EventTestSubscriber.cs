using UnityEngine;
using UnityEngine.Events;

namespace Ecanakli.Janitor.Tests.Events
{
    // Subscribes to its source in OnEnable with its active lifetime: the pattern the duplicate rule and the trigger's OnDisable exist for.
    [AddComponentMenu("")]
    internal sealed class EventTestSubscriber : MonoBehaviour
    {
        internal UnityEvent Source;
        internal int Calls;

        private void OnEnable()
        {
            if (Source != null)
            {
                Source.Subscribe(OnSource, this.GetActiveLifetime());
            }
        }

        private void OnSource()
        {
            Calls++;
        }
    }
}
