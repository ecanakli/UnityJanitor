using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Builds a small uGUI scene in code and wires the sample classes. Add it to an empty GameObject.</summary>
    public sealed class BasicUsageDemo : MonoBehaviour
    {
        // One SceneFlow per Play Mode session, under Lifetime.App so it outlives every reload.
        private static Lifetime _flowArea;
        private static SceneFlow _flow;

        private GameplayRoot _root;
        private Wallet _wallet;
        private FakeAdsSdk _ads;
        private ShopPopup _popup;
        private Countdown _countdown;
        private CoinPool _pool;
        private SfxPlayer _sfx;
        private AudioSource _audio;
        private AudioClip _beep;
        private RectTransform _canvas;
        private RectTransform _coinTarget;
        private RectTransform _launchOrigin;
        private TMP_Text _hudCoins;
        private TMP_Text _combatLabel;
        private int _combatSeconds;
        private GameplayLifetimes _lifetimes;
        private HintPulse _hintPulse;
        private bool _hintPulsing;
        private CountdownTracker _tracker;

        private void Start()
        {
            _canvas = DemoUi.CreateCanvas();
            _wallet = new Wallet();
            _ads = new FakeAdsSdk();
            Time.timeScale = 1f;                                       // a reload keeps the paused state otherwise
            ErrorReporting.Install(new ConsoleErrorReporter());        // every routed error also reaches the game's reporter
            _root = new GameObject("GameplayRoot").AddComponent<GameplayRoot>();   // Awake builds the categories
            var lifetimes = _root.Lifetimes;

            BuildHud();
            BuildPopup(lifetimes);
            BuildSupportObjects();
            BuildExtras(lifetimes);
            BuildButtons();

            _wallet.CoinsChanged.Subscribe(OnCoinsChanged, this);
            _countdown.Finished.Subscribe(OnCountdownFinished, this);
            lifetimes.Combat.Every(1f, OnCombatTick);

            if (EventSystem.current == null)
            {
                Debug.LogWarning("BasicUsageDemo: no EventSystem in the scene, so the buttons will not respond. Add one with GameObject > UI > Event System.");
            }
        }

        private void BuildHud()
        {
            _hudCoins = DemoUi.CreateLabel(_canvas, "CoinsHud", "Coins: 0", 44f, DemoUi.TopRight, new Vector2(-40f, -40f), new Vector2(400f, 70f));
            _coinTarget = _hudCoins.rectTransform;
            _combatLabel = DemoUi.CreateLabel(_canvas, "CombatTicks", "Combat ticks: 0", 30f, DemoUi.TopRight, new Vector2(-40f, -120f), new Vector2(400f, 50f));

            var countdownLabel = DemoUi.CreateLabel(_canvas, "CountdownLabel", string.Empty, 80f, DemoUi.TopRight, new Vector2(-40f, -180f), new Vector2(400f, 120f));
            _countdown = countdownLabel.gameObject.AddComponent<Countdown>();
            _countdown.Initialize(countdownLabel);
        }

        private void BuildPopup(GameplayLifetimes lifetimes)
        {
            // Created inactive and enabled by the button, so OnEnable always sees what Initialize gave it.
            var rect = DemoUi.CreateRect(_canvas, "ShopPopup", active: false);
            DemoUi.Place(rect, DemoUi.Center, DemoUi.Pick(new Vector2(520f, -120f), new Vector2(0f, -285f)), new Vector2(560f, 470f));
            rect.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.12f, 0.16f, 0.95f);

            DemoUi.CreateLabel(rect, "Title", "Shop", 40f, DemoUi.Center, new Vector2(0f, 190f), new Vector2(500f, 60f));
            var badge = DemoUi.CreateLabel(rect, "SaleBadge", "SALE!", 54f, DemoUi.Center, new Vector2(0f, 110f), new Vector2(300f, 70f));
            badge.color = new Color(1f, 0.6f, 0.1f, 1f);
            var coins = DemoUi.CreateLabel(rect, "CoinsLabel", "0", 36f, DemoUi.Center, new Vector2(0f, 20f), new Vector2(500f, 60f));
            var buy = DemoUi.CreateButton(rect, "Buy 100 coins");
            DemoUi.Place((RectTransform)buy.transform, DemoUi.Center, new Vector2(0f, -150f), new Vector2(360f, 70f));

            _popup = rect.gameObject.AddComponent<ShopPopup>();
            _popup.Initialize(lifetimes, _wallet, new OfferService(_wallet), buy, badge, coins);
        }

        private void BuildSupportObjects()
        {
            _pool = new CoinPool(CreateCoin);

            _sfx = new GameObject("SfxPlayer").AddComponent<SfxPlayer>();
            _audio = _sfx.gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _sfx.Initialize(_audio);
            _beep = CreateBeep();
            if (FindFirstObjectByType<AudioListener>() == null)
            {
                gameObject.AddComponent<AudioListener>();
            }

            // Inactive until Initialize has run, so Awake sees the SDK.
            var hooks = new GameObject("PlatformHooks");
            hooks.SetActive(false);
            hooks.AddComponent<PlatformHooks>().Initialize(_ads);
            hooks.SetActive(true);
        }

        private void BuildButtons()
        {
            var grid = DemoUi.CreateButtonGrid(_canvas);

            var shop = DemoUi.CreateButton(grid, "Shop: open / close");
            var addCoins = DemoUi.CreateButton(grid, "Add 25 coins");
            var startCountdown = DemoUi.CreateButton(grid, "Countdown: start");
            var stopCountdown = DemoUi.CreateButton(grid, "Countdown: stop");
            var launch = DemoUi.CreateButton(grid, "Launch coins");
            var sound = DemoUi.CreateButton(grid, "Play sound");
            var adReward = DemoUi.CreateButton(grid, "Ad: reward granted");
            var adClosed = DemoUi.CreateButton(grid, "Ad: closed");
            var cancelPopups = DemoUi.CreateButton(grid, "Cancel popups");
            var cancelEverything = DemoUi.CreateButton(grid, "Cancel everything");
            var reload = DemoUi.CreateButton(grid, "Reload scene");
            _launchOrigin = (RectTransform)launch.transform;

            // Each subscription names its owner; nothing below is ever removed by hand.
            shop.onClick.Subscribe(ToggleShop, this);
            addCoins.onClick.Subscribe(AddCoins, this);
            startCountdown.onClick.Subscribe(StartCountdown, this);
            stopCountdown.onClick.Subscribe(StopCountdown, this);
            launch.onClick.Subscribe(LaunchCoins, this);
            sound.onClick.Subscribe(PlaySound, this);
            adReward.onClick.Subscribe(AdReward, this);
            adClosed.onClick.Subscribe(AdClosed, this);
            cancelPopups.onClick.Subscribe(CancelPopups, this);
            cancelEverything.onClick.Subscribe(CancelEverything, this);
            reload.onClick.Subscribe(ReloadScene, this);

            var raiseError = DemoUi.CreateButton(grid, "Raise an error");
            var combatEffect = DemoUi.CreateButton(grid, "Combat effect");
            var hintPulse = DemoUi.CreateButton(grid, "Hint pulse: start / stop");
            var pause = DemoUi.CreateButton(grid, "Pause / resume game time");
            raiseError.onClick.Subscribe(RaiseError, this);
            combatEffect.onClick.Subscribe(SpawnCombatEffect, this);
            hintPulse.onClick.Subscribe(ToggleHintPulse, this);
            pause.onClick.Subscribe(TogglePause, this);

            var spawnCountdown = DemoUi.CreateButton(grid, "Spawn a countdown");
            var revealScore = DemoUi.CreateButton(grid, "Reveal a score");
            spawnCountdown.onClick.Subscribe(SpawnCountdown, this);
            revealScore.onClick.Subscribe(RevealScore, this);

            // The load needs the scene in Build Settings.
            if (SceneUtility.GetBuildIndexByScenePath(gameObject.scene.path) < 0)
            {
                reload.interactable = false;
                DemoUi.SetText(reload, "Reload: add scene to Build Settings");
            }
        }

        private void ToggleShop() => _popup.gameObject.SetActive(!_popup.gameObject.activeSelf);
        private void AddCoins() => _wallet.Add(25);
        private void StartCountdown() => _countdown.StartCountdown(10);
        private void StopCountdown() => _countdown.StopCountdown();
        private void LaunchCoins() => this.Run(LaunchCoinsAsync);
        private void AdReward() => _ads.RaiseRewardGranted("daily");
        private void AdClosed() => _ads.RaiseClosed();
        private void CancelPopups() => _root.Lifetimes.Popups.Cancel();
        private void CancelEverything() => _root.OnResetRequested();
        private void ReloadScene() => GetSceneFlow().Load(gameObject.scene.path);
        private void RaiseError() => this.Run(FailAsync);
        private void TogglePause() => Time.timeScale = Time.timeScale > 0f ? 0f : 1f;

        private void BuildExtras(GameplayLifetimes lifetimes)
        {
            _lifetimes = lifetimes;

            var clockLabel = DemoUi.CreateLabel(_canvas, "ClockLabel", "Game 0s  Real 0s", 28f, DemoUi.TopRight, new Vector2(-40f, -310f), new Vector2(400f, 40f));
            clockLabel.gameObject.AddComponent<SessionClock>().Initialize(clockLabel);

            // Inactive until Initialize has run, so Awake sees the wallet.
            var walletLabel = DemoUi.CreateLabel(_canvas, "WalletLabel", string.Empty, 28f, DemoUi.TopRight, new Vector2(-40f, -355f), new Vector2(400f, 40f));
            walletLabel.gameObject.SetActive(false);
            walletLabel.gameObject.AddComponent<CoinsLabel>().Initialize(_wallet, walletLabel);
            walletLabel.gameObject.SetActive(true);

            var hint = DemoUi.CreateLabel(_canvas, "HintLabel", "Tap the shop button", 32f, DemoUi.Center, DemoUi.Pick(new Vector2(0f, -300f), new Vector2(0f, -560f)), new Vector2(380f, 50f));
            _hintPulse = hint.gameObject.AddComponent<HintPulse>();
            _hintPulse.Initialize(hint.gameObject.AddComponent<CanvasGroup>());

            // A child of the popup: its own active lifetime follows the popup, so the block lasts while the popup is open.
            var blocker = DemoUi.CreateRect(_popup.transform, "ModalBlocker").gameObject.AddComponent<ModalBlocker>();
            blocker.Initialize(new InputGate());

            _tracker = new GameObject("CountdownTracker").AddComponent<CountdownTracker>();
        }

        private void ToggleHintPulse()
        {
            if (_hintPulsing)
            {
                _hintPulse.StopPulse();
            }
            else
            {
                _hintPulse.StartPulse();
            }

            _hintPulsing = !_hintPulsing;
        }

        private void SpawnCombatEffect()
        {
            var origin = DemoUi.Pick(new Vector2(0f, -150f), new Vector2(320f, -640f)) + new Vector2(UnityEngine.Random.Range(-60f, 60f), 0f);
            var label = DemoUi.CreateLabel(_canvas, "CombatEffect", string.Empty, 40f, DemoUi.Center, origin, new Vector2(160f, 50f));
            label.color = new Color(1f, 0.4f, 0.4f, 1f);

            // Inactive until Initialize has run, so Awake sees the category.
            label.gameObject.SetActive(false);
            var effect = label.gameObject.AddComponent<CombatEffect>();
            effect.Initialize(_lifetimes, label);
            label.gameObject.SetActive(true);
            effect.Play(UnityEngine.Random.Range(10, 99));
        }

        private async UniTask FailAsync(CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromMilliseconds(300), cancellationToken: ct);
            throw new InvalidOperationException("Demo failure inside a Run task.");
        }

        private void SpawnCountdown()
        {
            var label = DemoUi.CreateLabel(_canvas, "SpawnedCountdown", string.Empty, 44f, DemoUi.Center, DemoUi.Pick(new Vector2(0f, -230f), new Vector2(360f, -720f)), new Vector2(160f, 60f));
            var countdown = label.gameObject.AddComponent<Countdown>();
            countdown.Initialize(label);
            _tracker.Track(countdown);
            countdown.StartCountdown(3);
            Destroy(label.gameObject, 5f);     // a short-lived source: the tracker keeps nothing of it
        }

        private void RevealScore()
        {
            var label = DemoUi.CreateLabel(_canvas, "ScoreReveal", string.Empty, 44f, DemoUi.Center, DemoUi.Pick(new Vector2(0f, -380f), new Vector2(360f, -800f)), new Vector2(160f, 60f));
            var reveal = label.gameObject.AddComponent<ScoreReveal>();
            reveal.Initialize(label);
            reveal.Reveal(UnityEngine.Random.Range(500, 5000));
            Destroy(label.gameObject, 3f);
        }

        private void PlaySound() => _sfx.PlayAndRelease(_beep);

        private void OnCoinsChanged(int coins) => _hudCoins.text = $"Coins: {coins}";
        private void OnCountdownFinished() => Debug.Log("[Demo] Countdown finished.");

        private void OnCombatTick()
        {
            _combatSeconds++;
            _combatLabel.text = $"Combat ticks: {_combatSeconds}";
        }

        private async UniTask LaunchCoinsAsync(CancellationToken ct)
        {
            for (var i = 0; i < 5; i++)
            {
                var coin = _pool.Rent();
                coin.transform.position = _launchOrigin.position;
                coin.Launch(_coinTarget, _pool);
                await UniTask.Delay(TimeSpan.FromMilliseconds(120), cancellationToken: ct);
            }
        }

        private CoinPickup CreateCoin()
        {
            var image = DemoUi.CreateImage(_canvas, "Coin", new Color(1f, 0.85f, 0.2f, 1f), DemoUi.Center, Vector2.zero, new Vector2(36f, 36f));
            image.gameObject.SetActive(false);
            return image.gameObject.AddComponent<CoinPickup>();
        }

        private static SceneFlow GetSceneFlow()
        {
            // A new Play Mode session disposes the old App tree, so a stale area is replaced here.
            if (_flowArea == null || _flowArea.IsDisposed)
            {
                _flowArea = Lifetime.App.CreateChild("SceneFlow");
                _flow = new SceneFlow(_flowArea);
            }

            return _flow;
        }

        private static AudioClip CreateBeep()
        {
            const int rate = 22050;
            var samples = new float[rate];
            for (var i = 0; i < samples.Length; i++)
            {
                var fade = 1f - (float)i / samples.Length;
                samples[i] = Mathf.Sin(2f * Mathf.PI * 440f * i / rate) * 0.2f * fade;
            }

            var clip = AudioClip.Create("Beep", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
