using System.Collections;
using AiHackathon.Cutscene;
using AiHackathon.Parchment;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AiHackathon.Game
{
    /// <summary>
    /// Period 3 ("보스전"): runs the whole period.
    /// Entrance cutscene → title card → phase 1 → (HP ≤ <see cref="phase2At"/>) phase 2 cutscene → phase 2 →
    /// (HP ≤ <see cref="defeatAt"/>) defeat cutscene with the resonance QTE and the ultimate beam → cleared.
    ///
    /// The cutscenes are their own scenes (built by the cutscene builders) loaded additively on top of this one;
    /// while one plays, <see cref="playRoot"/> (arena, camera, lights, volumes, players, boss) is switched off.
    /// Each cutscene starts from and ends on black, which hides the switch.
    ///
    /// In play, lasers on the boss hurt him (more when both players hit at once: resonance), lasers pop curse orbs,
    /// and his ward only breaks under both lasers together.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class BossPeriod : MonoBehaviour
    {
        [Header("Scene")]
        public GameObject playRoot;
        public GameManager game;
        public BossController boss;
        public Camera playCamera;
        public ParchmentTitleCard card;
        public Volume curseVolume;
        public Volume phase2Volume;
        public Volume impactVolume;

        [Header("HUD")]
        public Font titleFont;
        public Font bodyFont;

        [Header("Cutscenes (scene paths, in Build Settings)")]
        public string entranceScene = "Assets/Cutscene/BossEntrance/BossEntranceCutscene.unity";
        public string phase2Scene = "Assets/Cutscene/BossPhase2/BossPhase2Cutscene.unity";
        public string defeatScene = "Assets/Cutscene/BossDefeatCutscene.unity";
        [Tooltip("Off: skip the cutscenes (fight testing).")]
        public bool playCutscenes = true;

        [Header("Title")]
        public string title = "저주받은 아이";
        public string subtitle = "두 빛이 하나가 될 때, 저주는 풀린다.";
        public float titleTime = 3.4f;

        [Header("Fight")]
        public float phase2At = 55f;
        [Tooltip("The defeat cutscene starts when the boss is down to this (\"아주 조금\").")]
        public float defeatAt = 8f;
        [Tooltip("HP per second one laser takes off the boss.")]
        public float laserDps = 1.7f;
        [Tooltip("Both lasers on the boss at once: each deals this much more.")]
        public float resonanceMultiplier = 1.5f;
        [Tooltip("HP the boss gets back when a curse orb reaches the players.")]
        public float orbHeal = 4f;
        [Tooltip("Dev keys: F1 = to the phase 2 cutscene, F2 = to the defeat cutscene.")]
        public bool devKeys = true;

        public enum Stage { Entrance, Title, Phase1, Phase2Cutscene, Phase2, DefeatCutscene, Cleared }
        public Stage Current { get; private set; } = Stage.Entrance;

        float fade = 1f;
        Color fadeColor = Color.black;
        float flash;
        string message;
        float messageTime = -10f;
        bool resonating;
        float resonanceShown;
        Vector3 cameraHome;
        float shake;
        Transform[] chestTargets = new Transform[0];

        float chip = 1f, chipHoldUntil;
        float lastHp = -1f;
        float stageShown = -10f;

        void Awake()
        {
            if (titleFont != null) GameGui.TitleFont = titleFont;
            if (bodyFont != null) GameGui.BodyFont = bodyFont;
            if (playRoot != null) playRoot.SetActive(false);
        }

        void Start()
        {
            if (boss != null)
            {
                boss.Message += Say;
                boss.OrbLanded += OnOrbLanded;
                boss.WardBroken += OnWardBroken;
            }
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            fade = 1f;
            Current = Stage.Entrance;
            if (playCutscenes) yield return PlayCutscene(entranceScene);
            EnterPlay();
            if (playCamera != null) cameraHome = playCamera.transform.localPosition;

            Current = Stage.Title;
            yield return Fade(0f, 0.8f);
            if (card != null)
            {
                card.Show(new TitleCardInfo(title, subtitle));
                yield return new WaitForSeconds(titleTime);
                card.Hide();
                yield return new WaitForSeconds(0.6f);
            }

            Current = Stage.Phase1;
            stageShown = Time.unscaledTime;
            boss.HealCap = boss.maxHp;
            boss.Begin(1);
            while (boss.Hp > phase2At) yield return null;
            boss.Hp = phase2At;
            yield return EndPhase();

            Current = Stage.Phase2Cutscene;
            if (playCutscenes) yield return PlayCutscene(phase2Scene);
            EnterPlay();
            SetPhase2Look(true);
            Current = Stage.Phase2;
            stageShown = Time.unscaledTime;
            yield return Fade(0f, 0.6f);
            Say("2페이즈! 저주가 더 거세진다!");
            boss.HealCap = phase2At;
            boss.Begin(2);
            while (boss.Hp > defeatAt) yield return null;
            boss.Hp = defeatAt;
            yield return EndPhase();

            Current = Stage.DefeatCutscene;
            if (playCutscenes) yield return PlayCutscene(defeatScene);
            Current = Stage.Cleared;
            fade = 1f;
        }

        /// <summary>The boss flinches, the frame slows a moment and fades to black.</summary>
        IEnumerator EndPhase()
        {
            boss.Halt();
            Time.timeScale = 0.35f;
            yield return new WaitForSecondsRealtime(0.9f);
            Time.timeScale = 1f;
            yield return Fade(1f, 0.4f);
        }

        void EnterPlay()
        {
            if (playRoot != null) playRoot.SetActive(true);
            if (chestTargets.Length == 0 && game != null)
            {
                chestTargets = new Transform[game.players.Length];
                for (int i = 0; i < game.players.Length; i++)
                {
                    var c = game.players[i].character;
                    var t = c != null ? FindDeep(c, "UpperChest") : null;
                    chestTargets[i] = t != null ? t : c;
                }
                boss.targets = chestTargets;
            }
            foreach (var p in game.players) if (p.rig != null) p.rig.ClearTrails();
        }

        void SetPhase2Look(bool on)
        {
            if (phase2Volume != null) phase2Volume.weight = on ? 1f : 0f;
            if (curseVolume != null) curseVolume.weight = on ? 0.4f : 1f;
        }

        IEnumerator PlayCutscene(string path)
        {
            fade = 1f;
            if (playRoot != null) playRoot.SetActive(false);
            if (!Application.CanStreamedLevelBeLoaded(path))
            {
                Debug.LogWarning("[BossPeriod] " + path + " is not in Build Settings; skipping it.");
                yield break;
            }
            var op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            while (!op.isDone) yield return null;
            var scene = SceneManager.GetSceneByPath(path);
            PlayableDirector director = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                director = root.GetComponentInChildren<PlayableDirector>();
                if (director != null) break;
            }
            // The cutscene fades in from its own black.
            fade = 0f;
            if (director != null)
            {
                var qte = director.GetComponent<ResonanceQte>();
                if (qte != null) qte.mouseTest = game == null || game.mouseTest;
                while (director != null && director.time < director.duration - 0.05)
                {
                    var kb = Keyboard.current;
                    // Space / Enter skip a plain cutscene; the QTE cutscene handles its own skipping.
                    if (qte == null && kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                        director.time = director.duration - 0.05;
                    yield return null;
                }
            }
            fade = 1f;
            yield return null;
            var unload = SceneManager.UnloadSceneAsync(scene);
            while (unload != null && !unload.isDone) yield return null;
        }

        IEnumerator Fade(float to, float seconds)
        {
            float from = fade;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds))
            {
                fade = Mathf.Lerp(from, to, t);
                yield return null;
            }
            fade = to;
        }

        void Say(string line)
        {
            message = line;
            messageTime = Time.unscaledTime;
        }

        void OnOrbLanded(CurseOrb orb)
        {
            boss.Heal(orbHeal);
            shake = Mathf.Max(shake, 0.12f);
            flash = 0.45f;
            Say("저주에 맞았다! 보스가 회복한다");
            foreach (var p in game.players)
            {
                var motion = p.character != null ? p.character.GetComponent<CasterMotion>() : null;
                if (motion != null) motion.Flinch(0.9f);
            }
        }

        void OnWardBroken()
        {
            shake = Mathf.Max(shake, 0.1f);
            flash = Mathf.Max(flash, 0.25f);
            StartCoroutine(HitStop(0.12f, 0.15f));
        }

        IEnumerator HitStop(float scale, float seconds)
        {
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(seconds);
            if (Time.timeScale == scale) Time.timeScale = 1f;
        }

        // ------------------------------------------------------------------ play

        void Update()
        {
            if (devKeys && Keyboard.current != null && boss != null && boss.Fighting)
            {
                if (Keyboard.current.f1Key.wasPressedThisFrame && Current == Stage.Phase1) boss.Hp = phase2At;
                if (Keyboard.current.f2Key.wasPressedThisFrame) boss.Hp = Current == Stage.Phase1 ? phase2At : defeatAt;
            }
            if (boss == null || !boss.Fighting || game == null) { resonating = false; return; }

            int onBoss = 0, onWard = 0;
            Vector3 wardPoint = Vector3.zero;
            float dt = Time.deltaTime;
            var hitters = new Color[game.players.Length];
            for (int i = 0; i < game.players.Length; i++)
            {
                var rig = game.players[i].rig;
                if (rig == null || !rig.isActiveAndEnabled || !rig.Firing || !rig.HasHit || rig.HitCollider == null) continue;
                var col = rig.HitCollider;
                var orb = col.GetComponentInParent<CurseOrb>();
                if (orb != null) { orb.Damage(dt); continue; }
                if (col.GetComponentInParent<WardFx>() != null) { onWard++; wardPoint = rig.HitPoint; continue; }
                if (col.transform.IsChildOf(boss.transform))
                {
                    hitters[onBoss] = game.players[i].profile != null ? game.players[i].profile.playerColor : Color.white;
                    onBoss++;
                }
            }
            resonating = onBoss >= 2;
            if (resonating) resonanceShown = Time.unscaledTime;
            for (int k = 0; k < onBoss; k++) boss.TakeDamage(laserDps * dt * (resonating ? resonanceMultiplier : 1f), hitters[k]);
            if (onWard >= 2) boss.DamageWard(dt, wardPoint);
            else if (onWard == 1)
            {
                boss.TouchWard(wardPoint);
                if (Time.unscaledTime - messageTime > 2.5f) Say("혼자서는 안 돼! 둘이 함께 결계를 쏴!");
            }
        }

        void LateUpdate()
        {
            flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime * 1.5f);
            if (impactVolume != null) impactVolume.weight = Mathf.Max(flash * 0.8f, boss != null && boss.Weak ? 0.15f : 0f);
            if (playCamera == null || !playCamera.isActiveAndEnabled) return;
            shake = Mathf.Max(0f, shake - Time.deltaTime * 0.3f);
            float t = Time.time * 25f;
            playCamera.transform.localPosition = cameraHome + new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * shake;
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            float s = GameGui.Scale;
            bool fighting = Current == Stage.Phase1 || Current == Stage.Phase2;
            if (fighting && boss != null) DrawBossHud(s);

            if (flash > 0f) GameGui.Rect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.55f, 0.15f, 0.7f, flash * 0.45f));
            if (fade > 0f) GameGui.Rect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(fadeColor.r, fadeColor.g, fadeColor.b, fade));
            if (Current == Stage.Cleared)
            {
                GameGui.DrawCentered(GameGui.Soft, new Vector2(Screen.width * 0.5f, Screen.height * 0.38f + 55f * s), 900f * s, new Color(1f, 0.75f, 0.35f, 0.18f));
                GameGui.GlowLabel(new Rect(0f, Screen.height * 0.38f, Screen.width, 110f * s), "3교시 클리어!", GameGui.Style(92, TextAnchor.MiddleCenter), new Color(1f, 0.9f, 0.62f), new Color(1f, 0.7f, 0.3f));
                GameGui.Label(new Rect(0f, Screen.height * 0.38f + 110f * s, Screen.width, 50f * s), "저주가 풀렸다. 두 사람의 빛으로.", GameGui.Style(34, TextAnchor.MiddleCenter, false), Color.white);
            }
        }

        void DrawBossHud(float s)
        {
            // Damage chip: holds a moment, then slides down to the HP.
            float hp01 = boss.Hp / boss.maxHp;
            if (lastHp >= 0f && boss.Hp < lastHp - 1e-4f) chipHoldUntil = Time.unscaledTime + 0.45f;
            if (boss.Hp > lastHp) chip = Mathf.Max(chip, hp01);
            lastHp = boss.Hp;
            if (Time.unscaledTime > chipHoldUntil) chip = Mathf.MoveTowards(chip, hp01, Time.unscaledDeltaTime * 0.35f);
            chip = Mathf.Max(chip, hp01);

            float w = 1100f * s, h = 20f * s, x = (Screen.width - w) * 0.5f, y = 72f * s;
            bool p2 = Current == Stage.Phase2;
            var lav = new Color(0.9f, 0.8f, 1f);
            // Crest and name.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
            var crest = new Vector2(x + 14f * s, y - 26f * s);
            GameGui.DrawCentered(GameGui.Soft, crest, 70f * s, new Color(0.75f, 0.35f, 1f, 0.35f + 0.25f * pulse));
            GameGui.DrawCentered(GameGui.Diamond, crest, 26f * s, new Color(0.95f, 0.8f, 1f, 0.95f));
            GameGui.DrawCentered(GameGui.Diamond, crest, 12f * s, p2 ? new Color(1f, 0.35f, 0.75f) : new Color(0.55f, 0.25f, 0.9f));
            GameGui.Label(new Rect(x + 38f * s, y - 52f * s, w, 44f * s), title, GameGui.Style(34, TextAnchor.MiddleLeft), lav);
            GameGui.Label(new Rect(x, y - 50f * s, w, 40f * s), p2 ? "PHASE  II" : "PHASE  I", GameGui.Style(24, TextAnchor.MiddleRight), p2 ? new Color(1f, 0.55f, 0.85f) : new Color(0.75f, 0.65f, 0.95f));

            Color from, to;
            if (boss.Weak)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 14f);
                from = Color.Lerp(new Color(1f, 0.62f, 0.25f), Color.white, 0.3f * k);
                to = Color.Lerp(new Color(1f, 0.9f, 0.55f), Color.white, 0.5f * k);
            }
            else if (p2) { from = new Color(0.45f, 0.06f, 0.42f); to = new Color(1f, 0.35f, 0.72f); }
            else { from = new Color(0.28f, 0.1f, 0.62f); to = new Color(0.8f, 0.5f, 1f); }
            GameGui.FancyBar(new Rect(x, y, w, h), hp01, chip, from, to, 1f, true, 0.45f);
            // Phase marks: a diamond above the bar where the next cutscene starts.
            foreach (var mark in new[] { phase2At, defeatAt })
            {
                if (p2 && mark == phase2At) continue;
                var mx = x + w * mark / boss.maxHp;
                GameGui.Rect(new Rect(mx - 1f * s, y - 3f * s, 2f * s, h + 6f * s), new Color(1f, 1f, 1f, 0.75f));
                GameGui.DrawCentered(GameGui.Diamond, new Vector2(mx, y - 9f * s), 14f * s, new Color(1f, 0.92f, 1f, 0.95f));
            }
            GameGui.Label(new Rect(x, y + h + 2f * s, w, 30f * s), Mathf.CeilToInt(boss.Hp) + " / " + Mathf.RoundToInt(boss.maxHp), GameGui.Style(18, TextAnchor.UpperRight, false), new Color(1f, 1f, 1f, 0.7f));

            float row = y + h + 30f * s;
            if (boss.ShieldUp)
            {
                float ww = 520f * s, wx = (Screen.width - ww) * 0.5f;
                GameGui.Label(new Rect(wx, row - 2f * s, ww, 30f * s), "결  계", GameGui.Style(22, TextAnchor.UpperCenter), new Color(0.85f, 0.75f, 1f));
                GameGui.FancyBar(new Rect(wx, row + 28f * s, ww, 10f * s), boss.ShieldFill, boss.ShieldFill, new Color(0.35f, 0.55f, 1f), new Color(0.95f, 0.6f, 1f), 1f, false, 0.6f + 0.3f * pulse);
                row += 50f * s;
            }
            if (Time.unscaledTime - resonanceShown < 0.4f)
                GameGui.GlowLabel(new Rect(x, y + h + 2f * s, w * 0.5f, 34f * s), "공명  ×" + resonanceMultiplier.ToString("0.0"), GameGui.Style(26, TextAnchor.UpperLeft), new Color(1f, 0.92f, 0.65f), new Color(1f, 0.8f, 0.3f));
            if (boss.Weak)
                GameGui.GlowLabel(new Rect(0f, row, Screen.width, 40f * s), "약점 노출!", GameGui.Style(32, TextAnchor.UpperCenter), new Color(1f, 0.9f, 0.6f), new Color(1f, 0.6f, 0.2f), 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 12f));

            // Phase banner as the fight starts, otherwise the latest message.
            float st = Time.unscaledTime - stageShown;
            if (st < 2.4f)
            {
                float a = Mathf.Clamp01(st / 0.3f) * Mathf.Clamp01((2.4f - st) / 0.5f);
                float by = Screen.height * 0.36f;
                GameGui.BannerBack(new Rect(0f, by - 20f * s, Screen.width, 150f * s), new Color(0.05f, 0f, 0.1f, 0.8f * a));
                GameGui.GlowLabel(new Rect(0f, by, Screen.width, 80f * s), p2 ? "PHASE  II" : "PHASE  I", GameGui.Style(64, TextAnchor.MiddleCenter), Color.white, p2 ? new Color(1f, 0.3f, 0.7f) : new Color(0.7f, 0.4f, 1f), a);
                GameGui.Label(new Rect(0f, by + 76f * s, Screen.width, 40f * s), p2 ? "저주가 깨어났다" : "저주받은 아이를 구하라", GameGui.Style(28, TextAnchor.MiddleCenter, false), new Color(0.9f, 0.85f, 1f), a);
            }
            else if (!string.IsNullOrEmpty(message) && Time.unscaledTime - messageTime < 2.6f)
            {
                float a = Mathf.Clamp01((Time.unscaledTime - messageTime) / 0.15f) * Mathf.Clamp01((2.6f - (Time.unscaledTime - messageTime)) / 0.5f);
                float my = Screen.height * 0.22f;
                GameGui.BannerBack(new Rect(Screen.width * 0.15f, my - 8f * s, Screen.width * 0.7f, 76f * s), new Color(0.05f, 0f, 0.1f, 0.75f * a));
                GameGui.Label(new Rect(0f, my, Screen.width, 60f * s), message, GameGui.Style(38, TextAnchor.MiddleCenter), Color.white, a);
            }
            if (game != null && game.mouseTest && CasterInput.Session(0) == null)
                GameGui.Label(new Rect(0f, Screen.height - 44f * s, Screen.width, 36f * s), "마우스 테스트: 좌클릭 = Amelia, 우클릭 = Nox · 보스와 저주 구슬을 쏘세요" + (devKeys ? "  ·  F1/F2 = 다음 단계로" : ""), GameGui.Style(19, TextAnchor.MiddleCenter, false), new Color(1f, 1f, 1f, 0.6f));
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}
