using UnityEngine;
using UnityEngine.Events;

namespace AiHackathon.Game
{
    /// <summary>
    /// A rock creature that lives in the dark (Period 2). It shuffles about on its own; caught in a wand light it
    /// startles ("!") and scurries out of the beam, faster. Every second it stays lit the stone creeps up its body
    /// (<see cref="Exposure"/>, shown by the Petrify shader), stiffening it; kept in the light for
    /// <see cref="petrifySeconds"/> it turns to stone for good. Out of the light the stone slowly recedes.
    /// It has no clips; all its motion is procedural. On <see cref="body"/>: lean, bob, roll and squash and stretch.
    /// On the rig's bones (the Tripo auto-rig: Legs are the hind legs, the Arm chains the forelegs): a diagonal trot
    /// that quickens into a run, the spine swaying, the head looking about when idle, a crouch and leap when startled,
    /// trembling while it hardens, and the pose it is caught in frozen once it is stone.
    /// </summary>
    public class StoneCreature : MonoBehaviour
    {
        public enum State { Dormant, Wander, Startled, Flee, Petrified, Chase }

        [Tooltip("Visual root the procedural animation moves; its rest pose is captured on Awake.")]
        public Transform body;
        public Renderer[] renderers = new Renderer[0];
        public float height = 0.8f;
        public float radius = 0.35f;

        [Header("Movement")]
        public float wanderSpeed = 0.4f;
        public float fleeSpeed = 1.05f;
        [Tooltip("Speed multiplier when almost stone.")]
        [Range(0.05f, 1f)] public float stiffSpeed = 0.3f;
        public float turnSpeed = 240f;
        public float strideLength = 0.32f;
        [Tooltip("Floor area it keeps to (world XZ).")]
        public Vector2 areaMin = new Vector2(-4f, 2f);
        public Vector2 areaMax = new Vector2(4f, 8f);
        [Tooltip("Keeps it inside the play camera's view: |x - apex.x| <= (z - apex.y) * viewSlope - viewMargin. 0 = off.")]
        public float viewSlope;
        public Vector2 viewApex = new Vector2(0f, -1.15f);
        public float viewMargin = 0.4f;
        [Tooltip("What it steers around (walls, pillars).")]
        public LayerMask obstacleMask = ~(1 << 2);

        [Header("Petrify")]
        public float petrifySeconds = 3f;
        [Tooltip("Stone lost per second out of the light (after a short grace).")]
        public float recoverRate = 0.28f;
        public float recoverDelay = 0.6f;
        [Tooltip("Seconds unlit before it calms down and wanders again.")]
        public float calmDelay = 2.2f;

        [Header("FX (inactive children, switched on to play)")]
        public GameObject alertFx;
        public GameObject petrifyFx;
        public ParticleSystem crumbleDust;
        public ParticleSystem stepDust;
        public Light flashLight;

        public UnityEvent<StoneCreature> onPetrified = new UnityEvent<StoneCreature>();

        [Header("Chase (summoned by the Period 3 boss)")]
        public float chaseSpeed = 0.7f;
        [Tooltip("Ground distance to its target at which it reaches the player.")]
        public float reachDistance = 0.8f;
        [Tooltip("Seconds of laser that turn a charging creature to stone and break it.")]
        public float shotHp = 0.3f;
        /// <summary>A charging creature got to its target (it crumbles away right after).</summary>
        public event System.Action<StoneCreature> Reached;
        /// <summary>A charging creature was shot to stone and is breaking up.</summary>
        public event System.Action<StoneCreature> Shattered;
        public Transform ChaseTarget { get; private set; }
        public bool Charging => Current == State.Chase;
        public bool Gone { get; private set; }

        float shotLeft = -1f, shatterAt = -1f;

        /// <summary>Runs at <paramref name="target"/> (the boss's summon) until it is shot or gets there.</summary>
        public void Charge(Transform target, float speed, float hp)
        {
            ChaseTarget = target;
            chaseSpeed = speed;
            shotHp = shotLeft = Mathf.Max(0.05f, hp);
            everSpotted = true;
            Current = State.Chase;
        }

