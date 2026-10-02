#pragma warning disable CS0649 // Assigned by Zenject.

using Ecanakli.Janitor;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>Builds a small uGUI scene in code and creates the sample's components through Zenject.</summary>
    public sealed class ZenjectUsageDemo : MonoBehaviour
    {
        [Inject] private IInstantiator _instantiator;
        [Inject] private SignalBus _signalBus;
        [Inject] private Wallet _wallet;
        [Inject] private GameplayLifetimes _lifetimes;
        [Inject] private FakeAdsSdk _ads;
        [InjectOptional] private SceneFlow _sceneFlow;

        private RectTransform _canvas;
        private ShopPopup _popup;
        private TMP_Text _hudCoins;

        private void Start()
        {
            if (_instantiator == null)
            {
                Debug.LogError("ZenjectUsageDemo: nothing was injected. Add a Scene Context with GameObject > Zenject > Scene Context (see the README).");
                return;
            }

            if (_signalBus == null || _wallet == null || _lifetimes == null || _ads == null)
            {
                Debug.LogError("ZenjectUsageDemo: the Scene Context did not bind the sample's services. Drag the GameplayInstaller component into its Mono Installers list.");
                return;
            }

            _canvas = DemoUi.CreateCanvas();
            _hudCoins = DemoUi.CreateLabel(_canvas, "CoinsHud", "Coins: 0", 44f, DemoUi.TopRight, new Vector2(-40f, -40f), new Vector2(400f, 70f));

            BuildPopup();
            BuildPlatformHooks();
            BuildStoreBanner();
            BuildButtons();

            _wallet.CoinsChanged.Subscribe(OnCoinsChanged, this);

            if (EventSystem.current == null)
            {
                Debug.LogWarning("ZenjectUsageDemo: no EventSystem in the scene, so the buttons will not respond. Add one with GameObject > UI > Event System.");
            }
        }

        private void BuildPopup()
        {
            // Created inactive, injected, then enabled by the button, so OnEnable always sees its dependencies.
            var rect = DemoUi.CreateRect(_canvas, "ShopPopup", active: false);
            DemoUi.Place(rect, DemoUi.Center, DemoUi.Pick(new Vector2(520f, -120f), new Vector2(0f, -285f)), new Vector2(560f, 470f));
            rect.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.12f, 0.16f, 0.95f);

            DemoUi.CreateLabel(rect, "Title", "Shop", 40f, DemoUi.Center, new Vector2(0f, 190f), new Vector2(500f, 60f));
            var badge = DemoUi.CreateLabel(rect, "SaleBadge", "SALE!", 54f, DemoUi.Center, new Vector2(0f, 110f), new Vector2(300f, 70f));
            badge.color = new Color(1f, 0.6f, 0.1f, 1f);
            var coins = DemoUi.CreateLabel(rect, "CoinsLabel", "0", 36f, DemoUi.Center, new Vector2(0f, 20f), new Vector2(500f, 60f));
            var buy = DemoUi.CreateButton(rect, "Buy 100 coins");
            DemoUi.Place((RectTransform)buy.transform, DemoUi.Center, new Vector2(0f, -150f), new Vector2(360f, 70f));

            _popup = _instantiator.InstantiateComponent<ShopPopup>(rect.gameObject);
            _popup.Initialize(buy, badge, coins);

            // A child of the popup: its own active lifetime follows the popup, so the watcher lives while the popup is open.
            var watch = DemoUi.CreateRect(rect, "OfferWatch");
            _instantiator.InstantiateComponent<OfferWatch>(watch.gameObject);
        }

        private void BuildPlatformHooks()
        {
            // Inactive while Zenject injects it, so Awake sees the SDK.
            var hooks = new GameObject("PlatformHooks");
            hooks.SetActive(false);
            _instantiator.InstantiateComponent<PlatformHooks>(hooks);
            hooks.SetActive(true);
        }

        private void BuildStoreBanner()
        {
            var label = DemoUi.CreateLabel(_canvas, "StoreBanner", "Store refreshed 0 time(s)", 30f, DemoUi.TopRight, new Vector2(-40f, -120f), new Vector2(400f, 50f));

            // Inactive while Zenject injects it, so Awake sees the bus and the lifetime.
            label.gameObject.SetActive(false);
            _instantiator.InstantiateComponent<StoreBanner>(label.gameObject).Initialize(label);
            label.gameObject.SetActive(true);
        }

        private void BuildButtons()
        {
            var grid = DemoUi.CreateButtonGrid(_canvas);

            var shop = DemoUi.CreateButton(grid, "Shop: open / close");
            var fire = DemoUi.CreateButton(grid, "Fire StoreRefreshedSignal");
            var addCoins = DemoUi.CreateButton(grid, "Add 25 coins");
            var cancelPopups = DemoUi.CreateButton(grid, "Cancel popups");
            var adReward = DemoUi.CreateButton(grid, "Ad: reward granted");
            var adClosed = DemoUi.CreateButton(grid, "Ad: closed");
            var reload = DemoUi.CreateButton(grid, "Reload scene");

            // Each subscription names its owner; nothing below is ever removed by hand.
            shop.onClick.Subscribe(ToggleShop, this);
            fire.onClick.Subscribe(FireStoreRefreshed, this);
            addCoins.onClick.Subscribe(AddCoins, this);
            cancelPopups.onClick.Subscribe(CancelPopups, this);
            adReward.onClick.Subscribe(AdReward, this);
            adClosed.onClick.Subscribe(AdClosed, this);
            reload.onClick.Subscribe(ReloadScene, this);

            // The load needs SceneFlow from the ProjectContext and the scene in Build Settings.
            if (_sceneFlow == null)
            {
                reload.interactable = false;
                DemoUi.SetText(reload, "Reload: add ProjectInstaller to the ProjectContext");
            }
            else if (SceneUtility.GetBuildIndexByScenePath(gameObject.scene.path) < 0)
            {
                reload.interactable = false;
                DemoUi.SetText(reload, "Reload: add scene to Build Settings");
            }
        }

        private void ToggleShop() => _popup.gameObject.SetActive(!_popup.gameObject.activeSelf);
        private void FireStoreRefreshed() => _signalBus.Fire(new StoreRefreshedSignal());
        private void AddCoins() => _wallet.Add(25);
        private void CancelPopups() => _lifetimes.Popups.Cancel();
        private void AdReward() => _ads.RaiseRewardGranted("daily");
        private void AdClosed() => _ads.RaiseClosed();
        private void ReloadScene() => _sceneFlow.Load(gameObject.scene.path);
        private void OnCoinsChanged(int coins) => _hudCoins.text = $"Coins: {coins}";
    }
}
