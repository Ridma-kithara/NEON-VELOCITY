using System;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Premium;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// All persistent options. Audio and accessibility changes are applied live; navigation
    /// commits everything atomically so a crash or app kill cannot leave a half-written profile.
    /// </summary>
    public sealed class SettingsScreen : MonoBehaviour, IScreenBackHandler
    {
        private GameApp _app;
        private UIRoot _ui;
        private InputField _nameField;
        private InputField _hostField;
        private InputField _portField;
        private InputField _premiumUrlField;
        private Text _presetLabel;
        private Text _resolutionLabel;
        private Text _displayModeLabel;
        private Text _frameRateLabel;
        private Text _renderScaleLabel;
        private Text _textureLabel;
        private Text _shadowLabel;
        private Text _reflectionLabel;
        private Text _aaLabel;
        private Text _effectsLabel;
        private Text _viewDistanceLabel;
        private Text _todModeLabel;
        private Text _todPhaseLabel;
        private Text _todCycleLabel;
        private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        private readonly List<string> _displayModes = new List<string>();
        private readonly Button[] _presetButtons = new Button[5];

        public static GameObject Build(GameApp app, UIRoot ui, bool returnToRun)
        {
            var root = ScreenKit.Root("Settings");
            var screen = root.gameObject.AddComponent<SettingsScreen>();
            screen._app = app;
            screen._ui = ui;
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            var root = (RectTransform)transform;
            ScreenKit.Backdrop(root, UITheme.Muted);
            ScreenKit.Header(root, _app, "SYSTEM", "SETTINGS",
                "Changes apply live and are saved when you leave this screen.", UITheme.Cyan, Back);

            UITheme.Scroll(root, "Settings Scroll", out RectTransform content);
            var scroll = root.Find("Settings Scroll");
            UITheme.Anchor((RectTransform)scroll, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 96f), new Vector2(-36f, -158f));

            BuildCallsign(content);
            BuildAudio(content);
            BuildInput(content);
            BuildVideo(content);
            BuildTimeOfDay(content);
            BuildAccessibility(content);
            BuildNetwork(content);

            var defaults = UITheme.Button(root, "Defaults", "RESTORE DEFAULTS", UITheme.Muted,
                RestoreDefaults, UITheme.SmallSize, false);
            UITheme.Anchor((RectTransform)defaults.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(36f, 20f), new Vector2(292f, 70f));

            var save = UITheme.Button(root, "Save", "SAVE & RETURN", UITheme.Lime, Back, UITheme.BodySize);
            UITheme.Anchor((RectTransform)save.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-300f, 20f), new Vector2(-36f, 70f));
        }

        private Image Section(RectTransform content, string title, string subtitle, Color accent, float height)
        {
            var panel = ScreenKit.Card(content, title, new Color(0.035f, 0.065f, 0.12f, 0.94f));
            UITheme.Layout(panel.gameObject, height, -1f, height);
            var edge = UITheme.Img(panel.transform, "Edge", accent);
            UITheme.Anchor((RectTransform)edge.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 14f), new Vector2(4f, -14f));
            var heading = UITheme.Label(panel.transform, "Heading", title, UITheme.HeadingSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -52f), new Vector2(-28f, -14f));
            var sub = UITheme.Label(panel.transform, "Subtitle", subtitle, UITheme.SmallSize, UITheme.Dim,
                TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)sub.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -80f), new Vector2(-28f, -54f));
            return panel;
        }

        private void BuildCallsign(RectTransform content)
        {
            var panel = Section(content, "CALLSIGN", "Shown in lobbies, results and leaderboards.",
                UITheme.Cyan, 154f);
            _nameField = UITheme.Input(panel.transform, "Name", _app.Profile.playerName,
                "RUNNER NAME", UITheme.BodySize);
            UITheme.Anchor((RectTransform)_nameField.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(26f, 24f), new Vector2(-230f, -94f));
            var apply = UITheme.Button(panel.transform, "Apply", "APPLY", UITheme.Cyan,
                ApplyName, UITheme.BodySize);
            UITheme.Anchor((RectTransform)apply.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-204f, 24f), new Vector2(-26f, 80f));
        }

        private void ApplyName()
        {
            string cleaned = RaceRules.SanitiseName(_nameField.text);
            _nameField.text = cleaned;
            _app.Progression.SetPlayerName(cleaned);
            _app.Race.SetPlayerName(cleaned);
            _app.UI.Toast("CALLSIGN UPDATED", UITheme.Cyan, 2f);
        }

        private void BuildAudio(RectTransform content)
        {
            var panel = Section(content, "AUDIO", "Original synthesized soundtrack and effects.",
                UITheme.Magenta, 358f);
            var s = _app.Profile.settings;
            AddSlider(panel.transform, "MASTER", s.masterVolume, 0, UITheme.White, value =>
            {
                s.masterVolume = value;
                _app.ApplySettings(false);
            });
            AddSlider(panel.transform, "MUSIC", s.musicVolume, 1, UITheme.Magenta, value =>
            {
                s.musicVolume = value;
                _app.ApplySettings(false);
            });
            AddSlider(panel.transform, "EFFECTS", s.sfxVolume, 2, UITheme.Cyan, value =>
            {
                s.sfxVolume = value;
                _app.ApplySettings(false);
            });
            AddSlider(panel.transform, "AMBIENCE", s.ambienceVolume, 3, UITheme.Violet, value =>
            {
                s.ambienceVolume = value;
                _app.ApplySettings(false);
            });
        }

        private void BuildInput(RectTransform content)
        {
            var panel = Section(content, "INPUT", "Keyboard, gamepad and touch gestures share one router.",
                UITheme.Lime, 190f);
            var s = _app.Profile.settings;
            AddSlider(panel.transform, "SWIPE SENSITIVITY", Mathf.InverseLerp(0.4f, 2.5f, s.swipeSensitivity),
                0, UITheme.Lime, value =>
                {
                    s.swipeSensitivity = Mathf.Lerp(0.4f, 2.5f, value);
                    if (_app.Session != null && _app.Session.Input != null)
                        _app.Session.Input.SetSensitivity(s.swipeSensitivity);
                });
            var hint = UITheme.Label(panel.transform, "Hint",
                "A / D or swipe to steer   \u00B7   SPACE or swipe up to jump   \u00B7   S or swipe down to slide",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)hint.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 20f), new Vector2(-26f, 54f));
        }

        private void BuildVideo(RectTransform content)
        {
            bool desktop = !Application.isMobilePlatform;
            var panel = Section(content, "VIDEO",
                desktop
                    ? "Display output and rendering quality. Custom exposes the individual PC settings."
                    : "Mobile profile: resolution is controlled by the device to protect battery and thermals.",
                UITheme.Amber, desktop ? 900f : 780f);
            var s = _app.Profile.settings;
            BuildResolutionOptions(s);

            _presetLabel = UITheme.Label(panel.transform, "Preset Label", "GRAPHICS PRESET",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_presetLabel.transform, new Vector2(0f, 1f), new Vector2(0.24f, 1f),
                new Vector2(26f, -138f), new Vector2(-8f, -96f));

            string[] presets = { "LOW", "MEDIUM", "HIGH", "ULTRA", "CUSTOM" };
            for (int i = 0; i < presets.Length; i++)
            {
                int index = i;
                float start = 0.24f;
                float each = (1f - start) / presets.Length;
                var button = UITheme.Button(panel.transform, "Preset " + i, presets[i],
                    index == s.graphicsPreset ? UITheme.Amber : UITheme.Muted, () => SelectPreset(index),
                    UITheme.SmallSize, index == s.graphicsPreset);
                UITheme.Anchor((RectTransform)button.transform,
                    new Vector2(start + i * each, 1f), new Vector2(start + (i + 1) * each, 1f),
                    new Vector2(i == 0 ? 0f : 3f, -138f), new Vector2(-3f, -96f));
                _presetButtons[i] = button;
            }

            float top = 150f;
            if (desktop)
            {
                _resolutionLabel = ChoiceRow(panel.transform, top, "RESOLUTION",
                    () => CycleResolution(-1), () => CycleResolution(1));
                top += 50f;
                _displayModeLabel = ChoiceRow(panel.transform, top, "DISPLAY MODE",
                    () => CycleDisplayMode(-1), () => CycleDisplayMode(1));
                top += 50f;
            }

            _frameRateLabel = ChoiceRow(panel.transform, top, "FRAME RATE",
                () => CycleFrameRate(-1), () => CycleFrameRate(1));
            top += 50f;
            _renderScaleLabel = ChoiceRow(panel.transform, top, "RENDER SCALE",
                () => CycleRenderScale(-1), () => CycleRenderScale(1));
            top += 50f;
            _textureLabel = ChoiceRow(panel.transform, top, "TEXTURE QUALITY",
                () => CycleDetail(refresh => refresh.textureQuality = Step(refresh.textureQuality, -1, 2)),
                () => CycleDetail(refresh => refresh.textureQuality = Step(refresh.textureQuality, 1, 2)));
            top += 50f;
            _shadowLabel = ChoiceRow(panel.transform, top, "SHADOWS",
                () => CycleDetail(refresh => refresh.shadowQuality = Step(refresh.shadowQuality, -1, 3)),
                () => CycleDetail(refresh => refresh.shadowQuality = Step(refresh.shadowQuality, 1, 3)));
            top += 50f;
            _reflectionLabel = ChoiceRow(panel.transform, top, "REFLECTIONS",
                () => CycleDetail(refresh => refresh.reflectionQuality = Step(refresh.reflectionQuality, -1, 2)),
                () => CycleDetail(refresh => refresh.reflectionQuality = Step(refresh.reflectionQuality, 1, 2)));
            top += 50f;
            _aaLabel = ChoiceRow(panel.transform, top, "ANTI-ALIASING",
                () => CycleAntiAliasing(-1), () => CycleAntiAliasing(1));
            top += 50f;
            _effectsLabel = ChoiceRow(panel.transform, top, "EFFECTS",
                () => CycleDetail(refresh => refresh.effectsQuality = Step(refresh.effectsQuality, -1, 2)),
                () => CycleDetail(refresh => refresh.effectsQuality = Step(refresh.effectsQuality, 1, 2)));
            top += 50f;
            _viewDistanceLabel = ChoiceRow(panel.transform, top, "VIEW DISTANCE",
                () => CycleDetail(refresh => refresh.viewDistance = Step(refresh.viewDistance, -1, 2)),
                () => CycleDetail(refresh => refresh.viewDistance = Step(refresh.viewDistance, 1, 2)));
            top += 56f;

            var shake = UITheme.Toggle(panel.transform, "Shake", "Camera shake", s.cameraShake, UITheme.Amber,
                value => { s.cameraShake = value; });
            UITheme.Anchor((RectTransform)shake.transform, new Vector2(0f, 1f), new Vector2(0.5f, 1f),
                new Vector2(26f, -top - 38f), new Vector2(-12f, -top));
            var motion = UITheme.Toggle(panel.transform, "Motion", "Reduced motion", s.reducedMotion, UITheme.Amber,
                value => { s.reducedMotion = value; _app.ApplySettings(false); });
            UITheme.Anchor((RectTransform)motion.transform, new Vector2(0.5f, 1f), new Vector2(1f, 1f),
                new Vector2(10f, -top - 38f), new Vector2(-26f, -top));
            top += 50f;

            var vsync = UITheme.Toggle(panel.transform, "VSync", "Vertical sync", s.vSync, UITheme.Amber,
                value => { s.vSync = value; _app.ApplySettings(false); });
            UITheme.Anchor((RectTransform)vsync.transform, new Vector2(0f, 1f), new Vector2(0.5f, 1f),
                new Vector2(26f, -top - 38f), new Vector2(-12f, -top));

            RefreshVideoLabels();
        }

        private Text ChoiceRow(Transform parent, float top, string label, Action previous, Action next)
        {
            var row = UITheme.NewRect(parent, label + " Row");
            UITheme.Anchor(row, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -top - 40f), new Vector2(-26f, -top));
            var name = UITheme.Label(row, "Label", label, UITheme.SmallSize, UITheme.Dim,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 0f), new Vector2(0.34f, 1f),
                Vector2.zero, Vector2.zero);
            var value = UITheme.Label(row, "Value", "", UITheme.BodySize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)value.transform, new Vector2(0.35f, 0f), new Vector2(0.84f, 1f),
                Vector2.zero, Vector2.zero);

            var left = UITheme.IconButton(row, "Previous", "\u2039", UITheme.Amber, () => previous(), 28);
            UITheme.Anchor((RectTransform)left.transform, new Vector2(0.85f, 0.5f), new Vector2(0.9f, 0.5f),
                new Vector2(0f, -20f), new Vector2(0f, 20f));
            var right = UITheme.IconButton(row, "Next", "\u203A", UITheme.Amber, () => next(), 28);
            UITheme.Anchor((RectTransform)right.transform, new Vector2(0.915f, 0.5f), new Vector2(0.965f, 0.5f),
                new Vector2(0f, -20f), new Vector2(0f, 20f));
            return value;
        }

        private void BuildResolutionOptions(SettingsData s)
        {
            _resolutions.Clear();
            _displayModes.Clear();
            _displayModes.Add("WINDOWED");
            _displayModes.Add("BORDERLESS");
            if (Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.LinuxPlayer ||
                Application.platform == RuntimePlatform.LinuxEditor)
                _displayModes.Add("FULLSCREEN");
            if (s.fullscreenMode >= _displayModes.Count) s.fullscreenMode = _displayModes.Count - 1;

            var seen = new HashSet<long>();
            void AddResolution(int width, int height)
            {
                if (width < 960 || height < 540) return;
                long key = ((long)width << 32) | (uint)height;
                if (!seen.Add(key)) return;
                _resolutions.Add(new Vector2Int(width, height));
            }

            AddResolution(Screen.width, Screen.height);
            var modes = Screen.resolutions;
            if (modes != null)
                for (int i = 0; i < modes.Length; i++) AddResolution(modes[i].width, modes[i].height);
            if (Display.main.systemWidth >= 3840 && Display.main.systemHeight >= 2160)
                AddResolution(3840, 2160);
            if (s.screenWidth > 0 && s.screenHeight > 0) AddResolution(s.screenWidth, s.screenHeight);
            _resolutions.Sort((a, b) =>
            {
                int area = ((long)a.x * a.y).CompareTo((long)b.x * b.y);
                return area != 0 ? area : a.x.CompareTo(b.x);
            });
        }

        private void SelectPreset(int index)
        {
            var s = _app.Profile.settings;
            s.graphicsPreset = index;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void CycleResolution(int direction)
        {
            if (_resolutions.Count == 0) return;
            var s = _app.Profile.settings;
            int width = s.screenWidth > 0 ? s.screenWidth : Screen.width;
            int height = s.screenHeight > 0 ? s.screenHeight : Screen.height;
            int current = _resolutions.FindIndex(r => r.x == width && r.y == height);
            if (current < 0) current = 0;
            int next = (current + direction + _resolutions.Count) % _resolutions.Count;
            s.screenWidth = _resolutions[next].x;
            s.screenHeight = _resolutions[next].y;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void CycleDisplayMode(int direction)
        {
            if (_displayModes.Count == 0) return;
            var s = _app.Profile.settings;
            s.fullscreenMode = (s.fullscreenMode + direction + _displayModes.Count) % _displayModes.Count;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void CycleFrameRate(int direction)
        {
            int[] rates = { 30, 60, 120, 144 };
            var s = _app.Profile.settings;
            int index = Array.IndexOf(rates, s.targetFrameRate);
            if (index < 0) index = 1;
            s.targetFrameRate = rates[(index + direction + rates.Length) % rates.Length];
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void CycleRenderScale(int direction)
        {
            var s = _app.Profile.settings;
            float[] scales = { 0.5f, 0.67f, 0.75f, 0.85f, 1f };
            int index = 0;
            float best = float.MaxValue;
            for (int i = 0; i < scales.Length; i++)
            {
                float d = Mathf.Abs(scales[i] - s.renderScale);
                if (d < best) { best = d; index = i; }
            }
            index = (index + direction + scales.Length) % scales.Length;
            s.renderScale = scales[index];
            s.graphicsPreset = 4;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void CycleDetail(Action<SettingsData> change)
        {
            var s = _app.Profile.settings;
            change(s);
            s.graphicsPreset = 4;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void CycleAntiAliasing(int direction)
        {
            int[] samples = { 0, 2, 4, 8 };
            var s = _app.Profile.settings;
            int index = Array.IndexOf(samples, s.antiAliasing);
            if (index < 0) index = 1;
            s.antiAliasing = samples[(index + direction + samples.Length) % samples.Length];
            s.graphicsPreset = 4;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private static int Step(int value, int direction, int max) =>
            Mathf.Clamp(value + direction, 0, max);

        private void RefreshVideoLabels()
        {
            var s = _app.Profile.settings;
            if (_presetLabel != null)
                _presetLabel.text = "GRAPHICS PRESET  \u00B7  " + PresetName(s.graphicsPreset);
            for (int i = 0; i < _presetButtons.Length; i++)
            {
                if (_presetButtons[i] == null) continue;
                var image = _presetButtons[i].GetComponent<Image>();
                if (image != null)
                    image.color = i == s.graphicsPreset
                        ? new Color(0.09f, 0.15f, 0.27f, 0.96f)
                        : new Color(0.05f, 0.09f, 0.17f, 0.82f);
            }
            if (_resolutionLabel != null)
                _resolutionLabel.text = ResolutionName(s);
            if (_displayModeLabel != null)
                _displayModeLabel.text = s.fullscreenMode >= 0 && s.fullscreenMode < _displayModes.Count
                    ? _displayModes[s.fullscreenMode] : "WINDOWED";
            if (_frameRateLabel != null)
                _frameRateLabel.text = s.targetFrameRate + " FPS";
            if (_renderScaleLabel != null)
                _renderScaleLabel.text = Mathf.RoundToInt(s.renderScale * 100f) + "%";
            if (_textureLabel != null)
                _textureLabel.text = s.textureQuality <= 0 ? "FULL" : s.textureQuality == 1 ? "HALF" : "QUARTER";
            if (_shadowLabel != null)
                _shadowLabel.text = s.shadowQuality <= 0 ? "OFF" : s.shadowQuality == 1 ? "LOW"
                    : s.shadowQuality == 2 ? "HIGH" : "ULTRA";
            if (_reflectionLabel != null)
                _reflectionLabel.text = s.reflectionQuality <= 0 ? "OFF"
                    : s.reflectionQuality == 1 ? "STANDARD" : "HIGH";
            if (_aaLabel != null)
                _aaLabel.text = s.antiAliasing <= 0 ? "OFF" : s.antiAliasing + "X MSAA";
            if (_effectsLabel != null)
                _effectsLabel.text = s.effectsQuality <= 0 ? "LOW" : s.effectsQuality == 1 ? "MEDIUM" : "HIGH";
            if (_viewDistanceLabel != null)
                _viewDistanceLabel.text = s.viewDistance <= 0 ? "NEAR" : s.viewDistance == 1 ? "STANDARD" : "FAR";
            if (_todModeLabel != null)
                _todModeLabel.text = s.timeOfDayMode == 1 ? "DYNAMIC"
                    : s.timeOfDayMode == 2 ? "FIXED" : "LOCATION";
            if (_todPhaseLabel != null)
                _todPhaseLabel.text = TimeOfDayCatalog.Label(s.timeOfDayPhase);
            if (_todCycleLabel != null)
                _todCycleLabel.text = Mathf.RoundToInt(s.timeOfDayCycleMetres / 1_000f) + " KM";
        }

        private static string PresetName(int preset)
        {
            switch (preset)
            {
                case 0: return "LOW";
                case 1: return "MEDIUM";
                case 2: return "HIGH";
                case 3: return "ULTRA";
                default: return "CUSTOM";
            }
        }

        private static string ResolutionName(SettingsData s)
        {
            int width = s.screenWidth > 0 ? s.screenWidth : Screen.width;
            int height = s.screenHeight > 0 ? s.screenHeight : Screen.height;
            if (width == 3840 && height == 2160) return "3840 \u00D7 2160  (4K UHD)";
            if (width == 2560 && height == 1440) return "2560 \u00D7 1440  (QHD)";
            return width + " \u00D7 " + height;
        }

        private void BuildTimeOfDay(RectTransform content)
        {
            var panel = Section(content, "TIME OF DAY",
                "Authored uses each location's own phase. Dynamic cycles all seven states with distance; fixed locks one phase.",
                UITheme.Violet, 226f);
            float top = 96f;
            _todModeLabel = ChoiceRow(panel.transform, top, "MODE",
                () => CycleTimeOfDayMode(-1), () => CycleTimeOfDayMode(1));
            top += 44f;
            _todPhaseLabel = ChoiceRow(panel.transform, top, "FIXED PHASE",
                () => CycleTimeOfDayPhase(-1), () => CycleTimeOfDayPhase(1));
            top += 44f;
            _todCycleLabel = ChoiceRow(panel.transform, top, "DAY CYCLE",
                () => CycleTimeOfDayCycle(-1), () => CycleTimeOfDayCycle(1));
        }

        private void CycleTimeOfDayMode(int direction)
        {
            var s = _app.Profile.settings;
            s.timeOfDayMode = (s.timeOfDayMode + direction + 3) % 3;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void CycleTimeOfDayPhase(int direction)
        {
            var s = _app.Profile.settings;
            s.timeOfDayPhase = (s.timeOfDayPhase + direction + TimeOfDayCatalog.Count) %
                               TimeOfDayCatalog.Count;
            // Adjusting the fixed phase is an explicit request to see that phase, so switch
            // away from authored/dynamic rather than silently changing an unused field.
            s.timeOfDayMode = 2;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void CycleTimeOfDayCycle(int direction)
        {
            float[] cycles = { 6_000f, 12_000f, 21_000f, 42_000f, 63_000f, 105_000f };
            var s = _app.Profile.settings;
            int index = 2;
            float best = float.MaxValue;
            for (int i = 0; i < cycles.Length; i++)
            {
                float delta = Mathf.Abs(cycles[i] - s.timeOfDayCycleMetres);
                if (delta < best) { best = delta; index = i; }
            }
            index = (index + direction + cycles.Length) % cycles.Length;
            s.timeOfDayCycleMetres = cycles[index];
            s.timeOfDayMode = 1;
            _app.ApplySettings(false);
            RefreshVideoLabels();
        }

        private void BuildAccessibility(RectTransform content)
        {
            var panel = Section(content, "ACCESSIBILITY", "Comfort and readability options.",
                UITheme.Lime, 162f);
            var s = _app.Profile.settings;
            var contrast = UITheme.Toggle(panel.transform, "Contrast", "High contrast text", s.highContrast,
                UITheme.Lime, value =>
                {
                    s.highContrast = value;
                    _app.ApplySettings(false);
                });
            UITheme.Anchor((RectTransform)contrast.transform, new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                new Vector2(26f, 24f), new Vector2(-10f, -92f));
            var vibration = UITheme.Toggle(panel.transform, "Vibration", "Haptic feedback", s.vibration,
                UITheme.Lime, value =>
                {
                    s.vibration = value;
                });
            UITheme.Anchor((RectTransform)vibration.transform, new Vector2(0.5f, 0f), new Vector2(1f, 1f),
                new Vector2(10f, 24f), new Vector2(-26f, -92f));
        }

        private void BuildNetwork(RectTransform content)
        {
            var panel = Section(content, "NETWORK",
                "Race authority and premium store endpoints. Loopback defaults are for local testing.",
                UITheme.Cyan, 310f);
            var s = _app.Profile.settings;
            _hostField = UITheme.Input(panel.transform, "Host", s.serverHost, "127.0.0.1", UITheme.BodySize);
            UITheme.Anchor((RectTransform)_hostField.transform, new Vector2(0f, 0f), new Vector2(0.68f, 0f),
                new Vector2(26f, 160f), new Vector2(-8f, 224f));
            _portField = UITheme.Input(panel.transform, "Port", s.serverPort.ToString(), "7777", UITheme.BodySize);
            UITheme.Anchor((RectTransform)_portField.transform, new Vector2(0.68f, 0f), new Vector2(1f, 0f),
                new Vector2(8f, 160f), new Vector2(-26f, 224f));

            var premiumLabel = UITheme.Label(panel.transform, "Premium Label",
                "PREMIUM STORE URL  (HTTPS IN PRODUCTION)", UITheme.SmallSize, UITheme.Lime,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)premiumLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 130f), new Vector2(-26f, 154f));
            _premiumUrlField = UITheme.Input(panel.transform, "Premium URL", s.premiumServerUrl,
                "https://premium.example.com", UITheme.BodySize);
            _premiumUrlField.characterLimit = 300;
            UITheme.Anchor((RectTransform)_premiumUrlField.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 60f), new Vector2(-26f, 124f));

            var note = UITheme.Label(panel.transform, "Note",
                "Multiplayer uses raw TCP. The premium store requires the HTTPS service; offline or unreachable shows STORE OFFLINE.",
                UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)note.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 12f), new Vector2(-26f, 48f));
        }

        private void AddSlider(Transform parent, string label, float value, int index, Color accent,
            Action<float> changed)
        {
            var row = UITheme.NewRect(parent, label);
            UITheme.Anchor(row, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(26f, -116f - index * 58f), new Vector2(-26f, -72f - index * 58f));
            var name = UITheme.Label(row, "Label", label, UITheme.SmallSize,
                accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)name.transform, new Vector2(0f, 0f), new Vector2(0.3f, 1f),
                Vector2.zero, Vector2.zero);
            var slider = UITheme.Slider(row, "Slider", value, accent, v => changed(v));
            UITheme.Anchor((RectTransform)slider.transform, new Vector2(0.3f, 0f), new Vector2(1f, 1f),
                new Vector2(8f, 8f), new Vector2(-8f, -8f));
        }

        private void RestoreDefaults()
        {
            _ui.ShowModal("RESTORE DEFAULTS", "Reset audio, video, accessibility and network settings? " +
                "Your profile, credits and unlocks are untouched.", "RESTORE", () =>
            {
                _app.Profile.settings = new SettingsData();
                _app.ApplySettings(true);
                _ui.Toast("DEFAULTS RESTORED", UITheme.Cyan, 2.4f);
                Rebuild();
            });
        }

        private void Back()
        {
            ApplyName();
            var s = _app.Profile.settings;
            if (_hostField != null) s.serverHost = string.IsNullOrWhiteSpace(_hostField.text)
                ? "127.0.0.1" : _hostField.text.Trim();
            if (_portField != null && int.TryParse(_portField.text, out int port)) s.serverPort = port;
            if (_premiumUrlField != null)
            {
                string premiumUrl = _premiumUrlField.text == null ? "" : _premiumUrlField.text.Trim();
                if (!string.IsNullOrEmpty(premiumUrl) && !PremiumService.IsValidServerUrl(premiumUrl))
                {
                    _ui.Toast("PREMIUM URL MUST START WITH HTTP:// OR HTTPS://", UITheme.Red, 3.2f);
                    return;
                }
                s.premiumServerUrl = string.IsNullOrEmpty(premiumUrl)
                    ? "http://127.0.0.1:8088" : premiumUrl;
            }
            _app.ApplySettings(true);
            _app.NavigateBack();
        }

        public bool HandleBackRequest()
        {
            Back();
            return true;
        }

        private void Rebuild()
        {
            var next = Build(_app, _ui, false);
            _app.UI.ShowScreen(next);
        }
    }
}