        /// <summary>Laser on a charging creature for <paramref name="seconds"/>: the stone creeps up it, then it breaks.</summary>
        public void Shot(float seconds)
        {
            if (Current != State.Chase) return;
            shotLeft -= seconds;
            lastLit = Time.time;
            Exposure = Mathf.Clamp01(1f - shotLeft / shotHp);
            if (shotLeft > 0f) return;
            Petrify();
            Break(0.35f);
            Shattered?.Invoke(this);
        }

        // Crumbles away after <paramref name="delay"/> (shrinks into a burst of grit, then is destroyed).
        void Break(float delay)
        {
            if (shatterAt >= 0f) return;
            shatterAt = Time.time + delay;
        }

        public State Current { get; private set; } = State.Dormant;
        public float Exposure { get; private set; }
        public bool Petrified => Current == State.Petrified;
        public bool Lit => Time.time - lastLit < 0.12f;
        public Vector3 Center => transform.position + Vector3.up * (height * 0.5f);
        /// <summary>Player whose light last fell on it.</summary>
        public int LastLighter { get; private set; } = -1;

        Vector3 restPos, restScale;
        Quaternion restRot;
        Material[] mats;
        Vector3 target;
        float speed, gait, idleUntil, lastLit = -10f, startleT = -1f, flash, petrifiedAt;
        Vector3 fleeDir;
        float seed;
        bool everSpotted;

        static readonly int PetrifyId = Shader.PropertyToID("_Petrify");
        static readonly int LitId = Shader.PropertyToID("_Lit");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int HeightMinId = Shader.PropertyToID("_HeightMin");
        static readonly int HeightMaxId = Shader.PropertyToID("_HeightMax");
        static readonly int UpId = Shader.PropertyToID("_UpOS");

        void Awake()
        {
            seed = Random.value * 100f;
            if (body == null) body = transform.childCount > 0 ? transform.GetChild(0) : transform;
            restPos = body.localPosition;
            restRot = body.localRotation;
            restScale = body.localScale;
            if (renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
            var list = new System.Collections.Generic.List<Material>();
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                var m = r.material;   // per-creature instance
                if (!m.HasProperty(PetrifyId)) continue;
                // The shader measures the stone front along the creature's up axis in the mesh's own space.
                var up = r.transform.InverseTransformDirection(transform.up).normalized;
                var mf = r.GetComponent<MeshFilter>();
                var smr = r as SkinnedMeshRenderer;
                var b = smr != null && smr.sharedMesh != null ? smr.sharedMesh.bounds
                    : mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float d = Vector3.Dot(c, up);
                    lo = Mathf.Min(lo, d);
                    hi = Mathf.Max(hi, d);
                }
                m.SetVector(UpId, up);
                m.SetFloat(HeightMinId, lo);
                m.SetFloat(HeightMaxId, hi);
                m.SetFloat(PetrifyId, 0f);
                list.Add(m);
            }
            mats = list.ToArray();
            BindBones();
            target = transform.position;
            idleUntil = Time.time + Random.Range(0.3f, 2f);
            if (flashLight != null) flashLight.enabled = false;
        }

        /// <summary>Starts it moving about (play begins).</summary>
        public void Wake()
        {
            if (Current != State.Dormant) return;
            Current = State.Wander;
            PickTarget();
        }

        /// <summary>Called every frame a wand light is on it; <paramref name="strength"/> sums the lights (≈1 each).</summary>
        public void Illuminate(float strength, int player, Vector3 lightOrigin, Vector3 lightDir)
        {
            if (Current == State.Petrified || Current == State.Dormant || strength <= 0f) return;
            lastLit = Time.time;
            LastLighter = player;
            Exposure = Mathf.Min(1f, Exposure + strength * Time.deltaTime / Mathf.Max(0.1f, petrifySeconds));

            // Out of the beam sideways, and a little away from the caster.
            Vector3 p = transform.position;
            Vector3 axis = new Vector3(lightDir.x, 0f, lightDir.z).normalized;
            Vector3 rel = p - lightOrigin; rel.y = 0f;
            Vector3 side = rel - Vector3.Dot(rel, axis) * axis;
            if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(Vector3.up, axis) * (Mathf.Sin(seed) > 0f ? 1f : -1f);
            fleeDir = (side.normalized * 1f + axis * 0.45f).normalized;

            if (!everSpotted || Current == State.Wander)
            {
                everSpotted = true;
                Current = State.Startled;
                startleT = 0f;
                Play(alertFx);
            }
            if (Exposure >= 1f) Petrify();
        }

