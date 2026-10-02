using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>A toast: a new message replaces the old one, and a badge pulses for as long as the component exists.</summary>
    public sealed class ToastView : MonoBehaviour
    {
        private Transform _panel;
        private Transform _badge;
        private TMP_Text _label;
        private Lifetime _toast;

        // The views arrive here because the demo builds its UI in code; call it before the object is enabled.
        public void Initialize(Transform panel, Transform badge, TMP_Text label)
        {
            _panel = panel;
            _badge = badge;
            _label = label;
        }

        private void Awake()
        {
            _toast = this.GetLifetime().CreateChild("Toast");
            _badge.DOScale(1.2f, 0.6f).SetLoops(-1, LoopType.Yoyo).AddTo(this);   // the component owner: it ends with this component
        }

        public void Show(string message)
        {
            _toast.Cancel();                                   // the previous toast stops
            _toast.Run(ct => ShowAsync(message, ct));
        }

        private async UniTask ShowAsync(string message, CancellationToken ct)
        {
            _label.text = message;
            _panel.localScale = Vector3.zero;

            // The lifetime overload registers the tween on _toast and awaits it: a cancel kills it and throws.
            await _panel.DOScale(1f, 0.25f).SetEase(Ease.OutBack).AwaitCompletionAsync(_toast);
            await UniTask.Delay(TimeSpan.FromSeconds(1.5), cancellationToken: ct);
            await _panel.DOScale(0f, 0.2f).AwaitCompletionAsync(_toast);
        }
    }
}
