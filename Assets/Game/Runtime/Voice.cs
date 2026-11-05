using System.Collections.Generic;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>Every voice line the game plays. Clips are bound in <see cref="VoiceBank"/> (Assets/Voice).</summary>
    public enum VoiceLine
    {
        // Amelia (player 1)
        AmeliaCalm,          // 마력 안정: Period 1 starts
        AmeliaRuneOk,        // 룬 제작 성공
        AmeliaRuneFail1,     // 룬 제작 실패
        AmeliaRuneFail2,     // 룬 제작 실패2
        AmeliaHurt,          // 피격: hit hard (many orbs at once)
        AmeliaHurtShort,     // 피격 short: hit
        AmeliaPeriod2,       // 2교시 시작 대사
        AmeliaSearch,        // 색적: after the Period 2 opening line
        AmeliaSkill,         // 스킬: a creature turned to stone
        AmeliaCombo,         // 적 연속 처치: her second creature
        AmeliaBossDown,      // 보스 처치: after the ultimate beam

        // Kaela / Nox (player 2)
        NoxRune,             // 룬 제작: Period 1 starts
        NoxRuneOk,           // 룬 제작 성공
        NoxRuneFail,         // 룬 실패
        NoxPeriod2,          // 2교시 진입
        NoxSearch,           // 색적
        NoxHurt,             // 피격
        NoxCombo,            // 연속처치: her second creature
        NoxBoost1,           // 1교시 강화: Period 1 cleared
        NoxBoost2,           // 2교시 강화: Period 2 cleared
        NoxPreUltimate,      // 궁극기 전 대사: right before the ultimate beam

        // Both
        UltimateBeam,        // 어르또메이또비이이이임: the ultimate beam fires

        // Boss
        BossDefeat,          // 빌런 패배
        BossSkill,           // 빌런 킄킄킄: attack pattern
        BossShield,          // 빌런 킄: ward raised
        BossPhase2,          // 빌런 2페이즈 각성
    }

    /// <summary>
    /// Plays voice lines on one 2D dialogue channel that never talks over itself: lines wait their turn, and one that
    /// would wait longer than its <c>maxWait</c> is dropped (so spammy events don't build a backlog). Lines play in the
    /// order asked, so callers ask Amelia's line before Kaela's when both speak at once. The boss has his own channel
    /// and cuts in at once.
    /// The player object lives across scene loads; the bank is <c>Resources/VoiceBank</c>.
    /// </summary>
    public class Voice : MonoBehaviour
    {
        struct Pending
        {
            public VoiceLine line;
            public float expires;
        }

        static Voice instance;
        static bool quitting;

        VoiceBank bank;
        AudioSource dialogue, boss;
        readonly List<Pending> queue = new List<Pending>();
        float busyUntil;

        /// <summary>Queues a line on the dialogue channel; dropped if it can't start within <paramref name="maxWait"/> seconds.</summary>
        public static void Say(VoiceLine line, float maxWait = 2.5f)
        {
            var v = Get();
            if (v == null || v.Clip(line) == null) return;
            float now = Time.unscaledTime;
            v.queue.Add(new Pending { line = line, expires = now + maxWait });
            v.Pump();
        }

        /// <summary>A boss line, straight away on his own channel (skipped if he is still mid-line, unless <paramref name="interrupt"/>).</summary>
        public static void Boss(VoiceLine line, bool interrupt = false)
        {
            var v = Get();
            var clip = v != null ? v.Clip(line) : null;
            if (clip == null || (v.boss.isPlaying && !interrupt)) return;
            v.boss.clip = clip;
            v.boss.volume = v.bank.Volume(line);
            v.boss.Play();
        }

        /// <summary>Drops every line still waiting (the one playing finishes).</summary>
        public static void ClearQueue()
        {
            if (instance != null) instance.queue.Clear();
        }

        /// <summary>Seconds until the dialogue channel is free.</summary>
        public static float BusyFor => instance != null ? Mathf.Max(0f, instance.busyUntil - Time.unscaledTime) : 0f;

        static Voice Get()
        {
            if (instance != null) return instance;
            if (quitting || !Application.isPlaying) return null;
            var bank = Resources.Load<VoiceBank>("VoiceBank");
            if (bank == null)
            {
                Debug.LogWarning("[Voice] Resources/VoiceBank is missing; run Tools/Periods/Set Up School Day.");
                return null;
            }
            var go = new GameObject("Voice");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Voice>();
            instance.bank = bank;
            instance.dialogue = instance.NewSource();
            instance.boss = instance.NewSource();
            return instance;
        }

        AudioSource NewSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;
            s.priority = 0;
            return s;
        }

        AudioClip Clip(VoiceLine line) => bank != null ? bank.Get(line) : null;

        void Update() => Pump();

        void Pump()
        {
            float now = Time.unscaledTime;
            if (now < busyUntil) return;
            while (queue.Count > 0)
            {
                var p = queue[0];
                queue.RemoveAt(0);
                if (now > p.expires) continue;
                var clip = Clip(p.line);
                dialogue.clip = clip;
                dialogue.volume = bank.Volume(p.line);
                dialogue.Play();
                busyUntil = now + clip.length + bank.gap;
                return;
            }
        }

        void OnApplicationQuit() => quitting = true;
    }
}
