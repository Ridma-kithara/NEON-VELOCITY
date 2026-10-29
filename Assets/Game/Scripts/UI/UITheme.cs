using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Original runtime UI language for NEON VELOCITY. Every control is generated from code so
    /// the project has no external font, sprite or prefab dependencies and remains valid if a
    /// scene is rebuilt. Layout is anchored, so it adapts to phone, tablet and desktop ratios.
    /// </summary>
    public static class UITheme
    {
        public static readonly Color Background = Palette.Void;
        public static readonly Color Panel = new Color(0.035f, 0.06f, 0.12f, 0.94f);
        public static readonly Color PanelSoft = new Color(0.05f, 0.09f, 0.17f, 0.82f);
        public static readonly Color PanelBright = new Color(0.08f, 0.14f, 0.25f, 0.96f);
        public static readonly Color Cyan = Palette.Cyan;
        public static readonly Color Magenta = Palette.Magenta;
        public static readonly Color Lime = Palette.Lime;
        public static readonly Color Amber = Palette.Amber;
        public static readonly Color Violet = Palette.Violet;
        public static readonly Color Red = Palette.Red;
        public static readonly Color White = Palette.White;
        public static readonly Color Muted = Palette.Muted;
        public static readonly Color Dim = new Color(0.48f, 0.58f, 0.72f, 1f);

        public const int DisplaySize = 72;
        public const int TitleSize = 44;
        public const int HeadingSize = 28;
        public const int BodySize = 22;
        public const int SmallSize = 17;

        public static Font Font => FontLibrary.Body;
        public static Sprite Rounded => TextureFactory.RoundedPanel;
        public static Sprite Glow => TextureFactory.Glow;
        public static Sprite Border => TextureFactory.PanelBorder;

        /// <summary>
        /// Signature "glass card": rounded dark panel under a 9-sliced technical border
        /// overlay. Returns the border image (child) so callers can tint the hairline.
        /// </summary>
        public static Image GlassCard(Transform parent, string name, Color fill, float borderOpacity = 0.55f)
        {
            var panel = PanelBox(parent, name, fill);
            var border = Img(panel.transform, "Border", White.Alpha(borderOpacity), Border);
            Stretch((RectTransform)border.transform, 2f, 2f, 2f, 2f);
            border.raycastTarget = false;
            return border;
        }

        public static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        public static Image Img(Transform parent, string name, Color color, Sprite sprite = null, bool raycast = false)
        {
            var rect = NewRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = raycast;
            if (sprite != null)
            {
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f;
            }
            return image;
        }

        public static Image PanelBox(Transform parent, string name, Color color, bool rounded = true)
        {
            var panel = Img(parent, name, color, rounded ? Rounded : null, true);
            return panel;
        }

        public static Text Label(Transform parent, string name, string value, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rect = NewRect(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.font = Font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        public static Shadow AddShadow(Graphic graphic, Color color, Vector2 distance)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
            return shadow;
        }

        public static Outline AddOutline(Graphic graphic, Color color, Vector2 distance)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            return outline;
        }

        public static Button Button(Transform parent, string name, string label, Color accent,
            UnityAction onClick, int fontSize = HeadingSize, bool filled = true)
        {
            var image = Img(parent, name, filled ? PanelBright : PanelSoft, Rounded, true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.78f, 0.82f, 0.92f, 1f);
            colors.selectedColor = new Color(1.12f, 1.16f, 1.2f, 1f);
            colors.disabledColor = new Color(0.35f, 0.38f, 0.45f, 0.65f);
            colors.fadeDuration = 0.08f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            if (onClick != null) button.onClick.AddListener(onClick);

            var bar = Img(image.transform, "Accent", accent);
            Anchor((RectTransform)bar.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 10f), new Vector2(5f, -10f));
            bar.raycastTarget = false;

            var text = Label(image.transform, "Label", label, fontSize, White, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch((RectTransform)text.transform, 26f, 8f, 26f, 8f);
            // Button labels are single-line by design. Wrapping to a hidden second line
            // at some canvas scales silently shortened labels (e.g. COPY ACCOUNT NUMBER
            // rendered as COPY ACCOUNT at 4K). Never wrap a button label.
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            AddShadow(text, new Color(0f, 0f, 0f, 0.55f), new Vector2(1f, -2f));
            return button;
        }

        public static Button IconButton(Transform parent, string name, string glyph, Color accent,
            UnityAction onClick, int size = 60)
        {
            var image = Img(parent, name, PanelBright, Rounded, true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.25f, 1.35f, 1f);
            colors.pressedColor = new Color(0.7f, 0.75f, 0.85f, 1f);
            button.colors = colors;
            var text = Label(image.transform, "Glyph", glyph, size, accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch((RectTransform)text.transform, 0f, 0f, 0f, 2f);
            return button;
        }

        public static Slider Slider(Transform parent, string name, float value, Color accent, UnityAction<float> onChanged)
        {
            var root = NewRect(parent, name);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Mathf.Clamp01(value);
            slider.transition = Selectable.Transition.None;

            var background = Img(root, "Background", new Color(0.12f, 0.18f, 0.28f, 0.9f), Rounded);
            Anchor((RectTransform)background.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -4f), new Vector2(0f, 4f));
            var fillArea = NewRect(root, "Fill Area");
            Anchor(fillArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -4f), new Vector2(-14f, 4f));
            var fill = Img(fillArea, "Fill", accent, Rounded);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var handleArea = NewRect(root, "Handle Slide Area");
            Stretch(handleArea, 8f, 0f, 8f, 0f);
            var handle = Img(handleArea, "Handle", Color.white, TextureFactory.Diamond, true);
            var handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(30f, 30f);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        public static Toggle Toggle(Transform parent, string name, string label, bool value, Color accent, UnityAction<bool> onChanged)
        {
            var root = NewRect(parent, name);
            var toggle = root.gameObject.AddComponent<Toggle>();
            var box = Img(root, "Box", PanelBright, Rounded, true);
            Anchor((RectTransform)box.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, -22f), new Vector2(44f, 22f));
            var check = Img(box.transform, "Check", accent, Rounded);
            Stretch((RectTransform)check.transform, 7f, 7f, 7f, 7f);
            var text = Label(root, "Label", label, BodySize, White, TextAnchor.MiddleLeft);
            Anchor((RectTransform)text.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(58f, 0f), Vector2.zero);
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = value;
            var colors = toggle.colors;
            colors.highlightedColor = new Color(1.2f, 1.25f, 1.3f, 1f);
            colors.pressedColor = new Color(0.75f, 0.8f, 0.9f, 1f);
            toggle.colors = colors;
            if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
            return toggle;
        }

        public static InputField Input(Transform parent, string name, string value, string placeholder,
            int size = BodySize)
        {
            var bg = Img(parent, name, Panel, Rounded, true);
            var input = bg.gameObject.AddComponent<InputField>();
            var text = Label(bg.transform, "Text", value, size, White, TextAnchor.MiddleLeft);
            Stretch((RectTransform)text.transform, 18f, 6f, 18f, 6f);
            text.raycastTarget = false;
            var hint = Label(bg.transform, "Placeholder", placeholder, size, Muted, TextAnchor.MiddleLeft);
            Stretch((RectTransform)hint.transform, 18f, 6f, 18f, 6f);
            hint.fontStyle = FontStyle.Italic;
            hint.raycastTarget = false;
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = bg;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 20;
            input.caretColor = Cyan;
            input.selectionColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            return input;
        }

        public static ScrollRect Scroll(Transform parent, string name, out RectTransform content)
        {
            var root = NewRect(parent, name);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            var viewport = NewRect(root, "Viewport");
            Stretch(viewport);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            content = NewRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(12, 12, 8, 24);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
            scroll.scrollSensitivity = 34f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            return scroll;
        }

        public static LayoutElement Layout(GameObject go, float preferredHeight = -1f, float preferredWidth = -1f,
            float minHeight = -1f, float flexibleHeight = -1f)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.preferredWidth = preferredWidth;
            element.minHeight = minHeight;
            element.flexibleHeight = flexibleHeight;
            return element;
        }

        public static GameObject Divider(Transform parent, Color color, float height = 2f)
        {
            var image = Img(parent, "Divider", color);
            Layout(image.gameObject, height, -1f, height);
            return image.gameObject;
        }

        public static Image Badge(Transform parent, string name, string value, Color color, int width = 120, int height = 30)
        {
            var bg = Img(parent, name, new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 0.95f), Rounded);
            var rect = (RectTransform)bg.transform;
            rect.sizeDelta = new Vector2(width, height);
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(color.r, color.g, color.b, 0.8f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            var text = Label(bg.transform, "Text", value, Mathf.Max(13, height - 12), color, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch((RectTransform)text.transform, 6f, 2f, 6f, 2f);
            return bg;
        }

        public static Color Alpha(this Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        public static string FormatNumber(int value) => FormatNumber((long)value);

        public static string FormatNumber(long value)
        {
            if (value >= 1000000000L) return (value / 1000000000.0).ToString("0.0") + "B";
            if (value >= 1000000L) return (value / 1000000.0).ToString("0.0") + "M";
            if (value >= 10000L) return (value / 1000.0).ToString("0.0") + "K";
            return value.ToString("N0");
        }

        public static string FormatDistance(float metres)
        {
            return metres >= 1000f ? (metres / 1000f).ToString("0.00") + " km" : Mathf.RoundToInt(metres) + " m";
        }

        public static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int minutes = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.FloorToInt(seconds % 60f);
            int tenths = Mathf.FloorToInt((seconds * 10f) % 10f);
            return minutes > 0 ? $"{minutes:0}:{secs:00}.{tenths}" : $"{secs:0}.{tenths}";
        }
    }

    /// <summary>Keeps full-screen UI clear of notches and rounded corners on phones.</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreen;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            if (Screen.safeArea != _lastSafeArea ||
                Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
                Apply();
        }

        private void Apply()
        {
            if (_rect == null) return;
            Rect safe = Screen.safeArea;
            _lastSafeArea = safe;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;
            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
