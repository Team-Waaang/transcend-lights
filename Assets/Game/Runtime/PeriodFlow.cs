using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiHackathon.Game
{
    /// <summary>
    /// The school day: connection screen → 1교시 → 2교시 → 3교시. Each period calls <see cref="Next"/> when it is
    /// cleared; the flow fades to black, shows which period is next, and loads it. A period that is not in Build
    /// Settings is skipped.
    /// </summary>
    public class PeriodFlow : MonoBehaviour
    {
        public struct Period
        {
            public string scene, title, subject;
            public Period(string scene, string title, string subject) { this.scene = scene; this.title = title; this.subject = subject; }
        }

        public const string ConnectionScene = "Assets/Scenes/Connection.unity";

        /// <summary>The periods in order.</summary>
        public static readonly Period[] Periods =
        {
            new Period("Assets/Cutscene/Period1/Period1Cutscene.unity", "1교시", "룬"),
            new Period("Assets/Cutscene/Period2/Period2Cutscene.unity", "2교시", "암흑"),
            new Period("Assets/Game/Period3/Period3.unity", "3교시", "보스전"),
        };

        static PeriodFlow instance;
        float fade;
        string card, cardSub;
        float cardAlpha;
        bool busy;

        public static bool Available(string scene) => Application.CanStreamedLevelBeLoaded(scene);

        /// <summary>First period that exists (where the connection screen goes).</summary>
        public static string First()
        {
            foreach (var p in Periods) if (Available(p.scene)) return p.scene;
            return null;
        }

        /// <summary>Index of the period the active scene belongs to, or -1.</summary>
        public static int CurrentIndex()
        {
            var path = SceneManager.GetActiveScene().path;
            for (int i = 0; i < Periods.Length; i++) if (Periods[i].scene == path) return i;
            return -1;
        }

        /// <summary>The next period after the current one that exists, or -1 when the day is over.</summary>
        public static int NextIndex()
        {
            for (int i = CurrentIndex() + 1; i < Periods.Length; i++) if (Available(Periods[i].scene)) return i;
            return -1;
        }

        /// <summary>Fades out and loads the next period. Returns false (and does nothing) when this was the last one.</summary>
        public static bool Next()
        {
            int i = NextIndex();
            if (i < 0) return false;
            Go(Periods[i].scene, Periods[i].title, Periods[i].subject);
            return true;
        }

        /// <summary>Back to the first period (after the last one is cleared).</summary>
        public static void Restart()
        {
            var first = First();
            if (first == null) return;
            var p = System.Array.Find(Periods, x => x.scene == first);
            Go(first, p.title, p.subject);
        }

        /// <summary>Loads <paramref name="scene"/> behind a fade with a period card.</summary>
        public static void Go(string scene, string title, string subject)
        {
            if (instance == null)
            {
                var go = new GameObject("Period Flow");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<PeriodFlow>();
            }
            if (!instance.busy) instance.StartCoroutine(instance.Load(scene, title, subject));
        }

        IEnumerator Load(string scene, string title, string subject)
        {
            busy = true;
            card = title;
            cardSub = subject;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.7f) { fade = t; yield return null; }
            fade = 1f;
            Time.timeScale = 1f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f) { cardAlpha = t; yield return null; }
            cardAlpha = 1f;
            var op = SceneManager.LoadSceneAsync(scene);
            float shown = 0f;
            while (!op.isDone || shown < 1.2f) { shown += Time.unscaledDeltaTime; yield return null; }
            // The period's own intro takes over from black.
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f) { cardAlpha = 1f - t; yield return null; }
            cardAlpha = 0f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.5f) { fade = 1f - t; yield return null; }
            fade = 0f;
            busy = false;
        }

        void OnGUI()
        {
            if (fade <= 0f && cardAlpha <= 0f) return;
            GUI.depth = -100;
            GameGui.Rect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.04f, fade));
            if (cardAlpha <= 0f || string.IsNullOrEmpty(card)) return;
            float s = GameGui.Scale, y = Screen.height * 0.4f;
            // A wide, faint pool of light behind the title rather than a round purple blob.
            GameGui.BannerBack(new Rect(Screen.width * 0.12f, y - 70f * s, Screen.width * 0.76f, 300f * s), new Color(0.42f, 0.32f, 0.75f, 0.16f * cardAlpha));
            GameGui.GlowLabel(new Rect(0f, y, Screen.width, 90f * s), card, GameGui.Style(76, TextAnchor.MiddleCenter), new Color(1f, 0.95f, 0.85f), new Color(0.75f, 0.5f, 1f), cardAlpha);
            if (!string.IsNullOrEmpty(cardSub))
                GameGui.Label(new Rect(0f, y + 92f * s, Screen.width, 44f * s), cardSub, GameGui.Style(32, TextAnchor.MiddleCenter, false), new Color(0.85f, 0.8f, 1f), cardAlpha);
            float dots = Mathf.Repeat(Time.unscaledTime * 1.5f, 3f);
            for (int i = 0; i < 3; i++)
                GameGui.DrawCentered(GameGui.Diamond, new Vector2(Screen.width * 0.5f + (i - 1) * 26f * s, y + 160f * s), 12f * s,
                    new Color(1f, 0.9f, 1f, cardAlpha * (i <= dots ? 0.9f : 0.25f)));
        }
    }
}
