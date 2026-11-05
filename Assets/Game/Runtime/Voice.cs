using System.Collections.Generic;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// Plays the voice lines one at a time on a 2D source that survives scene loads, so a line started at the end of
    /// a period finishes over the next one's fade. Amelia's and Kaela's lines asked for in the same frame go Amelia
    /// first; anything else keeps the order it was asked in. <see cref="Say"/> queues a line and drops it if it waited past its patience; <see cref="Bark"/> is
    /// chatter that only plays when no one is talking; <see cref="Cut"/> silences everything and plays at once.
    /// </summary>
    public class Voice : MonoBehaviour
    {
        struct Pending
        {
            public VoiceLine line;
            public AudioClip clip;
            public int frame, speaker;
            public float deadline;
        }

        /// <summary>Silence between two lines.</summary>
        const float Gap = 0.15f;

        static Voice instance;
        static VoiceBank bank;
        static readonly Dictionary<VoiceLine, float> lastPlayed = new Dictionary<VoiceLine, float>();

        readonly List<Pending> queue = new List<Pending>();
        AudioSource source;
        float nextAt;

        /// <summary>Someone is talking or waiting to.</summary>
        public static bool Talking => instance != null && (instance.source.isPlaying || instance.queue.Count > 0);
        public static bool Speaking => instance != null && instance.source != null && instance.source.isPlaying;

        /// <summary>Player 0 is Amelia, player 1 Kaela.</summary>
        public static VoiceLine? ForPlayer(int player, VoiceLine amelia, VoiceLine kaela) =>
            player == 0 ? amelia : player == 1 ? kaela : (VoiceLine?)null;

        /// <summary>
        /// Queues <paramref name="line"/>. It is dropped if it has not started within <paramref name="patience"/>
        /// seconds, or if it played less than <paramref name="cooldown"/> seconds ago.
        /// </summary>
        public static void Say(VoiceLine? line, float patience = 6f, float cooldown = 0f)
        {
            if (line == null || CoolingDown(line.Value, cooldown)) return;
            var clip = Clip(line.Value);
            if (clip != null) Get().Enqueue(line.Value, clip, patience);
        }

        /// <summary>Plays <paramref name="line"/> only if no one is talking and it is off cooldown.</summary>
        public static void Bark(VoiceLine? line, float cooldown = 8f)
        {
            if (line == null || Talking) return;
            Say(line, 0.5f, cooldown);
        }

        /// <summary>
        /// Stops the current line, drops the queue and plays <paramref name="line"/> now, starting
        /// <paramref name="skip"/> seconds in (past the clip's lead-in silence).
        /// </summary>
        public static void Cut(VoiceLine line, float skip = 0f)
        {
            var clip = Clip(line);
            if (clip == null) return;
            var v = Get();
            v.queue.Clear();
            v.source.Stop();
            v.Play(line, clip, Mathf.Clamp(skip, 0f, clip.length * 0.5f));
        }

        static bool CoolingDown(VoiceLine line, float cooldown) =>
            cooldown > 0f && lastPlayed.TryGetValue(line, out var at) && Time.unscaledTime - at < cooldown;

        static AudioClip Clip(VoiceLine line)
        {
            if (!Application.isPlaying) return null;
            if (bank == null) bank = Resources.Load<VoiceBank>("VoiceBank");
            return bank != null ? bank.Get(line) : null;
        }

        static int Speaker(VoiceLine line)
        {
            var name = line.ToString();
            return name.StartsWith("Amelia") ? 0 : name.StartsWith("Kaela") ? 1 : 2;
        }

        static Voice Get()
        {
            if (instance == null)
            {
                var go = new GameObject("Voice");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Voice>();
            }
            return instance;
        }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        void Enqueue(VoiceLine line, AudioClip clip, float patience)
        {
            foreach (var p in queue) if (p.line == line) return;
            var item = new Pending
            {
                line = line, clip = clip, frame = Time.frameCount, speaker = Speaker(line),
                deadline = Time.unscaledTime + Mathf.Max(0f, patience),
            };
            // Amelia goes ahead of any Kaela line asked for in the same frame; everything else keeps its order.
            int i = queue.Count;
            if (item.speaker == 0)
                for (int k = queue.Count - 1; k >= 0 && queue[k].frame == item.frame; k--)
                    if (queue[k].speaker == 1) i = k;
            queue.Insert(i, item);
        }

        void Update()
        {
            if (source.clip != null) source.volume = (bank != null ? bank.volume : 1f) * GameAudio.VoiceGain(source.clip);
            if (source.isPlaying || Time.unscaledTime < nextAt) return;
            while (queue.Count > 0)
            {
                var p = queue[0];
                queue.RemoveAt(0);
                if (Time.unscaledTime > p.deadline) continue;
                Play(p.line, p.clip);
                return;
            }
        }

        void Play(VoiceLine line, AudioClip clip, float skip = 0f)
        {
            source.clip = clip;
            source.volume = (bank != null ? bank.volume : 1f) * GameAudio.VoiceGain(clip);
            source.Play();
            source.time = skip;
            lastPlayed[line] = Time.unscaledTime;
            nextAt = Time.unscaledTime + clip.length - skip + Gap;
        }
    }
}
