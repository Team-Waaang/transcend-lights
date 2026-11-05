using System;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// The voice clips for each <see cref="VoiceLine"/>, loaded by <see cref="Voice"/> from Resources/VoiceBank.
    /// Filled from Assets/Voice by Tools/Periods/Set Up School Day.
    /// </summary>
    public class VoiceBank : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public VoiceLine line;
            public AudioClip clip;
            [Range(0f, 2f)] public float volume = 1f;
        }

        [Range(0f, 1f)] public float masterVolume = 1f;
        [Tooltip("Silence between two queued lines.")]
        public float gap = 0.15f;
        public Entry[] entries = new Entry[0];

        public AudioClip Get(VoiceLine line)
        {
            foreach (var e in entries) if (e.line == line) return e.clip;
            return null;
        }

        public float Volume(VoiceLine line)
        {
            foreach (var e in entries) if (e.line == line) return Mathf.Clamp01(e.volume * masterVolume);
            return masterVolume;
        }
    }
}
