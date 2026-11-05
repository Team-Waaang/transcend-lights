using System.Linq;
using UnityEngine;

namespace AiHackathon.Game.Editor
{
    /// <summary>ClaudeBridge helper: what the voice player is saying right now in Play mode.</summary>
    public static class VoiceProbe
    {
        public static string Status()
        {
            var v = Object.FindAnyObjectByType<Voice>();
            if (v == null) return "no Voice object";
            return string.Join(" | ", v.GetComponents<AudioSource>().Select(s => (s.isPlaying ? "playing " : "idle ") + (s.clip != null ? s.clip.name + " @" + s.time.ToString("0.0") : "-")));
        }
    }
}
