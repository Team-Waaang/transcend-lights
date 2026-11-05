using AiHackathon.Game;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace AiHackathon.Cutscene
{
    /// <summary>
    /// The resonance QTE inside the boss defeat cutscene. During the beam struggle the timeline reaches a QTE section:
    /// a short resonance intro (<see cref="introAt"/>..<see cref="loopStart"/>), then a live stretch
    /// (<see cref="loopStart"/>..<see cref="loopEnd"/>) where both players aim at the clash point and mash their
    /// phone button. Each tap inside the target ring fills that player's gauge, which drains on its own; when both
    /// gauges are full at once the timeline jumps (after <see cref="ultimateDelay"/>) to <see cref="ultimateAt"/>: the ultimate beam. If the live stretch
    /// runs out first, it plays again from <see cref="loopStart"/>.
    /// Space / Enter skip the parts around the QTE, never the QTE itself.
    /// </summary>
    [RequireComponent(typeof(PlayableDirector))]
    [DefaultExecutionOrder(50)]
    public class ResonanceQte : MonoBehaviour
    {
        [Header("Timeline (seconds)")]
        public float introAt = 4.9f;
        public float loopStart = 7.1f;
        public float loopEnd = 15.1f;
        public float ultimateAt = 15.1f;
        /// <summary>The call-out when the ultimate beam fires (☆ and ∀ are in the GowunBatang title font).</summary>
        public const string UltimateCall = "ULTIM@TE☆BE∀M!!";
        [Tooltip("Seconds between the resonance landing and the ultimate beam, so its call starts just ahead of it.")]
        public float ultimateDelay = 0.6f;

        [Header("Target")]
        [Tooltip("The beam clash point; the target ring sits on it.")]
        public Transform clash;
        [Tooltip("Target ring radius as a fraction of the screen height.")]
        public float targetRadius = 0.13f;

        [Header("Input")]
        [Tooltip("Without phones: left click = player 1, right click = player 2, both aiming with the cursor.")]
        public bool mouseTest = true;
        [Tooltip("Gauge gained per tap inside the target.")]
        public float tapGain = 0.085f;
        [Tooltip("Gauge lost per second.")]
        public float drain = 0.12f;

        [Header("Players")]
        public string[] names = { "Amelia", "Nox" };
        public Color[] colors = { new Color(1f, 0.8f, 0.35f), new Color(0.45f, 0.75f, 1f) };

        [Header("Tug of war")]
        [Tooltip("Moved along +X (toward the boss) with the shared gauge: the clash point and every beam head.")]
        public Transform[] pushed = new Transform[0];
        [Tooltip("Clash offset at an empty / full shared gauge.")]
        public Vector2 pushRange = new Vector2(-0.15f, 0.35f);
        public Light resonanceLight;
        public float tapShake = 0.9f;

        [Header("HUD")]
        public Font titleFont;
        public Font bodyFont;

        public UnityEvent onSuccess = new UnityEvent();
        [Tooltip("The live stretch ran out and starts again.")]
        public UnityEvent onRetry = new UnityEvent();

        /// <summary>Gauge level that counts as full.</summary>
        public const float FullZone = 0.95f;

        public enum State { Waiting, Live, Success }
        public State Current { get; private set; }
        public bool Succeeded => Current == State.Success;
        /// <summary>How full the shared resonance is (0..1, the lower of the two gauges).</summary>
        public float Shared => Mathf.Clamp01(Mathf.Min(gauge[0], gauge[1]) / FullZone);
        public int Retries => retries;
        public static ResonanceQte Active { get; private set; }

        PlayableDirector director;
        readonly float[] gauge = new float[2];
        readonly bool[] wasPressed = new bool[2];
        readonly float[] lastTap = { -10f, -10f };
        readonly Vector2[] pointer = new Vector2[2];
        readonly bool[] hasPointer = new bool[2];
        readonly bool[] inside = new bool[2];
        Vector2 target;
        bool targetVisible;
        float retryMessage = -10f;
        bool soloWasPressed;
        int retries;
        float ultimateJumpAt = -1f;

        float push, shake, flare;
        CinemachineBasicMultiChannelPerlin shakeNoise;
        float shakeBase;

        void Awake()
        {
            director = GetComponent<PlayableDirector>();
            if (titleFont != null) GameGui.TitleFont = titleFont;
            if (bodyFont != null) GameGui.BodyFont = bodyFont;
        }
        void OnEnable() => Active = this;
        void OnDisable() { if (Active == this) Active = null; }

        void Update()
        {
            double t = director.time;
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            {
                if (t < introAt - 0.3) director.time = introAt;
                else if (Current == State.Success && t < director.duration - 1.0) director.time = director.duration - 0.1;
            }

            UpdateTarget();
            if (ultimateJumpAt >= 0f && Time.unscaledTime >= ultimateJumpAt)
            {
                ultimateJumpAt = -1f;
                if (director.time < ultimateAt) director.time = ultimateAt;
            }
            if (Current == State.Waiting && t >= loopStart && t < loopEnd) Begin();
            if (Current != State.Live) return;

            // One person testing with the mouse: either button counts for both players.
            bool solo = mouseTest && CasterInput.Session(0) == null && CasterInput.Session(1) == null;
            bool soloTap = false;
            if (solo)
            {
                bool any = CasterInput.Pressed(0, true) || CasterInput.Pressed(1, true);
                soloTap = any && !soloWasPressed;
                soloWasPressed = any;
            }
            for (int i = 0; i < 2; i++)
            {
                hasPointer[i] = CasterInput.TryGetPointer(i, mouseTest, out pointer[i]);
                var px = new Vector2(pointer[i].x * Screen.width, (1f - pointer[i].y) * Screen.height);
                inside[i] = hasPointer[i] && targetVisible && (px - target).magnitude <= targetRadius * Screen.height;
                bool pressed = CasterInput.Pressed(i, mouseTest);
                bool tap = solo ? soloTap : pressed && !wasPressed[i];
                gauge[i] = Mathf.Max(0f, gauge[i] - drain * Time.unscaledDeltaTime * (inside[i] ? 1f : 1.6f));
                if (tap && inside[i])
                {
                    gauge[i] = Mathf.Min(1f, gauge[i] + tapGain);
                    lastTap[i] = Time.unscaledTime;
                    shake = Mathf.Min(2.5f, shake + tapShake);
                    flare = 1f;
                }
                wasPressed[i] = pressed;
            }

            // Both in the full zone at the same moment: resonance. (A gauge only touches 1 on the frame of a tap,
            // so "full" is a band the players have to keep topped up together.)
            if (gauge[0] >= FullZone && gauge[1] >= FullZone)
            {
                Current = State.Success;
                ultimateJumpAt = Time.unscaledTime + ultimateDelay;
                onSuccess.Invoke();
                return;
            }
            if (director.time >= loopEnd - 0.05)
            {
                // Out of time: the boss holds on, go again.
                director.time = loopStart;
                retryMessage = Time.unscaledTime;
                retries++;
                for (int i = 0; i < 2; i++) gauge[i] *= 0.5f;
                onRetry.Invoke();
            }
        }

        void Begin()
        {
            Current = State.Live;
            for (int i = 0; i < 2; i++) { gauge[i] = 0f; wasPressed[i] = CasterInput.Pressed(i, mouseTest); }
        }

        void UpdateTarget()
        {
            var cam = Camera.main;
            targetVisible = false;
            if (cam == null) return;
            var world = clash != null ? clash.position : Vector3.zero;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;
            target = new Vector2(sp.x, Screen.height - sp.y);
            targetVisible = true;
        }

        // After the timeline has placed the beams: slide the struggle with the players' resonance, kick the camera on
        // taps and flare the light between them.
        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            float shared = Mathf.Clamp01(Mathf.Min(gauge[0], gauge[1]) / FullZone);
            // Keep the clash pushed toward the boss while the ultimate is about to fire.
            bool live = Current == State.Live || ultimateJumpAt >= 0f;
            float want = live ? Mathf.Lerp(pushRange.x, pushRange.y, shared) : 0f;
            push = Mathf.Lerp(push, want, 1f - Mathf.Exp(-(live ? 6f : 3f) * dt));
            if (Mathf.Abs(push) > 1e-4f)
            {
                float jitter = live ? (Mathf.PerlinNoise(Time.time * 9f, 0.7f) - 0.5f) * 0.05f : 0f;
                foreach (var t in pushed) if (t != null) t.position += Vector3.right * (push + jitter);
            }
            shake = Mathf.Max(0f, shake - dt * 4f);
            flare = Mathf.Max(0f, flare - dt * 5f);
            if (resonanceLight != null && live) resonanceLight.intensity += shared * 6f + flare * 4f;

            var brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
            var cam = brain != null ? brain.ActiveVirtualCamera as CinemachineCamera : null;
            var noise = cam != null ? cam.GetComponent<CinemachineBasicMultiChannelPerlin>() : null;
            if (noise != shakeNoise)
            {
                if (shakeNoise != null) shakeNoise.AmplitudeGain = shakeBase;
                shakeNoise = noise;
                if (noise != null) shakeBase = noise.AmplitudeGain;
            }
            if (shakeNoise != null && live) shakeNoise.AmplitudeGain = shakeBase + shake;
        }

        void OnGUI()
        {
            if (director == null) return;
            float t = (float)director.time;
            float s = GameGui.Scale;
            float now = Time.unscaledTime;
            var mid = Color.Lerp(colors[0], colors[1], 0.5f + 0.5f * Mathf.Sin(now * 5f));

            // Intro: the two of them start to resonate.
            if (Current != State.Success && t >= introAt + 0.3f && t < loopStart)
            {
                float a = Mathf.Clamp01((t - introAt - 0.3f) / 0.35f);
                float y = Screen.height * 0.17f;
                GameGui.BannerBack(new Rect(0f, y - 30f * s, Screen.width, 210f * s), new Color(0.012f, 0.011f, 0.01f, 0.75f * a));
                float punch = 1f + 0.25f * Mathf.Exp(-(t - introAt - 0.3f) * 8f);
                GameGui.GlowLabel(new Rect(0f, y, Screen.width, 120f * s), "공  명", GameGui.Style(Mathf.RoundToInt(104 * punch), TextAnchor.MiddleCenter), Color.white, mid, a);
                GameGui.Label(new Rect(0f, y + 118f * s, Screen.width, 50f * s), "두 사람이 함께 가운데 빛을 겨누고, 버튼을 연타하세요", GameGui.Style(32, TextAnchor.MiddleCenter, false), new Color(0.95f, 0.92f, 1f), a);
            }

            if (Current == State.Live)
            {
                float shared = Mathf.Clamp01(Mathf.Min(gauge[0], gauge[1]) / FullZone);
                if (targetVisible)
                {
                    float r = targetRadius * Screen.height;
                    float pulse = 1f + 0.05f * Mathf.Sin(now * (6f + shared * 16f));
                    // Glow core that swells with the shared gauge, a ring, and one arc of lights per player.
                    GameGui.DrawCentered(GameGui.Soft, target, r * (2.4f + 1.4f * shared) * pulse, new Color(1f, 0.9f, 0.75f, 0.12f + 0.4f * shared + 0.25f * flare));
                    GameGui.DrawCentered(GameGui.Ring, target, r * 2f * pulse, new Color(1f, 0.97f, 0.9f, 0.9f));
                    GameGui.DrawCentered(GameGui.Ring, target, r * 2f * (1.12f + 0.06f * Mathf.Sin(now * 3f)), new Color(1f, 0.85f, 0.6f, 0.3f));
                    var off0 = new Color(colors[0].r, colors[0].g, colors[0].b, 0.25f);
                    var off1 = new Color(colors[1].r, colors[1].g, colors[1].b, 0.25f);
                    GameGui.DotArc(target, r * 1.32f, Mathf.PI * 1.03f, Mathf.PI * 1.97f, 18, gauge[0] / FullZone, colors[0], off0, 9f * s);
                    GameGui.DotArc(target, r * 1.32f, Mathf.PI * 0.97f, Mathf.PI * 0.03f, 18, gauge[1] / FullZone, colors[1], off1, 9f * s);
                    // Tick marks at the four quarters.
                    for (int k = 0; k < 4; k++)
                    {
                        float ang = k * Mathf.PI * 0.5f + now * 0.6f;
                        var pt = target + new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * r * 1.12f;
                        GameGui.DrawCentered(GameGui.Diamond, pt, 14f * s, new Color(1f, 0.95f, 0.85f, 0.85f));
                    }
                }
                for (int i = 0; i < 2; i++)
                {
                    if (!hasPointer[i]) continue;
                    float tapK = Mathf.Clamp01(1f - (now - lastTap[i]) / 0.25f);
                    var c = inside[i] ? colors[i] : new Color(colors[i].r, colors[i].g, colors[i].b, 0.5f);
                    GameGui.Crosshair(pointer[i], c, (44f + 26f * tapK) * s);
                    if (tapK > 0f)
                        GameGui.DrawCentered(GameGui.Ring, new Vector2(pointer[i].x * Screen.width, (1f - pointer[i].y) * Screen.height), (60f + 110f * (1f - tapK)) * s, new Color(colors[i].r, colors[i].g, colors[i].b, tapK));
                }

                // Gauges: one per player in the bottom corners, the shared resonance in the middle.
                float w = 430f * s, h = 18f * s, y = Screen.height - 120f * s;
                for (int i = 0; i < 2; i++)
                {
                    float x = i == 0 ? 70f * s : Screen.width - w - 70f * s;
                    var dark = new Color(colors[i].r * 0.45f, colors[i].g * 0.45f, colors[i].b * 0.45f);
                    bool full = gauge[i] >= FullZone;
                    var name = GameGui.Style(34, i == 0 ? TextAnchor.LowerLeft : TextAnchor.LowerRight);
                    GameGui.GlowLabel(new Rect(x, y - 52f * s, w, 46f * s), names[i], name, Color.Lerp(colors[i], Color.white, 0.25f), colors[i], 1f);
                    if (!inside[i])
                        GameGui.Label(new Rect(x, y + h + 6f * s, w, 30f * s), "가운데를 조준!", GameGui.Style(20, i == 0 ? TextAnchor.UpperLeft : TextAnchor.UpperRight, false), new Color(1f, 0.75f, 0.85f), 0.6f + 0.4f * Mathf.Sin(now * 10f));
                    GameGui.FancyBar(new Rect(x, y, w, h), gauge[i], gauge[i], dark, Color.Lerp(colors[i], Color.white, full ? 0.5f : 0.15f), 1f, false, full ? 0.9f : 0.4f);
                }
                float cw = 560f * s;
                GameGui.GlowLabel(new Rect(0f, y - 56f * s, Screen.width, 50f * s), "공명  " + Mathf.RoundToInt(shared * 100f) + "%", GameGui.Style(40, TextAnchor.LowerCenter), Color.Lerp(Color.white, new Color(1f, 0.95f, 0.75f), shared), mid, 1f);
                GameGui.FancyBar(new Rect((Screen.width - cw) * 0.5f, y, cw, h), shared, shared, colors[0], colors[1], 1f, false, 0.4f + 0.5f * shared);

                float hy = 34f * s;
                GameGui.BannerBack(new Rect(Screen.width * 0.2f, hy - 6f * s, Screen.width * 0.6f, 68f * s), new Color(0.012f, 0.011f, 0.01f, 0.7f));
                GameGui.Label(new Rect(0f, hy, Screen.width, 56f * s), "가운데 빛을 겨누고  연타!", GameGui.Style(42, TextAnchor.MiddleCenter), Color.white, 0.7f + 0.3f * Mathf.Sin(now * 8f));
                if (now - retryMessage < 1.8f)
                    GameGui.GlowLabel(new Rect(0f, hy + 64f * s, Screen.width, 50f * s), "저주가 버틴다!  다시, 더 빠르게!", GameGui.Style(36, TextAnchor.MiddleCenter), new Color(1f, 0.7f, 0.9f), new Color(0.9f, 0.2f, 0.7f));
                if (mouseTest && CasterInput.Session(0) == null && CasterInput.Session(1) == null)
                    GameGui.Label(new Rect(0f, Screen.height - 44f * s, Screen.width, 36f * s), "마우스 테스트: 커서를 가운데에 두고 아무 버튼이나 연타 (두 게이지가 같이 찬다)", GameGui.Style(19, TextAnchor.MiddleCenter, false), new Color(1f, 1f, 1f, 0.65f));
            }

            // The ultimate beam call-out.
            if (Current == State.Success && t >= ultimateAt - 0.05f && t < ultimateAt + 1.7f)
            {
                float k = (t - ultimateAt) / 1.7f;
                float a = Mathf.Clamp01(k < 0.8f ? 1f : (1f - k) / 0.2f);
                float punch = 1f + 0.45f * Mathf.Exp(-k * 14f);
                float y = Screen.height * 0.14f;
                GameGui.BannerBack(new Rect(0f, y - 20f * s, Screen.width, 200f * s), new Color(1f, 0.9f, 0.7f, 0.18f * a));
                // Fit the call-out (and its punch-in) to the screen width: the Latin line runs wider than the old Hangul one.
                var style = GameGui.Style(Mathf.RoundToInt(120 * punch), TextAnchor.MiddleCenter);
                float wide = style.CalcSize(new GUIContent(UltimateCall)).x;
                if (wide > Screen.width * 0.9f) style.fontSize = Mathf.FloorToInt(style.fontSize * Screen.width * 0.9f / wide);
                GameGui.GlowLabel(new Rect(0f, y, Screen.width, 160f * s), UltimateCall, style, new Color(1f, 0.97f, 0.88f), Color.Lerp(colors[0], colors[1], 0.5f + 0.5f * Mathf.Sin(now * 20f)), a);
            }
        }
    }
}
