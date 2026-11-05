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
        [Tooltip("HP the boss gets back when a stone spirit reaches a player.")]
        public float minionHeal = 2.5f;
        [Tooltip("Dev keys: F1 = to the phase 2 cutscene, F2 = to the defeat cutscene.")]
        public bool devKeys = true;

        [Header("Voice cues (cutscene seconds)")]
        [Tooltip("Phase 2 cutscene: the villain's awakening line, just before the eruption (9.05).")]
        public float phase2RoarAt = 8.6f;
        [Tooltip("Defeat cutscene: the villain shouts as he pushes the clash back.")]
        public float bossPushAt = 3.8f;
        [Tooltip("Defeat cutscene: the blast that beats him; his defeat line, then Amelia's.")]
        public float bossBlastAt = 17.65f;
        [Tooltip("Shared resonance at which Kaela calls the ultimate.")]
        [Range(0f, 1f)] public float ultimateCallAt = 0.6f;
        [Tooltip("Lead-in silence of the ultimate clip, skipped so the call is not late.")]
        public float ultimateVoiceSkip = 0.15f;
        [Tooltip("Two orbs landing on the same player within this many seconds count as a heavy hit.")]
        public float heavyHitWindow = 1.5f;

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

        /// <summary>What each player did in the fight, for the results screen.</summary>
        public class PlayerStats { public float damage, resonance; public int orbs, spirits, crystals; }
        public PlayerStats[] Stats { get; private set; } = new PlayerStats[0];

        float chip = 1f, chipHoldUntil;
        float clearedAt = -10f;
        bool lastPeriod;
        float lastHp = -1f;
        float stageShown = -10f;
        readonly float[] orbHits = { -10f, -10f };
        bool chainHeld;
        int wardBreaks;

        /// <summary>Something to run when a cutscene reaches <see cref="at"/>; skipped if it is jumped past by more than <see cref="late"/>.</summary>
        struct Cue
        {
            public float at, late;
            public System.Action run;
            public Cue(float at, float late, System.Action run) { this.at = at; this.late = late; this.run = run; }
        }

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
                boss.MinionReached += OnMinionReached;
                boss.Chained += OnChained;
                boss.ChainHeld += OnChainHeld;
                boss.SkillUsed += () => Voice.Say(VoiceLine.VillainSkill, 1f, 5f);
                boss.WardRaised += () => Voice.Say(VoiceLine.VillainShield, 1f, 5f);
                boss.DecoyShot += i => { if (i == 0) Voice.Say(VoiceLine.AmeliaMiss, 1f, 10f); };
            }
            Stats = new PlayerStats[game != null ? game.players.Length : 2];
            for (int i = 0; i < Stats.Length; i++) Stats[i] = new PlayerStats();
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
            if (playCutscenes) yield return PlayCutscene(phase2Scene, new Cue(phase2RoarAt, float.MaxValue, () => Voice.Say(VoiceLine.VillainPhase2, 2f)));
            else Voice.Say(VoiceLine.VillainPhase2, 2f);
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
            if (playCutscenes)
                yield return PlayCutscene(defeatScene,
                    new Cue(bossPushAt, 0.5f, () => Voice.Say(VoiceLine.VillainPush, 0.5f)),
                    new Cue(bossBlastAt, float.MaxValue, () =>
                    {
                        Voice.Say(VoiceLine.VillainDefeat, 6f);
                        Voice.Say(VoiceLine.AmeliaBossDown, 14f);
                    }));
            Current = Stage.Cleared;
            // The additive cutscene's listener is gone and playRoot stays hidden behind the results.
            bool hasListener = false;
            foreach (var listener in FindObjectsByType<AudioListener>())
                if (listener.isActiveAndEnabled) { hasListener = true; break; }
            if (!hasListener) gameObject.AddComponent<AudioListener>();
            fade = 1f;
            clearedAt = Time.unscaledTime;
            Voice.Say(VoiceLine.KaelaVictory, 16f);
            // The results stay up until Enter / Space / a click, then the next period if there is one (it also moves on
            // by itself after a while); after the last period the same press starts the day again.
            lastPeriod = PeriodFlow.NextIndex() < 0;
            GameAudio.Play(lastPeriod ? "game_complete" : "period_clear");
            yield return new WaitForSecondsRealtime(2.5f);
            for (float t = 0f; lastPeriod || t < 11.5f; t += Time.unscaledDeltaTime)
            {
                var k = Keyboard.current;
                var mouse = Mouse.current;
                if ((k != null && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame)) || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                {
                    GameAudio.Play("ui_confirm");
                    break;
                }
                yield return null;
            }
            if (lastPeriod) PeriodFlow.Restart();
            else PeriodFlow.Next();
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

        IEnumerator PlayCutscene(string path, params Cue[] cues)
        {
            var cueList = new System.Collections.Generic.List<Cue>(cues);
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
                bool ultimateCalled = false;
                if (qte != null)
                {
                    qte.mouseTest = game == null || game.mouseTest;
                    cueList.Add(new Cue(qte.introAt, 0.5f, () =>
                    {
                        Voice.Say(VoiceLine.AmeliaQteReady, 2f);
                        Voice.Say(VoiceLine.KaelaQteStart, 6f);
                    }));
                    qte.onRetry.AddListener(() => Voice.Say(qte.Retries % 2 == 1 ? VoiceLine.AmeliaQteFail : VoiceLine.KaelaQteFail, 1.5f));
                    qte.onSuccess.AddListener(() => Voice.Cut(VoiceLine.Ultimate, ultimateVoiceSkip));
                }
                while (director != null && director.time < director.duration - 0.05)
                {
                    RunCues(cueList, (float)director.time);
                    if (qte != null && !ultimateCalled && qte.Current == ResonanceQte.State.Live && qte.Shared >= ultimateCallAt)
                    {
                        ultimateCalled = true;
                        Voice.Say(VoiceLine.KaelaBeforeUltimate, 2f);
                    }
                    var kb = Keyboard.current;
                    // Space / Enter skip a plain cutscene; the QTE cutscene handles its own skipping.
                    if (qte == null && kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                    {
                        GameAudio.Play("ui_confirm");
                        director.time = director.duration - 0.05;
                    }
                    yield return null;
                }
                if (director != null) RunCues(cueList, (float)director.duration);
            }
            fade = 1f;
            yield return null;
            var unload = SceneManager.UnloadSceneAsync(scene);
            while (unload != null && !unload.isDone) yield return null;
        }

        static void RunCues(System.Collections.Generic.List<Cue> cues, float time)
        {
            for (int i = cues.Count - 1; i >= 0; i--)
            {
                var c = cues[i];
                if (time < c.at) continue;
                cues.RemoveAt(i);
                if (time - c.at <= c.late) c.run();
            }
        }

        int PlayerOf(Transform target) => target != null ? System.Array.IndexOf(chestTargets, target) : -1;

        void HitLine(int player) => Voice.Say(Voice.ForPlayer(player, VoiceLine.AmeliaHitShort, VoiceLine.KaelaHit), 0.8f, 3f);

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
            // Only the player the orb flew at speaks. A second orb on her close behind the first is a heavy hit
            // (Amelia's long line).
            int who = PlayerOf(orb.Target);
            if (who < 0 || who >= orbHits.Length) return;
            float now = Time.unscaledTime;
            bool heavy = now - orbHits[who] < heavyHitWindow;
            orbHits[who] = now;
            if (heavy && who == 0) Voice.Say(VoiceLine.AmeliaHit, 2f, 20f);
            else HitLine(who);
        }

        string NameOf(int i) => game != null && i >= 0 && i < game.players.Length && game.players[i].profile != null
            ? game.players[i].profile.displayName : "P" + (i + 1);

        void Flinch(int i, float strength)
        {
            if (game == null || i < 0 || i >= game.players.Length) return;
            var c = game.players[i].character;
            var motion = c != null ? c.GetComponent<CasterMotion>() : null;
            if (motion != null) motion.Flinch(strength);
        }

        void OnMinionReached(int i)
        {
            GameAudio.Play("curse_impact");
            boss.Heal(minionHeal);
            shake = Mathf.Max(shake, 0.09f);
            flash = Mathf.Max(flash, 0.3f);
            Flinch(i, 0.8f);
            HitLine(i);
            Say(NameOf(i) + "에게 돌정령이 부딪혔다! 보스가 회복한다");
        }

        void OnChained(int i, bool on)
        {
            if (game == null || i < 0 || i >= game.players.Length) return;
            var rig = game.players[i].rig;
            if (rig != null) rig.fireBlocked = on;
            int other = (i + 1) % game.players.Length;
            if (on)
            {
                chainHeld = false;
                Say(NameOf(i) + "이(가) 저주 사슬에 묶였다! " + NameOf(other) + ", 수정을 쏴서 풀어줘!");
                Flinch(i, 0.7f);
            }
            // Let go without holding to the end (and not because the fight stopped): the other one broke it.
            else if (boss.Fighting && !chainHeld)
                Voice.Say(Voice.ForPlayer(other, VoiceLine.AmeliaQteSuccess, VoiceLine.KaelaQteSuccess), 1.5f);
        }

        void OnChainHeld(int i)
        {
            chainHeld = true;
            shake = Mathf.Max(shake, 0.1f);
            flash = Mathf.Max(flash, 0.35f);
            Flinch(i, 1f);
            HitLine(i);
        }

        void OnWardBroken()
        {
            shake = Mathf.Max(shake, 0.1f);
            flash = Mathf.Max(flash, 0.25f);
            StartCoroutine(HitStop(0.12f, 0.15f));
            // Broken by both of them together; they take turns saying so.
            Voice.Say(wardBreaks++ % 2 == 0 ? VoiceLine.AmeliaQteSuccess : VoiceLine.KaelaQteSuccess, 1.5f);
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

            int onBoss = 0, onWard = 0, wardShooter = -1;
            Vector3 wardPoint = Vector3.zero;
            float dt = Time.deltaTime;
            var hitters = new Color[game.players.Length];
            var hitterIndex = new int[game.players.Length];
            for (int i = 0; i < game.players.Length; i++)
            {
                var rig = game.players[i].rig;
                if (rig == null || !rig.isActiveAndEnabled || !rig.Firing || !rig.HasHit || rig.HitCollider == null) continue;
                var col = rig.HitCollider;
                var orb = col.GetComponentInParent<CurseOrb>();
                if (orb != null)
                {
                    if (orb.Damage(dt)) Stats[i].orbs++;
                    continue;
                }
                var minion = col.GetComponentInParent<StoneCreature>();
                if (minion != null)
                {
                    bool was = minion.Charging;
                    minion.Shot(dt);
                    if (was && !minion.Charging) Stats[i].spirits++;
                    continue;
                }
                var decoy = col.GetComponentInParent<BossDecoy>();
                if (decoy != null) { decoy.Damage(dt, i); continue; }
                var crystal = col.GetComponentInParent<ChainCrystal>();
                if (crystal != null)
                {
                    bool was = crystal.Done;
                    crystal.Damage(dt);
                    if (!was && crystal.Done) Stats[i].crystals++;
                    continue;
                }
                if (col.GetComponentInParent<WardFx>() != null) { onWard++; wardPoint = rig.HitPoint; wardShooter = i; continue; }
                if (col.transform.IsChildOf(boss.transform))
                {
                    hitters[onBoss] = game.players[i].profile != null ? game.players[i].profile.playerColor : Color.white;
                    hitterIndex[onBoss] = i;
                    onBoss++;
                }
            }
            if (boss.ChainedPlayer >= 0 && Mathf.Repeat(Time.time, 0.9f) < dt) Flinch(boss.ChainedPlayer, 0.35f);
            bool wasResonating = resonating;
            resonating = onBoss >= 2;
            if (resonating && !wasResonating) GameAudio.Play("resonance", null, 0.65f);
            if (resonating) resonanceShown = Time.unscaledTime;
            for (int k = 0; k < onBoss; k++)
            {
                var st = Stats[hitterIndex[k]];
                st.damage += boss.TakeDamage(laserDps * dt * (resonating ? resonanceMultiplier : 1f), hitters[k]);
                if (resonating) st.resonance += dt;
            }
            if (onWard >= 2) boss.DamageWard(dt, wardPoint);
            else if (onWard == 1)
            {
                boss.TouchWard(wardPoint);
                if (Time.unscaledTime - messageTime > 2.5f)
                {
                    Say("혼자서는 안 돼! 둘이 함께 결계를 쏴!");
                    if (wardShooter == 0) Voice.Bark(VoiceLine.AmeliaMiss, 20f);
                }
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
                float rise = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - clearedAt - 1.6f) / 0.6f);
                float ty = Mathf.Lerp(Screen.height * 0.38f, Screen.height * 0.08f, rise);
                GameGui.DrawCentered(GameGui.Soft, new Vector2(Screen.width * 0.5f, ty + 55f * s), 900f * s, new Color(1f, 0.75f, 0.35f, 0.18f));
                GameGui.GlowLabel(new Rect(0f, ty, Screen.width, 110f * s), "3교시 클리어!", GameGui.Style(Mathf.RoundToInt(Mathf.Lerp(92f, 72f, rise)), TextAnchor.MiddleCenter), new Color(1f, 0.9f, 0.62f), new Color(1f, 0.7f, 0.3f));
                // The banner rises out of the way when the results come in.
                float up = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - clearedAt - 1.6f) / 0.6f);
                float by = Mathf.Lerp(Screen.height * 0.38f, Screen.height * 0.08f, up);
                GameGui.Label(new Rect(0f, by + 110f * s, Screen.width, 50f * s), "저주가 풀렸다. 두 사람의 빛으로.", GameGui.Style(34, TextAnchor.MiddleCenter, false), Color.white, 1f - up);
                DrawResults(s, Mathf.Clamp01((Time.unscaledTime - clearedAt - 1.9f) / 0.5f));
            }
        }

        /// <summary>
        /// Results: one card per player (damage with its share of the total, orbs shot down, stone spirits broken,
        /// chain crystals broken, seconds in resonance), the top damage dealer marked MVP.
        /// </summary>
        void DrawResults(float s, float a)
        {
            if (a <= 0f || Stats.Length == 0 || game == null) return;
            float total = 0f, best = -1f;
            int mvp = -1;
            for (int i = 0; i < Stats.Length; i++)
            {
                total += Stats[i].damage;
                if (Stats[i].damage > best) { best = Stats[i].damage; mvp = i; }
            }
            float cardW = 560f * s, cardH = 470f * s, gap = 60f * s;
            float x0 = Screen.width * 0.5f - cardW - gap * 0.5f, y0 = Screen.height * 0.27f;
            GameGui.Label(new Rect(0f, y0 - 58f * s, Screen.width, 44f * s), "전투 결과", GameGui.Style(36, TextAnchor.MiddleCenter), new Color(1f, 0.93f, 0.78f), a);
            for (int i = 0; i < Stats.Length && i < game.players.Length; i++)
            {
                var st = Stats[i];
                var profile = game.players[i].profile;
                var color = profile != null ? profile.playerColor : Color.white;
                string name = profile != null ? profile.displayName : "P" + (i + 1);
                // Slide in from its side.
                float slide = (1f - a) * (i == 0 ? -80f : 80f) * s;
                var r = new Rect(x0 + i * (cardW + gap) + slide, y0, cardW, cardH);
                GameGui.BannerBack(new Rect(r.x - 40f * s, r.y - 20f * s, r.width + 80f * s, r.height + 40f * s), new Color(0.012f, 0.011f, 0.01f, 0.9f * a));
                GameGui.Rect(new Rect(r.x, r.y + 70f * s, r.width, Mathf.Max(1f, 2f * s)), new Color(color.r, color.g, color.b, 0.6f * a));
                GameGui.GlowLabel(new Rect(r.x, r.y + 8f * s, r.width, 56f * s), name, GameGui.Style(44, TextAnchor.MiddleCenter), Color.Lerp(color, Color.white, 0.3f), color, a);
                if (i == mvp && best > 0f)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
                    GameGui.GlowLabel(new Rect(r.xMax - 120f * s, r.y + 14f * s, 110f * s, 40f * s), "MVP", GameGui.Style(30, TextAnchor.MiddleRight), new Color(1f, 0.9f, 0.55f), new Color(1f, 0.7f, 0.25f), a * pulse);
                }

                // Damage: the number, then a bar of the share.
                float share = total > 0f ? st.damage / total : 0f;
                GameGui.Label(new Rect(r.x + 30f * s, r.y + 92f * s, r.width - 60f * s, 34f * s), "보스에게 준 피해", GameGui.Style(24, TextAnchor.MiddleLeft, false), new Color(1f, 1f, 1f, 0.75f), a);
                GameGui.GlowLabel(new Rect(r.x + 30f * s, r.y + 126f * s, r.width - 60f * s, 70f * s), Mathf.RoundToInt(st.damage).ToString(),
                    GameGui.Style(62, TextAnchor.MiddleLeft), Color.white, color, a);
                GameGui.Label(new Rect(r.x + 30f * s, r.y + 140f * s, r.width - 60f * s, 50f * s), Mathf.RoundToInt(share * 100f) + "%",
                    GameGui.Style(34, TextAnchor.MiddleRight), Color.Lerp(color, Color.white, 0.4f), a);
                // Share bar on a neutral track (the HUD's FancyBar track is the boss's violet).
                var bar = new Rect(r.x + 30f * s, r.y + 204f * s, r.width - 60f * s, 12f * s);
                float edge = Mathf.Max(1f, 1.5f * s);
                GameGui.Rect(new Rect(bar.x - edge, bar.y - edge, bar.width + 2f * edge, bar.height + 2f * edge), new Color(1f, 1f, 1f, 0.18f * a));
                GameGui.Rect(bar, new Color(0.06f, 0.06f, 0.06f, 0.9f * a));
                var fill = new Rect(bar.x, bar.y, bar.width * share * a, bar.height);
                GameGui.Rect(fill, new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, a));
                GameGui.Rect(new Rect(fill.x, fill.y, fill.width, fill.height * 0.45f), new Color(Mathf.Lerp(color.r, 1f, 0.4f), Mathf.Lerp(color.g, 1f, 0.4f), Mathf.Lerp(color.b, 1f, 0.4f), 0.8f * a));

                // The rest, one line each.
                string[] labels = { "저주 구슬 격추", "돌정령 처치", "사슬 수정 파괴", "공명 시간" };
                string[] values = { st.orbs.ToString(), st.spirits.ToString(), st.crystals.ToString(), st.resonance.ToString("0.0") + "초" };
                for (int k = 0; k < labels.Length; k++)
                {
                    float ly = r.y + 250f * s + k * 52f * s;
                    GameGui.DrawCentered(GameGui.Diamond, new Vector2(r.x + 40f * s, ly + 20f * s), 12f * s, new Color(color.r, color.g, color.b, 0.9f * a));
                    GameGui.Label(new Rect(r.x + 62f * s, ly, r.width - 120f * s, 40f * s), labels[k], GameGui.Style(26, TextAnchor.MiddleLeft, false), new Color(1f, 1f, 1f, 0.85f), a);
                    GameGui.Label(new Rect(r.x + 62f * s, ly, r.width - 92f * s, 40f * s), values[k], GameGui.Style(30, TextAnchor.MiddleRight), new Color(1f, 0.95f, 0.85f), a);
                }
            }
            float blink = 0.55f + 0.3f * Mathf.Sin(Time.unscaledTime * 3f);
            float fy = y0 + cardH + 36f * s;
            if (lastPeriod)
            {
                GameGui.GlowLabel(new Rect(0f, fy, Screen.width, 56f * s), "오늘의 수업 끝", GameGui.Style(44, TextAnchor.MiddleCenter), new Color(1f, 0.95f, 0.85f), new Color(1f, 0.72f, 0.35f), a);
                fy += 58f * s;
            }
            GameGui.Label(new Rect(0f, fy, Screen.width, 36f * s), lastPeriod ? "Enter · 클릭: 처음부터" : "Enter · 클릭: 계속", GameGui.Style(24, TextAnchor.MiddleCenter, false), new Color(1f, 1f, 1f, blink), a);
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
                GameGui.BannerBack(new Rect(0f, by - 20f * s, Screen.width, 150f * s), new Color(0.012f, 0.011f, 0.01f, 0.8f * a));
                GameGui.GlowLabel(new Rect(0f, by, Screen.width, 80f * s), p2 ? "PHASE  II" : "PHASE  I", GameGui.Style(64, TextAnchor.MiddleCenter), Color.white, p2 ? new Color(1f, 0.3f, 0.7f) : new Color(0.7f, 0.4f, 1f), a);
                GameGui.Label(new Rect(0f, by + 76f * s, Screen.width, 40f * s), p2 ? "저주가 깨어났다" : "저주받은 아이를 구하라", GameGui.Style(28, TextAnchor.MiddleCenter, false), new Color(0.9f, 0.85f, 1f), a);
            }
            else if (!string.IsNullOrEmpty(message) && Time.unscaledTime - messageTime < 2.6f)
            {
                float a = Mathf.Clamp01((Time.unscaledTime - messageTime) / 0.15f) * Mathf.Clamp01((2.6f - (Time.unscaledTime - messageTime)) / 0.5f);
                float my = Screen.height * 0.22f;
                GameGui.BannerBack(new Rect(Screen.width * 0.15f, my - 8f * s, Screen.width * 0.7f, 76f * s), new Color(0.012f, 0.011f, 0.01f, 0.75f * a));
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
