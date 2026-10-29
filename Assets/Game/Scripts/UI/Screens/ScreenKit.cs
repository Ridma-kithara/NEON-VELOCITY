using System;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Shared layout primitives for the runtime screens. The console/phone-first layout uses a
    /// fixed 1920x1080 reference and anchored regions so it scales cleanly on every target.
    /// </summary>
    public static class ScreenKit
    {
        public static RectTransform Root(string name)
        {
            var rect = UITheme.NewRect(null, name);
            UITheme.Stretch(rect);
            return rect;
        }

        public static void Backdrop(RectTransform root, Color accent, bool translucent = false)
        {
            var bg = UITheme.Img(root, "Backdrop", new Color(0.018f, 0.03f, 0.06f, translucent ? 0.42f : 0.965f));
            UITheme.Stretch((RectTransform)bg.transform);
            bg.raycastTarget = false;

            var gradient = RawImageFor(root, "Gradient",
                TextureFactory.VerticalGradient("screen-gradient", new Color(0.01f, 0.02f, 0.05f, 1f),
                    new Color(0.03f, 0.07f, 0.12f, 1f)), translucent ? 0.55f : 0.85f);
            UITheme.Stretch((RectTransform)gradient.transform);
            gradient.raycastTarget = false;

            var glow = UITheme.Img(root, "Glow", new Color(accent.r, accent.g, accent.b, translucent ? 0.08f : 0.16f),
                UITheme.Glow, false);
            UITheme.Anchor((RectTransform)glow.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-880f, -560f), new Vector2(160f, 240f));
            glow.raycastTarget = false;

            var line = UITheme.Img(root, "Horizon", new Color(accent.r, accent.g, accent.b, 0.22f));
            UITheme.Anchor((RectTransform)line.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 84f), new Vector2(0f, 86f));
            line.raycastTarget = false;
        }

        public static RawImage RawImageFor(Transform parent, string name, Texture texture, float alpha = 1f)
        {
            var rect = UITheme.NewRect(parent, name);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = new Color(1f, 1f, 1f, alpha);
            image.raycastTarget = false;
            return image;
        }

        public static void Header(RectTransform root, GameApp app, string section, string title, string subtitle,
            Color accent, Action back)
        {
            var band = UITheme.Img(root, "Header Band", new Color(0.025f, 0.05f, 0.09f, 0.92f));
            UITheme.Anchor((RectTransform)band.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -132f), Vector2.zero);
            band.raycastTarget = false;
            var rule = UITheme.Img(band.transform, "Rule", accent);
            UITheme.Anchor((RectTransform)rule.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 3f));

            if (back != null)
            {
                var backButton = UITheme.IconButton(band.transform, "Back", "\u2039", accent, () => back(), 42);
                UITheme.Anchor((RectTransform)backButton.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(28f, -28f), new Vector2(84f, 28f));
                UITheme.AddOutline(backButton.targetGraphic, new Color(accent.r, accent.g, accent.b, 0.55f),
                    new Vector2(1.5f, -1.5f));
            }

            float contentLeft = back != null ? 112f : 34f;
            // Three stacked rows keep every section label, title and subtitle on their own
            // line, so long section names can never collide with the description.
            var sectionLabel = UITheme.Label(band.transform, "Section", section, UITheme.SmallSize, accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)sectionLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(contentLeft, 101f), new Vector2(-320f, 123f));

            var titleLabel = UITheme.Label(band.transform, "Title", title, UITheme.TitleSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)titleLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(contentLeft, 45f), new Vector2(-320f, 99f));

            var subtitleLabel = UITheme.Label(band.transform, "Subtitle", subtitle, UITheme.SmallSize, UITheme.Dim,
                TextAnchor.MiddleLeft);
            UITheme.Anchor((RectTransform)subtitleLabel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(contentLeft, 11f), new Vector2(-320f, 39f));

            CurrencyStrip(band.transform, app);
        }

        public static Text CurrencyStrip(Transform parent, GameApp app)
        {
            var strip = UITheme.Label(parent, "Currency", "", UITheme.BodySize, UITheme.Amber,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Anchor((RectTransform)strip.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-430f, -22f), new Vector2(-32f, 22f));
            RefreshCurrency(strip, app);
            return strip;
        }

        public static void RefreshCurrency(Text label, GameApp app)
        {
            if (label == null || app == null || app.Profile == null) return;
            label.text = "LVL " + app.Profile.level + "   \u25C6 " + UITheme.FormatNumber(app.Profile.credits) + " CREDITS";
        }

        public static Image Card(Transform parent, string name, Color? color = null)
        {
            return UITheme.Img(parent, name, color ?? UITheme.Panel, UITheme.Rounded, false);
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            return UITheme.Label(parent, name, text, size, color, anchor, style);
        }

        public static Button MenuButton(Transform parent, string label, string subtitle, Color accent,
            Action action, bool enabled = true, bool compact = false)
        {
            var button = UITheme.Button(parent, label, "", accent,
                enabled ? () => action() : (UnityEngine.Events.UnityAction)null,
                compact ? UITheme.BodySize : UITheme.HeadingSize);
            var labelText = button.GetComponentInChildren<Text>();
            // The button's built-in label is reused for the title; subtitle is a second line.
            if (labelText != null)
            {
                labelText.text = label;
                labelText.alignment = TextAnchor.MiddleLeft;
                labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
                if (compact)
                    UITheme.Stretch((RectTransform)labelText.transform, 22f, 30f, 42f, 5f);
                else
                    UITheme.Stretch((RectTransform)labelText.transform, 30f, 40f, 64f, 6f);
            }
            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = UITheme.Label(button.transform, "Sub", subtitle, UITheme.SmallSize, UITheme.Dim,
                    TextAnchor.MiddleLeft);
                sub.horizontalOverflow = HorizontalWrapMode.Overflow;
                if (compact)
                    UITheme.Stretch((RectTransform)sub.transform, 22f, 4f, 42f, 32f);
                else
                    UITheme.Stretch((RectTransform)sub.transform, 30f, 6f, 64f, 46f);
            }
            // Right chevron marks the row as navigable; underline sweeps in on hover.
            var chevron = UITheme.Label(button.transform, "Chevron", "\u203A", UITheme.BodySize, accent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UITheme.Stretch((RectTransform)chevron.transform, -160f, 6f, 24f, 6f);
            var underline = UITheme.Img(button.transform, "Underline", accent);
            UITheme.Anchor((RectTransform)underline.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(30f, 2f), new Vector2(160f, 4f));
            underline.raycastTarget = false;
            underline.canvasRenderer.SetAlpha(0f);
            if (enabled)
            {
                var hover = UiFx.Hover(button.gameObject, 1.035f, labelText, Color.white);
                UiFx.Press(button.gameObject, 0.97f);
                hover.gameObject.AddComponent<MenuButtonUnderline>().Configure(underline);
            }
            if (!enabled)
            {
                button.interactable = false;
            }
            return button;
        }

        public static GameObject Tag(Transform parent, string text, Color color, int width = 120, int height = 28)
        {
            return UITheme.Badge(parent, "Tag", text, color, width, height).gameObject;
        }

        public static void ProgressBar(Transform parent, string name, float value, Color accent,
            out Image fill, Vector2 offsetMin, Vector2 offsetMax)
        {
            var bg = UITheme.Img(parent, name, new Color(0.1f, 0.16f, 0.24f, 0.95f), UITheme.Rounded, false);
            UITheme.Anchor((RectTransform)bg.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), offsetMin, offsetMax);
            fill = UITheme.Img(bg.transform, "Fill", accent, UITheme.Rounded, false);
            var rect = (RectTransform)fill.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void Footer(RectTransform root, string text)
        {
            var label = UITheme.Label(root, "Footer", text, UITheme.SmallSize, UITheme.Muted,
                TextAnchor.MiddleCenter);
            UITheme.Anchor((RectTransform)label.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 18f), new Vector2(-24f, 46f));
        }

        public static string ModeStatus(GameApp app, NeonVelocity.Core.ModeSpec mode)
        {
            var best = app.Profile.FindModeBest(mode.Id.ToString());
            string bestText = best != null && best.distance > 0f
                ? "BEST " + UITheme.FormatDistance(best.distance)
                : "NO RECORD";
            return bestText;
        }
    }

    /// <summary>Fades a menu row's underline in while the row is hovered or focused.</summary>
    public sealed class MenuButtonUnderline : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        private Image _underline;
        private Color _color;
        private float _weight;
        private bool _entered;

        public void Configure(Image underline)
        {
            _underline = underline;
            if (_underline != null) _color = _underline.color;
        }

        public void OnPointerEnter(PointerEventData eventData) => _entered = true;
        public void OnPointerExit(PointerEventData eventData) => _entered = false;
        public void OnSelect(BaseEventData eventData) => _entered = true;
        public void OnDeselect(BaseEventData eventData) => _entered = false;

        private void Update()
        {
            if (_underline == null) return;
            _weight = Mathf.Lerp(_weight, _entered ? 1f : 0f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            float a = _weight * _color.a;
            _underline.canvasRenderer.SetAlpha(a);
        }
    }
}
