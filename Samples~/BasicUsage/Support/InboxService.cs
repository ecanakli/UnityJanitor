using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Stand-in for the game's inbox code: a message list, an unread counter and a fake fetch.</summary>
    public sealed class InboxService
    {
        private readonly OwnedEvent<int> _unreadChanged = new("UnreadChanged");
        private readonly List<string> _messages = new();
        private int _unread;

        public int Unread => _unread;

        public IOwnedEvent<int> UnreadChanged => _unreadChanged;

        public void AddMessage()
        {
            _messages.Add($"Message {_messages.Count + 1}");
            _unread++;
            _unreadChanged.Invoke(_unread);
        }

        // Loading the inbox marks everything as read.
        public async UniTask<IReadOnlyList<string>> FetchAsync(CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(1.5), cancellationToken: ct);
            _unread = 0;
            _unreadChanged.Invoke(_unread);
            return _messages.ToArray();
        }
    }
}
