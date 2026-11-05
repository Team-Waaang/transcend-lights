using UnityEngine;
using Waaang;

namespace AiHackathon.Game
{
    /// <summary>Equipment magic only: no footsteps, character foley or vocalizations.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class CasterSpellAudio : MonoBehaviour
    {
        WandPointerRig rig;
        WandLight lightBeam;
        RunePeriod runes;
        LightHuntPeriod hunt;
        BossPeriod fight;
        bool firing;

        void Start()
        {
            rig = GetComponent<WandPointerRig>();
            runes = FindAnyObjectByType<RunePeriod>();
            hunt = FindAnyObjectByType<LightHuntPeriod>();
            fight = FindAnyObjectByType<BossPeriod>();
            foreach (var candidate in FindObjectsByType<WandLight>())
                if (candidate.rig == rig) { lightBeam = candidate; break; }
        }

        void LateUpdate()
        {
            bool playing = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GameManager.Phase.Play;
            if (runes != null && runes.Cleared || hunt != null && hunt.Cleared) playing = false;
            if (fight != null && (fight.boss == null || !fight.boss.Fighting)) playing = false;
            bool want = playing && rig != null && rig.isActiveAndEnabled && rig.Firing && !rig.fireBlocked;
            bool torch = lightBeam != null;
            if (want && !firing) GameAudio.Play(torch ? "magic_ignite" : "beam_start", transform);
            if (!want && firing) GameAudio.Play("beam_stop", transform, 0.6f);
            firing = want;
            GameAudio.Loop(torch ? "torch_loop" : "beam_loop", transform, want ? 1f : 0f);
        }

        void OnDisable()
        {
            firing = false;
            GameAudio.Loop("beam_loop", transform, 0f);
            GameAudio.Loop("torch_loop", transform, 0f);
        }
    }
}
