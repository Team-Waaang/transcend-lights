using System.Collections.Generic;
using System.IO;
using AiHackathon.Cutscene;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Period 3 boss arena: the academy's ruined duelling court at night, under a cursed moon. A round flagstone
    /// floor with a faint violet seal under the boss, a broken colonnade with a gate and torn school banners behind
    /// him, a ruined outer wall, academy spires lost in the fog, curse crystals breaking through the ground, floating
    /// rubble and violet braziers.
    /// Built in local space of <c>root</c>: the boss stands at the origin facing local -X, the players' side
    /// (local -X) is left open. The boss cutscenes build it at the world origin; a play scene can rotate the root
    /// (e.g. <see cref="CutsceneKit.BossRotation"/> puts the boss ahead of players who face +Z).
    /// Shared assets live in <see cref="Dir"/> so every scene that builds the arena reuses them.
    /// </summary>
    public static class BossArena
    {
        public const string Dir = "Assets/Cutscene/BossArena";
        public static readonly Color FogColor = new Color(0.11f, 0.09f, 0.15f);
        public static readonly Color Violet = new Color(0.62f, 0.38f, 1f);

        /// <summary>Seal glow alpha at rest (material _BaseColor.a on "Seal").</summary>
        public const float SealAlpha = 0.3f;
        public const int BrazierCount = 4, CrystalClusterCount = 6;
        /// <summary>Brazier fire light intensity at rest ("Brazier i/Fire Light").</summary>
        public const float FireLight = 1.6f;
        /// <summary>Crystal glow light intensity at rest ("Crystal Glow i").</summary>
        public const float CrystalLight = 0.9f;

        /// <summary>Radius of the flagstone floor; the boss fight happens inside it.</summary>
        public const float FloorRadius = 3.4f;

        /// <summary>
        /// Builds the arena under a new "Environment" object and sets the scene fog and ambient.
        /// Returns the looping effects (flames, mist) a timeline should keep running with a control track.
        /// <paramref name="colliders"/>: keep colliders on floor, columns and walls so lasers can hit them (play scenes).
        /// </summary>
        public static List<GameObject> Build(Transform parent = null, bool colliders = false)
        {
            foreach (var sub in new[] { "", "/Materials", "/Textures", "/Meshes" }) CutsceneKit.EnsureFolder(Dir + sub);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.075f;
            RenderSettings.fogColor = FogColor;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.4f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.31f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.14f, 0.18f);
            RenderSettings.skybox = null;

            var env = new GameObject("Environment").transform;
            if (parent != null) env.SetParent(parent, false);
            var loops = new List<GameObject>();
            var rng = new System.Random(31);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            var earth = Lit("Earth", new Color(0.12f, 0.11f, 0.13f), 0.1f);
            var stone = Lit("ArenaStone", new Color(0.34f, 0.32f, 0.37f), 0.08f);
            var darkStone = Lit("ArenaStoneDark", new Color(0.2f, 0.19f, 0.23f), 0.06f);
            var flag = Lit("Flagstones", new Color(0.5f, 0.48f, 0.55f), 0.12f, FlagstoneTexture(Dir + "/Textures/flagstones.png"));
            var navy = Lit("BannerNavy", new Color(0.1f, 0.11f, 0.26f), 0.1f);
            var gold = Lit("BannerGold", new Color(0.7f, 0.52f, 0.22f), 0.5f);
            var crystal = Unlit("CurseCrystal", new Color(0.95f, 0.55f, 2.4f));
            var crystalCore = Lit("CurseCrystalBase", new Color(0.16f, 0.08f, 0.24f), 0.85f);
            var window = Unlit("SpireWindow", new Color(1.6f, 1.05f, 0.6f));

            // ---- Ground and the round court ----
            Block(env, "Ground", PrimitiveType.Cylinder, new Vector3(0f, -0.06f, 0f), new Vector3(60f, 0.05f, 60f), earth);
            Block(env, "Court", PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(FloorRadius * 2f, 0.03f, FloorRadius * 2f), flag);
            Block(env, "Court Plinth", PrimitiveType.Cylinder, new Vector3(0f, -0.02f, 0f), new Vector3(FloorRadius * 2f + 0.5f, 0.04f, FloorRadius * 2f + 0.5f), darkStone);
            // Curb of cut stones round the court, a few missing or knocked askew.
            const int curbCount = 40;
            for (int i = 0; i < curbCount; i++)
            {
                float a = i * 360f / curbCount;
                if (rng.NextDouble() < 0.12) continue;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.right;
                bool loose = rng.NextDouble() < 0.18;
                var rot = Quaternion.Euler(loose ? R(-12f, 12f) : 0f, a + 90f + (loose ? R(-15f, 15f) : 0f), loose ? R(-8f, 8f) : 0f);
                Block(env, "Curb", PrimitiveType.Cube, dir * (FloorRadius + 0.12f) + Vector3.up * 0.07f, new Vector3(0.5f, 0.14f, 0.22f), loose ? darkStone : stone, rot);
            }
            // The seal under the boss: engraved rings, a seven-point star and rune glyphs, glowing faint violet.
            var seal = Block(env, "Seal", PrimitiveType.Quad, new Vector3(0f, 0.065f, 0f), new Vector3(4.6f, 4.6f, 1f),
                Additive("Seal", SealTexture(Dir + "/Textures/seal.png"), new Color(Violet.r * 1.3f, Violet.g * 1.3f, Violet.b * 1.3f, SealAlpha)),
                Quaternion.Euler(90f, 0f, 0f));
            NoCollider(seal);
            seal.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ---- Colonnade round the back and sides (players' side, local -X, stays open) ----
            const float colRadius = 5.2f;
            float[] colAngles = { -132f, -106f, -80f, -54f, -18f, 18f, 54f, 80f, 106f, 132f };
            for (int i = 0; i < colAngles.Length; i++)
            {
                var pos = Quaternion.Euler(0f, colAngles[i], 0f) * Vector3.right * colRadius;
                bool gate = Mathf.Abs(colAngles[i]) < 20f;
                // Gate columns stand whole; the rest are snapped at different heights, one lies fallen.
                float h = gate ? 3.6f : (i == 2 || i == 8 ? 0f : R(1.0f, 3.2f));
                if (h <= 0f) { FallenColumn(env, pos, colAngles[i], stone, darkStone, R); continue; }
                Column(env, pos, h, !gate && rng.NextDouble() < 0.75, stone, darkStone, R);
            }
            // Gate: a lintel over the two whole columns behind the boss, torn academy banners hanging from it.
            var gateA = Quaternion.Euler(0f, -18f, 0f) * Vector3.right * colRadius;
            var gateB = Quaternion.Euler(0f, 18f, 0f) * Vector3.right * colRadius;
            var gateMid = (gateA + gateB) * 0.5f;
            float gateSpan = (gateB - gateA).magnitude;
            Block(env, "Gate Lintel", PrimitiveType.Cube, gateMid + Vector3.up * 3.82f, new Vector3(0.55f, 0.36f, gateSpan + 0.9f), stone);
            Block(env, "Gate Cornice", PrimitiveType.Cube, gateMid + Vector3.up * 4.06f, new Vector3(0.7f, 0.12f, gateSpan + 1.1f), darkStone);
            Block(env, "Gate Keystone", PrimitiveType.Cube, gateMid + new Vector3(-0.05f, 3.82f, 0f), new Vector3(0.6f, 0.5f, 0.42f), darkStone);
            foreach (var p in new[] { gateA, gateB })
            {
                var hang = p + new Vector3(-0.34f, 0f, -Mathf.Sign(p.z) * 0.15f);
                float len = p.z < 0f ? 2.1f : 1.45f; // the right one is torn shorter
                Block(env, "Banner", PrimitiveType.Cube, hang + Vector3.up * (3.64f - len * 0.5f), new Vector3(0.02f, len, 0.7f), navy, Quaternion.Euler(0f, 0f, p.z < 0f ? 2f : -4f));
                Block(env, "Banner Stripe", PrimitiveType.Cube, hang + new Vector3(-0.015f, 3.64f - len + 0.25f, 0f), new Vector3(0.012f, 0.07f, 0.7f), gold);
                Block(env, "Banner Star", PrimitiveType.Cube, hang + new Vector3(-0.015f, 3.1f, 0f), new Vector3(0.012f, 0.17f, 0.17f), gold, Quaternion.Euler(45f, 0f, 0f));
            }

            // ---- Ruined outer wall ----
            for (int i = 0; i < 14; i++)
            {
                float a = Mathf.Lerp(-150f, 150f, i / 13f) + R(-4f, 4f);
                var pos = Quaternion.Euler(0f, a, 0f) * Vector3.right * R(8.6f, 9.6f);
                float h = R(1.4f, 4.8f), w = R(1.6f, 2.6f);
                var rot = Quaternion.Euler(0f, a + 90f, 0f);
                Block(env, "Wall", PrimitiveType.Cube, pos + Vector3.up * h * 0.5f, new Vector3(w, h, 0.5f), darkStone, rot);
                // Broken top: a smaller block left standing on one end.
                if (rng.NextDouble() < 0.6)
                    Block(env, "Wall Top", PrimitiveType.Cube, pos + rot * new Vector3(w * R(-0.3f, 0.3f), h + 0.4f, 0f), new Vector3(w * 0.35f, 0.8f, 0.48f), darkStone, rot * Quaternion.Euler(0f, 0f, R(-6f, 6f)));
                // Window gap: a lit-from-behind slit reads as a hole against the fog.
                if (h > 3.2f)
                    Block(env, "Wall Arch", PrimitiveType.Cube, pos + rot * new Vector3(0f, h * 0.55f, -0.26f), new Vector3(0.36f, 1.0f, 0.02f), Lit("WallHollow", FogColor * 0.7f, 0f), rot);
            }

            // ---- Academy spires in the fog behind ----
            var spire = SpireMesh(Dir + "/Meshes/Spire.asset");
            var spireStone = Lit("SpireStone", new Color(0.15f, 0.14f, 0.19f), 0.05f);
            var spireRoof = Lit("SpireRoof", new Color(0.12f, 0.1f, 0.18f), 0.2f);
            (float angle, float dist, float h, float w)[] spires = { (-38f, 17f, 9f, 2.2f), (-12f, 21f, 13f, 2.8f), (6f, 19f, 11f, 2.0f), (28f, 16f, 8f, 2.4f), (52f, 20f, 10f, 2.0f), (-62f, 22f, 7f, 3.0f) };
            foreach (var (angle, dist, h, w) in spires)
            {
                var pos = Quaternion.Euler(0f, angle, 0f) * Vector3.right * dist;
                Block(env, "Spire", PrimitiveType.Cube, pos + Vector3.up * h * 0.5f, new Vector3(w, h, w), spireStone);
                var roof = new GameObject("Spire Roof", typeof(MeshFilter), typeof(MeshRenderer));
                roof.transform.SetParent(env, false);
                roof.transform.localPosition = pos + Vector3.up * h;
                roof.transform.localScale = new Vector3(w * 1.25f, w * 1.9f, w * 1.25f);
                roof.GetComponent<MeshFilter>().sharedMesh = spire;
                roof.GetComponent<MeshRenderer>().sharedMaterial = spireRoof;
                // A few windows still lit inside the school.
                int lit = 1 + (int)R(0f, 2.99f);
                // The cube is axis-aligned, so put the windows on whichever face looks most toward the court.
                var toward = -pos.normalized;
                var faceN = Mathf.Abs(toward.x) > Mathf.Abs(toward.z) ? new Vector3(Mathf.Sign(toward.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toward.z));
                var tangent = Vector3.Cross(Vector3.up, faceN);
                for (int k = 0; k < lit; k++)
                {
                    var wPos = pos + faceN * (w * 0.5f + 0.02f) + tangent * (R(-0.3f, 0.3f) * w) + Vector3.up * R(h * 0.4f, h * 0.85f);
                    // A quad shows its -Z side: point +Z away from the court.
                    NoCollider(Block(env, "Spire Window", PrimitiveType.Quad, wPos, new Vector3(0.28f, 0.5f, 1f), window, Quaternion.LookRotation(-faceN)));
                }
            }

            // ---- Cursed moon low over the gate, with a soft halo ----
            var moonDir = (Quaternion.Euler(0f, -8f, 0f) * Vector3.right * 15f) + Vector3.up * 6.5f;
            var moon = Block(env, "Moon", PrimitiveType.Quad, moonDir, new Vector3(2.2f, 2.2f, 1f),
                Additive("Moon", DiscTexture(Dir + "/Textures/moon.png", 0.82f, 0.08f), new Color(2.4f, 2.1f, 2.9f, 1f)), Quaternion.LookRotation(moonDir.normalized));
            var halo = Block(env, "Moon Halo", PrimitiveType.Quad, moonDir * 1.01f, new Vector3(7.5f, 7.5f, 1f),
                Additive("MoonHalo", DiscTexture(Dir + "/Textures/halo.png", 0f, 1f), new Color(0.7f, 0.5f, 1.1f, 0.35f)), Quaternion.LookRotation(moonDir.normalized));
            foreach (var go in new[] { moon, halo }) { NoCollider(go); go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off; }

            // ---- Curse crystals breaking through the court's edge ----
            float[] clusterAngles = { -140f, -106f, -72f, 70f, 104f, 138f }; // clear of the players' side (-X)
            for (int ci = 0; ci < clusterAngles.Length; ci++)
            {
                float a = clusterAngles[ci];
                var c = Quaternion.Euler(0f, a + R(-6f, 6f), 0f) * Vector3.right * R(3.7f, 4.4f);
                int n = 3 + (int)R(0f, 2.99f);
                for (int k = 0; k < n; k++)
                {
                    float h = R(0.3f, 0.85f) * (k == 0 ? 1.35f : 1f);
                    var rot = Quaternion.Euler(R(-28f, 28f), R(0f, 360f), R(-28f, 28f));
                    var p = c + new Vector3(R(-0.3f, 0.3f), 0f, R(-0.3f, 0.3f));
                    var shard = Block(env, "Curse Crystal", PrimitiveType.Cube, p + rot * Vector3.up * h * 0.35f, new Vector3(0.12f, h, 0.12f) * R(0.8f, 1.3f), crystal, rot * Quaternion.Euler(0f, 45f, 0f));
                    shard.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                Block(env, "Crystal Rubble", PrimitiveType.Cube, c + Vector3.up * 0.06f, new Vector3(0.5f, 0.14f, 0.42f), crystalCore, Quaternion.Euler(0f, R(0f, 360f), R(-6f, 6f)));
                var glow = CutsceneKit.Light(env, "Crystal Glow " + ci, LightType.Point, Violet, CrystalLight, 1.8f);
                glow.transform.localPosition = c + Vector3.up * 0.5f;
                glow.shadows = LightShadows.None;
            }

            // ---- Rubble the curse holds up in the air ----
            var floating = new GameObject("Floating Rubble").transform;
            floating.SetParent(env, false);
            for (int i = 0; i < 10; i++)
            {
                float a = R(-140f, 140f); // not over the players' side, where the play camera looks in from
                var p = Quaternion.Euler(0f, a, 0f) * Vector3.right * R(2.8f, 5.0f) + Vector3.up * R(0.7f, 1.9f);
                float s = R(0.06f, 0.16f);
                var rock = Block(floating, "Rubble", PrimitiveType.Cube, p, new Vector3(s, s * R(0.6f, 0.9f), s * R(1f, 1.5f)), i % 4 == 0 ? darkStone : stone, Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f)));
                NoCollider(rock);
                var lev = rock.AddComponent<Levitate>();
                lev.amplitude = R(0.04f, 0.1f);
                lev.period = R(3.5f, 6f);
                lev.spin = R(-12f, 12f);
                lev.phase = R(0f, 1f);
            }

            // ---- Braziers with violet curse-fire ----
            float[] brazierAngles = { -122f, -52f, 52f, 122f };
            for (int bi = 0; bi < brazierAngles.Length; bi++)
            {
                var p = Quaternion.Euler(0f, brazierAngles[bi], 0f) * Vector3.right * (FloorRadius + 0.55f);
                var b = new GameObject("Brazier " + bi).transform;
                b.SetParent(env, false);
                b.localPosition = p;
                Block(b, "Pedestal", PrimitiveType.Cylinder, new Vector3(0f, 0.38f, 0f), new Vector3(0.22f, 0.38f, 0.22f), darkStone);
                Block(b, "Pedestal Foot", PrimitiveType.Cube, new Vector3(0f, 0.06f, 0f), new Vector3(0.38f, 0.12f, 0.38f), stone);
                Block(b, "Bowl", PrimitiveType.Cylinder, new Vector3(0f, 0.8f, 0f), new Vector3(0.48f, 0.06f, 0.48f), stone);
                Block(b, "Bowl Rim", PrimitiveType.Cylinder, new Vector3(0f, 0.86f, 0f), new Vector3(0.52f, 0.025f, 0.52f), darkStone);
                var light = CutsceneKit.Light(b, "Fire Light", LightType.Point, new Color(0.7f, 0.42f, 1f), FireLight, 3.2f);
                light.transform.localPosition = new Vector3(0f, 1.15f, 0f);
                light.shadows = LightShadows.None;
                var flame = Flame(b, new Vector3(0f, 0.88f, 0f));
                loops.Add(flame);
            }

            loops.Add(GroundMist(env));

            if (!colliders)
                foreach (var c in env.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            else
                foreach (var c in env.GetComponentsInChildren<Collider>(true))
                    if (c is CapsuleCollider && c.transform.localScale.y < 0.2f) Object.DestroyImmediate(c); // flat discs would balloon
            return loops;
        }

        /// <summary>
        /// Lets a cutscene animate the arena with its beats: adds an Animator to the scene's "Environment" and an
        /// animation track playing a clip that <paramref name="fill"/> keys (paths "Seal", "Brazier i/Fire Light",
        /// "Crystal Glow i"; see <see cref="SealCurve"/> and friends).
        /// </summary>
        public static void AddTimelineClip(UnityEngine.Timeline.TimelineAsset timeline, UnityEngine.Timeline.TrackAsset group, UnityEngine.Playables.PlayableDirector director,
            string clipName, float end, System.Action<AnimationClip> fill)
        {
            var env = GameObject.Find("Environment");
            if (!env.TryGetComponent<Animator>(out var animator)) animator = env.AddComponent<Animator>();
            var clip = new AnimationClip { name = clipName };
            fill(clip);
            CutsceneKit.AddAnimation(timeline, group, director, "Arena", animator, CutsceneKit.SaveClip(clip), end);
        }

        /// <summary>Keys the seal's glow alpha.</summary>
        public static void SealCurve(AnimationClip clip, AnimationCurve alpha) =>
            CutsceneKit.Bind(clip, "Seal", typeof(MeshRenderer), "material._BaseColor.a", alpha);

        /// <summary>Keys every brazier's fire light with the same intensity curve.</summary>
        public static void FireCurve(AnimationClip clip, AnimationCurve intensity)
        {
            for (int i = 0; i < BrazierCount; i++) CutsceneKit.Bind(clip, $"Brazier {i}/Fire Light", typeof(Light), "m_Intensity", intensity);
        }

        /// <summary>Keys every curse crystal's glow light with the same intensity curve.</summary>
        public static void CrystalCurve(AnimationClip clip, AnimationCurve intensity)
        {
            for (int i = 0; i < CrystalClusterCount; i++) CutsceneKit.Bind(clip, $"Crystal Glow {i}", typeof(Light), "m_Intensity", intensity);
        }

        // ------------------------------------------------------------------ pieces

        static void Column(Transform env, Vector3 pos, float h, bool broken, Material stone, Material dark, System.Func<float, float, float> R)
        {
            Block(env, "Column Base", PrimitiveType.Cube, pos + Vector3.up * 0.12f, new Vector3(0.78f, 0.24f, 0.78f), dark);
            Block(env, "Column Plinth", PrimitiveType.Cylinder, pos + Vector3.up * 0.3f, new Vector3(0.62f, 0.06f, 0.62f), stone);
            float shaft = h - 0.36f;
            Block(env, "Column", PrimitiveType.Cylinder, pos + Vector3.up * (0.36f + shaft * 0.5f), new Vector3(0.46f, shaft * 0.5f, 0.46f), stone);
            if (broken)
            {
                // Jagged break: two tilted chunks on the stump, the fallen drum at its foot.
                Block(env, "Column Break", PrimitiveType.Cube, pos + Vector3.up * (h + 0.02f), new Vector3(0.36f, 0.2f, 0.3f), stone, Quaternion.Euler(R(-25f, 25f), R(0f, 360f), R(-25f, 25f)));
                var foot = pos + Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.right * R(0.55f, 0.9f);
                Block(env, "Column Drum", PrimitiveType.Cylinder, foot + Vector3.up * 0.2f, new Vector3(0.42f, 0.2f, 0.42f), stone, Quaternion.Euler(R(70f, 95f), R(0f, 360f), 0f));
            }
            else
            {
                Block(env, "Column Capital", PrimitiveType.Cube, pos + Vector3.up * (h + 0.08f), new Vector3(0.7f, 0.16f, 0.7f), dark);
            }
        }

        static void FallenColumn(Transform env, Vector3 pos, float angle, Material stone, Material dark, System.Func<float, float, float> R)
        {
            Block(env, "Column Base", PrimitiveType.Cube, pos + Vector3.up * 0.12f, new Vector3(0.78f, 0.24f, 0.78f), dark);
            Block(env, "Column Stump", PrimitiveType.Cylinder, pos + Vector3.up * 0.42f, new Vector3(0.46f, 0.18f, 0.46f), stone);
            // Lying along the ring, broken in two.
            var along = Quaternion.Euler(0f, angle + 90f + R(-20f, 20f), 0f);
            Block(env, "Fallen Column", PrimitiveType.Cylinder, pos + along * new Vector3(0f, 0.23f, 1.3f), new Vector3(0.44f, 0.9f, 0.44f), stone, along * Quaternion.Euler(90f, 0f, 0f));
            Block(env, "Fallen Column", PrimitiveType.Cylinder, pos + along * new Vector3(0.12f, 0.23f, 2.75f), new Vector3(0.44f, 0.5f, 0.44f), stone, along * Quaternion.Euler(90f, 12f, 0f));
            Block(env, "Fallen Capital", PrimitiveType.Cube, pos + along * new Vector3(0.25f, 0.2f, 3.45f), new Vector3(0.7f, 0.4f, 0.7f), dark, along * Quaternion.Euler(0f, 20f, 8f));
        }

        static GameObject Flame(Transform parent, Vector3 position)
        {
            var go = new GameObject("Curse Flame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 1f, 1f), new Color(0.75f, 0.45f, 1f, 1f));
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = 70f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.12f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 2.2f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.1f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.45f, 1f), 0.4f), new GradientColorKey(new Color(0.35f, 0.15f, 0.7f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Additive("Flame", DiscTexture(Dir + "/Textures/softdot.png", 0f, 1f), new Color(1.6f, 1.4f, 1.9f, 1f));
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Slow violet-grey mist drifting low over the court.</summary>
        static GameObject GroundMist(Transform env)
        {
            var go = new GameObject("Ground Mist");
            go.transform.SetParent(env, false);
            go.transform.localPosition = new Vector3(0.8f, 0.15f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.36f, 0.55f, 0.1f), new Color(0.3f, 0.26f, 0.4f, 0.07f));
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = 9f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(11f, 0.2f, 12f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.02f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0.01f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).defaultParticleMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.alignment = ParticleSystemRenderSpace.View;
            return go;
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Block(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static void NoCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }

        static Material Lit(string name, Color color, float smoothness, Texture texture = null)
        {
            var path = $"{Dir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            if (texture != null) m.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color color)
        {
            var path = $"{Dir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Additive(string name, Texture texture, Color color)
        {
            var path = $"{Dir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Four-sided spire roof, apex at y = 1, base a unit square at y = 0.</summary>
        static Mesh SpireMesh(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var apex = new Vector3(0f, 1f, 0f);
            Vector3[] b = { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int s = verts.Count;
                verts.Add(b[i]); verts.Add(apex); verts.Add(b[(i + 1) % 4]);
                tris.AddRange(new[] { s, s + 1, s + 2 });
            }
            var mesh = new Mesh { name = "Spire" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------------ procedural textures

        static Texture2D SaveTexture(Texture2D tex, string path, bool alpha, TextureWrapMode wrap)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = alpha;
            importer.wrapMode = wrap;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Round soft disc: solid to <paramref name="core"/>, fading out over <paramref name="soft"/> (both 0..1 of the radius).</summary>
        static Texture2D DiscTexture(string path, float core, float soft)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = new Vector2(x - (n - 1) * 0.5f, y - (n - 1) * 0.5f).magnitude / (n * 0.5f);
                float a = r <= core ? 1f : Mathf.Clamp01(1f - (r - core) / Mathf.Max(0.001f, soft));
                a = soft >= 1f ? Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f) : a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return SaveTexture(tex, path, true, TextureWrapMode.Clamp);
        }

        /// <summary>Round court paving: concentric rings of flagstones with mortar joints, per-stone tone and grit.</summary>
        static Texture2D FlagstoneTexture(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int n = 1024;
            float[] rings = { 0f, 0.13f, 0.27f, 0.4f, 0.53f, 0.66f, 0.79f, 0.9f, 1.0f };
            var rng = new System.Random(5);
            var counts = new int[rings.Length - 1];
            var offsets = new float[rings.Length - 1];
            for (int i = 0; i < counts.Length; i++) { counts[i] = i == 0 ? 1 : 6 + i * 5; offsets[i] = (float)rng.NextDouble(); }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float th = (Mathf.Atan2(v, u) / (Mathf.PI * 2f) + 1f) % 1f;
                int ring = 0;
                while (ring < counts.Length - 1 && r > rings[ring + 1]) ring++;
                float edge = Mathf.Min(r - rings[ring], rings[ring + 1] - r);
                int cell = 0;
                if (counts[ring] > 1)
                {
                    float f = (th + offsets[ring]) * counts[ring];
                    cell = Mathf.FloorToInt(f) % counts[ring];
                    float frac = f - Mathf.Floor(f);
                    edge = Mathf.Min(edge, Mathf.Min(frac, 1f - frac) * Mathf.PI * 2f * r / counts[ring]);
                }
                if (ring == 0) edge = rings[1] - r;
                uint h = (uint)(ring * 7919 + cell * 104729) * 2654435761u;
                float tone = 0.78f + (h % 1000) / 1000f * 0.32f;
                float grit = Mathf.PerlinNoise(u * 40f, v * 40f) * 0.18f + Mathf.PerlinNoise(u * 9f + 3f, v * 9f) * 0.14f;
                float c = tone * (0.86f + grit);
                // Mortar joint, then a darkened bevel just inside each stone.
                float joint = 0.0045f;
                if (edge < joint) c = 0.28f;
                else if (edge < joint * 3.2f) c *= Mathf.Lerp(0.7f, 1f, (edge - joint) / (joint * 2.2f));
                // Wear: the centre is darker where the curse sits.
                c *= Mathf.Lerp(0.8f, 1f, Mathf.SmoothStep(0f, 1f, r / 0.45f));
                pixels[y * n + x] = new Color(c * 0.95f, c * 0.92f, c, 1f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return SaveTexture(tex, path, false, TextureWrapMode.Clamp);
        }

        /// <summary>Engraved seal: double outer ring with a band of rune glyphs, a seven-point star, an inner ring and hub.</summary>
        static Texture2D SealTexture(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int n = 1024;
            const int points = 7, glyphs = 42;
            var star = new Vector2[points];
            for (int i = 0; i < points; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI * 2f / points;
                star[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.8f;
            }
            float Seg(Vector2 p, Vector2 a, Vector2 b)
            {
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return (p - (a + ab * t)).magnitude;
            }
            float Line(float d, float w) => Mathf.Clamp01(1f - (d - w) / (w * 1.5f));
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float r = p.magnitude;
                float a = 0f;
                a = Mathf.Max(a, Line(Mathf.Abs(r - 0.97f), 0.006f));
                a = Mathf.Max(a, Line(Mathf.Abs(r - 0.82f), 0.004f));
                a = Mathf.Max(a, Line(Mathf.Abs(r - 0.42f), 0.005f) * 0.9f);
                a = Mathf.Max(a, Line(Mathf.Abs(r - 0.12f), 0.006f));
                // {7/3} star: each point joined to the one three steps on.
                float ds = 10f;
                for (int i = 0; i < points; i++) ds = Mathf.Min(ds, Seg(p, star[i], star[(i + 3) % points]));
                a = Mathf.Max(a, Line(ds, 0.003f) * 0.85f);
                // Glyph band between the outer rings.
                if (r > 0.835f && r < 0.955f)
                {
                    float th = (Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 1f) % 1f * glyphs;
                    int cell = Mathf.FloorToInt(th);
                    float lu = (th - cell - 0.5f) * (Mathf.PI * 2f * r / glyphs) / 0.1f; // tangent, ~-0.6..0.6
                    float lv = (r - 0.895f) / 0.05f;                                     // radial, -1..1
                    uint h = (uint)(cell * 2654435761u + 97u);
                    float g = 0f;
                    const float w = 0.11f;
                    if ((h & 1) != 0) g = Mathf.Max(g, Line(Mathf.Abs(lu), w) * (Mathf.Abs(lv) < 0.85f ? 1f : 0f));
                    if ((h & 2) != 0) g = Mathf.Max(g, Line(Mathf.Abs(lv - 0.45f), w) * (Mathf.Abs(lu) < 0.4f ? 1f : 0f));
                    if ((h & 4) != 0) g = Mathf.Max(g, Line(Mathf.Abs(lu - lv * 0.4f), w) * (lv > -0.1f && lv < 0.85f ? 1f : 0f));
                    if ((h & 8) != 0) g = Mathf.Max(g, Line(Mathf.Abs(lu + lv * 0.4f), w) * (lv < 0.1f && lv > -0.85f ? 1f : 0f));
                    if ((h & 16) != 0) g = Mathf.Max(g, Line(Mathf.Abs(new Vector2(lu, lv + 0.35f).magnitude - 0.25f), w * 0.8f));
                    if ((h & 3) == 0) g = Mathf.Max(g, Line(Mathf.Abs(lu), w));
                    a = Mathf.Max(a, g * 0.95f);
                }
                // Small rings on the star points.
                for (int i = 0; i < points; i++) a = Mathf.Max(a, Line(Mathf.Abs((p - star[i]).magnitude - 0.035f), 0.004f));
                // Faint glow wash so the lines sit in light, not on black.
                a = Mathf.Max(a, Mathf.Clamp01(1f - r) * 0.12f);
                if (r > 1f) a = 0f;
                pixels[y * n + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return SaveTexture(tex, path, true, TextureWrapMode.Clamp);
        }
    }
}
