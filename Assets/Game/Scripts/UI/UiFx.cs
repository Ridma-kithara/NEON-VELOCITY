using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Shared interactive/animated UGUI primitives used across screens. All components
    /// live on Behaviour-monobehaviours that drive transform/graphics each frame only
    /// while a transition or loop is active, keeping the UI cheap on frame budget.
    /// </summary>
    public static class UiFx
    {
        public const float HoverSpring = 14f;

        /// <summary>
        /// Adds hover grow + press sink to any UI element. Optional colour ramp drives a
        /// target Graphic toward the hover tint (uses its base colour as idle).
        /// </summary>
        public static HoverScale Hover(GameObject go, float scale = 1.055f, Graphic tintTarget = null, Color? hoverTint = null)
        {
            var hover = go.GetComponent<HoverScale>();
            if (hover == null) hover = go.AddComponent<HoverScale>();
            hover.Configure(scale, tintTarget, hoverTint);
            return hover;
        }

        /// <summary>Sets up the pointer "press sink" micro-animation used by every button.</summary>
        public static PressScale Press(GameObject go, float scale = 0.955f)
        {
            var press = go.GetComponent<PressScale>();
            if (press == null) press = go.AddComponent<PressScale>();
            press.Configure(scale);
            return press;
        }
    }

    /// <summary>Pointer-driven grow effect with crit-damped spring back to rest.</summary>
    public sealed class HoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public float Scale => _scale;
        public bool Entered { get; private set; }
        private float _scale = 1.06f;
        private Vector3 _baseScale = Vector3.one;
        private float _weight;
        private Graphic _tintTarget;
        private Color? _hoverTint;
        private Color _baseTint = Color.white;
        private bool _capturedBase;

        public void Configure(float scale, Graphic tintTarget, Color? hoverTint)
        {
            _scale = scale;
            _tintTarget = tintTarget;
            _hoverTint = hoverTint;
        }

        private void CaptureBase()
        {
            if (_capturedBase) return;
            _baseScale = transform.localScale;
            if (_tintTarget != null) _baseTint = _tintTarget.color;
            _capturedBase = true;
        }

        private void OnEnable()
        {
            CaptureBase();
            transform.localScale = _baseScale;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Entered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Entered = false;
        }

        public void OnSelect(BaseEventData eventData)
        {
            Entered = true;
        }

        public void OnDeselect(BaseEventData eventData) => Entered = false;

        private void Update()
        {
            if (!_capturedBase) CaptureBase();
            float target = Entered ? 1f : 0f;
            _weight = Mathf.Lerp(_weight, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * UiFx.HoverSpring));
            if (_weight < 0.001f && target == 0f)
            {
                if (transform.localScale != _baseScale) transform.localScale = _baseScale;
                if (_tintTarget != null && _hoverTint != null && _tintTarget.color != _baseTint)
                    _tintTarget.color = _baseTint;
                return;
            }
            float eased = Mathf.SmoothStep(0f, 1f, _weight);
            transform.localScale = _baseScale * (1f + (_scale - 1f) * eased);
            if (_tintTarget != null && _hoverTint != null)
                _tintTarget.color = Color.Lerp(_baseTint, _hoverTint.Value, eased);
        }
    }

    /// <summary>Pointer press "sink": shrinks toward the pivot while held, springs back on release.</summary>
    public sealed class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private float _scale = 0.955f;
        private Vector3 _baseScale = Vector3.one;
        private float _weight;
        private bool _held;
        private bool _capturedBase;

        public void Configure(float scale) => _scale = scale;

        private void CaptureBase()
        {
            if (_capturedBase) return;
            _baseScale = transform.localScale;
            _capturedBase = true;
        }

        private void OnEnable()
        {
            CaptureBase();
            transform.localScale = _baseScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _held = true;
        }

        public void OnPointerUp(PointerEventData eventData) => _held = false;

        private void OnDisable() => _held = false;

        private void Update()
        {
            if (!_capturedBase) CaptureBase();
            float target = _held ? 1f : 0f;
            _weight = Mathf.Lerp(_weight, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * (_held ? 22f : 16f)));
            if (_weight < 0.001f && target == 0f)
            {
                if (transform.localScale != _baseScale) transform.localScale = _baseScale;
                return;
            }
            float eased = Mathf.SmoothStep(0f, 1f, _weight);
            transform.localScale = _baseScale * (1f - (1f - _scale) * eased);
        }
    }

    /// <summary>
    /// Rolling number display: eases toward a target value over ~0.65s, formatting through
    /// a formatter each frame. Used for GEMS counters, distance readouts and scores.
    /// </summary>
    public sealed class NumberTicker : MonoBehaviour
    {
        public Text Label { get; private set; }
        public long Target { get; private set; }
        private double _shown;
        private long _from;
        private float _t;
        private System.Func<long, string> _formatter = v => v.ToString("N0");
        private bool _formattingDirty = true;

        public static NumberTicker Attach(GameObject owner, Text label, long initial = 0, System.Func<long, string> formatter = null)
        {
            var ticker = owner.GetComponent<NumberTicker>() ?? owner.AddComponent<NumberTicker>();
            ticker.Label = label;
            ticker._formatter = formatter ?? ticker._formatter;
            ticker._shown = initial;
            ticker.Target = initial;
            if (ticker.Label != null) ticker.Label.text = ticker.CurrentText;
            return ticker;
        }

        public void SetFormatter(System.Func<long, string> formatter)
        {
            _formatter = formatter;
            _formattingDirty = true;
        }

        /// <summary>Sets a new target; jumps by default to near-target to re-roll the roll.</summary>
        public void Set(long value)
        {
            if (Target == value) return;
            _from = (long)_shown;
            Target = value;
            _t = 0f;
            _formattingDirty = true;
        }

        public void JumpTo(long value)
        {
            Target = value;
            _from = value;
            _shown = value;
            _t = 1f;
            if (Label != null) Label.text = CurrentText;
        }

        private string CurrentText => _formatter?.Invoke((long)_shown) ?? ((long)_shown).ToString("N0");

        private void Update()
        {
            if (Label == null) return;
            if (Mathf.Approximately(_t, 1f) && !_formattingDirty) return;
            if (_t < 1f)
            {
                _t = Mathf.MoveTowards(_t, 1f, Time.unscaledDeltaTime / 0.65f);
                float eased = 1f - Mathf.Pow(1f - _t, 3f);
                _shown = Mathf.Lerp(_from, Target, eased);
            }
            else
            {
                _shown = Target;
            }
            Label.text = CurrentText;
            _formattingDirty = false;
        }
    }

    /// <summary>
    /// Smoothly driven progress bar: eases toward target fill plus a "sheen" sweep that
    /// crosses the bar once per great change. Drives an Image (filled) or a child fill rect.
    /// </summary>
    public sealed class PulseBar : MonoBehaviour
    {
        public Image Fill { get; private set; }
        public RectTransform Sheen { get; private set; }
        private float _shown = -1f;
        private float _target;
        private float _sheenTimer = -1f;

        public static PulseBar Attach(Image fillImage, RectTransform sheen = null)
        {
            var holder = fillImage != null ? fillImage.gameObject : null;
            var bar = holder != null ? holder.GetComponent<PulseBar>() ?? holder.AddComponent<PulseBar>() : null;
            if (bar == null) return null;
            bar.Fill = fillImage;
            bar.Sheen = sheen;
            return bar;
        }

        public void Set(float linear01)
        {
            linear01 = Mathf.Clamp01(linear01);
            if (_target.Approximately(linear01)) return;
            if (_shown >= 0f && linear01 > _target) TriggerSheen();
            _target = linear01;
        }

        public void Snap(float linear01)
        {
            _target = Mathf.Clamp01(linear01);
            _shown = _target;
            ApplyFill();
        }

        public void TriggerSheen()
        {
            if (Sheen == null) return;
            _sheenTimer = 0f;
            Sheen.gameObject.SetActive(true);
        }

        private void ApplyFill()
        {
            if (Fill == null) return;
            Fill.fillAmount = _shown;
        }

        private void Update()
        {
            if (_shown < 0f) _shown = _target;
            if (!Mathf.Approximately(_shown, _target))
            {
                _shown = Mathf.Lerp(_shown, _target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6f));
                if (Mathf.Abs(_shown - _target) < 0.0008f) _shown = _target;
                ApplyFill();
            }
            if (_sheenTimer >= 0f)
            {
                _sheenTimer += Time.unscaledDeltaTime;
                const float duration = 0.7f;
                float t = _sheenTimer / duration;
                if (t >= 1f)
                {
                    _sheenTimer = -1f;
                    if (Sheen != null) Sheen.gameObject.SetActive(false);
                }
                else if (Sheen != null)
                {
                    var parent = Sheen.parent as RectTransform;
                    float span = parent != null ? parent.rect.width : 400f;
                    Sheen.anchoredPosition = new Vector2(
                        Mathf.Lerp(-span * 0.5f - Sheen.rect.width, span * 0.5f, Mathf.SmoothStep(0f, 1f, t)),
                        Sheen.anchoredPosition.y);
                }
            }
        }
    }

    internal static class PulseBarExtensions
    {
        public static bool Approximately(this float a, float b) => Mathf.Approximately(a, b);
    }
}
