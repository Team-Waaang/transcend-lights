using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiHackathon.Game
{
    /// <summary>Bounded, scene-independent playback for non-character SFX. No voice assets are loaded here.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        [Serializable]
        public class Cue
        {
            public string name;
            public float volume = 0.4f;
            public float cooldown = 0.06f;
            public int priority = 128;
            public bool loop;
            public bool spatial;
            public bool ambience;
        }

        [Serializable] class Catalog { public Cue[] cues = Array.Empty<Cue>(); }
        sealed class LoopVoice
        {
            public Transform owner;
            public AudioSource source;
            public Cue cue;
            public float requestedAt, target;
        }

        const int OneShotCount = 16, MaxLoops = 16;
        static GameAudio instance;
        static bool quitting;
        public static float MasterVolume { get; set; } = 0.8f;
        public static float EffectsVolume { get; set; } = 0.8f;
        public static float AmbienceVolume { get; set; } = 0.45f;

        readonly Dictionary<string, Cue> cues = new Dictionary<string, Cue>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<(EntityId, string), float> lastPlayed = new Dictionary<(EntityId, string), float>();
        readonly Dictionary<(EntityId, string), LoopVoice> loops = new Dictionary<(EntityId, string), LoopVoice>();
        readonly List<(EntityId, string)> finished = new List<(EntityId, string)>();
        readonly HashSet<string> missing = new HashSet<string>();
        readonly AudioSource[] shots = new AudioSource[OneShotCount];

        public int ActiveLoops => loops.Count;
        public int PlayedCount { get; private set; }
        public string LastPlayed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            instance = null;
            quitting = false;
            MasterVolume = 0.8f;
            EffectsVolume = 0.8f;
            AmbienceVolume = 0.45f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // The phone controller already has haptics; sound is emitted once, by the PC game.
            if (Application.isMobilePlatform || SceneManager.GetActiveScene().name == "WaaangPhoneController") return;
            Ensure();
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static GameAudio Ensure()
        {
            if (!Application.isPlaying || quitting || Application.isMobilePlatform) return null;
            if (instance == null)
            {
                var go = new GameObject("Game Audio (SFX)");
                instance = go.AddComponent<GameAudio>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }

        void Awake()
        {
            var json = Resources.Load<TextAsset>("Audio/SfxCatalog");
            if (json != null)
                foreach (var cue in JsonUtility.FromJson<Catalog>(json.text).cues) cues[cue.name] = cue;
            for (int i = 0; i < shots.Length; i++) shots[i] = MakeSource("One Shot " + i);
        }

        static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (instance == null || !scene.IsValid()) return;
            if (mode == LoadSceneMode.Single)
            {
                // Connection/game objects may be destroyed without receiving another audio update.
                foreach (var voice in instance.loops.Values) voice.target = 0f;
                instance.lastPlayed.Clear();
                foreach (var source in instance.shots)
                    if (source != null && source.spatialBlend > 0f) source.Stop();
            }
            foreach (var root in scene.GetRootGameObjects())
                foreach (var director in root.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true))
                    if (CutsceneSoundtrack.Supports(scene.name) && director.GetComponent<CutsceneSoundtrack>() == null)
                        director.gameObject.AddComponent<CutsceneSoundtrack>();
        }

        AudioSource MakeSource(string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.minDistance = 8f;
            source.maxDistance = 30f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.reverbZoneMix = 0f;
            return source;
        }

        AudioClip Clip(string id)
        {
            if (clips.TryGetValue(id, out var clip)) return clip;
            clip = Resources.Load<AudioClip>("Audio/SFX/" + id);
            if (clip != null) clips[id] = clip;
            else if (missing.Add(id)) Debug.LogWarning("[GameAudio] Missing SFX: " + id);
            return clip;
        }

        static float Gain(Cue cue) => Mathf.Clamp01(MasterVolume) *
            Mathf.Clamp01(cue.ambience ? AmbienceVolume : EffectsVolume) * cue.volume;

        public static void Play(string id, Transform at = null, float volume = 1f, float pitch = 1f)
        {
            var audio = Ensure();
            if (audio == null || volume <= 0f || !audio.cues.TryGetValue(id, out var cue)) return;
            var key = (at != null ? at.GetEntityId() : default(EntityId), id);
            float now = Time.unscaledTime;
            if (audio.lastPlayed.TryGetValue(key, out var last) && now - last < cue.cooldown) return;
            var clip = audio.Clip(id);
            if (clip == null) return;
            AudioSource chosen = null;
            int same = 0;
            foreach (var source in audio.shots)
            {
                if (source.isPlaying && source.clip == clip) same++;
                if (!source.isPlaying && chosen == null) chosen = source;
            }
            if (same >= 3) return;
            if (chosen == null)
            {
                // A high-priority cue may replace a lower-priority one; UI cannot evict an ultimate.
                foreach (var source in audio.shots)
                    if (source.priority > cue.priority && (chosen == null || source.priority > chosen.priority)) chosen = source;
            }
            if (chosen == null) return;
            audio.lastPlayed[key] = now;
            chosen.Stop();
            chosen.loop = false;
            chosen.clip = clip;
            chosen.volume = Gain(cue) * Mathf.Clamp01(volume);
            chosen.pitch = Mathf.Clamp(pitch, 0.75f, 1.35f);
            chosen.priority = cue.priority;
            chosen.spatialBlend = at != null && cue.spatial ? 0.5f : 0f;
            chosen.transform.position = at != null ? at.position : Vector3.zero;
            chosen.Play();
            audio.PlayedCount++;
            audio.LastPlayed = id;
        }

        /// <summary>Renew every frame while wanted. Owner disappearance or a missed renewal fades the loop out.</summary>
        public static void Loop(string id, Transform owner, float volume = 1f, float pitch = 1f)
        {
            if (owner == null) return;
            var audio = Ensure();
            if (audio == null || !audio.cues.TryGetValue(id, out var cue) || !cue.loop) return;
            var key = (owner.GetEntityId(), id);
            if (!audio.loops.TryGetValue(key, out var voice))
            {
                if (volume <= 0f || audio.loops.Count >= MaxLoops) return;
                var clip = audio.Clip(id);
                if (clip == null) return;
                var source = audio.MakeSource("Loop " + id);
                source.clip = clip;
                source.loop = true;
                source.volume = 0f;
                source.priority = cue.priority;
                source.spatialBlend = cue.spatial ? 0.5f : 0f;
                voice = new LoopVoice { owner = owner, source = source, cue = cue };
                audio.loops.Add(key, voice);
                source.Play();
            }
            voice.requestedAt = Time.unscaledTime;
            voice.target = Mathf.Clamp01(volume);
            voice.source.pitch = Mathf.Clamp(pitch, 0.75f, 1.35f);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            foreach (var pair in loops)
            {
                var voice = pair.Value;
                bool alive = voice.owner != null && voice.owner.gameObject.activeInHierarchy && now - voice.requestedAt < 0.2f;
                float want = alive ? voice.target * Gain(voice.cue) : 0f;
                voice.source.volume = Mathf.MoveTowards(voice.source.volume, want, Time.unscaledDeltaTime * 2f);
                if (voice.owner != null) voice.source.transform.position = voice.owner.position;
                if (want <= 0f && voice.source.volume < 0.0001f)
                {
                    voice.source.Stop();
                    Destroy(voice.source.gameObject);
                    finished.Add(pair.Key);
                }
            }
            foreach (var key in finished) loops.Remove(key);
            finished.Clear();
            // Bound cooldown bookkeeping during long play sessions with many summoned objects.
            if (lastPlayed.Count > 512) lastPlayed.Clear();
            UpdateEnvironment();
        }

        void UpdateEnvironment()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "Connection" || scene == "Period1Cutscene" || scene == "Game")
                Loop("classroom_ambience", transform);
            else if (scene == "Period2Cutscene") Loop("dark_hall_ambience", transform);
            else if (scene == "Period3" || scene.StartsWith("Boss", StringComparison.Ordinal))
                Loop("arena_ambience", transform);
        }

        void OnApplicationQuit() => quitting = true;
        void OnDestroy()
        {
            if (instance != this) return;
            SceneManager.sceneLoaded -= SceneLoaded;
            instance = null;
        }
    }
}
