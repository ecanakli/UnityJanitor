using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Counts a score up once; nothing restarts it, and it ends with the object (or with a cancel of its scene).</summary>
    public sealed class ScoreReveal : MonoBehaviour
    {
        private TMP_Text _label;

        // The view arrives here because the demo builds its UI in code.
        public void Initialize(TMP_Text label) => _label = label;

        // Call it once per object: a second call would start a second task. Countdown shows the restart form.
        public void Reveal(int score) => this.Run(ct => RevealAsync(score, ct));

        private async UniTask RevealAsync(int score, CancellationToken ct)
        {
            for (var shown = 0; shown < score; shown += Mathf.Max(1, score / 20))
            {
                _label.text = shown.ToString();
                await UniTask.Delay(TimeSpan.FromMilliseconds(50), cancellationToken: ct);
            }

            _label.text = score.ToString();
        }
    }
}
