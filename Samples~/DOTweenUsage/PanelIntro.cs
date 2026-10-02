using DG.Tweening;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Plays an intro Sequence; only the root Sequence is registered, the tweens inside it end with it.</summary>
    public sealed class PanelIntro : MonoBehaviour
    {
        private RectTransform _panel;

        // The view arrives here because the demo builds its UI in code.
        public void Initialize(RectTransform panel) => _panel = panel;

        private void OnEnable()
        {
            var shown = this.GetActiveLifetime();       // cancelled by SetActive(false) and by an outside Cancel

            _panel.localScale = Vector3.zero;
            _panel.localRotation = Quaternion.identity;
            DOTween.Sequence()
                .Append(_panel.DOScale(1.1f, 0.25f).SetEase(Ease.OutQuad))
                .Append(_panel.DOScale(1f, 0.1f))
                .Join(_panel.DOShakeRotation(0.1f, 4f))
                .AddTo(shown, TweenCancelMode.Complete);   // the root only; a cut-short intro lands on its end pose
        }
    }
}