        void Petrify()
        {
            Current = State.Petrified;
            Exposure = 1f;
            petrifiedAt = Time.time;
            flash = 1f;
            Play(petrifyFx);
            if (crumbleDust != null) crumbleDust.Emit(28);
            onPetrified.Invoke(this);
        }

        static void Play(GameObject fx)
        {
            if (fx == null) return;
            fx.SetActive(false);
            fx.SetActive(true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 move = Vector3.zero;
            float wantSpeed = 0f;
            float stiff = Mathf.Lerp(1f, stiffSpeed, Exposure);

            switch (Current)
            {
                case State.Wander:
                    if (Time.time < idleUntil) break;
                    Vector3 to = target - transform.position; to.y = 0f;
                    if (to.magnitude < 0.25f)
                    {
                        idleUntil = Time.time + Random.Range(0.8f, 3f);
                        PickTarget();
                        break;
                    }
                    move = to.normalized;
                    wantSpeed = wanderSpeed * stiff;
                    break;
                case State.Startled:
                    startleT += dt;
                    if (startleT > 0.42f) Current = State.Flee;
                    break;
                case State.Chase:
                    if (ChaseTarget == null) break;
                    Vector3 toward = ChaseTarget.position - transform.position; toward.y = 0f;
                    if (toward.magnitude < reachDistance)
                    {
                        // Got there: it bursts against the player.
                        Current = State.Petrified;
                        flash = 0.6f;
                        Reached?.Invoke(this);
                        Break(0f);
                        break;
                    }
                    move = toward.normalized;
                    wantSpeed = chaseSpeed * stiff;
                    break;
                case State.Flee:
                    move = fleeDir;
                    wantSpeed = fleeSpeed * stiff;
                    if (Time.time - lastLit > calmDelay)
                    {
                        Current = State.Wander;
                        idleUntil = Time.time + Random.Range(0.4f, 1.2f);
                        PickTarget();
                    }
                    break;
            }

            if (Current != State.Petrified && Time.time - lastLit > recoverDelay)
                Exposure = Mathf.Max(0f, Exposure - recoverRate * dt);

            // Steer: keep inside the area, slide around walls and pillars.
            if (move.sqrMagnitude > 0.01f)
            {
                move = Steer(move);
                if (Current == State.Flee) fleeDir = Vector3.Slerp(fleeDir, move, 4f * dt);
            }
            speed = Mathf.MoveTowards(speed, wantSpeed, (wantSpeed > speed ? 4f : 3f) * dt);
            if (move.sqrMagnitude > 0.01f)
            {
                var face = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, turnSpeed * stiff * dt);
                // Walk where it faces, so turns arc rather than slide.
                Vector3 step = transform.forward * (speed * dt);
                var p = transform.position + step;
                transform.position = Clamp(p);
            }
            else if (speed > 0f)
            {
                transform.position += transform.forward * (speed * dt);
            }

            Animate(dt);
            if (shatterAt >= 0f && Time.time >= shatterAt)
            {
                float k = (Time.time - shatterAt) / 0.22f;
                if (k <= Time.deltaTime / 0.22f && crumbleDust != null) crumbleDust.Emit(36);
                body.localScale = frozenScale * Mathf.Max(0f, 1f - k);
                if (k >= 1f && !Gone)
                {
                    Gone = true;
                    // Let the grit finish before the object goes.
                    foreach (var r in renderers) if (r != null) r.enabled = false;
                    Destroy(gameObject, 1.5f);
                }
            }

            float litK = Lit ? 1f : 0f;
            foreach (var m in mats)
            {
                m.SetFloat(PetrifyId, Exposure);
                m.SetFloat(LitId, Mathf.MoveTowards(m.GetFloat(LitId), Current == State.Petrified ? 0f : litK, dt * 6f));
                m.SetFloat(FlashId, flash);
            }
            flash = Mathf.MoveTowards(flash, 0f, dt * 1.4f);
            if (flashLight != null)
            {
                flashLight.enabled = flash > 0.01f;
                flashLight.intensity = flash * flash * 3.5f;
            }
            if (crumbleDust != null && Current != State.Petrified)
            {
                // Grit sheds while the stone is creeping up it.
                var e = crumbleDust.emission;
                e.rateOverTime = Lit ? 10f + Exposure * 30f : 0f;
            }
        }

