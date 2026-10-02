using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>CPU work on the thread pool that stops on cancel and returns to the main thread before it touches Unity.</summary>
    public sealed class BackgroundWork : MonoBehaviour
    {
        private TMP_Text _label;
        private Lifetime _job;

        // The view arrives here because the demo builds its UI in code.
        public void Initialize(TMP_Text label) => _label = label;

        private void Awake() => _job = this.GetLifetime().CreateChild("Job");

        public void CountPrimes(int limit)
        {
            _job.Cancel();                                 // a job already running stops here, so only the latest answer lands
            _job.Run(ct => CountPrimesAsync(limit, ct));
        }

        private async UniTask CountPrimesAsync(int limit, CancellationToken ct)
        {
            await UniTask.SwitchToThreadPool();
            var count = 0;
            for (var n = 2; n < limit; n++)
            {
                ct.ThrowIfCancellationRequested();     // a thread-pool loop does not stop unless it checks the token
                if (IsPrime(n))
                {
                    count++;
                }
            }

            // The token form throws after a cancel; the token-less form would resume here and touch a dead label.
            await UniTask.SwitchToMainThread(ct);
            _label.text = $"{count} primes below {limit}";
        }

        private static bool IsPrime(int n)
        {
            for (var d = 2; d * d <= n; d++)
            {
                if (n % d == 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
