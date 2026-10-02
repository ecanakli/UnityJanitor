using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Awaits a tween and shows the three ways to handle a cancel inside an async method.</summary>
    public sealed class RewardView : MonoBehaviour
    {
        private Transform _chest;
        private TMP_Text _amountLabel;
        private Lifetime _show;

        // The views arrive here because the demo builds its UI in code.
        public void Initialize(Transform chest, TMP_Text amountLabel)
        {
            _chest = chest;
            _amountLabel = amountLabel;
        }

        private void Awake() => _show = this.GetLifetime().CreateChild("Show");

        public void Show(int amount)
        {
            _show.Cancel();                                    // a show already running stops here
            _show.Run(ct => ShowAsync(amount, ct));
        }

        private async UniTask ShowAsync(int amount, CancellationToken ct)
        {
            // Reset, so the view can be shown again after a cancel.
            _chest.gameObject.SetActive(true);
            _chest.localRotation = Quaternion.identity;
            _amountLabel.text = "0";

            // Run already ends silently on cancel: no log, no try/catch needed.
            await _chest.DOShakeRotation(0.4f, 15f).AwaitCompletionAsync(ct);

            // Cleanup on cancel: catch, clean up, rethrow.
            try
            {
                await CountUpAsync(amount, ct);
            }
            catch (OperationCanceledException)
            {
                if (this != null) _amountLabel.text = amount.ToString();   // land on the final value unless destroyed
                throw;
            }

            // A bool instead of an exception: UniTask's SuppressCancellationThrow.
            var cancelled = await UniTask.Delay(TimeSpan.FromSeconds(2), cancellationToken: ct).SuppressCancellationThrow();
            if (cancelled) return;
            _chest.gameObject.SetActive(false);
        }

        private async UniTask CountUpAsync(int amount, CancellationToken ct)
        {
            for (var shown = 0; shown <= amount; shown += Mathf.Max(1, amount / 30))
            {
                _amountLabel.text = shown.ToString();

                // Immediate: the catch above runs inside Cancel(), before a restarted show resets the label.
                await UniTask.Yield(PlayerLoopTiming.Update, ct, cancelImmediately: true);
            }

            _amountLabel.text = amount.ToString();   // the last step may stop short of the amount
        }
    }
}
