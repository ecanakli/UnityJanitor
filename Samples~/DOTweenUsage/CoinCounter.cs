using DG.Tweening;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>A counter whose running roll jumps to its end value when the next roll starts.</summary>
    public sealed class CoinCounter : MonoBehaviour
    {
        private TMP_Text _label;
        private Lifetime _roll;
        private int _shown;

        // The view arrives here because the demo builds its UI in code.
        public void Initialize(TMP_Text label) => _label = label;

        private void Awake() => _roll = this.GetLifetime().CreateChild("Roll");

        public void RollTo(int target)
        {
            _roll.Cancel();   // Complete mode: a roll still running jumps to its end value instead of freezing midway
            DOTween.To(() => _shown, SetShown, target, 0.5f).AddTo(_roll, TweenCancelMode.Complete);
        }

        private void SetShown(int value)
        {
            _shown = value;
            _label.text = value.ToString();
        }
    }
}
