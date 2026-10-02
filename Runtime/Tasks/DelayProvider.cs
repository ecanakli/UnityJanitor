using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor
{
    // Creates the delay behind After and Every. Tests swap in a manual clock through LifetimeTree.Delay.
    internal delegate UniTask DelayProvider(float seconds, bool ignoreTimeScale, CancellationToken token);

    internal static class DelayProviders
    {
        internal static readonly DelayProvider Default = DefaultDelay;

        private static UniTask DefaultDelay(float seconds, bool ignoreTimeScale, CancellationToken token)
        {
            var type = ignoreTimeScale ? DelayType.UnscaledDeltaTime : DelayType.DeltaTime;
            return UniTask.Delay(TimeSpan.FromSeconds(seconds), type, PlayerLoopTiming.Update, token);
        }
    }
}
