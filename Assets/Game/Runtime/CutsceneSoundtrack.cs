using UnityEngine;
using UnityEngine.Playables;

namespace AiHackathon.Game
{
    /// <summary>VFX-synchronized cues that survive scene rebuilds and skip silently across timeline seeks.</summary>
    [RequireComponent(typeof(PlayableDirector))]
    public sealed class CutsceneSoundtrack : MonoBehaviour
    {
        readonly struct Beat
        {
            public readonly double time;
            public readonly string id;
            public Beat(double time, string id) { this.time = time; this.id = id; }
        }

        PlayableDirector director;
        Beat[] beats;
        double previous = -0.001;
        string scene;

        public static bool Supports(string name) => name == "Period1Cutscene" || name == "Period2Cutscene" ||
            name == "BossEntranceCutscene" || name == "BossPhase2Cutscene" || name == "BossDefeatCutscene";

        void Awake()
        {
            director = GetComponent<PlayableDirector>();
            scene = gameObject.scene.name;
            switch (scene)
            {
                case "Period1Cutscene":
                    beats = new[] { new Beat(9.3, "beam_start"), new Beat(11.0, "rune_success") };
                    break;
                case "Period2Cutscene":
                    beats = new[] { new Beat(2.2, "blackout"), new Beat(7.0, "magic_ignite"), new Beat(10.85, "magic_ignite") };
                    break;
                case "BossEntranceCutscene":
                    beats = new[] { new Beat(0.15, "ominous_pulse"), new Beat(2.45, "summon"), new Beat(2.53, "blast"),
                        new Beat(10.0, "curse_charge"), new Beat(10.9, "ward_raise"), new Beat(13.5, "cutscene_slam") };
                    break;
                case "BossPhase2Cutscene":
                    beats = new[] { new Beat(0.6, "curse_impact"), new Beat(3.0, "ominous_pulse"), new Beat(3.9, "ominous_pulse"),
                        new Beat(4.6, "ominous_pulse"), new Beat(5.15, "ominous_pulse"), new Beat(7.7, "curse_charge"),
                        new Beat(9.05, "blast"), new Beat(13.1, "cutscene_slam") };
                    break;
                default:
                    beats = new[] { new Beat(0.7, "beam_start"), new Beat(1.1, "curse_impact"), new Beat(3.9, "ominous_pulse"),
                        new Beat(4.9, "resonance"), new Beat(17.65, "blast"), new Beat(23.5, "magic_reveal"), new Beat(28.7, "purify") };
                    break;
            }
        }

        void LateUpdate()
        {
            if (director == null || director.playableAsset == null || director.state != PlayState.Playing) return;
            double now = director.time;
            // Large jumps are skips/seeks: never emit all the cues that were skipped over.
            bool continuous = now >= previous && now - previous < 0.5;
            if (continuous)
                foreach (var beat in beats)
                    if (previous < beat.time && now >= beat.time) GameAudio.Play(beat.id);
            previous = now;
            if (scene == "Period1Cutscene" && now >= 9.3 && now < 11.0)
                GameAudio.Loop("rune_trace_loop", transform, 0.65f);
            if (scene == "BossDefeatCutscene")
            {
                var qte = GetComponent<AiHackathon.Cutscene.ResonanceQte>();
                if (now >= 1.1 && now < 17.65)
                    GameAudio.Loop("qte_loop", transform, qte != null && qte.Succeeded ? 0.9f : 0.55f);
            }
        }

        void OnDisable()
        {
            GameAudio.Loop("rune_trace_loop", transform, 0f);
            GameAudio.Loop("qte_loop", transform, 0f);
        }
    }
}
