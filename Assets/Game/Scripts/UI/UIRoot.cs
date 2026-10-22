using System.Collections;
using System.Collections.Generic;
using NeonVelocity.Core;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>
    /// Owns every runtime canvas, swaps screens, hosts the gameplay HUD and shows transient
    /// notifications. Screens are plain GameObjects, which keeps navigation explicit and cheap.
    /// </summary>
    public sealed class UIRoot : MonoBehaviour
    {
        public Canvas ScreenCanvas { get; private set; }
        public Canvas HudCanvas { get; private set; }
        public Canvas OverlayCanvas { get; private set; }
        public Canvas ToastCanvas { get; private set; }
        public RectTransform ScreenLayer { get; private set; }
        public RectTransform HudLayer { get; private set; }
        public RectTransform OverlayLayer { get; private set; }
        public RectTransform ToastLayer { get; private set; }
        public HUDScreen Hud { get; private set; }
        public GameObject CurrentScreen { get; private set; }
        public GameApp App { get; private set; }
        public bool IsScreenOpen => CurrentScreen != null;
        public bool HasModal => _modal != null;
        public bool HighContrast { get; private set; }

        private EventSystem _eventSystem;
        private Image _fade;
        private readonly List<GameObject> _toasts = new List<GameObject>();
        private GameObject _modal;
        private Coroutine _fadeRoutine;
        private float _hudScale = 1f;

        public void Initialise(GameApp app)
        {
            App = app;
            gameObject.name = "NEON VELOCITY UI";
            transform.SetParent(app.transform, false);

            var eventGo = new GameObject("Event System");
            eventGo.transform.SetParent(transform, false);
            _eventSystem = eventGo.AddComponent<EventSystem>();
            eventGo.AddComponent<InputSystemUIInputModule>();

            ScreenCanvas = BuildCanvas("Screens", 0, out RectTransform screenRoot);
            HudCanvas = BuildCanvas("HUD", 20, out RectTransform hudRoot);
            OverlayCanvas = BuildCanvas("Overlay", 40, out RectTransform overlayRoot);
            ToastCanvas = BuildCanvas("Toasts", 60, out RectTransform toastRoot);

            ScreenLayer = BuildSafeLayer(screenRoot, "Screen Layer");
            HudLayer = BuildSafeLayer(hudRoot, "HUD Layer");
            OverlayLayer = BuildSafeLayer(overlayRoot, "Overlay Layer");
            ToastLayer = BuildSafeLayer(toastRoot, "Toast Layer");
            ToastLayer.gameObject.GetComponent<SafeAreaFitter>().enabled = false;

            var fadeImage = UITheme.Img(overlayRoot, "Fade", Color.black, null, false);
            UITheme.Stretch((RectTransform)fadeImage.transform);
            _fade = fadeImage;

            HideHud();
        }

        private Canvas BuildCanvas(string name, int order, out RectTransform root)
        {
            var go = new GameObject(name + " Canvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            root = (RectTransform)go.transform;
            return canvas;
        }

        private static RectTransform BuildSafeLayer(RectTransform parent, string name)
        {
            var rect = UITheme.NewRect(parent, name);
            UITheme.Stretch(rect);
            rect.gameObject.AddComponent<SafeAreaFitter>();
            return rect;
        }

        public void ShowScreen(GameObject screen)
        {
            if (CurrentScreen != null) Destroy(CurrentScreen);
            CurrentScreen = screen;
            if (screen == null) return;
            screen.transform.SetParent(ScreenLayer, false);
            var rect = (RectTransform)screen.transform;
            UITheme.Stretch(rect);
            if (HighContrast) ApplyContrast(screen);
            StartCoroutine(FadeFromBlack(0.22f));
        }

        public void CloseScreen()
        {
            if (CurrentScreen != null) Destroy(CurrentScreen);
            CurrentScreen = null;
        }

        public HUDScreen ShowHud()
        {
            HideHud();
            Hud = HUDScreen.Build(HudLayer, App);
            if (HighContrast && Hud != null) ApplyContrast(Hud.gameObject);
            return Hud;
        }

        public void HideHud()
        {
            if (Hud != null)
            {
                Destroy(Hud.gameObject);
                Hud = null;
            }
        }

        public void SetHudVisible(bool visible)
        {
            if (Hud != null) Hud.gameObject.SetActive(visible);
        }

        public void Toast(string message, Color color, float duration = 2.6f)
        {
            if (string.IsNullOrEmpty(message)) return;
            var panel = UITheme.Img(ToastLayer, "Toast", new Color(0.03f, 0.06f, 0.11f, 0.94f), UITheme.Rounded, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f - _toasts.Count * 62f);
            rect.sizeDelta = new Vector2(560f, 50f);
            var text = UITheme.Label(panel.transform, "Text", message, UITheme.BodySize, color,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UITheme.Stretch((RectTransform)text.transform, 18f, 4f, 18f, 4f);
            panel.gameObject.AddComponent<CanvasGroup>();
            _toasts.Add(panel.gameObject);
            StartCoroutine(ToastRoutine(panel.gameObject, duration));
        }

        private IEnumerator ToastRoutine(GameObject toast, float duration)
        {
            var group = toast.GetComponent<CanvasGroup>();
            yield return FadeGroup(group, 0f, 1f, 0.15f);
            yield return new WaitForSecondsRealtime(duration);
            yield return FadeGroup(group, 1f, 0f, 0.3f);
            _toasts.Remove(toast);
            if (toast != null) Destroy(toast);
        }

        private static IEnumerator FadeGroup(CanvasGroup group, float from, float to, float seconds)
        {
            float t = 0f;
            group.alpha = from;
            while (t < seconds && group != null)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
                yield return null;
            }
            if (group != null) group.alpha = to;
        }

        public void ShowModal(string title, string message, string confirmLabel, System.Action onConfirm,
            string cancelLabel = "CANCEL", System.Action onCancel = null)
        {
            CloseModal();
            var root = UITheme.NewRect(OverlayLayer, "Modal");
            UITheme.Stretch(root);
            var dim = UITheme.Img(root, "Dim", new Color(0f, 0f, 0f, 0.72f), null, true);
            UITheme.Stretch((RectTransform)dim.transform);
            var panel = UITheme.Img(root, "Panel", UITheme.Panel, UITheme.Rounded, true);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(680f, 360f);
            var accent = UITheme.Img(panel.transform, "Accent", UITheme.Cyan);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 18f), new Vector2(5f, -18f));

            var heading = UITheme.Label(panel.transform, "Title", title, UITheme.TitleSize, UITheme.White,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -82f), new Vector2(-36f, -24f));
            var body = UITheme.Label(panel.transform, "Message", message, UITheme.BodySize, UITheme.Dim,
                TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)body.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(36f, 120f), new Vector2(-36f, -92f));

            var confirm = UITheme.Button(panel.transform, "Confirm", confirmLabel, UITheme.Cyan, () =>
            {
                CloseModal();
                onConfirm?.Invoke();
            }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)confirm.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-286f, 30f), new Vector2(-36f, 92f));

            var cancel = UITheme.Button(panel.transform, "Cancel", cancelLabel, UITheme.Muted, () =>
            {
                CloseModal();
                onCancel?.Invoke();
            }, UITheme.BodySize);
            UITheme.Anchor((RectTransform)cancel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-540f, 30f), new Vector2(-302f, 92f));

            _modal = root.gameObject;
            if (HighContrast) ApplyContrast(_modal);
            if (_eventSystem != null) _eventSystem.SetSelectedGameObject(confirm.gameObject);
        }

        public void CloseModal()
        {
            if (_modal != null) Destroy(_modal);
            _modal = null;
        }

        public void FadeOut(float seconds = 0.25f)
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(0f, 1f, seconds));
        }

        public void FadeIn(float seconds = 0.25f)
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(1f, 0f, seconds));
        }

        private IEnumerator FadeFromBlack(float seconds)
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(1f, 0f, seconds));
            yield break;
        }

        private IEnumerator FadeRoutine(float from, float to, float seconds)
        {
            _fade.gameObject.SetActive(true);
            _fade.raycastTarget = to > 0.01f;
            float t = 0f;
            Color c = _fade.color;
            c.a = from;
            _fade.color = c;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.01f, seconds)));
                _fade.color = c;
                yield return null;
            }
            c.a = to;
            _fade.color = c;
            _fade.raycastTarget = to > 0.01f;
            if (to <= 0.01f) _fade.gameObject.SetActive(false);
        }

        public void SetSelected(GameObject target)
        {
            if (_eventSystem != null && target != null) _eventSystem.SetSelectedGameObject(target);
        }

        /// <summary>
        /// Accessibility toggle. Rather than re-theming a dozen ad-hoc screens, every existing
        /// label is pushed toward full-bright white and retained outlines are already present on
        /// the important text. New screens are normalised as they are shown.
        /// </summary>
        public void ApplyHighContrast(bool enabled)
        {
            HighContrast = enabled;
            if (!enabled)
            {
                if (CurrentScreen != null) RestoreContrast(CurrentScreen);
                if (_modal != null) RestoreContrast(_modal);
                if (Hud != null) RestoreContrast(Hud.gameObject);
                for (int i = 0; i < _toasts.Count; i++)
                    if (_toasts[i] != null) RestoreContrast(_toasts[i]);
                return;
            }
            if (CurrentScreen != null) ApplyContrast(CurrentScreen);
            if (_modal != null) ApplyContrast(_modal);
            if (Hud != null) ApplyContrast(Hud.gameObject);
            for (int i = 0; i < _toasts.Count; i++)
                if (_toasts[i] != null) ApplyContrast(_toasts[i]);
        }

        private static void ApplyContrast(GameObject root)
        {
            if (root == null) return;
            var texts = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var c = texts[i].color;
                if (c.a <= 0.05f) continue;
                var backup = texts[i].GetComponent<ContrastBackup>();
                if (backup == null)
                {
                    backup = texts[i].gameObject.AddComponent<ContrastBackup>();
                    backup.Original = c;
                }
                texts[i].color = new Color(
                    Mathf.Max(c.r, 0.92f),
                    Mathf.Max(c.g, 0.95f),
                    Mathf.Max(c.b, 0.98f), 1f);
            }
        }

        private static void RestoreContrast(GameObject root)
        {
            if (root == null) return;
            var backups = root.GetComponentsInChildren<ContrastBackup>(true);
            for (int i = 0; i < backups.Length; i++)
            {
                var text = backups[i].GetComponent<Text>();
                if (text != null) text.color = backups[i].Original;
            }
        }

        public void DestroyAll()
        {
            CloseScreen();
            HideHud();
            CloseModal();
        }
    }

    public sealed class ContrastBackup : MonoBehaviour
    {
        public Color Original;
    }
}