        float HalfWidthAt(float z) => viewSlope > 0f ? Mathf.Max(0.3f, (z - viewApex.y) * viewSlope - viewMargin) : float.MaxValue;

        Vector3 Clamp(Vector3 p)
        {
            p.z = Mathf.Clamp(p.z, areaMin.y, areaMax.y);
            float half = HalfWidthAt(p.z);
            p.x = Mathf.Clamp(p.x, Mathf.Max(areaMin.x, viewApex.x - half), Mathf.Min(areaMax.x, viewApex.x + half));
            return p;
        }

        Vector3 Steer(Vector3 dir)
        {
            Vector3 p = transform.position;
            // Pull back toward the middle near the edges of the area (and of the camera's view).
            float m = 0.6f;
            Vector3 push = Vector3.zero;
            float half = HalfWidthAt(p.z);
            if (p.x < Mathf.Max(areaMin.x, viewApex.x - half) + m) push.x += 1f;
            if (p.x > Mathf.Min(areaMax.x, viewApex.x + half) - m) push.x -= 1f;
            if (p.z < areaMin.y + m) push.z += 1f;
            if (p.z > areaMax.y - m) push.z -= 1f;
            dir = (dir + push * 1.2f).normalized;
            Vector3 origin = p + Vector3.up * (height * 0.45f);
            if (!Physics.SphereCast(origin, radius * 0.8f, dir, out _, 0.7f, obstacleMask, QueryTriggerInteraction.Ignore)) return dir;
            // Try angles either side, nearest first; prefer the side it was already turning to.
            float sign = Mathf.Sin(seed + Time.time * 0.1f) > 0f ? 1f : -1f;
            for (int a = 30; a <= 150; a += 30)
                for (int s = 0; s < 2; s++)
                {
                    var d = Quaternion.Euler(0f, a * (s == 0 ? sign : -sign), 0f) * dir;
                    if (!Physics.SphereCast(origin, radius * 0.8f, d, out _, 0.7f, obstacleMask, QueryTriggerInteraction.Ignore))
                        return d;
                }
            return -dir;
        }

