using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Builds a small uGUI scene in code and wires the sample classes. Add it to an empty GameObject.</summary>
    public sealed class DOTweenUsageDemo : MonoBehaviour
    {
        private static readonly Color PanelBlue = new Color(0.25f, 0.45f, 0.9f, 1f);
        private static readonly Color Gold = new Color(1f, 0.85f, 0.2f, 1f);

        private RectTransform _canvas;
        private RectTransform _launchOrigin;
        private CoinPool _pool;
        private CoinCounter _counter;
        private TMP_Text _counterLabel;
        private RewardView _reward;
        private CustomizationService _customization;
        private ProfileResetService _profileReset;
        private TutorialController _tutorial;
        private TutorialStep _step;
        private DamageNumbers _damage;
        private CombatFlow _combat;
        private Transform[] _damageLabels;
        private PanelIntro _intro;
        private int _total;
        private ToastView _toast;
        private int _toastCount;

        private void Start()
        {
            _canvas = DemoUi.CreateCanvas();
            _pool = new CoinPool(CreateCoin);

            var gameplay = new GameplayLifetimes(this.GetLifetime().CreateChild("Gameplay"));
            _damage = new DamageNumbers(gameplay);
            _combat = new CombatFlow(gameplay);

            var cards = DemoUi.CreateGrid(_canvas, "Cards", DemoUi.TopRight, new Vector2(-30f, -30f), new Vector2(960f, 1030f), new Vector2(460f, 330f), new Vector2(20f, 20f));
            BuildCounterCard(cards);
            BuildRewardCard(cards);
            BuildOutfitCard(cards);
            BuildTutorialCard(cards);
            BuildDamageCard(cards);
            BuildIntroCard(cards);
            BuildToast();
            BuildButtons();

            if (EventSystem.current == null)
            {
                Debug.LogWarning("DOTweenUsageDemo: no EventSystem in the scene, so the buttons will not respond. Add one with GameObject > UI > Event System.");
            }
        }

        private void BuildCounterCard(RectTransform cards)
        {
            var card = DemoUi.CreateCard(cards, "Coin counter");
            _counterLabel = DemoUi.CreateLabel(card, "CounterLabel", "0", 80f, DemoUi.Center, Vector2.zero, new Vector2(400f, 120f));
            _counter = _counterLabel.gameObject.AddComponent<CoinCounter>();
            _counter.Initialize(_counterLabel);
        }

        private void BuildRewardCard(RectTransform cards)
        {
            var card = DemoUi.CreateCard(cards, "Reward");
            var chest = DemoUi.CreateImage(card, "Chest", Gold, DemoUi.Center, new Vector2(-100f, -10f), new Vector2(110f, 110f));
            var amount = DemoUi.CreateLabel(card, "Amount", "0", 60f, DemoUi.Center, new Vector2(100f, -10f), new Vector2(180f, 80f));

            _reward = DemoUi.CreateRect(card, "RewardView").gameObject.AddComponent<RewardView>();
            _reward.Initialize(chest.transform, amount);
        }

        private void BuildOutfitCard(RectTransform cards)
        {
            var card = DemoUi.CreateCard(cards, "Outfit");
            var body = DemoUi.CreateImage(card, "Avatar", new Color(0.6f, 0.6f, 0.6f, 1f), DemoUi.Center, new Vector2(0f, 20f), new Vector2(140f, 140f));
            var outfitName = DemoUi.CreateLabel(card, "OutfitName", "No outfit", 28f, DemoUi.Center, new Vector2(0f, -90f), new Vector2(400f, 40f));

            var preview = new AvatarPreview(body, outfitName);
            _customization = new CustomizationService(this.GetLifetime(), preview, new FakeOutfitRepository());
            _profileReset = new ProfileResetService(_customization);
        }

        private void BuildTutorialCard(RectTransform cards)
        {
            var card = DemoUi.CreateCard(cards, "Tutorial");
            var hintLabel = DemoUi.CreateLabel(card, "Hint", "Tap the highlighted button", 30f, DemoUi.Center, new Vector2(0f, 90f), new Vector2(420f, 50f));
            var hint = hintLabel.gameObject.AddComponent<CanvasGroup>();
            var target = DemoUi.CreateButton(card, "Target");
            DemoUi.Place((RectTransform)target.transform, DemoUi.Center, new Vector2(0f, -100f), new Vector2(200f, 60f));
            var arrow = DemoUi.CreateImage(card, "Arrow", new Color(1f, 0.6f, 0.1f, 1f), DemoUi.Center, new Vector2(0f, -25f), new Vector2(36f, 36f));
            var skip = DemoUi.CreateButton(card, "Skip");
            DemoUi.Place((RectTransform)skip.transform, DemoUi.TopRight, new Vector2(-12f, -10f), new Vector2(120f, 44f));

            // Created inactive and enabled after Initialize, so Awake sees the views.
            var rect = DemoUi.CreateRect(card, "TutorialController", active: false);
            _tutorial = rect.gameObject.AddComponent<TutorialController>();
            _tutorial.Initialize(arrow.rectTransform, hint, skip);
            rect.gameObject.SetActive(true);

            _step = new TutorialStep(target, new Vector3(0f, -55f, 0f));
        }

        private void BuildDamageCard(RectTransform cards)
        {
            var card = DemoUi.CreateCard(cards, "Damage numbers");
            _damageLabels = new Transform[5];
            for (var i = 0; i < _damageLabels.Length; i++)
            {
                var label = DemoUi.CreateLabel(card, "Damage" + i, "-" + (12 + i * 5), 36f, DemoUi.Center, new Vector2(-160f + i * 80f, -10f), new Vector2(80f, 50f));
                label.color = new Color(1f, 0.4f, 0.4f, 1f);
                _damageLabels[i] = label.transform;
            }
        }

        private void BuildIntroCard(RectTransform cards)
        {
            var card = DemoUi.CreateCard(cards, "Intro panel");

            // Inactive until the button shows it, so OnEnable starts the Sequence.
            var host = DemoUi.CreateRect(card, "IntroHost", active: false);
            DemoUi.Stretch(host);
            var panel = DemoUi.CreateImage(host, "Panel", PanelBlue, DemoUi.Center, Vector2.zero, new Vector2(240f, 140f));
            _intro = host.gameObject.AddComponent<PanelIntro>();
            _intro.Initialize(panel.rectTransform);
        }

        private void BuildButtons()
        {
            var grid = DemoUi.CreateGrid(_canvas, "Buttons", DemoUi.TopLeft, DemoUi.Pick(new Vector2(30f, -30f), new Vector2(30f, -1090f)), new Vector2(790f, 1000f), new Vector2(380f, 56f), new Vector2(10f, 10f));

            var launch = DemoUi.CreateButton(grid, "Launch coins");
            var roll = DemoUi.CreateButton(grid, "Roll counter twice");
            var showReward = DemoUi.CreateButton(grid, "Reward: show");
            var cancelReward = DemoUi.CreateButton(grid, "Reward: cancel");
            var applyTwice = DemoUi.CreateButton(grid, "Outfit: apply twice");
            var resetProfile = DemoUi.CreateButton(grid, "Outfit: reset profile");
            var showStep = DemoUi.CreateButton(grid, "Tutorial: show step");
            var playerMoved = DemoUi.CreateButton(grid, "Tutorial: player moved");
            var abort = DemoUi.CreateButton(grid, "Tutorial: abort");
            var pop = DemoUi.CreateButton(grid, "Damage: pop numbers");
            var popAndInterrupt = DemoUi.CreateButton(grid, "Damage: pop, then interrupt");
            var toggleIntro = DemoUi.CreateButton(grid, "Intro panel: show / hide");
            _launchOrigin = (RectTransform)launch.transform;

            // Each subscription names its owner; nothing below is ever removed by hand.
            launch.onClick.Subscribe(LaunchCoins, this);
            roll.onClick.Subscribe(RollCounterTwice, this);
            showReward.onClick.Subscribe(ShowReward, this);
            cancelReward.onClick.Subscribe(CancelReward, this);
            applyTwice.onClick.Subscribe(ApplyOutfitTwice, this);
            resetProfile.onClick.Subscribe(ResetProfile, this);
            showStep.onClick.Subscribe(ShowStep, this);
            playerMoved.onClick.Subscribe(PlayerMoved, this);
            abort.onClick.Subscribe(AbortTutorial, this);
            pop.onClick.Subscribe(PopNumbers, this);
            popAndInterrupt.onClick.Subscribe(PopAndInterrupt, this);
            toggleIntro.onClick.Subscribe(ToggleIntro, this);

            var toast = DemoUi.CreateButton(grid, "Toast: show");
            toast.onClick.Subscribe(ShowToast, this);
        }

        private void LaunchCoins() => this.Run(LaunchCoinsAsync);
        private void ShowReward() => _reward.Show(250);
        private void CancelReward() => _reward.GetLifetime().Cancel();
        private void ResetProfile() => _profileReset.ResetProfile();
        private void ShowStep() => _tutorial.ShowStep(_step);
        private void PlayerMoved() => _tutorial.OnPlayerMoved();
        private void AbortTutorial() => _tutorial.Abort();
        private void ToggleIntro() => _intro.gameObject.SetActive(!_intro.gameObject.activeSelf);
        private void ShowToast() => _toast.Show($"Saved ({++_toastCount})");

        private void BuildToast()
        {
            var panel = DemoUi.CreateImage(_canvas, "ToastPanel", PanelBlue, DemoUi.Center, DemoUi.Pick(new Vector2(-530f, -300f), new Vector2(0f, -640f)), new Vector2(560f, 90f));
            panel.transform.localScale = Vector3.zero;
            var label = DemoUi.CreateLabel(panel.transform, "ToastText", string.Empty, 32f, DemoUi.Center, Vector2.zero, new Vector2(460f, 60f));
            var badge = DemoUi.CreateImage(panel.transform, "ToastBadge", new Color(1f, 0.6f, 0.1f, 1f), DemoUi.Center, new Vector2(250f, 0f), new Vector2(28f, 28f));

            // Created inactive and enabled after Initialize, so Awake sees the views.
            var holder = DemoUi.CreateRect(_canvas, "ToastView", active: false);
            _toast = holder.gameObject.AddComponent<ToastView>();
            _toast.Initialize(panel.transform, badge.transform, label);
            holder.gameObject.SetActive(true);
        }

        // The second roll starts while the first still runs, so Complete mode lands the first on its end value.
        private void RollCounterTwice()
        {
            RollAgain();
            this.After(0.2f, RollAgain);
        }

        private void RollAgain()
        {
            _total += 100;
            _counter.RollTo(_total);
        }

        // The second apply starts before the first one finished loading, so only the second is shown and saved.
        private void ApplyOutfitTwice()
        {
            _customization.Apply(new Outfit("red"));
            this.After(0.4f, ApplyBlue);
        }

        private void ApplyBlue() => _customization.Apply(new Outfit("blue"));

        private void PopNumbers()
        {
            for (var i = 0; i < _damageLabels.Length; i++)
            {
                _damageLabels[i].localScale = Vector3.one;
                _damage.Pop(_damageLabels[i]);
            }
        }

        // The interrupt lands in the middle of the punch, so the numbers stay mid-pop.
        private void PopAndInterrupt()
        {
            PopNumbers();
            this.After(0.1f, _combat.OnCombatInterrupted);
        }

        private async UniTask LaunchCoinsAsync(CancellationToken ct)
        {
            for (var i = 0; i < 5; i++)
            {
                var coin = _pool.Rent();
                coin.transform.position = _launchOrigin.position;
                coin.Launch(_counterLabel.rectTransform, _pool);
                await UniTask.Delay(TimeSpan.FromMilliseconds(120), cancellationToken: ct);
            }
        }

        private CoinPickup CreateCoin()
        {
            var image = DemoUi.CreateImage(_canvas, "Coin", Gold, DemoUi.Center, Vector2.zero, new Vector2(36f, 36f));
            image.gameObject.SetActive(false);
            return image.gameObject.AddComponent<CoinPickup>();
        }
    }
}
