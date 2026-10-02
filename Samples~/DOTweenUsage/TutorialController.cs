using System.Collections;
using DG.Tweening;
using Ecanakli.Janitor;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Three ways to cancel: the whole lifetime, a child area, or one registration.</summary>
    public sealed class TutorialController : MonoBehaviour
    {
        private RectTransform _arrow;
        private CanvasGroup _hint;
        private Button _skipButton;

        private Lifetime _step;
        private LifetimeRegistration _hintPulse;

        // The views arrive here because the demo builds its UI in code; call it before the object is enabled.
        public void Initialize(RectTransform arrow, CanvasGroup hint, Button skipButton)
        {
            _arrow = arrow;
            _hint = hint;
            _skipButton = skipButton;
        }

        private void Awake()
        {
            _step = this.GetLifetime().CreateChild("Step");
            _skipButton.onClick.Subscribe(SkipStep, this);            // lives on the whole lifetime
        }

        public void ShowStep(TutorialStep step)
        {
            _step.Cancel();                                            // the previous step's work stops
            _arrow.DOLocalMove(step.ArrowPosition, 0.6f).SetLoops(-1, LoopType.Yoyo).AddTo(_step);
            _hintPulse = _step.StartCoroutine(this, PulseHint());
            step.Target.onClick.Subscribe(CompleteStep, _step);
        }

        public void OnPlayerMoved() => _hintPulse.Cancel();            // 3. one registration: only the pulse stops
        public void SkipStep() => _step.Cancel();                      // 2. child area: the whole step stops
        public void Abort() => this.GetLifetime().Cancel();            // 1. whole lifetime: everything, the skip listener too

        private IEnumerator PulseHint()
        {
            while (true)
            {
                _hint.alpha = Mathf.PingPong(Time.time, 1f);
                yield return null;
            }
        }

        private void CompleteStep()
        {
            Debug.Log("[TutorialController] Step completed.");
            _step.Cancel();   // advance the tutorial here; the step's arrow, pulse and listener end with it
        }
    }
}