        void PickTarget()
        {
            for (int i = 0; i < 8; i++)
            {
                var t = Clamp(new Vector3(Random.Range(areaMin.x + 0.5f, areaMax.x - 0.5f), 0f, Random.Range(areaMin.y + 0.5f, areaMax.y - 0.5f)));
                Vector3 from = transform.position + Vector3.up * (height * 0.45f);
                Vector3 to = t + Vector3.up * (height * 0.45f) - from;
                if (to.magnitude < 1.2f) continue;
                if (!Physics.SphereCast(from, radius * 0.7f, to.normalized, out _, to.magnitude, obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    target = t;
                    return;
                }
            }
            target = transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 1.5f;
        }

        // ------------------------------------------------------------------ procedural animation

        void Animate(float dt)
        {
            if (Current == State.Petrified)
            {
                // Frozen as it was caught (the bones keep their last pose); one small crunch as the stone sets.
                float k = Mathf.Clamp01((Time.time - petrifiedAt) / 0.3f);
                float punch = Mathf.Sin(k * Mathf.PI) * 0.05f;
                body.localScale = frozenScale * (1f + punch);
                return;
            }

            float t = Time.time + seed;
            float stiff = 1f - Exposure * 0.75f;
            float moving = Mathf.Clamp01(speed / Mathf.Max(0.01f, wanderSpeed));
            float fast = Mathf.Clamp01((speed - wanderSpeed) / Mathf.Max(0.01f, fleeSpeed - wanderSpeed));

            // One trot cycle per two strides; a dip and a squash as each diagonal pair lands.
            gait += speed * dt / strideLength * Mathf.PI * Mathf.Lerp(1f, 0.8f, Exposure);
            float s = Mathf.Sin(gait);
            float contact = Mathf.Pow(1f - Mathf.Abs(s), 6f);
            float bob = Mathf.Abs(s) * (0.018f + 0.03f * fast) * moving;
            float roll = s * (3f + 3f * fast) * moving;
            float lean = (3f + 6f * fast) * moving;

            float idle = 1f - moving;
            float breathe = Mathf.Sin(t * 1.7f) * 0.02f * idle * stiff;
            float squash = contact * (0.035f + 0.04f * fast) * moving;
            float jumpY = 0f, stretch = 0f, crouch = 0f, tuck = 0f;
            if (Current == State.Startled && startleT >= 0f)
            {
                // Crouch, spring up with the legs tucked, land.
                float u = startleT / 0.42f;
                if (u < 0.22f) crouch = Mathf.Sin(u / 0.22f * Mathf.PI * 0.5f);
                else if (u < 0.85f)
                {
                    float v = (u - 0.22f) / 0.63f;
                    jumpY = 4f * 0.2f * v * (1f - v);
                    stretch = Mathf.Sin(v * Mathf.PI) * 0.08f;
                    tuck = Mathf.Sin(v * Mathf.PI);
                    crouch = Mathf.Max(0f, 1f - v * 4f);
                }
                else crouch = Mathf.Sin((u - 0.85f) / 0.15f * Mathf.PI) * 0.7f;
                squash += crouch * 0.1f;
            }

            Vector3 shake = Vector3.zero;
            float jitter = 0f;
            if (Lit || Exposure > 0.05f)
            {
                float a = (Lit ? 1f : 0.3f) * (0.003f + 0.01f * Exposure);
                shake = new Vector3(Mathf.PerlinNoise(t * 40f, 1f) - 0.5f, 0f, Mathf.PerlinNoise(2f, t * 40f) - 0.5f) * 2f * a;
                jitter = (Lit ? 1f : 0.3f) * Exposure;
            }

            float sy = 1f - squash + stretch + breathe;
            float sxz = 1f + squash * 0.5f - stretch * 0.4f - breathe * 0.4f;
            body.localPosition = restPos + Vector3.up * (bob + jumpY - crouch * 0.04f) + shake;
            body.localRotation = Quaternion.Euler(lean - tuck * 8f, Mathf.Cos(gait) * 3f * moving, roll) * restRot;
            body.localScale = Vector3.Scale(restScale, new Vector3(sxz, sy, sxz));
            frozenScale = body.localScale;

            if (bones != null) AnimateBones(t, s, moving, fast, idle, stiff, crouch, tuck, jitter);

            if (stepDust != null && moving > 0.5f && contact > 0.95f && Time.time - lastStep > 0.12f)
            {
                lastStep = Time.time;
                stepDust.Emit(Current == State.Flee ? 4 : 2);
            }
        }

        // ------------------------------------------------------------------ bones

        enum B { Hips, Spine, Chest, Neck, Head, HindL, ShinL, HindR, ShinR, FrontL, ForeL, FrontR, ForeR, Count }
        static readonly string[] BoneNames =
        {
            "Hips", "Spine", "Chest", "Neck", "Head", "Left_UpperLeg", "Left_LowerLeg", "Right_UpperLeg", "Right_LowerLeg",
            "Left_UpperArm", "Left_LowerArm", "Right_UpperArm", "Right_LowerArm",
        };
        Transform[] bones;
        Quaternion[] boneRest;

        void BindBones()
        {
            var all = body.GetComponentsInChildren<Transform>(true);
            var found = new Transform[(int)B.Count];
            int hits = 0;
            for (int i = 0; i < found.Length; i++)
                foreach (var tr in all)
                    if (tr.name == BoneNames[i]) { found[i] = tr; hits++; break; }
            if (hits < 9) return;   // not this rig: body motion only
            bones = found;
            boneRest = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null) boneRest[i] = bones[i].localRotation;
        }

