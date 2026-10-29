using System;
using System.Collections;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Player;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// In-run HUD. Everything is bound to the live GameSession so no game object needs to be
    /// wired in the scene. Online races additionally show a five-runner board and connection
    /// state, including a clear warning that an interrupted race cannot grant rewards.
    /// Visual language: glass cards with technical borders (UITheme.GlassCard), rolling
    /// NumberTicker readouts for score/distance/credits, a pinned district callout on biome
    /// changes and a sheen-swept velocity gauge across the bottom.
    /// </summary>
    public sealed class HUDScreen : MonoBehaviour
    {
        private GameApp _app;
        private GameSession _session;
        private IReadOnlyList<WirePlayer> _players;

        private Text _name;
        private Text _score;
        private Text _distance;
        private Text _timer;
        private Text _timerCaption;
        private Text _objectiveLabel;
        private Image _objectiveFill;
        private Text _credits;
        private Text _lives;
        private Image _speedFill;
        private Text _speedText;
        private Text _countdown;
        private Text _banner;
        private Text _location;
        private Text _prompt;
        private Text _connection;
        private Text _spectating;
        private Text _runOver;
        private Text _windowLabel;
        private Image _flash;
        private GameObject _onlinePanel;
        private RectTransform _effectsRow;
        private RectTransform _onlineRows;
        private readonly List<Text> _onlineNames = new List<Text>(5);
        private readonly List<Text> _onlineMeta = new List<Text>(5);
        private readonly List<GameObject> _effectChips = new List<GameObject>();

        private NumberTicker _scoreTicker;
        private NumberTicker _distanceTicker;
        private NumberTicker _creditsTicker;
        private PulseBar _speedBar;
        private int _speedBand;
        private Image _abilityPanel;
        private Button _abilityButton;
        private Text _abilityLabel;
        private Text _abilityCooldown;
        private GameObject _junctionPanel;
        private Text _junctionTitle;
        private Text _junctionHint;
        private Image _junctionAccent;
        private JunctionPhase _lastJunctionPhase;
        private int _lastJunctionSegment = -1;
        private float _junctionFlash;

        private float _refreshTimer;
        private float _effectsTimer;
        private Coroutine _bannerRoutine;
        private Coroutine _flashRoutine;
        private Coroutine _locationRoutine;
        private float _creditsPulse;
        private int _pendingEnergy;
        private float _energyToastTimer;

        public static HUDScreen Build(RectTransform parent, GameApp app)
        {
            var root = UITheme.NewRect(parent, "HUD");
            UITheme.Stretch(root);
            var hud = root.gameObject.AddComponent<HUDScreen>();
            hud._app = app;
            hud.BuildLayout();
            return hud;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            root.SetAsLastSibling();

            // Full-screen damage / pickup flash.
            _flash = UITheme.Img(root, "Flash", new Color(1f, 1f, 1f, 0f), null, false);
            UITheme.Stretch((RectTransform)_flash.transform);
            _flash.raycastTarget = false;

            BuildTelemetryGroup(root);
            BuildObjectivePanel(root);
            BuildCurrencyPanel(root);
            BuildSpeedGauge(root);
            BuildOnlinePanel(root);
            BuildPrompt(root);
            BuildCountdown(root);
            BuildJunctionPanel(root);
            BuildWindowTag(root);

            // Pause is always available; it does not consume a swipe on touch.
            var pause = UITheme.IconButton(root, "Pause", "II", UITheme.Cyan, () => _app.ShowPause(), 30);
            UITheme.Anchor((RectTransform)pause.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-80f, 34f), new Vector2(-26f, 88f));

            BuildAbilityButton(root);
        }

        /// <summary>
        /// Premium-frame ability trigger. The panel only appears for a runner that actually
        /// owns a signature ability, so standard frames keep the original HUD silhouette.
        /// </summary>
        private void BuildAbilityButton(RectTransform root)
        {
            _abilityPanel = UITheme.Img(root, "Ability Panel", UITheme.PanelSoft, UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)_abilityPanel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-318f, 96f), new Vector2(-26f, 178f));
            _abilityButton = UITheme.Button(_abilityPanel.transform, "Ability", "ABILITY", UITheme.Violet,
                ActivateAbility, UITheme.BodySize);
            UITheme.Stretch((RectTransform)_abilityButton.transform, 10f, 30f, 10f, 10f);
            _abilityLabel = _abilityButton.GetComponentInChildren<Text>();
            _abilityCooldown = UITheme.Label(_abilityPanel.transform, "Ability Cooldown", "READY", 13,
                UITheme.Lime, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_abilityCooldown.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(10f, 6f), new Vector2(-10f, 26f));
            _abilityPanel.gameObject.SetActive(false);
        }

        private void ActivateAbility()
        {
            if (_session == null) return;
            _app.Audio.PlayUiClick();
            _session.Runner?.ActivateAbility();
        }

        private void RefreshAbility()
        {
            var runner = _session != null ? _session.Runner : null;
            bool hasAbility = runner != null && runner.HasAbility;
            if (_abilityPanel != null && _abilityPanel.gameObject.activeSelf != hasAbility)
                _abilityPanel.gameObject.SetActive(hasAbility);
            if (!hasAbility) return;
            float remaining = runner.AbilityCooldownRemaining;
            bool ready = remaining <= 0.01f;
            if (_abilityLabel != null)
                _abilityLabel.text = runner.AbilityActive
                    ? runner.AbilityName + " ACTIVE"
                    : runner.AbilityName;
            if (_abilityCooldown != null)
            {
                _abilityCooldown.text = ready ? "READY  [Q / E]" : Mathf.CeilToInt(remaining) + "S";
                _abilityCooldown.color = ready ? UITheme.Lime : UITheme.Muted;
            }
            if (_abilityButton != null)
            {
                _abilityButton.interactable = ready;
                var colors = _abilityButton.colors;
                colors.normalColor = ready ? Color.white : new Color(0.62f, 0.62f, 0.72f, 0.85f);
                _abilityButton.colors = colors;
            }
        }

        /// <summary>Top-left card: runner identity, rolling score and distance tape.</summary>
        private void BuildTelemetryGroup(RectTransform root)
        {
            var border = UITheme.GlassCard(root, "Runner Panel", UITheme.PanelSoft, 0.5f);
            var panel = border.transform.parent as RectTransform;
            UITheme.Anchor(panel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -198f), new Vector2(430f, -24f));
            border.color = UITheme.Cyan.Alpha(0.5f);
            var accent = UITheme.Img(panel.transform, "Accent", UITheme.Cyan);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 12f), new Vector2(4f, -12f));

            _name = UITheme.Label(panel.transform, "Name", "RUNNER", UITheme.SmallSize, UITheme.Dim,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(22f, -40f), new Vector2(-16f, -12f));

            var scoreLabel = UITheme.Label(panel.transform, "Score Label", "SCORE", 13, UITheme.Muted,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)scoreLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(22f, -70f), new Vector2(-16f, -44f));

            _score = UITheme.Label(panel.transform, "Score", "0", 40, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_score.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, -114f), new Vector2(-16f, -62f));
            UITheme.AddShadow(_score, new Color(0f, 0f, 0f, 0.6f), new Vector2(1f, -2f));
            _scoreTicker = NumberTicker.Attach(_score.gameObject, _score, 0);

            _distance = UITheme.Label(panel.transform, "Distance", "0 m", 30, UITheme.Cyan,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_distance.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(22f, 12f), new Vector2(-16f, 52f));
            _distanceTicker = NumberTicker.Attach(_distance.gameObject, _distance, 0, v => FormatDistance((float)v));
        }

        private void BuildObjectivePanel(RectTransform root)
        {
            var border = UITheme.GlassCard(root, "Timer Panel", UITheme.PanelSoft, 0.4f);
            var panel = border.transform.parent as RectTransform;
            UITheme.Anchor(panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-196f, -134f), new Vector2(196f, -24f));
            border.color = Color.white.Alpha(0.32f);

            _timerCaption = UITheme.Label(panel.transform, "Timer Caption", "RUN TIME", 12, UITheme.Muted,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_timerCaption.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(12f, -18f), new Vector2(-12f, -2f));

            _timer = UITheme.Label(panel.transform, "Timer", "0.0", 38, UITheme.White,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_timer.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(12f, -64f), new Vector2(-12f, -18f));

            _objectiveLabel = UITheme.Label(panel.transform, "Objective", "DISTANCE", UITheme.SmallSize,
                UITheme.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_objectiveLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(18f, 22f), new Vector2(-18f, 44f));

            var bar = UITheme.Img(panel.transform, "Objective Bar", new Color(0.12f, 0.18f, 0.28f, 0.9f),
                UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)bar.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(18f, 12f), new Vector2(-18f, 19f));
            _objectiveFill = UITheme.Img(bar.transform, "Fill", UITheme.Lime, UITheme.Rounded, false);
            var fillRect = (RectTransform)_objectiveFill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            _connection = UITheme.Label(panel.transform, "Connection", "", UITheme.SmallSize, UITheme.Amber,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_connection.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(10f, -34f), new Vector2(-10f, -8f));
        }

        private void BuildCurrencyPanel(RectTransform root)
        {
            var border = UITheme.GlassCard(root, "Currency Panel", UITheme.PanelSoft, 0.45f);
            var panel = border.transform.parent as RectTransform;
            UITheme.Anchor(panel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-392f, -130f), new Vector2(-142f, -24f));
            border.color = UITheme.Amber.Alpha(0.5f);

            var creditIcon = UITheme.Img(panel.transform, "Credit Icon", UITheme.Amber, TextureFactory.Diamond, false);
            UITheme.Anchor((RectTransform)creditIcon.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(18f, -13f), new Vector2(44f, 13f));

            var currencyHeader = UITheme.Label(panel.transform, "Credits Title", "CREDITS", 13, UITheme.Muted,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)currencyHeader.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(54f, -28f), new Vector2(-12f, -8f));

            _credits = UITheme.Label(panel.transform, "Credits", "0", 26, UITheme.Amber,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_credits.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(54f, 4f), new Vector2(-12f, -30f));
            _creditsTicker = NumberTicker.Attach(_credits.gameObject, _credits, _app.Profile != null ? _app.Profile.credits : 0);
            _creditsTicker.SetFormatter(v => v.ToString("N0"));

            var livesPanel = UITheme.Img(root, "Lives Panel", UITheme.PanelSoft, UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)livesPanel.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-132f, -130f), new Vector2(-24f, -24f));
            _lives = UITheme.Label(livesPanel.transform, "Lives", "\u25C6", 28, UITheme.Red,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Stretch((RectTransform)_lives.transform, 4f, 4f, 4f, 4f);
        }

        /// <summary>Bottom velocity gauge: sheen-swept fill plus a numeric readout.</summary>
        private void BuildSpeedGauge(RectTransform root)
        {
            var speedBg = UITheme.Img(root, "Speed Bar", new Color(0.08f, 0.12f, 0.2f, 0.78f), UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)speedBg.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 24f), new Vector2(-24f, 42f));
            _speedFill = UITheme.Img(speedBg.transform, "Fill", UITheme.Magenta, UITheme.Rounded, false);
            var speedRect = (RectTransform)_speedFill.transform;
            speedRect.anchorMin = Vector2.zero;
            speedRect.anchorMax = new Vector2(0f, 1f);
            speedRect.offsetMin = Vector2.zero;
            speedRect.offsetMax = Vector2.zero;

            // Sheen quad sweeps the bar when the velocity band jumps (boost, ramp-up).
            var sheen = UITheme.Img(speedBg.transform, "Sheen", Color.white.Alpha(0.28f), null, false);
            var sheenRect = (RectTransform)sheen.transform;
            sheenRect.anchorMin = new Vector2(0f, 0f);
            sheenRect.anchorMax = new Vector2(0f, 1f);
            sheenRect.pivot = new Vector2(0.5f, 0.5f);
            sheenRect.sizeDelta = new Vector2(56f, 0f);
            sheenRect.anchoredPosition = new Vector2(-200f, 0f);
            sheen.gameObject.SetActive(false);

            _speedBar = PulseBar.Attach(_speedFill, sheenRect);
            _speedBar.Snap(0f);

            _speedText = UITheme.Label(speedBg.transform, "Speed Text", "0 M/S", UITheme.SmallSize,
                UITheme.White, TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Stretch((RectTransform)_speedText.transform, -128f, 4f, 16f, 4f);
        }
        private void BuildOnlinePanel(RectTransform root)
        {
            var border = UITheme.GlassCard(root, "Online Panel", new Color(0.03f, 0.06f, 0.11f, 0.78f), 0.4f);
            var panel = border.transform.parent as RectTransform;
            border.color = UITheme.Lime.Alpha(0.4f);
            _onlinePanel = panel.gameObject;
            UITheme.Anchor(panel, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-350f, -190f), new Vector2(-24f, 190f));
            var heading = UITheme.Label(panel.transform, "Heading", "CIRCUIT", UITheme.SmallSize,
                UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(18f, -38f), new Vector2(-18f, -8f));

            _onlineRows = UITheme.NewRect(panel.transform, "Rows");
            UITheme.Anchor(_onlineRows, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(12f, 14f), new Vector2(-12f, -44f));
            var layout = _onlineRows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            for (int i = 0; i < RaceRules.MaxPlayers; i++)
            {
                var row = UITheme.NewRect(_onlineRows, "Row " + (i + 1));
                UITheme.Layout(row.gameObject, 58f, -1f, 58f);
                var bg = UITheme.Img(row, "BG", i % 2 == 0 ? UITheme.PanelSoft : new Color(0.04f, 0.07f, 0.13f, 0.6f),
                    UITheme.Rounded, false);
                UITheme.Stretch((RectTransform)bg.transform);
                var name = UITheme.Label(row, "Name", "", 18, UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
                UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 0.35f), new Vector2(1f, 1f),
                    new Vector2(14f, 0f), new Vector2(-48f, -4f));
                var meta = UITheme.Label(row, "Meta", "", 15, UITheme.Dim, TextAnchor.MiddleLeft);
                UITheme.Anchor((RectTransform)meta.transform, new Vector2(0f, 0f), new Vector2(1f, 0.4f),
                    new Vector2(14f, 4f), new Vector2(-48f, 0f));
                var place = UITheme.Label(row, "Place", (i + 1).ToString(), 22, UITheme.Cyan,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UITheme.Anchor((RectTransform)place.transform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                    new Vector2(-42f, 4f), new Vector2(-4f, -4f));
                place.name = "Place";
                row.gameObject.SetActive(false);
                _onlineNames.Add(name);
                _onlineMeta.Add(meta);
            }
        }

        private void BuildPrompt(RectTransform root)
        {
            _prompt = UITheme.Label(root, "Prompt", "", UITheme.BodySize, UITheme.White,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_prompt.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-620f, 56f), new Vector2(620f, 104f));
            UITheme.AddShadow(_prompt, new Color(0f, 0f, 0f, 0.9f), new Vector2(1f, -2f));

            _effectsRow = UITheme.NewRect(root, "Effects");
            UITheme.Anchor(_effectsRow, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 52f), new Vector2(620f, 116f));
            var layout = _effectsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        private void BuildCountdown(RectTransform root)
        {
            _countdown = UITheme.Label(root, "Countdown", "", 150, UITheme.White, TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UITheme.Anchor((RectTransform)_countdown.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-420f, -150f), new Vector2(420f, 190f));
            UITheme.AddOutline(_countdown, new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.85f),
                new Vector2(3f, -3f));
            _countdown.gameObject.SetActive(false);

            _banner = UITheme.Label(root, "Banner", "", 52, UITheme.Cyan, TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UITheme.Anchor((RectTransform)_banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-560f, -40f), new Vector2(560f, 70f));
            UITheme.AddOutline(_banner, new Color(0f, 0f, 0f, 0.9f), new Vector2(2f, -2f));
            _banner.gameObject.SetActive(false);

            // District callout: pinned under the objective panel between the glass cards.
            _location = UITheme.Label(root, "Location", "", 34, UITheme.White, TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UITheme.Anchor((RectTransform)_location.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-420f, -196f), new Vector2(420f, -148f));
            UITheme.AddOutline(_location, new Color(0f, 0f, 0f, 0.9f), new Vector2(2f, -2f));
            _location.gameObject.SetActive(false);

            _runOver = UITheme.Label(root, "Run Over", "", 30, UITheme.Red, TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UITheme.Anchor((RectTransform)_runOver.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-400f, -130f), new Vector2(400f, -76f));
            _runOver.gameObject.SetActive(false);

            _spectating = UITheme.Label(root, "Spectating", "SPECTATING - AWAITING SERVER RESULT", 20,
                UITheme.Amber, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_spectating.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-400f, 120f), new Vector2(400f, 156f));
            _spectating.gameObject.SetActive(false);
        }

        private void BuildJunctionPanel(RectTransform root)
        {
            var panel = UITheme.Img(root, "Junction Panel", new Color(0.025f, 0.045f, 0.075f, 0.86f),
                UITheme.Rounded, false);
            _junctionPanel = panel.gameObject;
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-440f, 150f), new Vector2(440f, 258f));

            _junctionAccent = UITheme.Img(panel.transform, "Junction Accent", UITheme.Cyan, null, false);
            UITheme.Anchor((RectTransform)_junctionAccent.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 4f));

            _junctionTitle = UITheme.Label(panel.transform, "Junction Title", "JUNCTION", 30,
                UITheme.White, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_junctionTitle.transform, new Vector2(0f, 0.5f), new Vector2(1f, 1f),
                new Vector2(20f, 0f), new Vector2(-20f, -10f));

            _junctionHint = UITheme.Label(panel.transform, "Junction Hint", "", 20,
                UITheme.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_junctionHint.transform, new Vector2(0f, 0f), new Vector2(1f, 0.5f),
                new Vector2(20f, 10f), new Vector2(-20f, 4f));
            _junctionPanel.SetActive(false);
        }

        private void RefreshJunction()
        {
            if (_junctionPanel == null) return;
            var runner = _session.Runner;
            JunctionState state = default;
            bool show = runner != null && !_session.CountdownVisible && !_session.IsFinished &&
                        _session.Track != null;
            if (show) show = _session.Track.GetJunctionState(_session.Distance, out state);
            if (!show)
            {
                if (_junctionPanel.activeSelf) _junctionPanel.SetActive(false);
                _lastJunctionPhase = JunctionPhase.None;
                _lastJunctionSegment = -1;
                return;
            }

            if (!_junctionPanel.activeSelf) _junctionPanel.SetActive(true);
            var plan = state.Plan;
            if (_lastJunctionSegment != plan.SegmentIndex)
            {
                _lastJunctionSegment = plan.SegmentIndex;
                _junctionFlash = 1f;
            }
            if (_lastJunctionPhase != state.Phase)
            {
                _lastJunctionPhase = state.Phase;
                _junctionFlash = Mathf.Max(_junctionFlash, 0.7f);
            }

            string title;
            string hint;
            Color accent = UITheme.Cyan;
            switch (state.Phase)
            {
                case JunctionPhase.Warning:
                    int metres = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(state.MetresToCommit)));
                    title = plan.DisplayName + " AHEAD  ·  " + metres + " M";
                    hint = plan.IsCollapsingBridge ? "HOLD CENTRE  ·  JUMP THE COLLAPSING GAP"
                        : plan.IsBrokenRoad ? "HOLD CENTRE  ·  JUMP THE BREAK"
                        : plan.IsNarrowBridge ? "MOVE TO THE CENTRE LANE"
                        : plan.IsCurvedRoad ? (plan.CurveSign < 0
                            ? "ROAD BENDS LEFT  ·  HOLD YOUR LINE"
                            : "ROAD BENDS RIGHT  ·  HOLD YOUR LINE")
                        : plan.IsShortcut ? "MARKED SIDE LANE OPENS  ·  SWIPE FOR REWARDS"
                        : "CHOOSE A BRANCH  ·  \u25C0 LEFT   RIGHT \u25B6";
                    accent = UITheme.Amber;
                    break;
                case JunctionPhase.Commit:
                    title = "COMMIT NOW  ·  " + plan.DisplayName;
                    hint = plan.IsBrokenRoad ? "JUMP"
                        : plan.IsNarrowBridge ? "CENTRE LANE"
                        : plan.IsCurvedRoad ? "HOLD THE BEND"
                        : plan.IsShortcut ? "SWIPE INTO THE MARKED LANE"
                        : "SWIPE LEFT OR RIGHT";
                    accent = UITheme.Amber;
                    break;
                case JunctionPhase.Branch:
                    var commitment = runner.JunctionCommitment;
                    if (plan.IsBrokenRoad)
                    {
                        title = plan.IsCollapsingBridge ? "COLLAPSING BRIDGE" : "BROKEN ROAD";
                        hint = "HOLD CENTRE  ·  CLEAR THE GAP";
                        accent = UITheme.Amber;
                    }
                    else if (plan.IsCurvedRoad)
                    {
                        title = plan.CurveSign < 0 ? "CURVING LEFT" : "CURVING RIGHT";
                        hint = "FOLLOW THE ROAD LIGHTS";
                        accent = UITheme.Cyan;
                    }
                    else if (plan.IsShortcut)
                    {
                        bool onShortcut = runner.Lane == plan.RewardLane;
                        title = onShortcut ? "SHORTCUT ROUTE" : "MAIN ROUTE";
                        hint = onShortcut ? "REWARD ROUTE  ·  HOLD THE LINE" : "HOLD YOUR LINE";
                        accent = onShortcut ? UITheme.Lime : UITheme.Cyan;
                    }
                    else
                    {
                        title = commitment == JunctionBranch.Left ? "LEFT BRANCH"
                            : commitment == JunctionBranch.Right ? "RIGHT BRANCH"
                            : plan.IsNarrowBridge ? "NARROW BRIDGE"
                            : "CHOOSE A BRANCH";
                        hint = plan.IsNarrowBridge ? "HOLD THE CENTRE LINE"
                            : commitment == JunctionBranch.None ? "\u25C0 LEFT   OR   RIGHT \u25B6"
                            : plan.RewardOnLeft == (commitment == JunctionBranch.Left)
                                ? "REWARD ROUTE  ·  HOLD THE LINE"
                                : "STANDARD ROUTE  ·  HOLD THE LINE";
                        accent = plan.RewardOnLeft == (commitment == JunctionBranch.Left)
                            ? UITheme.Lime : UITheme.Cyan;
                    }
                    break;
                default:
                    title = "MERGE  ·  " + plan.DisplayName;
                    hint = "HOLD YOUR LINE";
                    accent = UITheme.Lime;
                    break;
            }

            _junctionTitle.text = title;
            _junctionHint.text = hint;
            _junctionHint.color = accent;
            _junctionAccent.color = accent;

            if (_junctionFlash > 0f)
            {
                _junctionFlash = Mathf.Max(0f, _junctionFlash - Time.unscaledDeltaTime * 2.8f);
                float scale = 1f + _junctionFlash * 0.035f;
                _junctionPanel.transform.localScale = Vector3.one * scale;
            }
            else _junctionPanel.transform.localScale = Vector3.one;
        }

        private void BuildWindowTag(RectTransform root)
        {
            _windowLabel = UITheme.Label(root, "Mode", "", UITheme.SmallSize, UITheme.Muted,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_windowLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -200f), new Vector2(-24f, -174f));
        }

        public void Bind(GameSession session)
        {
            _session = session;
            if (session == null) return;
            _name.text = _app.Profile != null ? _app.Profile.playerName : "RUNNER";
            _windowLabel.text = session.Mode.Title.ToUpperInvariant() + "  /  " + session.Mode.CodeName;
            if (session.IsOnline) _windowLabel.color = UITheme.Lime;
            if (_onlinePanel != null) _onlinePanel.SetActive(session.IsOnline);
            bool showLives = session.Mode.StartingLives > 1 || session.Mode.Id == GameModeId.Survival;
            if (_lives.transform.parent != null) _lives.transform.parent.gameObject.SetActive(showLives);
            _lives.gameObject.SetActive(showLives);
            if (_scoreTicker != null) _scoreTicker.JumpTo(session.Score);
            if (_distanceTicker != null) _distanceTicker.JumpTo(Mathf.RoundToInt(session.Distance));
        }

        private string FormatDistance(float metres)
        {
            if (metres >= 1000f)
                return (metres / 1000f).ToString("0.00") + " km";
            return Mathf.RoundToInt(metres) + " m";
        }

        private void Update()
        {
            if (_session == null) return;

            if (_scoreTicker != null) _scoreTicker.Set(_session.Score);
            if (_distanceTicker != null) _distanceTicker.Set(Mathf.RoundToInt(_session.Distance));
            _timer.text = _session.TimerLabel;
            if (_timerCaption != null) _timerCaption.text = _session.TimerCaption;
            if (_creditsTicker != null) _creditsTicker.Set(_app.Profile.credits);
            _objectiveLabel.text = "OBJECTIVE · " + _session.ObjectiveLabel;
            _objectiveFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(_session.ObjectiveProgress), 1f);
            float speedNorm = _session.Runner != null ? _session.Runner.SpeedNormalised : 0f;
            if (_speedBar != null) _speedBar.Set(speedNorm);
            // Sweep the gauge sheen once when the velocity band jumps (boost or ramp-up).
            int band = Mathf.FloorToInt(speedNorm * 5f);
            if (band != _speedBand)
            {
                bool jumped = band > _speedBand;
                _speedBand = band;
                if (jumped && _speedBar != null) _speedBar.TriggerSheen();
            }
            if (_speedText != null && _session.Runner != null)
                _speedText.text = Mathf.RoundToInt(_session.Runner.Speed) + " M/S";

            RefreshAbility();
            RefreshJunction();

            if (_session.Lives > 1)
                _lives.text = "\u25C6 " + _session.Lives;
            else
                _lives.text = "\u25C6";

            _countdown.gameObject.SetActive(_session.CountdownVisible);
            if (_session.CountdownVisible) _countdown.text = _session.CountdownText;

            _connection.text = _session.ConnectionWarning;
            _connection.gameObject.SetActive(!string.IsNullOrEmpty(_session.ConnectionWarning));
            if (_creditsPulse > 0f)
            {
                _creditsPulse = Mathf.Max(0f, _creditsPulse - Time.unscaledDeltaTime * 3f);
                _credits.transform.localScale = Vector3.one * (1f + _creditsPulse * 0.25f);
                _credits.color = Color.Lerp(UITheme.Amber, Color.white, _creditsPulse);
                if (_creditsPulse <= 0.001f) _credits.transform.localScale = Vector3.one;
            }

            if (_pendingEnergy > 0)
            {
                _energyToastTimer -= Time.unscaledDeltaTime;
                if (_energyToastTimer <= 0f)
                {
                    if (_app != null)
                        _app.UI.Toast("+" + _pendingEnergy + " ENERGY", UITheme.Amber, 0.55f);
                    _pendingEnergy = 0;
                    _energyToastTimer = 0.55f;
                }
            }

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_session.IsOnline && _refreshTimer <= 0f)
            {
                _refreshTimer = 0.2f;
                RefreshOnlineRows(_session.OnlinePlayers);
            }

            _effectsTimer -= Time.unscaledDeltaTime;
            if (_effectsTimer <= 0f)
            {
                _effectsTimer = 0.12f;
                RefreshEffects();
            }
        }

        public void SetOnlinePlayerList(IReadOnlyList<WirePlayer> players)
        {
            _players = players;
            if (players != null && players.Count > 0) RefreshOnlineRows(players);
        }

        private void RefreshOnlineRows(IReadOnlyList<WirePlayer> players)
        {
            if (players == null) players = _players;
            int count = players != null ? players.Count : 0;
            for (int i = 0; i < _onlineNames.Count; i++)
            {
                bool active = i < count;
                _onlineNames[i].transform.parent.gameObject.SetActive(active);
                if (!active) continue;
                var player = players[i];
                string tag = player.bot ? "BOT" : (player.connected ? "LIVE" : "LINK LOST");
                _onlineNames[i].text = string.IsNullOrEmpty(player.name) ? "RUNNER" : player.name;
                _onlineNames[i].color = player.playerId == _app.Race?.PlayerId ? UITheme.Cyan : UITheme.White;
                _onlineMeta[i].text = tag + "   " + UITheme.FormatDistance(player.distance) +
                    (player.finished ? "   FINISHED" : "");
                var place = _onlineNames[i].transform.parent.Find("Place").GetComponent<Text>();
                if (place != null)
                {
                    place.text = player.placement > 0 ? player.placement.ToString() : "-";
                    place.color = player.placement == 1 ? UITheme.Amber : UITheme.Cyan;
                }
            }
        }

        private void RefreshEffects()
        {
            if (_session.Runner == null) return;
            var effects = _session.Runner.ActiveEffects;
            int wanted = 0;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Timed ? effects[i].Remaining > 0.1f : effects[i].Charges > 0) wanted++;
            }
            while (_effectChips.Count < wanted) CreateEffectChip();
            for (int i = 0; i < _effectChips.Count; i++)
            {
                bool active = i < wanted;
                _effectChips[i].SetActive(active);
                if (!active) continue;
                var effect = effects[i];
                var bg = _effectChips[i].GetComponent<Image>();
                var label = _effectChips[i].GetComponentInChildren<Text>();
                if (bg != null) bg.color = new Color(effect.Color.r * 0.22f, effect.Color.g * 0.22f,
                    effect.Color.b * 0.22f, 0.94f);
                if (label != null)
                {
                    label.color = effect.Color;
                    label.text = effect.Label + (effect.Timed
                        ? "\n" + effect.Remaining.ToString("0.0") + "s"
                        : "\n" + effect.Charges + "x");
                }
            }
        }

        private void CreateEffectChip()
        {
            var bg = UITheme.Img(_effectsRow, "Effect " + _effectChips.Count, UITheme.PanelSoft, UITheme.Rounded, false);
            var rect = (RectTransform)bg.transform;
            rect.sizeDelta = new Vector2(118f, 58f);
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
            outline.effectDistance = new Vector2(1f, -1f);
            var label = UITheme.Label(bg.transform, "Label", "", 15, UITheme.White,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Stretch((RectTransform)label.transform, 4f, 3f, 4f, 3f);
            _effectChips.Add(bg.gameObject);
        }

        public void SetPrompt(string prompt)
        {
            _prompt.text = prompt ?? "";
            _prompt.gameObject.SetActive(!string.IsNullOrEmpty(prompt));
        }

        public void SetConnectionWarning(string warning)
        {
            _connection.text = warning ?? "";
            _connection.gameObject.SetActive(!string.IsNullOrEmpty(warning));
        }

        public void SetSpectating(bool spectating)
        {
            _spectating.gameObject.SetActive(spectating);
        }

        public void SetRunOver(bool over)
        {
            _runOver.gameObject.SetActive(over);
            if (over && _junctionPanel != null) _junctionPanel.SetActive(false);
            _runOver.text = over ? "RUN OVER" : "";
        }

        /// <summary>
        /// District callout whenever the city biome changes: "ENTERING \u2014 NAME".
        /// Uses the banner-free label so race banners and district changes never collide.
        /// </summary>
        public void ShowLocation(string themeName)
        {
            if (string.IsNullOrEmpty(themeName) || _location == null) return;
            if (_locationRoutine != null) StopCoroutine(_locationRoutine);
            _locationRoutine = StartCoroutine(LocationRoutine(themeName.ToUpperInvariant()));
        }

        private IEnumerator LocationRoutine(string themeName)
        {
            _location.text = "\u2014  ENTERING  " + themeName + "  \u2014";
            _location.color = Color.white;
            _location.gameObject.SetActive(true);
            var group = _location.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = _location.gameObject.AddComponent<CanvasGroup>();
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01(t / 0.3f);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.9f);
            t = 0f;
            while (t < 0.5f)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / 0.5f);
                yield return null;
            }
            _location.gameObject.SetActive(false);
            group.alpha = 1f;
        }

        public void Flash(Color color, float duration)
        {
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine(color, duration));
        }

        private IEnumerator FlashRoutine(Color color, float duration)
        {
            duration = Mathf.Max(0.05f, duration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Lerp(0.34f, 0f, t / duration);
                _flash.color = new Color(color.r, color.g, color.b, a);
                yield return null;
            }
            _flash.color = new Color(color.r, color.g, color.b, 0f);
        }

        public void PulseCredits(int amount)
        {
            _creditsPulse = 1f;
            _pendingEnergy += amount;
        }

        public void Toast(string message, Color color)
        {
            if (_app != null) _app.UI.Toast(message, color, 1.5f);
        }

        public void ShowBanner(string message, Color color, float duration)
        {
            if (_bannerRoutine != null) StopCoroutine(_bannerRoutine);
            _bannerRoutine = StartCoroutine(BannerRoutine(message, color, duration));
        }

        private IEnumerator BannerRoutine(string message, Color color, float duration)
        {
            _banner.text = message;
            _banner.color = color;
            _banner.gameObject.SetActive(true);
            var group = _banner.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = _banner.gameObject.AddComponent<CanvasGroup>();
            float t = 0f;
            while (t < 0.16f)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01(t / 0.16f);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, duration));
            t = 0f;
            while (t < 0.25f)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / 0.25f);
                yield return null;
            }
            _banner.gameObject.SetActive(false);
            group.alpha = 1f;
        }
    }
}
