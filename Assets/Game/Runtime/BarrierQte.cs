using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Waaang;

namespace AiHackathon.Game
{
    /// <summary>
    /// Phase 2's opening QTE (<see cref="BossPeriod"/>, right after the phase 2 cutscene): the boss gathers his
    /// awakening blast, and each player gets <see cref="sealsPerPlayer"/> seals in her own colour, one at a time on her
    /// side of the arena, each with a ring closing in on it. Shooting a seal with her own laser before the ring closes
    /// sets that piece of the barrier. With at least <see cref="passRatio"/> of the seals set, the barrier holds: the
    /// blast is turned back and the boss reels, open to extra damage for a while. Otherwise the blast goes off: a
    /// heavy hit that stuns both players for a moment.
    /// </summary>
    public class BarrierQte : MonoBehaviour
    {
        public GameObject sealTemplate;
        public GameObject breakFx;
        public GameObject missFx;

        [Header("Tuning")]
        public int sealsPerPlayer = 4;
        [Tooltip("Seconds each seal waits for its laser.")]
        public float window = 1.75f;
        [Tooltip("Pause between one seal and the next.")]
        public float gap = 0.3f;
        public float intro = 1.3f;
        [Range(0f, 1f)] public float passRatio = 0.75f;
        [Tooltip("Barrier held: seconds the boss stays weak once phase 2 begins.")]
        public float weakTime = 5f;
        [Tooltip("Blast went off: seconds both players cannot fire.")]
        public float stunTime = 1.2f;
        public float resultTime = 1.8f;

        [Header("Where seals appear (arena space: players at -X looking +X, Amelia at +Z)")]
        public Vector2 xRange = new Vector2(-1.15f, -0.45f);
        public Vector2 yRange = new Vector2(0.75f, 1.9f);
        public Vector2 sideRange = new Vector2(0.6f, 1.9f);

        public bool Active { get; private set; }
        public bool Succeeded { get; private set; }

        enum Phase { Intro, Seals, Held, Broke }

        BossPeriod period;
        Phase phase;
        float phaseAt, flashGold;
        int[][] results;               // per player, per seal: 0 waiting, 1 set, 2 missed
        readonly QteSeal[] live = new QteSeal[2];
        readonly List<GameObject> spawned = new List<GameObject>();
        int lanesRunning;

        public IEnumerator Run(BossPeriod owner)
        {
            period = owner;
            var game = owner.game;
            var boss = owner.boss;
            int players = game != null ? Mathf.Min(game.players.Length, live.Length) : 0;
            results = new int[players][];
            for (int i = 0; i < players; i++) results[i] = new int[sealsPerPlayer];
            Active = true;
            Succeeded = false;
            SetPhase(Phase.Intro);
            GameAudio.Play("qte_start");
            GameAudio.Play("curse_charge", boss != null ? boss.transform : null, 0.8f);
            boss?.Act("Cast", 0.25f);
            yield return new WaitForSeconds(intro);

            SetPhase(Phase.Seals);
            lanesRunning = players;
            for (int i = 0; i < players; i++) StartCoroutine(Lane(i));
            while (lanesRunning > 0) yield return null;

            int set = 0, total = players * sealsPerPlayer;
            foreach (var r in results) foreach (int v in r) if (v == 1) set++;
            Succeeded = total > 0 && set >= Mathf.CeilToInt(total * passRatio);
            if (Succeeded)
            {
                SetPhase(Phase.Held);
                flashGold = 1f;
                // The boss's Stagger below adds the reel.
                GameAudio.Play("barrier_hold");
                owner.Impact(0.1f, 0f);
                // Weak from now through the result and into the first seconds of the fight.
                boss?.Stagger(resultTime + weakTime);
                Voice.Say(VoiceLine.AmeliaQteSuccess, 1.5f);
            }
            else
            {
                SetPhase(Phase.Broke);
                GameAudio.Play("blast", null, 0.9f);
                if (boss != null) FxUtil.Burst(boss.roarFx, boss.transform.position + Vector3.up * 0.6f);
                owner.Impact(0.24f, 0.9f);
                for (int i = 0; i < players; i++)
                {
                    owner.Flinch(i, 1f);
                    StartCoroutine(Stun(game.players[i].rig));
                }
                Voice.Say(VoiceLine.KaelaQteFail, 1.5f);
            }
            yield return new WaitForSeconds(resultTime);
            Clear();
            Active = false;
        }

