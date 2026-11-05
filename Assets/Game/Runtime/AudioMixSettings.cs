using System;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>Shared gain staging; source audio and voice-bank assignments remain intact.</summary>
    [Serializable]
    public sealed class AudioMixSettings
    {
        [Serializable] public class VoiceLevel { public string name; public float gain = 1f; }
        public float master = 0.8f, effects = 0.8f, ambience = 0.35f, music = 0.52f, voice = 0.95f;
        public float musicVoiceDuck = 0.30f, musicEventDuck = 0.65f;
        public float effectsVoiceDuck = 0.70f, importantEffectsVoiceDuck = 0.88f, ambienceVoiceDuck = 0.50f;
        public float voiceAttack = 0.045f, voiceRelease = 0.6f, voiceHold = 0.18f;
        public float musicCrossfade = 1.8f;
        public VoiceLevel[] voiceLevels = Array.Empty<VoiceLevel>();

        static AudioMixSettings current;
        public static AudioMixSettings Current
        {
            get
            {
                if (current == null)
                {
                    var json = Resources.Load<TextAsset>("Audio/MixSettings");
                    current = json != null ? JsonUtility.FromJson<AudioMixSettings>(json.text) : new AudioMixSettings();
                }
                return current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => current = null;
    }
}
