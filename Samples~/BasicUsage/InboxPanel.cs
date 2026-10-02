using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>An inbox panel: hiding it stops everything, and Refresh restarts only the load.</summary>
    public sealed class InboxPanel : MonoBehaviour
    {
        private InboxService _inbox;
        private Button _refreshButton;
        private Graphic _spinner;
        private TMP_Text _unreadLabel;
        private TMP_Text _statusLabel;
        private Lifetime _load;

        // Arrives here instead of [Inject] and the Inspector; call it before the object is enabled.
        public void Initialize(InboxService inbox, Button refreshButton, Graphic spinner, TMP_Text unreadLabel, TMP_Text statusLabel)
        {
            _inbox = inbox;
            _refreshButton = refreshButton;
            _spinner = spinner;
            _unreadLabel = unreadLabel;
            _statusLabel = statusLabel;
        }

        private void OnEnable()
        {
            // Cancelled by SetActive(false); disposed on destroy.
            var shown = this.GetActiveLifetime();

            // An area under the active lifetime: SetActive(false) cancels it, so it is kept and reused.
            _load ??= shown.CreateChild("Load");

            OnUnreadChanged(_inbox.Unread);
            shown.StartCoroutine(this, Spin());
            _refreshButton.onClick.Subscribe(Refresh, shown);
            _inbox.UnreadChanged.Subscribe(OnUnreadChanged, shown);
            _load.Run(LoadAsync);
        }

        // The running load stops here; the spinner and the listeners stay.
        private void Refresh()
        {
            _load.Cancel();
            _load.Run(LoadAsync);
        }

        private async UniTask LoadAsync(CancellationToken ct)
        {
            _statusLabel.text = "Loading...";
            var messages = await _inbox.FetchAsync(ct);
            _statusLabel.text = $"{messages.Count} message(s)";
        }

        private IEnumerator Spin()
        {
            while (true)
            {
                _spinner.rectTransform.Rotate(0f, 0f, -360f * Time.deltaTime);
                yield return null;
            }
        }

        private void OnUnreadChanged(int unread) => _unreadLabel.text = $"Unread: {unread}";
    }
}
