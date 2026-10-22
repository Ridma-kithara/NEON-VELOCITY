using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVelocity.UI
{
    /// <summary>Pause / abandon screen. Ranked runs cannot be restarted; abandoning forfeits rewards.</summary>
    public sealed class PauseScreen : MonoBehaviour
    {
        private GameApp _app;
        private UIRoot _ui;

        public static GameObject Build(GameApp app, UIRoot ui)
        {
            var root = ScreenKit.Root("Pause");
            var screen = root.gameObject.AddComponent<PauseScreen>();
            screen._app = app;
            screen._ui = ui;
            screen.BuildLayout();
            return root.gameObject;
        }

        private void BuildLayout()
        {
            _ui.CloseModal();
            var root = (RectTransform)transform;
            var dim = UITheme.Img(root, "Dim", new Color(0f, 0f, 0f, 0.66f), null, true);
            UITheme.Stretch((RectTransform)dim.transform);

            var session = _app.Session;
            var panel = ScreenKit.Card(root, "Panel", new Color(0.025f, 0.05f, 0.09f, 0.98f));
            UITheme.Anchor((RectTransform)panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-360f, -330f), new Vector2(360f, 330f));
            var accent = UITheme.Img(panel.transform, "Accent", UITheme.Cyan);
            UITheme.Anchor((RectTransform)accent.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 24f), new Vector2(5f, -24f));

            var title = UITheme.Label(panel.transform, "Title", "RUN PAUSED", UITheme.TitleSize,
                UITheme.White, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(36f, -92f), new Vector2(-36f, -28f));

            var mode = UITheme.Label(panel.transform, "Mode",
                session != null ? session.Mode.Title + "  /  " + session.Mode.CodeName : "",
                UITheme.SmallSize, UITheme.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UITheme.Anchor((RectTransform)mode.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(38f, -122f), new Vector2(-36f, -94f));

            var stats = UITheme.Label(panel.transform, "Stats",
                session != null
                    ? "DISTANCE  " + UITheme.FormatDistance(session.Distance) + "\n" +
                      "SCORE  " + UITheme.FormatNumber(session.Score) + "\n" +
                      "TIME  " + UITheme.FormatTime(session.RaceElapsed) + "\n" +
                      "ENERGY  " + session.Coins
                    : "",
                UITheme.BodySize, UITheme.White, TextAnchor.UpperLeft);
            UITheme.Anchor((RectTransform)stats.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(38f, -272f), new Vector2(-36f, -136f));

            var list = UITheme.NewRect(panel.transform, "Buttons");
            UITheme.Anchor(list, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(36f, 32f), new Vector2(-36f, 362f));
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var resume = UITheme.Button(list, "Resume", "RESUME RUN", UITheme.Cyan, _app.ResumeFromPause);
            UITheme.Layout(resume.gameObject, 62f, -1f, 62f);
            var restart = UITheme.Button(list, "Restart", session != null && session.IsOnline
                ? "RESTART DISABLED IN RANKED"
                : "RESTART RUN", UITheme.Amber, () =>
                {
                    _app.UI.CloseScreen();
                    _app.RestartSession();
                });
            UITheme.Layout(restart.gameObject, 62f, -1f, 62f);
            restart.interactable = session == null || !session.IsOnline;

            var settings = UITheme.Button(list, "Settings", "SETTINGS", UITheme.Muted,
                () => _app.ShowSettings(true));
            UITheme.Layout(settings.gameObject, 62f, -1f, 62f);

            var abandon = UITheme.Button(list, "Abandon", "ABANDON RUN", UITheme.Red,
                ConfirmAbandon);
            UITheme.Layout(abandon.gameObject, 62f, -1f, 62f);
        }

        private void ConfirmAbandon()
        {
            if (_app.Session != null && _app.Session.IsOnline)
            {
                _ui.ShowModal("ABANDON RANKED RUN?",
                    "Leaving now forfeits this race's placement and all server rewards. The disconnect is recorded by the authority.",
                    "ABANDON", () => { _ui.CloseScreen(); _app.EndSession(true); }, "KEEP RACING");
            }
            else
            {
                _ui.ShowModal("ABANDON RUN?", "Progress from this local run will not be awarded.",
                    "ABANDON", () => { _ui.CloseScreen(); _app.EndSession(true); }, "KEEP RUNNING");
            }
        }
    }
}
