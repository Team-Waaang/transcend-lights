using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// Everything that makes a player character who she is: model, look, wand, handedness and colours.
    /// Scene builders read it to spawn the caster; <see cref="GameManager"/> keeps it with the player at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "AiHackathon/Caster Profile", fileName = "CasterProfile")]
    public class CasterProfile : ScriptableObject
    {
        public string displayName = "Caster";

        [Header("Model")]
        public GameObject characterModel;
        [Tooltip("Body material (the rim-lit lilToon copy).")]
        public Material bodyMaterial;
        public GameObject wandModel;
        public Material wandMaterial;
        [Tooltip("Scales the whole staff (wand, tip and grips). Nox's twisted wand reads long, so 0.85.")]
        public float staffScale = 1f;
        [Tooltip("Lead hand is the left one: the staff is held on her left side.")]
        public bool leftHanded;

        [Header("Colours")]
        [Tooltip("Crosshair, hit marker, hit light and HUD.")]
        public Color playerColor = Color.white;
        [Tooltip("Laser effect tint; alpha 0 keeps the effect's own colours.")]
        public Color beamTint = new Color(0f, 0f, 0f, 0f);
        [ColorUsage(false, true)] public Color trailColor = new Color(1.5f, 0.78f, 0.28f, 1f);
        public Color tipLightColor = new Color(1f, 0.78f, 0.45f);

        [Header("Laser")]
        [Tooltip("HCFX beam effect (impact at the pivot, strokes up +Y). Empty: the rig's default Beam_03.")]
        public GameObject beamPrefab;
        [Tooltip("Laser thickness relative to the effect.")]
        public float beamThickness = 0.09f;
        [Tooltip("Light at the wand tip. It sits inside the wand head, so anything above ~0.1 burns the ornament into a white ball. 0 turns it off.")]
        public float tipLightIntensity = 0f;
        [Tooltip("Light where the laser lands.")]
        public float hitLightIntensity = 0.9f;

        [Header("Animation")]
        [Tooltip("Looping idle under the aiming IK (CasterPose).")]
        public AnimationClip idle;

        public string LeadSide => leftHanded ? "Left_" : "Right_";
    }
}
