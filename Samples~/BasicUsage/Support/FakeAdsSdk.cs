using System;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>An ads SDK that raises its events when the demo asks it to.</summary>
    public sealed class FakeAdsSdk : IAdsSdk
    {
        public event Action<string> RewardGranted;
        public event Action Closed;

        public void RaiseRewardGranted(string placement) => RewardGranted?.Invoke(placement);

        public void RaiseClosed() => Closed?.Invoke();
    }
}
