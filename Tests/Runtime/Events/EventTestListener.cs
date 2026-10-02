using UnityEngine;

namespace Ecanakli.Janitor.Tests.Events
{
    // A Unity object that serves as a handler target (destroyed-target cases) and as a component owner.
    [AddComponentMenu("")]
    internal sealed class EventTestListener : MonoBehaviour
    {
        internal int Calls;

        internal void Zero()
        {
            Calls++;
        }

        internal void One(int value)
        {
            Calls++;
        }
    }
}
