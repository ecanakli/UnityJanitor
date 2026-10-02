using System;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>Stand-in for a third-party ads SDK: events the game does not own.</summary>
    public interface IAdsSdk
    {
        event Action<string> RewardGranted;
        event Action Closed;
    }
}
