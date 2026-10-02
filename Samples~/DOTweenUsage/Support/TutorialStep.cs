using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Stand-in for a step of the game's tutorial: what to click and where the arrow moves.</summary>
    public sealed class TutorialStep
    {
        public TutorialStep(Button target, Vector3 arrowPosition)
        {
            Target = target;
            ArrowPosition = arrowPosition;
        }

        public Button Target { get; }
        public Vector3 ArrowPosition { get; }
    }
}
