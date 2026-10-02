using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.Binding
{
    // The restart shape: OnEnable takes the active lifetime, registers one listener-like entry on it, and runs a load on a child area
    // that is created once. Restart cancels that area and runs the load again.
    [AddComponentMenu("")]
    internal sealed class RestartableLoader : MonoBehaviour
    {
        private Lifetime _load;

        internal readonly OwnedEvent Changed = new OwnedEvent("changed");

        // The token of every load that was started, in order.
        internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();

        internal int Started;

        // How many loads saw their token cancelled.
        internal int Stopped;

        // The area of the load; the same instance for the life of the component.
        internal Lifetime Load => _load;

        internal void Restart()
        {
            _load.Cancel();
            StartLoad();
        }

        private void OnEnable()
        {
            var shown = this.GetActiveLifetime();
            Changed.Subscribe(OnChanged, shown);
            _load ??= shown.CreateChild("Load");
            StartLoad();
        }

        private static void OnChanged()
        {
        }

        private void StartLoad()
        {
            _load.Run(this, static (loader, token) => loader.LoadAsync(token));
        }

        private async UniTask LoadAsync(CancellationToken token)
        {
            Started++;
            Tokens.Add(token);
            token.Register(static state => ((RestartableLoader)state).Stopped++, this);
            await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: token);
        }
    }
}
