namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Stops the Combat category without knowing who registered what into it.</summary>
    public sealed class CombatFlow
    {
        private readonly GameplayLifetimes _lifetimes;

        public CombatFlow(GameplayLifetimes lifetimes) => _lifetimes = lifetimes;

        public void OnCombatInterrupted() => _lifetimes.Combat.Cancel();
    }
}