        // Turns a bone about an axis given in the creature's own frame (x = right, y = up, z = forward).
        void Turn(B b, Vector3 axis, float degrees)
        {
            var bone = bones[(int)b];
            if (bone == null || Mathf.Abs(degrees) < 0.01f) return;
            bone.rotation = Quaternion.AngleAxis(degrees, body.TransformDirection(axis)) * bone.rotation;
        }

        void AnimateBones(float t, float s, float moving, float fast, float idle, float stiff, float crouch, float tuck, float jitter)
        {
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null) bones[i].localRotation = boneRest[i];
            float c = Mathf.Cos(gait);
            var right = Vector3.right;
            var up = Vector3.up;

            // Spine: sway with the trot, head steadies against it and looks about when idle.
            float lookYaw = (Mathf.PerlinNoise(t * 0.35f, seed) - 0.5f) * 80f * idle * stiff;
            float lookPitch = (Mathf.PerlinNoise(seed, t * 0.5f) - 0.5f) * 24f * idle * stiff - 6f * fast * moving;
            float breathe = Mathf.Sin(t * 1.7f) * 2.5f * idle * stiff;
            Turn(B.Hips, up, c * 4f * moving);
            Turn(B.Spine, up, -c * 3f * moving);
            Turn(B.Chest, right, breathe - crouch * 6f + tuck * 6f);
            Turn(B.Neck, up, lookYaw * 0.4f - c * 2f * moving);
            Turn(B.Neck, right, lookPitch * 0.4f + crouch * 10f - tuck * 14f);
            Turn(B.Head, up, lookYaw * 0.6f);
            Turn(B.Head, right, lookPitch * 0.6f + Mathf.Abs(s) * 4f * moving);

            // Trot: left hind with right fore, then the other pair. Swing forward/back about the side axis; the
            // shin / forearm folds while its leg swings forward (off the ground).
            float swing = (20f + 14f * fast) * moving * Mathf.Lerp(1f, 0.6f, Exposure);
            float fold = (28f + 18f * fast) * moving;
            float liftA = Mathf.Max(0f, c), liftB = Mathf.Max(0f, -c);
            Turn(B.HindL, right, -s * swing - crouch * 22f + tuck * 30f);
            Turn(B.ShinL, right, liftA * fold + crouch * 34f + tuck * 30f);
            Turn(B.HindR, right, s * swing - crouch * 22f + tuck * 30f);
            Turn(B.ShinR, right, liftB * fold + crouch * 34f + tuck * 30f);
            Turn(B.FrontR, right, -s * swing + crouch * 18f - tuck * 34f);
            Turn(B.ForeR, right, -liftA * fold - crouch * 26f - tuck * 30f);
            Turn(B.FrontL, right, s * swing + crouch * 18f - tuck * 34f);
            Turn(B.ForeL, right, -liftB * fold - crouch * 26f - tuck * 30f);

            if (jitter > 0f)
            {
                float j = jitter * 7f;
                Turn(B.Neck, up, (Mathf.PerlinNoise(t * 30f, 5f) - 0.5f) * j);
                Turn(B.Chest, right, (Mathf.PerlinNoise(7f, t * 30f) - 0.5f) * j);
                Turn(B.HindL, right, (Mathf.PerlinNoise(t * 33f, 9f) - 0.5f) * j);
                Turn(B.FrontR, right, (Mathf.PerlinNoise(11f, t * 33f) - 0.5f) * j);
            }
        }

        Vector3 frozenScale = Vector3.one;
        float lastStep;
    }
}
