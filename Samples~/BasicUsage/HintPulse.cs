using System.Collections;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A pulsing hint whose coroutine is stopped alone, through the registration handle.</summary>
    public sealed class HintPulse : MonoBehaviour
    {
        private CanvasGroup _hint;
        private LifetimeRegistration _pulse;

        // The view arrives here because the demo builds its UI in code.
        public void Initialize(CanvasGroup hint) => _hint = hint;

        public void StartPulse()
        {
            _pulse.Cancel();                                            // a default or stale handle does nothing
            _pulse = this.GetLifetime().StartCoroutine(this, Pulse());
        }

        // Only this coroutine stops; everything else on the lifetime keeps running.
        public void StopPulse()
        {
            _pulse.Cancel();
            _hint.alpha = 1f;
        }

        private IEnumerator Pulse()
        {
            while (true)
            {
                _hint.alpha = Mathf.PingPong(Time.time, 1f);
                yield return null;
            }
        }
    }
}
