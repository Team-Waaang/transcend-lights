using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AiHackathon.Parchment
{
    /// <summary>
    /// Plays the three period title cards in a loop for the Parchment scene. Space / Enter / click skips ahead.
    /// </summary>
    public class ParchmentTitleCardDemo : MonoBehaviour
    {
        public ParchmentTitleCard card;
        public TitleCardInfo[] cards =
        {
            new("룬", "손끝이 떨리면, 룬도 떨린다."),
            new("암흑", "빛이 꺼져도 지팡이 끝은 기억한다."),
            new("잿빛", "둘의 빛을 하나로 겹쳐라."),
        };
        public float startDelay = 0.5f;
        public float gap = 0.9f;
        public bool loop = true;

        IEnumerator Start()
        {
            if (!card || cards.Length == 0) yield break;
            yield return new WaitForSecondsRealtime(startDelay);
            do
            {
                foreach (var info in cards)
                {
                    yield return card.Play(info);
                    yield return new WaitForSecondsRealtime(gap);
                }
            } while (loop);
        }

        void Update()
        {
            if (!card) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool pressed = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                           || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (pressed) card.Advance();
        }
    }
}
