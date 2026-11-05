using System;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>Every voice line in the game. Names start with the speaker: Amelia, Kaela, Villain (Ultimate is both).</summary>
    public enum VoiceLine
    {
        AmeliaCalm, AmeliaRuneSuccess, AmeliaRuneFail, AmeliaRuneFail2, AmeliaHit, AmeliaHitShort,
        AmeliaPeriod2Start, AmeliaSearch, AmeliaSkill, AmeliaStreak, AmeliaBossDown,
        AmeliaGuard, AmeliaMiss, AmeliaQteReady, AmeliaQteSuccess, AmeliaQteFail,

        KaelaRuneStart, KaelaRuneSuccess, KaelaRuneFail, KaelaPeriod2Start, KaelaSearch, KaelaHit, KaelaStreak,
        KaelaPowerUp1, KaelaPowerUp2, KaelaBeforeUltimate, KaelaQteStart, KaelaQteSuccess, KaelaQteFail, KaelaVictory,

        Ultimate,

        VillainDefeat, VillainSkill, VillainShield, VillainPhase2, VillainPush,
    }

    /// <summary>
    /// The voice clips, kept in Resources (Assets/Game/Resources/VoiceBank.asset) so <see cref="Voice"/> finds them
    /// from any scene. Filled by Tools/Periods/Set Up School Day from Assets/Voice.
    /// </summary>
    [CreateAssetMenu(menuName = "AiHackathon/Voice Bank", fileName = "VoiceBank")]
    public class VoiceBank : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public VoiceLine line;
            public AudioClip clip;
        }

        [Range(0f, 1f)] public float volume = 1f;
        public Entry[] entries = new Entry[0];

        public AudioClip Get(VoiceLine line)
        {
            foreach (var e in entries) if (e.line == line) return e.clip;
            return null;
        }
    }
}
