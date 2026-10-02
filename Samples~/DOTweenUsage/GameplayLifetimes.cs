using Ecanakli.Janitor;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Named areas that objects join, or that other classes register work into.</summary>
    public sealed class GameplayLifetimes
    {
        public Lifetime Popups { get; }
        public Lifetime Combat { get; }

        public GameplayLifetimes(Lifetime life)
        {
            Popups = life.CreateChild("Popups");
            Combat = life.CreateChild("Combat");
        }
    }
}
