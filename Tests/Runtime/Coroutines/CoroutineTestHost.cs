using UnityEngine;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // A plain host for lifetime-bound coroutines; the tests only need something that can run them.
    [AddComponentMenu("")]
    internal sealed class CoroutineTestHost : MonoBehaviour
    {
    }
}