        IEnumerator Lane(int player)
        {
            var game = period.game;
            var color = game.players[player].profile != null ? game.players[player].profile.playerColor : Color.white;
            var character = game.players[player].character;
            float side = character != null && character.position.z < 0f ? -1f : 1f;
            Vector3 last = new Vector3(0f, 99f, 0f);
            // Stagger the two lanes a little so the seals don't pop in pairs.
            yield return new WaitForSeconds(player * 0.25f);
            for (int k = 0; k < sealsPerPlayer; k++)
            {
                var spot = Spot(side, last);
                last = spot;
                var go = Instantiate(sealTemplate, sealTemplate.transform.parent);
                go.transform.position = spot;
                go.SetActive(true);
                spawned.Add(go);
                var seal = go.GetComponent<QteSeal>();
                seal.Arm(player, color, window);
                live[player] = seal;
                GameAudio.Play("seal_appear", go.transform);
                while (!seal.Broken && !seal.Expired) yield return null;
                live[player] = null;
                if (seal.Broken)
                {
                    results[player][k] = 1;
                    FxUtil.Burst(breakFx, spot);
                    // Each seal set rises a little in pitch, so the barrier audibly builds.
                    GameAudio.Play("seal_set", go.transform, 1f, 1f + 0.06f * k);
                }
                else
                {
                    results[player][k] = 2;
                    FxUtil.Burst(missFx, spot);
                    GameAudio.Play("seal_miss", go.transform);
                    period.Flinch(player, 0.4f);
                }
                spawned.Remove(go);
                Destroy(go);
                yield return new WaitForSeconds(gap);
            }
            lanesRunning--;
        }

        /// <summary>A spot on the player's side, inside the camera's view and away from her previous seal.</summary>
        Vector3 Spot(float side, Vector3 avoid)
        {
            var cam = period.playCamera != null ? period.playCamera : Camera.main;
            var best = Vector3.zero;
            for (int tries = 0; tries < 16; tries++)
            {
                var p = new Vector3(Random.Range(xRange.x, xRange.y), Random.Range(yRange.x, yRange.y), side * Random.Range(sideRange.x, sideRange.y));
                best = p;
                if ((p - avoid).sqrMagnitude < 0.5f) continue;
                if (cam == null) break;
                var v = cam.WorldToViewportPoint(p);
                if (v.z > 0.5f && v.x > 0.1f && v.x < 0.9f && v.y > 0.18f && v.y < 0.82f) break;
            }
            return best;
        }

        IEnumerator Stun(WandPointerRig rig)
        {
            if (rig == null) yield break;
            rig.fireBlocked = true;
            yield return new WaitForSeconds(stunTime);
            rig.fireBlocked = false;
        }

        /// <summary>Routes this frame's lasers: each player's beam only counts on her own seal.</summary>
        public void Shoot(GameManager game, float dt)
        {
            if (phase != Phase.Seals) return;
            for (int i = 0; i < game.players.Length && i < live.Length; i++)
            {
                var rig = game.players[i].rig;
                if (rig == null || !rig.isActiveAndEnabled || !rig.Firing || !rig.HasHit || rig.HitCollider == null) continue;
                var seal = rig.HitCollider.GetComponentInParent<QteSeal>();
                if (seal != null && seal.Owner == i) seal.Shoot(dt);
            }
        }

        void Update()
        {
            if (!Active || period == null || period.boss == null) return;
            flashGold = Mathf.Max(0f, flashGold - Time.deltaTime * 2.4f);
            var boss = period.boss;
            // The blast gathering on him: a violet glow that builds while the seals are up.
            if (phase == Phase.Intro || phase == Phase.Seals)
            {
                float build = Mathf.Clamp01((Time.time - phaseAt) / (phase == Phase.Intro ? intro * 4f : window * sealsPerPlayer));
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(4f, 12f, build));
                boss.Glow(new Color(0.75f, 0.35f, 1f), 0.35f + 0.55f * build * pulse);
                if (Mathf.Repeat(Time.time, 1.1f) < Time.deltaTime) FxUtil.Burst(boss.castFx, boss.wandTip != null ? boss.wandTip.position : boss.transform.position + Vector3.up);
            }
        }

        void SetPhase(Phase p)
        {
            phase = p;
            phaseAt = Time.time;
        }

        void Clear()
        {
            foreach (var go in spawned) if (go != null) Destroy(go);
            spawned.Clear();
            live[0] = live[1] = null;
        }

        void OnDisable() => Clear();

        // ------------------------------------------------------------------ HUD

        public void DrawHud(float s)
        {
            if (!Active || period == null) return;
            var game = period.game;
            float now = Time.time, since = now - phaseAt;
            var gold = new Color(1f, 0.85f, 0.5f);

            if (flashGold > 0f) GameGui.Rect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(1f, 0.93f, 0.75f, 0.32f * flashGold * flashGold));

