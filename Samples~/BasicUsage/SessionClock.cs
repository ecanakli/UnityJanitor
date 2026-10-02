using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Two repeating timers on the component: one follows the time scale, one ignores it.</summary>
    public sealed class SessionClock : MonoBehaviour
    {
        private TMP_Text _label;
        private int _gameSeconds;
        private int _realSeconds;

        // The view arrives here because the demo builds its UI in code.
        public void Initialize(TMP_Text label) => _label = label;

        private void Start()
        {
            this.Every(1f, TickGame);                                   // stops while Time.timeScale is 0
            this.Every(1f, TickReal, ignoreTimeScale: true);            // keeps running while the game is paused
        }

        private void TickGame()
        {
            _gameSeconds++;
            Refresh();
        }

        private void TickReal()
        {
            _realSeconds++;
            Refresh();
        }

        private void Refresh() => _label.text = $"Game {_gameSeconds}s  Real {_realSeconds}s";
    }
}
