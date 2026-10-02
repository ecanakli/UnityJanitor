using Ecanakli.Janitor;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>Bound once per scene; Zenject injects the lifetime, a child of the scene lifetime.</summary>
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
