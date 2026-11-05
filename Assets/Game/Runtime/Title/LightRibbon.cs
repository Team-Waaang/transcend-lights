using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// A ribbon of light circling the pair on the main screen: the object (with a trail and sparkle emitter on it)
    /// runs round an ellipse that rises and dips twice per lap and swells a little three times per lap, so two
    /// ribbons half a lap apart twine round each other. Every term is periodic in the lap, so the path is closed.
    /// </summary>
    public class LightRibbon : MonoBehaviour
    {
        public Vector3 center = new Vector3(0f, 1.1f, 0f);
        public Vector2 radius = new Vector2(1.15f, 0.75f);
        public float height = 0.5f;
        [Tooltip("Seconds per lap; negative runs the other way round.")]
        public float lap = 7f;
        [Range(0f, 1f)] public float phase;
        public float swell = 0.12f;
        public float heightPhase;

        void Update()
        {
            float a = (Time.time / lap + phase) * Mathf.PI * 2f;
            float r = 1f + swell * Mathf.Sin(3f * a + 0.7f);
            transform.position = center + new Vector3(Mathf.Cos(a) * radius.x * r, height * Mathf.Sin(2f * a + heightPhase), Mathf.Sin(a) * radius.y * r);
        }
    }
}
