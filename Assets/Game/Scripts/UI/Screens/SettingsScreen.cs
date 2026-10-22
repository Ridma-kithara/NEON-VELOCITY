using System;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// All persistent options. Audio and accessibility changes are applied live; navigation
    /// commits everything atomically so a crash or app kill cannot leave a half-written profile.
    /// </summary>
    public sealed class SettingsScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;
        private bool _returnToRun;
        private InputField _nameField;
        private InputField _hostField;
        private InputField _portField;
        private Text _graphicsLabel;
        private Text _fpsLabel;

        public static GameObject Build(GameApp app, UIRoot ui, bool returnToRun)
        {
            var root = ScreenKit.Root("Settings");
            var screen = root.gameObject.AddComponent<SettingsScreen>();
            screen._app = app;
            screen._ui = ui;
            screen._returnToRun = returnToRun;
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
                UITheme.Magenta, 300f);
            var s = _app.Profile.settings;
            AddSlider(panel.transform, "MUSIC", s.musicVolume, 0, UITheme.Magenta, value =>
            {
                s.musicVolume = value;
                _app.ApplySettings(false);
            });
            AddSlider(panel.transform, "EFFECTS", s.sfxVolume, 1, UITheme.Cyan, value =>
            {
                s.sfxVolume = value;
                _app.ApplySettings(false);
            });
            AddSlider(panel.transform, "AMBIENCE", s.ambienceVolume, 2, UITheme.Violet, value =>
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
            var panel = Section(content, "VIDEO", "Quality scales shadows, particles and resolution.",
                UITheme.Amber, 300f);
            var s = _app.Profile.settings;
            _graphicsLabel = UITheme.Label(panel.transform, "Graphics Label", "GRAPHICS PRESET",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_graphicsLabel.transform, new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                new Vector2(26f, 196f), new Vector2(-10f, 248f));

            string[] presets = { "LOW", "MEDIUM", "HIGH", "ULTRA" };
            for (int i = 0; i < presets.Length; i++)
            {
                int index = i;
                var button = UITheme.Button(panel.transform, "Preset " + i, presets[i],
                    i == s.graphicsPreset ? UITheme.Amber : UITheme.Muted, () =>
                    {
                        s.graphicsPreset = index;
                        _app.ApplySettings(false);
                        Rebuild();
                    }, UITheme.SmallSize, i == s.graphicsPreset);
                UITheme.Anchor((RectTransform)button.transform, new Vector2(i / 4f, 0f), new Vector2((i + 1) / 4f, 0f),
                    new Vector2(26f + (i == 0 ? 0f : 4f), 144f), new Vector2(-26f, 188f));
            }

            _fpsLabel = UITheme.Label(panel.transform, "Fps Label", "FRAME RATE",
                UITheme.SmallSize, UITheme.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)_fpsLabel.transform, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(26f, 98f), new Vector2(-10f, 136f));
            int[] rates = { 30, 60, 144 };
            for (int i = 0; i < rates.Length; i++)
            {
                int rate = rates[i];
                var button = UITheme.Button(panel.transform, "Fps " + rate, rate + " FPS",
                    s.targetFrameRate == rate ? UITheme.Amber : UITheme.Muted, () =>
                    {
                        s.targetFrameRate = rate;
                        _app.ApplySettings(false);
                        Rebuild();
                    }, UITheme.SmallSize, s.targetFrameRate == rate);
                UITheme.Anchor((RectTransform)button.transform, new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 0f),
                    new Vector2(26f + (i == 0 ? 0f : 4f), 58f), new Vector2(-26f, 94f));
            }

            var shake = UITheme.Toggle(panel.transform, "Shake", "Camera shake", s.cameraShake, UITheme.Amber,
                value => { s.cameraShake = value; });
            UITheme.Anchor((RectTransform)shake.transform, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(26f, 10f), new Vector2(-12f, 46f));
            var motion = UITheme.Toggle(panel.transform, "Motion", "Reduced motion", s.reducedMotion, UITheme.Amber,
                value => { s.reducedMotion = value; _app.ApplySettings(false); });
            UITheme.Anchor((RectTransform)motion.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                new Vector2(10f, 10f), new Vector2(-26f, 46f));
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
            var panel = Section(content, "NETWORK", "Authoritative race server. Use the host's LAN address for local play.",
                UITheme.Cyan, 236f);
            var s = _app.Profile.settings;
            _hostField = UITheme.Input(panel.transform, "Host", s.serverHost, "127.0.0.1", UITheme.BodySize);
            UITheme.Anchor((RectTransform)_hostField.transform, new Vector2(0f, 0f), new Vector2(0.68f, 0f),
                new Vector2(26f, 52f), new Vector2(-8f, 116f));
            _portField = UITheme.Input(panel.transform, "Port", s.serverPort.ToString(), "7777", UITheme.BodySize);
            UITheme.Anchor((RectTransform)_portField.transform, new Vector2(0.68f, 0f), new Vector2(1f, 0f),
                new Vector2(8f, 52f), new Vector2(-26f, 116f));

            var note = UITheme.Label(panel.transform, "Note",
                "Raw TCP multiplayer is available in macOS, Windows, iOS and Android builds. WebGL builds are offline-only.",
                UITheme.SmallSize, UITheme.Muted, TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)note.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 14f), new Vector2(-26f, 48f));
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
            _app.ApplySettings(true);
            if (_returnToRun && _app.Session != null)
            {
                _ui.CloseScreen();
                _app.Session.SetPaused(false);
            }
            else
            {
                _app.ShowMainMenu();
            }
        }

        private void Rebuild()
        {
            var next = Build(_app, _ui, _returnToRun);
            _app.UI.ShowScreen(next);
        }
    }
}
