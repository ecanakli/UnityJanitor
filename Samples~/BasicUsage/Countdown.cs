using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A countdown restarted with Cancel() and then Run; it stops with its object.</summary>
    public sealed class Countdown : MonoBehaviour
    {
        private readonly OwnedEvent _finished = new("Finished");
        private TMP_Text _label;
        private Lifetime _run;

        public IOwnedEvent Finished => _finished;

        // The view arrives here because the demo builds its UI in code.
        public void Initialize(TMP_Text label) => _label = label;

        private void Awake() => _run = this.GetLifetime().CreateChild("Run");

        public void StartCountdown(int seconds)
        {
            _run.Cancel();                                    // a countdown already running stops here
            _run.Run(ct => TickAsync(seconds, ct));
        }

        public void StopCountdown() => _run.Cancel();

        private async UniTask TickAsync(int seconds, CancellationToken ct)
        {
            for (var left = seconds; left > 0; left--)
            {
                _label.text = left.ToString();
                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
            }

            _label.text = "Go!";
            _finished.Invoke();
        }
    }
}
