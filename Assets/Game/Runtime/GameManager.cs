using System;
using System.Collections.Generic;
using AiHackathon.Cutscene;
using UnityEngine;
using UnityEngine.Events;
using Waaang;

namespace AiHackathon.Game
{
    /// <summary>
    /// Owns the players of a period scene and the hand-over from its opening cutscene to play.
    /// During the intro the timeline animates the characters (rigs off, staff in the lead hand). When the intro
    /// finishes, each character's timeline animator stops, the staff goes back to the aiming rig, CasterPose takes
    /// the idle and the WandPointerRig the arms, blended in over <see cref="handoverBlend"/> so nothing pops.
    /// Without phones, <see cref="mouseTest"/> lets one mouse play both: left click fires the left player, right
    /// click the right one, each aiming at the cursor while its button is held.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class GameManager : MonoBehaviour
    {
        [Serializable]
        public class Player
        {
            public CasterProfile profile;
            public Transform character;
            public WandPointerRig rig;
            public CasterPose pose;
            [Tooltip("Animator the intro timeline drives; switched off for play.")]
            public Animator cutsceneAnimator;
            public Transform staff;
            [Tooltip("Where the player aims when play starts (e.g. her rune stone).")]
            public Transform startAim;
        }

        public enum Phase { Intro, Play }

        public Player[] players = new Player[0];
        public PeriodIntro intro;

        [Header("Input")]
        [Tooltip("Mouse plays both players when no phone is connected: left click = player 1, right click = player 2.")]
        public bool mouseTest = true;

        [Header("Hand-over")]
        public float handoverBlend = 0.5f;

        public UnityEvent onPlayStarted = new UnityEvent();

        public Phase CurrentPhase { get; private set; } = Phase.Intro;
        public static GameManager Instance { get; private set; }

        struct Captured
        {
            public Transform[] bones;
            public Quaternion[] rotations;
            public Vector3 staffPosition;
            public Quaternion staffRotation;
        }

        Captured[] captured;
        float playStart = -1f;

        void Awake()
        {
            Instance = this;
            foreach (var p in players)
            {
                if (p.rig != null) p.rig.gameObject.SetActive(false);
                if (p.pose != null) p.pose.enabled = false;
            }
        }

        void Start()
        {
            if (intro != null) intro.onFinished.AddListener(BeginPlay);
            else BeginPlay();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Switches from the intro's animation to live play. Safe to call more than once.</summary>
        public void BeginPlay()
        {
            if (CurrentPhase == Phase.Play) return;
            CurrentPhase = Phase.Play;
            captured = new Captured[players.Length];
            var cam = Camera.main;
            for (int i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p.character == null) continue;
                if (p.cutsceneAnimator != null) p.cutsceneAnimator.enabled = false;
                if (p.staff != null && p.staff.parent != p.character) p.staff.SetParent(p.character, true);

                var bones = new List<Transform>(p.character.GetComponentsInChildren<Transform>(true));
                bones.RemoveAll(b => b == p.character || (p.staff != null && b.IsChildOf(p.staff)));
                var c = new Captured { bones = bones.ToArray(), rotations = new Quaternion[bones.Count] };
                for (int b = 0; b < c.bones.Length; b++) c.rotations[b] = c.bones[b].localRotation;
                if (p.staff != null)
                {
                    c.staffPosition = p.staff.localPosition;
                    c.staffRotation = p.staff.localRotation;
                }
                captured[i] = c;

                if (p.pose != null) p.pose.enabled = true;
                if (p.rig != null)
                {
                    p.rig.mouseMode = mouseTest;
                    p.rig.mouseButton = i == 0 ? WandPointerRig.MouseFireButton.Left : WandPointerRig.MouseFireButton.Right;
                    p.rig.mouseAimWhileFiring = true;
                    p.rig.gameObject.SetActive(true);
                    if (p.startAim != null && cam != null)
                    {
                        var v = cam.WorldToViewportPoint(p.startAim.position);
                        p.rig.SetPointer(new Vector2(v.x, v.y));
                    }
                }
            }
            playStart = Time.time;
            onPlayStarted.Invoke();
        }

        // After CasterPose (Update) and the rigs' IK (LateUpdate): ease from the intro's last pose into live play.
        void LateUpdate()
        {
            if (CurrentPhase != Phase.Play || captured == null || playStart < 0f) return;
            float k = handoverBlend > 0f ? Mathf.Clamp01((Time.time - playStart) / handoverBlend) : 1f;
            if (k >= 1f) { captured = null; return; }
            k = k * k * (3f - 2f * k);
            for (int i = 0; i < players.Length; i++)
            {
                var c = captured[i];
                if (c.bones == null) continue;
                for (int b = 0; b < c.bones.Length; b++)
                    c.bones[b].localRotation = Quaternion.Slerp(c.rotations[b], c.bones[b].localRotation, k);
                var staff = players[i].staff;
                if (staff != null)
                {
                    staff.localPosition = Vector3.Lerp(c.staffPosition, staff.localPosition, k);
                    staff.localRotation = Quaternion.Slerp(c.staffRotation, staff.localRotation, k);
                }
            }
        }
    }
}
