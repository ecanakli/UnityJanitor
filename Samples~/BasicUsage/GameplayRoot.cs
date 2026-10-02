using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>The root area of the gameplay: it builds the categories and cancels everything under them.</summary>
    public sealed class GameplayRoot : MonoBehaviour
    {
        private Lifetime _gameplay;

        public GameplayLifetimes Lifetimes { get; private set; }

        private void Awake()
        {
            _gameplay = this.GetLifetime().CreateChild("Gameplay");   // disposed with this object or its scene
            Lifetimes = new GameplayLifetimes(_gameplay);
        }

        // A signal triggers, the tree executes: one handler, no broadcast.
        public void OnResetRequested() => _gameplay.Cancel();          // every area stops; each stays usable
    }
}
