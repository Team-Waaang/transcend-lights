using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// The Period 3 boss in play: HP, the attack patterns of each phase, the animator states that go with them, and
    /// the procedural life on top: he floats and bobs, glides or blinks to a new spot between patterns, turns to the
    /// players, leans into his moves, flinches under lasers and roars into phase 2.
    /// <see cref="BossPeriod"/> starts and stops him, feeds him laser damage and reads his state for the HUD.
    ///
    /// Patterns: a curse volley (orbs arc toward the players; shoot them down or he heals), stone spirits summoned out
    /// of the seal that run at the players (a few frames of laser breaks one), a ward (only breaks while both players
    /// hit it at once; breaking it staggers him and opens a weak spot), and in phase 2 clones (shoot the real one; a
    /// false one bursts and fires back), a curse chain (one player is bound and cannot fire until the other shoots the
    /// anchor crystal) and a volley from above. Between patterns he idles or moves: that is when lasers hurt most.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class BossController : MonoBehaviour
    {
        [Serializable]
        public class Tuning
        {
            public float idleMin = 2.4f, idleMax = 3.4f;
            public int volley = 3;
            public float orbTravel = 5.5f;
            public float orbHp = 0.3f;
            [Tooltip("Seconds the ward stays up if not broken.")]
            public float wardTime = 7f;
            [Tooltip("Seconds of both lasers on the ward to break it.")]
            public float wardHp = 1.6f;
            public int summonVolley = 0;
            [Header("Stone spirits")]
            public int minions = 3;
            public float minionSpeed = 0.95f;
            [Tooltip("Seconds of laser that break one.")]
            public float minionHp = 0.3f;
            [Header("Clones (0 = no clone pattern)")]
            public int decoys = 0;
            public float decoyHp = 0.25f;
            public float cloneTime = 7f;
            [Header("Curse chain (0 = no chain pattern)")]
            public float chainTime = 0f;
            [Tooltip("Seconds of laser on the anchor crystal to break the chain.")]
            public float crystalHp = 1.2f;
            [Tooltip("Chance to blink (vanish and reappear) instead of gliding when he moves.")]
            [Range(0f, 1f)] public float blinkChance = 0.3f;
        }

        [Header("Parts (wired by the builder)")]
        public Animator animator;
        public Transform wandTip;
        public Transform chest;
        public WardFx ward;
        public GameObject orbTemplate;
        public GameObject orbPopFx;
        public GameObject orbHitFx;
        public GameObject castFx;
        public GameObject blinkOutFx, blinkInFx;
        public GameObject roarFx;
        public GameObject phase2Aura;
        public Light hitLight;

        [Header("Health")]
        public float maxHp = 100f;
        [Tooltip("Damage taken while staggered after a broken ward is multiplied by this.")]
        public float weakMultiplier = 1.8f;
        public float weakTime = 3f;

        [Header("Phases")]
        public Tuning phase1 = new Tuning();
        public Tuning phase2 = new Tuning { idleMin = 1.6f, idleMax = 2.4f, volley = 5, orbTravel = 4.2f, wardTime = 7f, wardHp = 2.4f, summonVolley = 7, blinkChance = 0.6f,
            minions = 5, minionSpeed = 1.15f, decoys = 2, chainTime = 6f };

        [Header("Summons, clones and chain (wired by the builder)")]
        public GameObject minionTemplate;
        public GameObject summonFx;
        public GameObject decoyTemplate;
        public GameObject crystalTemplate;
        public GameObject crystalBreakFx;

        [Header("Motion")]
        public float hover = 0.14f;
        public float bob = 0.05f;
        [Tooltip("How far he moves sideways (local Z of the court) and back (X).")]
        public Vector2 roamZ = new Vector2(-1.3f, 1.3f);
        public Vector2 roamX = new Vector2(-0.3f, 0.5f);

        public float Hp { get; set; }
        public int Phase { get; private set; } = 1;
        public bool Fighting { get; private set; }
        public bool ShieldUp => ward != null && ward.Up;
        public float ShieldFill => wardMax > 0f ? Mathf.Clamp01(wardHp / wardMax) : 0f;
        public bool Weak => Time.time < weakUntil;
        /// <summary>Upper bound for healing in the current phase (the HP the phase started with).</summary>
        public float HealCap { get; set; } = 100f;

        /// <summary>A line for the HUD (e.g. "결계!").</summary>
        public event Action<string> Message;
        /// <summary>An orb reached the players.</summary>
        public event Action<CurseOrb> OrbLanded;
        /// <summary>The ward broke.</summary>
        public event Action WardBroken;
        /// <summary>A stone spirit got to player i.</summary>
        public event Action<int> MinionReached;
        /// <summary>Player i is bound by the curse chain (true) or freed / the chain ran out (false).</summary>
        public event Action<int, bool> Chained;
        /// <summary>The chain on player i held to the end (it was not broken).</summary>
        public event Action<int> ChainHeld;

        /// <summary>Where the orbs fly: the players' chests (set by the period).</summary>
        public Transform[] targets = new Transform[0];

        readonly List<CurseOrb> orbs = new List<CurseOrb>();
        float wardHp, wardMax;
        float weakUntil;
        float hitGlow;
        Color hitColor = Color.white;
        Vector3 home, spot, glideFrom;
        float glideT = 1f, glideTime = 1f;
        float lean, flinch, visible = 1f;
        Quaternion baseRotation;
        Vector3 baseScale;
        Transform spine, chestBone, neck, head;

        public IReadOnlyList<CurseOrb> Orbs => orbs;
        readonly List<StoneCreature> minions = new List<StoneCreature>();
        readonly List<BossDecoy> decoys = new List<BossDecoy>();
        ChainCrystal chainCrystal;
        int chainedPlayer = -1;
        /// <summary>Player currently bound by the chain, or -1.</summary>
        public int ChainedPlayer => chainedPlayer;
        public ChainCrystal Crystal => chainCrystal;

        void Awake()
        {
            Hp = maxHp;
            HealCap = maxHp;
            home = spot = glideFrom = transform.position;
            baseRotation = transform.rotation;
            baseScale = transform.localScale;
            if (phase2Aura != null) phase2Aura.SetActive(false);
            foreach (var t in GetComponentsInChildren<Transform>(true))
                switch (t.name)
                {
                    case "Spine": spine = t; break;
                    case "Chest": chestBone = t; break;
                    case "Neck": neck = t; break;
                    case "Head": head = t; break;
                }
        }

        void Update()
        {
            hitGlow = Mathf.Max(0f, hitGlow - Time.deltaTime * 5f);
            flinch = Mathf.Max(0f, flinch - Time.deltaTime * 2.5f);
            if (hitLight != null) { hitLight.intensity = hitGlow * 2.5f; hitLight.color = hitColor; }

            // Glide along a smooth path to the current spot; lean into the move.
            Vector3 prev = transform.position;
            glideT = Mathf.Min(1f, glideT + Time.deltaTime / glideTime);
            float e = glideT * glideT * (3f - 2f * glideT);
            var ground = Vector3.Lerp(glideFrom, spot, e);
            float t = Time.time;
            var pos = ground + Vector3.up * (hover + bob * Mathf.Sin(t * 2.4f) + 0.015f * Mathf.Sin(t * 5.3f));
            transform.position = pos;
            var vel = (pos - prev) / Mathf.Max(1e-4f, Time.deltaTime);

            // Face the players, leaning into the glide.
            var look = transform.forward;
            if (targets.Length > 0)
            {
                var mid = Vector3.zero;
                foreach (var tr in targets) if (tr != null) mid += tr.position;
                mid /= targets.Length;
                var flat = Vector3.ProjectOnPlane(mid - pos, Vector3.up);
                if (flat.sqrMagnitude > 0.01f) look = flat.normalized;
            }
            var side = Vector3.Cross(Vector3.up, look);
            lean = Mathf.Lerp(lean, Mathf.Clamp(Vector3.Dot(vel, side) * 7f, -14f, 14f), 1f - Mathf.Exp(-6f * Time.deltaTime));
            var target = Quaternion.LookRotation(look) * Quaternion.Euler(0f, 0f, -lean);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
            transform.localScale = baseScale * visible;

            if (ward != null && ward.Visible) ward.transform.position = pos + Vector3.up * (0.72f * baseScale.y - hover);
        }

        // After the animator: flinch back under fire and keep the head on the players.
        void LateUpdate()
        {
            var right = transform.right;
            float f = flinch * flinch;
            if (spine != null) spine.rotation = Quaternion.AngleAxis(-6f * f, right) * spine.rotation;
            if (chestBone != null) chestBone.rotation = Quaternion.AngleAxis(-9f * f + 1.5f * Mathf.Sin(Time.time * 31f) * f, right) * chestBone.rotation;
            if (head != null && targets.Length > 0 && targets[0] != null)
            {
                var mid = Vector3.zero;
                foreach (var tr in targets) mid += tr.position;
                mid /= targets.Length;
                var toward = (mid - head.position).normalized;
                var current = head.rotation * Vector3.forward;
                var turn = Quaternion.FromToRotation(transform.forward, toward);
                head.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.35f) * Quaternion.AngleAxis(-5f * f, right) * head.rotation;
            }
        }

        public void Begin(int phase)
        {
            Phase = phase;
            Fighting = true;
            if (phase2Aura != null) phase2Aura.SetActive(phase >= 2);
            StopAllCoroutines();
            ClearSummons();
            visible = 1f;
            StartCoroutine(Loop());
        }

        /// <summary>Stops the fight: clears orbs and the ward, plays the flinch.</summary>
        public void Halt()
        {
            Fighting = false;
            StopAllCoroutines();
            ClearOrbs();
            ClearSummons();
            if (ward != null) ward.Clear();
            visible = 1f;
            flinch = 1f;
            Play("Hit", 0.1f, 1.0f);
        }

        /// <summary>Laser damage; returns the HP actually taken off (0 while the ward is up).</summary>
        public float TakeDamage(float amount, Color from)
        {
            if (!Fighting || ShieldUp || amount <= 0f) return 0f;
            GameAudio.Play("spell_hit", transform);
            float before = Hp;
            Hp = Mathf.Max(0f, Hp - amount * (Weak ? weakMultiplier : 1f));
            hitGlow = 1f;
            hitColor = from;
            flinch = Mathf.Min(1f, flinch + amount * 0.9f);
            return before - Hp;
        }

        /// <summary>Both lasers on the ward for <paramref name="seconds"/>; <paramref name="point"/> is where one landed.</summary>
        public void DamageWard(float seconds, Vector3 point)
        {
            if (!ShieldUp) return;
            wardHp -= seconds;
            ward.Strength = ShieldFill;
            ward.Hit(point);
        }

        /// <summary>A single laser on the ward: it ripples but holds.</summary>
        public void TouchWard(Vector3 point)
        {
            if (ShieldUp) ward.Hit(point);
        }

        public void Heal(float amount) => Hp = Mathf.Min(HealCap, Hp + amount);

        public void ClearOrbs()
        {
            foreach (var o in orbs) if (o != null) Destroy(o.gameObject);
            orbs.Clear();
        }

        // ------------------------------------------------------------------ patterns

        IEnumerator Loop()
        {
            int step = 0;
            if (Phase >= 2)
            {
                // Into phase 2 with a roar.
                Play("Roar", 0.2f, 0.4f);
                FxUtil.Burst(roarFx, transform.position + Vector3.up * 0.1f);
                yield return new WaitForSeconds(1.6f);
            }
            else yield return new WaitForSeconds(1.0f);
            while (Fighting)
            {
                var t = Phase >= 2 ? phase2 : phase1;
                yield return Idle(UnityEngine.Random.Range(t.idleMin, t.idleMax), t);
                // Orbs are one pattern in three (phase 1) or two in six (phase 2), mixed with the others.
                if (Phase >= 2)
                {
                    switch (step % 6)
                    {
                        case 0: yield return Volley(t.volley, t, false); break;
                        case 1: yield return Summon(t); break;
                        case 2: yield return Clones(t); break;
                        case 3: yield return Ward(t); break;
                        case 4: yield return Chain(t); break;
                        default: yield return Volley(t.summonVolley, t, true); break;
                    }
                }
                else
                {
                    switch (step % 3)
                    {
                        case 0: yield return Volley(t.volley, t, false); break;
                        case 1: yield return Summon(t); break;
                        default: yield return Ward(t); break;
                    }
                }
                step++;
            }
        }

        /// <summary>Idles, moving to a new spot half way through (glide, or blink in a puff of curse smoke).</summary>
        IEnumerator Idle(float seconds, Tuning t)
        {
            Play("Idle", 0.3f);
            yield return new WaitForSeconds(seconds * 0.35f);
            var next = PickSpot();
            if (UnityEngine.Random.value < t.blinkChance) yield return Blink(next);
            else GlideTo(next, 1.1f);
            yield return new WaitForSeconds(seconds * 0.65f);
        }

        Vector3 PickSpot()
        {
            for (int i = 0; i < 8; i++)
            {
                var p = home + new Vector3(UnityEngine.Random.Range(roamX.x, roamX.y), 0f, UnityEngine.Random.Range(roamZ.x, roamZ.y));
                if ((p - spot).magnitude > 0.7f) return p;
            }
            return home;
        }

        void GlideTo(Vector3 p, float seconds)
        {
            glideFrom = Vector3.Lerp(glideFrom, spot, glideT * glideT * (3f - 2f * glideT));
            spot = p;
            glideT = 0f;
            glideTime = seconds;
        }

        IEnumerator Blink(Vector3 p)
        {
            GameAudio.Play("teleport", transform);
            FxUtil.Burst(blinkOutFx, transform.position - Vector3.up * hover);
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.18f) { visible = 1f - k * k; yield return null; }
            visible = 0f;
            glideFrom = spot = p;
            glideT = 1f;
            yield return new WaitForSeconds(0.25f);
            FxUtil.Burst(blinkInFx, p);
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.22f) { visible = 1f - (1f - k) * (1f - k) + 0.12f * Mathf.Sin(k * Mathf.PI); yield return null; }
            visible = 1f;
        }

        IEnumerator Volley(int count, Tuning t, bool fromAbove)
        {
            GameAudio.Play("curse_charge", transform);
            Message?.Invoke(fromAbove ? "저주의 비! 전부 쏴서 떨어뜨려!" : "저주 구슬! 레이저로 쏴서 막아!");
            Play(fromAbove ? "Summon" : "Cast", 0.2f, fromAbove ? 0.4f : 0.5f);
            if (castFx != null && wandTip != null)
            {
                var charge = Instantiate(castFx, wandTip);
                charge.transform.localPosition = Vector3.zero;
                charge.SetActive(true);
                Destroy(charge, 2.5f);
            }
            // Casts clip: the arm thrusts at ~1.22s; the wand-high clip peaks at ~1.5s.
            yield return new WaitForSeconds(fromAbove ? 1.1f : 0.72f);
            for (int i = 0; i < count; i++)
            {
                SpawnOrb(i, count, t, fromAbove);
                yield return new WaitForSeconds(fromAbove ? 0.18f : 0.22f);
            }
            yield return new WaitForSeconds(0.5f);
            Play("Idle", 0.4f);
            GlideTo(PickSpot(), 1.6f);
            float until = Time.time + t.orbTravel + 0.5f;
            while (Time.time < until && orbs.Count > 0) yield return null;
        }

        void SpawnOrb(int i, int count, Tuning t, bool fromAbove)
        {
            if (orbTemplate == null || targets.Length == 0) return;
            var go = Instantiate(orbTemplate, orbTemplate.transform.parent);
            go.SetActive(true);
            var orb = go.GetComponent<CurseOrb>();
            orb.hp = t.orbHp;
            var target = targets[i % targets.Length];
            float spread = count > 1 ? (i / (count - 1f) - 0.5f) : 0f;
            Vector3 start = wandTip != null ? wandTip.position : transform.position + Vector3.up;
            Vector3 arc = new Vector3(0f, 0.7f, spread * 2.2f);
            if (fromAbove)
            {
                // Summoned high over the court, they drop onto the players from above.
                start = transform.position + new Vector3(UnityEngine.Random.Range(-1.2f, 0.4f), 2.6f + UnityEngine.Random.Range(0f, 0.6f), spread * 3.4f);
                arc = new Vector3(0f, 1.2f, 0f);
            }
            var offset = new Vector3(0f, UnityEngine.Random.Range(-0.15f, 0.25f), UnityEngine.Random.Range(-0.35f, 0.35f));
            orb.Launch(start, target, offset, t.orbTravel * UnityEngine.Random.Range(0.9f, 1.1f), arc);
            orb.Popped += OnPopped;
            orb.Reached += OnReached;
            orbs.Add(orb);
        }

        void OnPopped(CurseOrb orb)
        {
            FxUtil.Burst(orbPopFx, orb.transform.position);
            orbs.Remove(orb);
            Destroy(orb.gameObject);
        }

        void OnReached(CurseOrb orb)
        {
            FxUtil.Burst(orbHitFx, orb.transform.position);
            orbs.Remove(orb);
            Destroy(orb.gameObject);
            OrbLanded?.Invoke(orb);
        }

        // ------------------------------------------------------------------ stone spirits

        /// <summary>
        /// He raises the wand and stone spirits claw up out of the seal in front of him, then run at the players.
        /// One that gets there heals him and knocks its player back; a few frames of laser break one.
        /// </summary>
        IEnumerator Summon(Tuning t)
        {
            if (minionTemplate == null || targets.Length == 0 || t.minions <= 0) yield break;
            Message?.Invoke("돌정령 소환! 다가오기 전에 레이저로 부숴!");
            Play("Summon", 0.2f, 0.4f);
            yield return new WaitForSeconds(0.9f);
            for (int i = 0; i < t.minions; i++)
            {
                float u = t.minions > 1 ? i / (t.minions - 1f) : 0.5f;
                // Out of the far side of the court, behind him; they run past him at the players.
                var spot = new Vector3(home.x + 2.2f + UnityEngine.Random.Range(0f, 0.5f), 0f, Mathf.Lerp(-1.8f, 1.8f, u) + UnityEngine.Random.Range(-0.2f, 0.2f));
                StartCoroutine(RaiseMinion(spot, i % targets.Length, t));
                yield return new WaitForSeconds(0.28f);
            }
            Play("Idle", 0.4f);
            float until = Time.time + 14f;
            yield return new WaitForSeconds(0.8f);
            while (Time.time < until && minions.Exists(m => m != null && !m.Gone)) yield return null;
            ClearMinions();
        }

        // Out of the floor in a puff of curse smoke, a beat to get its bearings, then it charges.
        IEnumerator RaiseMinion(Vector3 spot, int target, Tuning t)
        {
            GameAudio.Play("summon", transform, 0.75f);
            FxUtil.Burst(summonFx, spot);
            var go = Instantiate(minionTemplate, minionTemplate.transform.parent);
            go.transform.SetPositionAndRotation(spot + Vector3.down * 0.8f, Quaternion.LookRotation(Vector3.left));
            go.SetActive(true);
            var m = go.GetComponent<StoneCreature>();
            minions.Add(m);
            m.Reached += _ => MinionReached?.Invoke(target);
            // Broken by a laser: a bright pop so the kill reads in the dark arena.
            m.Shattered += c => FxUtil.Burst(orbPopFx, c.Center);
            // Grit breaks off as it claws out of the floor.
            if (m.crumbleDust != null) m.crumbleDust.Emit(16);
            if (m.stepDust != null) m.stepDust.Emit(6);
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.5f)
            {
                if (m == null) yield break;
                go.transform.position = spot + Vector3.down * (0.8f * (1f - k) * (1f - k));
                yield return null;
            }
            if (m == null) yield break;
            go.transform.position = spot;
            yield return new WaitForSeconds(0.25f);
            if (m != null) m.Charge(targets[target], t.minionSpeed * UnityEngine.Random.Range(0.9f, 1.1f), t.minionHp);
        }

        void ClearMinions()
        {
            foreach (var m in minions) if (m != null) Destroy(m.gameObject);
            minions.Clear();
        }

        // ------------------------------------------------------------------ clones

        /// <summary>
        /// He vanishes and comes back as three: himself and false copies that waver now and then. Hitting him breaks
        /// the trick; a false one bursts and fires orbs back at its shooter. Untouched, the copies each fire an orb.
        /// </summary>
        IEnumerator Clones(Tuning t)
        {
            if (decoyTemplate == null || t.decoys <= 0) yield break;
            GameAudio.Play("clone_appear", transform);
            Message?.Invoke("분신! 진짜를 찾아 쏴! (가짜는 가끔 일렁여)");
            var spots = new List<Vector3>();
            int count = t.decoys + 1;
            for (int i = 0; i < count; i++)
                spots.Add(home + new Vector3(0.1f, 0f, Mathf.Lerp(-1.25f, 1.25f, count > 1 ? i / (count - 1f) : 0.5f)));
            int real = UnityEngine.Random.Range(0, count);
            yield return Blink(spots[real]);
            for (int i = 0; i < count; i++)
            {
                if (i == real) continue;
                FxUtil.Burst(blinkInFx, spots[i]);
                var go = Instantiate(decoyTemplate, decoyTemplate.transform.parent);
                go.SetActive(true);
                var d = go.GetComponent<BossDecoy>();
                d.hp = t.decoyHp;
                d.targets = targets;
                d.Place(spots[i]);
                d.Popped += (decoy, shooter) => OnDecoyPopped(decoy, shooter, t);
                decoys.Add(d);
            }
            Play("Idle", 0.3f);
            float hp0 = Hp;
            float end = Time.time + t.cloneTime;
            while (Time.time < end && Hp > hp0 - 0.01f && decoys.Exists(d => d != null)) yield return null;
            if (Hp <= hp0 - 0.01f)
            {
                GameAudio.Play("magic_reveal", transform);
                Message?.Invoke("진짜를 찾았다!");
                flinch = 1f;
                weakUntil = Mathf.Max(weakUntil, Time.time + 1.5f);
                Play("Hit", 0.1f, 1.0f);
            }
            else if (decoys.Exists(d => d != null))
            {
                // Time's up: each copy left fires once at a player and fades.
                foreach (var d in decoys)
                    if (d != null) SpawnOrbFrom(d.transform.position + Vector3.up * 0.9f, targets[UnityEngine.Random.Range(0, targets.Length)], t);
            }
            ClearDecoys();
            yield return new WaitForSeconds(0.5f);
        }

        void OnDecoyPopped(BossDecoy decoy, int shooter, Tuning t)
        {
            GameAudio.Play("clone_break", decoy.transform);
            var at = decoy.transform.position + Vector3.up * 0.9f;
            FxUtil.Burst(orbPopFx, at);
            FxUtil.Burst(blinkOutFx, decoy.transform.position);
            Message?.Invoke("가짜다! 반격이 온다!");
            var target = targets.Length > 0 ? targets[Mathf.Clamp(shooter, 0, targets.Length - 1)] : null;
            if (target != null)
                for (int k = 0; k < 2; k++) SpawnOrbFrom(at + Vector3.forward * (k - 0.5f) * 0.3f, target, t);
            decoys.Remove(decoy);
            Destroy(decoy.gameObject);
        }

        void ClearDecoys()
        {
            foreach (var d in decoys)
                if (d != null)
                {
                    FxUtil.Burst(blinkOutFx, d.transform.position);
                    Destroy(d.gameObject);
                }
            decoys.Clear();
        }

        void SpawnOrbFrom(Vector3 start, Transform target, Tuning t)
        {
            if (orbTemplate == null || target == null) return;
            var go = Instantiate(orbTemplate, orbTemplate.transform.parent);
            go.SetActive(true);
            var orb = go.GetComponent<CurseOrb>();
            orb.hp = t.orbHp;
            var offset = new Vector3(0f, UnityEngine.Random.Range(-0.15f, 0.25f), UnityEngine.Random.Range(-0.3f, 0.3f));
            orb.Launch(start, target, offset, t.orbTravel * UnityEngine.Random.Range(0.9f, 1.1f), new Vector3(0f, 0.6f, UnityEngine.Random.Range(-0.6f, 0.6f)));
            orb.Popped += OnPopped;
            orb.Reached += OnReached;
            orbs.Add(orb);
        }

        // ------------------------------------------------------------------ curse chain

        /// <summary>
        /// He casts a chain of curse at one player: it binds her (she cannot fire) to a crystal floating out on her
        /// side. The other player has to shoot the crystal to break it; if it holds, the curse seeps in.
        /// </summary>
        IEnumerator Chain(Tuning t)
        {
            if (crystalTemplate == null || targets.Length < 2 || t.chainTime <= 0f) yield break;
            GameAudio.Play("curse_charge", transform, 0.7f);
            int p = UnityEngine.Random.Range(0, targets.Length);
            Play("Cast", 0.2f, 0.5f);
            yield return new WaitForSeconds(0.7f);
            var target = targets[p];
            var at = new Vector3(home.x - 1.5f, 1.15f, Mathf.Clamp(target.position.z * 1.7f, -2f, 2f));
            FxUtil.Burst(blinkInFx, at + Vector3.down * 0.4f);
            var go = Instantiate(crystalTemplate, crystalTemplate.transform.parent);
            go.transform.position = at;
            go.SetActive(true);
            chainCrystal = go.GetComponent<ChainCrystal>();
            chainCrystal.Bind(target, t.crystalHp);
            chainedPlayer = p;
            Chained?.Invoke(p, true);
            Play("Idle", 0.4f);
            float end = Time.time + t.chainTime;
            while (Time.time < end && !chainCrystal.Done) yield return null;
            bool broken = chainCrystal.Done;
            FxUtil.Burst(crystalBreakFx, chainCrystal.crystal != null ? chainCrystal.crystal.position : at);
            if (broken) Message?.Invoke("사슬이 끊어졌다!");
            else
            {
                Message?.Invoke("사슬이 버텼다… 저주가 스며든다");
                GameAudio.Play("curse_impact", transform);
                Heal(4f);
                ChainHeld?.Invoke(p);
            }
            ClearChain();
            yield return new WaitForSeconds(0.4f);
        }

        void ClearChain()
        {
            if (chainCrystal != null) Destroy(chainCrystal.gameObject);
            chainCrystal = null;
            if (chainedPlayer >= 0) Chained?.Invoke(chainedPlayer, false);
            chainedPlayer = -1;
        }

        void ClearSummons()
        {
            ClearMinions();
            ClearDecoys();
            ClearChain();
        }

        IEnumerator Ward(Tuning t)
        {
            Message?.Invoke("결계! 두 사람이 동시에 쏴야 깨진다!");
            GlideTo(home + new Vector3(0.2f, 0f, 0f), 0.6f);
            Play("Guard", 0.15f);
            yield return new WaitForSeconds(0.35f);
            wardMax = wardHp = t.wardHp;
            if (ward != null) ward.Raise();
            float end = Time.time + t.wardTime;
            while (Time.time < end && wardHp > 0f) yield return null;
            if (wardHp <= 0f)
            {
                if (ward != null) ward.Break();
                WardBroken?.Invoke();
                Message?.Invoke("결계 파괴! 지금이야!");
                weakUntil = Time.time + weakTime;
                flinch = 1f;
                Play("Hit", 0.1f, 1.0f);
                yield return new WaitForSeconds(weakTime);
            }
            else
            {
                if (ward != null) ward.Drop();
                Message?.Invoke("결계가 버텼다…");
                Heal(3f);
            }
        }

        void Play(string state, float fade, float offset = 0f)
        {
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.CrossFadeInFixedTime(state, fade, 0, offset);
        }
    }
}
