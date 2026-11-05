using System.Collections;
using AiHackathon.Parchment;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AiHackathon.Cutscene
{
    /// <summary>
    /// Runs a period-start cutscene around its timeline: rolls the parchment title card open and shut at fixed
    /// timeline times, lets Space / Enter / click skip ahead, and when the timeline ends either hands over to
    /// <see cref="nextScene"/> behind a short fade or just fires <see cref="onFinished"/>.
    /// </summary>
    [RequireComponent(typeof(PlayableDirector))]
    public class PeriodIntro : MonoBehaviour
    {
        public ParchmentTitleCard card;
        public string title = "룬";
        public string subtitle = "";
        [Tooltip("Timeline time the card starts unrolling.")]
        public float showAt = 11.8f;
        [Tooltip("Timeline time the card rolls back up.")]
        public float hideAt = 15.2f;

        [Header("Skip")]
        public bool allowSkip = true;
        [Tooltip("Skipping jumps here: the hand-over into the game.")]
        public float skipTo = 15.2f;

        [Header("End")]
        [Tooltip("Scene loaded after the timeline ends (must be in Build Settings). Empty: stay on the last frame.")]
        public string nextScene = "";
        public Image exitFade;
        public float exitFadeTime = 0.45f;
        public UnityEvent onFinished = new UnityEvent();

        PlayableDirector director;
        bool cardOpen, ended;

        void Awake() => director = GetComponent<PlayableDirector>();

        void Update()
        {
            double t = director.time;
            if (allowSkip && !ended && t < skipTo - 0.3 && SkipPressed())
            {
                AiHackathon.Game.GameAudio.Play("ui_confirm");
                director.time = skipTo;
                director.Evaluate();
                t = skipTo;
            }
            // The card follows the timeline window, so scrubbing or skipping back and forth stays in step.
            bool wantOpen = t >= showAt && t < hideAt;
            if (wantOpen != cardOpen)
            {
                cardOpen = wantOpen;
                if (card)
                {
                    if (wantOpen) card.Show(new TitleCardInfo(title, subtitle));
                    else card.Hide();
                }
            }
            if (t < director.duration - 0.1) ended = false;
            if (!ended && director.playableAsset != null && t >= director.duration - 0.02)
            {
                ended = true;
                onFinished.Invoke();
                if (!string.IsNullOrEmpty(nextScene)) StartCoroutine(Exit());
            }
        }

        static bool SkipPressed()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                   || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        }

        IEnumerator Exit()
        {
            if (exitFade != null)
            {
                for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / exitFadeTime)
                {
                    var c = exitFade.color; c.a = k; exitFade.color = c;
                    yield return null;
                }
            }
            if (Application.CanStreamedLevelBeLoaded(nextScene)) SceneManager.LoadScene(nextScene);
            else Debug.LogWarning("[PeriodIntro] '" + nextScene + "' is not in Build Settings; staying here.");
        }
    }
}