            if (phase == Phase.Intro || phase == Phase.Seals)
            {
                // Call-out: big while it comes in, then a smaller line along the top.
                float big = phase == Phase.Intro ? 1f : 1f - Mathf.SmoothStep(0f, 1f, since / 0.5f);
                float y = Mathf.Lerp(34f * s, Screen.height * 0.3f, big);
                float a = phase == Phase.Intro ? Mathf.Clamp01(since / 0.25f) : 1f;
                GameGui.BannerBack(new Rect(Screen.width * 0.15f, y - 14f * s, Screen.width * 0.7f, (96f + 40f * big) * s), new Color(0.012f, 0.011f, 0.01f, 0.75f * a));
                GameGui.GlowLabel(new Rect(0f, y, Screen.width, (54f + 30f * big) * s), "각성 폭발이 온다!", GameGui.Style(Mathf.RoundToInt(44 + 28 * big), TextAnchor.MiddleCenter),
                    new Color(1f, 0.92f, 0.96f), new Color(0.8f, 0.35f, 1f), a);
                GameGui.Label(new Rect(0f, y + (54f + 30f * big) * s, Screen.width, 40f * s), "자기 색 봉인을 링이 닫히기 전에 쏴서 결계를 세워!",
                    GameGui.Style(Mathf.RoundToInt(24 + 6 * big), TextAnchor.MiddleCenter, false), Color.white, a * (0.75f + 0.25f * Mathf.Sin(now * 6f)));

                var cam = period.playCamera != null ? period.playCamera : Camera.main;
                for (int i = 0; i < live.Length; i++)
                {
                    var seal = live[i];
                    if (seal == null || cam == null || seal.Broken || seal.Expired) continue;
                    var sp = cam.WorldToScreenPoint(seal.transform.position);
                    if (sp.z <= 0f) continue;
                    var c = new Vector2(sp.x, Screen.height - sp.y);
                    float r = 0.3f * Screen.height / (2f * sp.z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                    float k = seal.Progress;
                    var col = Color.Lerp(seal.Color, new Color(1f, 0.45f, 0.5f), Mathf.Clamp01((k - 0.6f) / 0.4f));
                    // The target ring, and the timing ring closing onto it.
                    GameGui.DrawCentered(GameGui.Ring, c, r * 2.2f, new Color(seal.Color.r, seal.Color.g, seal.Color.b, 0.35f));
                    GameGui.DrawCentered(GameGui.Ring, c, r * 2.2f * Mathf.Lerp(2.6f, 1f, k), new Color(col.r, col.g, col.b, 0.5f + 0.5f * k));
                    GameGui.Label(new Rect(c.x - 120f * s, c.y + r * 1.3f, 240f * s, 30f * s), Name(i), GameGui.Style(22, TextAnchor.UpperCenter), Color.Lerp(seal.Color, Color.white, 0.3f), 0.9f);
                }
            }

            // Each player's tally along the bottom.
            if (results != null)
                for (int i = 0; i < results.Length; i++)
                {
                    var color = game.players[i].profile != null ? game.players[i].profile.playerColor : Color.white;
                    float w = 300f * s, x = i == 0 ? Screen.width * 0.5f - w - 60f * s : Screen.width * 0.5f + 60f * s, y = Screen.height - 120f * s;
                    GameGui.BannerBack(new Rect(x - 30f * s, y - 10f * s, w + 60f * s, 80f * s), new Color(0.012f, 0.011f, 0.01f, 0.75f));
                    GameGui.GlowLabel(new Rect(x, y, w, 32f * s), Name(i), GameGui.Style(26, TextAnchor.MiddleCenter), Color.Lerp(color, Color.white, 0.25f), color);
                    for (int k = 0; k < results[i].Length; k++)
                    {
                        int v = results[i][k];
                        var dc = v == 1 ? Color.Lerp(color, Color.white, 0.3f) : v == 2 ? new Color(0.75f, 0.3f, 0.35f, 0.85f) : new Color(1f, 1f, 1f, 0.2f);
                        GameGui.DrawCentered(GameGui.Diamond, new Vector2(x + w * 0.5f + (k - (results[i].Length - 1) * 0.5f) * 30f * s, y + 52f * s), (v == 1 ? 18f : 14f) * s, dc);
                    }
                }

            if (phase == Phase.Held || phase == Phase.Broke)
            {
                bool held = phase == Phase.Held;
                float a = Mathf.Clamp01(since / 0.2f) * Mathf.Clamp01((resultTime - since) / 0.3f);
                float punch = 1f + 0.35f * Mathf.Exp(-since * 9f);
                float y = Screen.height * 0.3f;
                GameGui.BannerBack(new Rect(0f, y - 20f * s, Screen.width, 190f * s), held ? new Color(1f, 0.9f, 0.7f, 0.16f * a) : new Color(0.012f, 0.011f, 0.01f, 0.8f * a));
                GameGui.GlowLabel(new Rect(0f, y, Screen.width, 110f * s), held ? "결계 완성!" : "저주 폭발!", GameGui.Style(Mathf.RoundToInt(86 * punch), TextAnchor.MiddleCenter),
                    held ? new Color(1f, 0.96f, 0.85f) : new Color(1f, 0.78f, 0.9f), held ? gold : new Color(0.85f, 0.25f, 0.75f), a);
                GameGui.Label(new Rect(0f, y + 110f * s, Screen.width, 44f * s), held ? "폭발을 되받아쳤다! 보스가 흔들린다, 지금 공격해!" : "결계가 무너졌다… 잠시 움직일 수 없다!",
                    GameGui.Style(30, TextAnchor.MiddleCenter, false), Color.white, a);
            }
        }

        string Name(int i)
        {
            var g = period != null ? period.game : null;
            return g != null && i < g.players.Length && g.players[i].profile != null ? g.players[i].profile.displayName : "P" + (i + 1);
        }
    }
}
