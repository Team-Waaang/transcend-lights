using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace AiHackathon.Game
{
    /// <summary>One persistent music pair: crossfades lessons and boss variations without restarting QTE retries.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class GameMusic : MonoBehaviour
    {
        readonly AudioSource[] sources = new AudioSource[2];
        readonly float[] weights = new float[2];
        readonly float[] from = new float[2];
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly HashSet<string> missing = new HashSet<string>();
        BossPeriod boss;
        PlayableDirector defeatDirector;
        bool purified;
        string scene;
        string current;
        int selected = -1;
        float fadeTime;

        public string CurrentTrack => current;
        public float CurrentGain => selected >= 0 ? sources[selected].volume : 0f;
        public int PlayingSources => (sources[0].isPlaying ? 1 : 0) + (sources[1].isPlaying ? 1 : 0);

        void Awake()
        {
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Music " + (i + 1));
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                source.priority = 180;
                source.volume = 0f;
                sources[i] = source;
            }
            RefreshScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnEnable() => SceneManager.sceneLoaded += RefreshScene;
        void OnDisable()
        {
            SceneManager.sceneLoaded -= RefreshScene;
            foreach (var source in sources) if (source != null) source.Stop();
        }

        void RefreshScene(Scene loaded, LoadSceneMode mode)
        {
            missing.Clear();
            if (mode == LoadSceneMode.Single) { purified = false; defeatDirector = null; }
            scene = SceneManager.GetActiveScene().name;
            boss = FindAnyObjectByType<BossPeriod>();
            if (loaded.name == "BossDefeatCutscene")
            {
                purified = false;
                foreach (var root in loaded.GetRootGameObjects())
                {
                    var director = root.GetComponentInChildren<PlayableDirector>(true);
                    if (director != null) { defeatDirector = director; break; }
                }
            }
        }

        string DefeatTrack()
        {
            // Hold the last timeline state through the frame in which the additive scene unloads.
            if (defeatDirector != null) purified = defeatDirector.time >= 28.7;
            return purified ? "bgm_victory" : "bgm_boss_resonance";
        }

        string DesiredTrack()
        {
            if (boss != null && boss.isActiveAndEnabled)
            {
                switch (boss.Current)
                {
                    // Still Period 2's hall (and its music) until the boss shows himself in the entrance.
                    case BossPeriod.Stage.Fall: return "bgm_dark_hall";
                    case BossPeriod.Stage.Phase2Cutscene:
                    case BossPeriod.Stage.Phase2: return "bgm_boss_phase2";
                    case BossPeriod.Stage.DefeatCutscene:
                        return DefeatTrack();
                    case BossPeriod.Stage.Cleared: return "bgm_victory";
                    default: return "bgm_boss_phase1";
                }
            }
            switch (scene)
            {
                case "Title":
                case "Connection":
                case "Period1Cutscene":
                case "Game": return "bgm_rune_class";
                case "Period2Cutscene": return "bgm_dark_hall";
                case "Period3":
                case "BossEntranceCutscene": return "bgm_boss_phase1";
                case "BossFallCutscene": return "bgm_dark_hall";
                case "BossPhase2Cutscene": return "bgm_boss_phase2";
                case "BossDefeatCutscene":
                    return DefeatTrack();
                default: return null;
            }
        }

        void Select(string id)
        {
            if (id == current) return;
            if (id != null && missing.Contains(id)) return;
            AudioClip clip = null;
            if (id != null && !clips.TryGetValue(id, out clip))
            {
                clip = Resources.Load<AudioClip>("Audio/Music/" + id);
                if (clip == null)
                {
                    if (missing.Add(id)) Debug.LogWarning("[GameMusic] Missing music: " + id);
                    return;
                }
                clips[id] = clip;
            }
            for (int i = 0; i < 2; i++) from[i] = weights[i];
            if (id == null) selected = -1;
            else
            {
                int next = weights[0] <= weights[1] ? 0 : 1;
                for (int i = 0; i < 2; i++)
                    if (sources[i].isPlaying && sources[i].clip == clip) { next = i; break; }
                if (!sources[next].isPlaying || sources[next].clip != clip)
                {
                    sources[next].Stop();
                    sources[next].clip = clip;
                    sources[next].volume = 0f;
                    weights[next] = from[next] = 0f;
                    sources[next].Play();
                }
                selected = next;
            }
            current = id;
            fadeTime = 0f;
        }

        void Update()
        {
            Select(DesiredTrack());
            fadeTime += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fadeTime / Mathf.Max(0.1f, AudioMixSettings.Current.musicCrossfade)));
            for (int i = 0; i < 2; i++)
            {
                weights[i] = Mathf.Lerp(from[i], i == selected ? 1f : 0f, k);
                // Linear amplitudes keep the sum <= 1, including scene transitions with unrelated harmonies.
                sources[i].volume = weights[i] * GameAudio.MusicGain;
                if (k >= 1f && i != selected && sources[i].isPlaying) sources[i].Stop();
            }
        }
    }
}
