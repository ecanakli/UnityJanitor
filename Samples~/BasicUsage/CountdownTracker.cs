using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Lives as long as the scene and counts the countdowns that finish; each countdown owns its subscription.</summary>
    public sealed class CountdownTracker : MonoBehaviour
    {
        private int _finished;

        public void Track(Countdown countdown)
        {
            // The owner is the countdown, not this tracker: it ends first, so the entry leaves with it instead of piling up here.
            countdown.Finished.Subscribe(OnFinished, countdown);
        }

        private void OnFinished() => Debug.Log($"[CountdownTracker] {++_finished} countdown(s) finished.");
    }
}
