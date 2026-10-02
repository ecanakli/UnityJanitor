using Ecanakli.Janitor;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>Holds the coin count and publishes changes through a subscribe-only event.</summary>
    public sealed class Wallet
    {
        private readonly OwnedEvent<int> _coinsChanged = new("CoinsChanged");
        private int _coins;

        public int Coins => _coins;

        public IOwnedEvent<int> CoinsChanged => _coinsChanged;   // outsiders subscribe; only Wallet invokes

        public void Add(int amount)
        {
            _coins += amount;
            _coinsChanged.Invoke(_coins);
        }
    }
}
