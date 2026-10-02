using Ecanakli.Janitor;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Named areas that objects join with GetLifetime(area) or GetActiveLifetime(area).</summary>
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
