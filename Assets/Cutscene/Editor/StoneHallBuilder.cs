using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static AiHackathon.Cutscene.Editor.CutsceneKit;
using static AiHackathon.Cutscene.Editor.PeriodSceneKit;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Period 2's room: the academy's old stone gallery, deeper than the rune classroom so there is floor to hunt on.
    /// Flagstones and ashlar walls (textures drawn here), two rows of columns (one broken) and rubble to hide behind,
    /// plinths with old statues along the sides, tall arched windows on the far wall onto a night sky
    /// (<see cref="SkyTexture"/>, generated art when present) and the class's chalkboard behind the students.
    /// Players stand at the origin facing +Z as in every period.
    /// </summary>
    internal static class StoneHallBuilder
    {
        internal const float HalfWidth = 7f;
        internal const float Front = -4.6f;    // wall behind the students
        internal const float Back = 11.5f;     // window wall
        internal const float WallHeight = 6.4f;
        internal static readonly Vector3[] Columns =
        {
            new Vector3(-3.3f, 0f, 4.2f), new Vector3(3.3f, 0f, 4.2f),
            new Vector3(-3.3f, 0f, 8.4f), new Vector3(3.3f, 0f, 8.4f),
        };
        /// <summary>Generated window art (night sky over the academy); a drawn sky stands in when it is missing.</summary>
        internal const string SkyTexture = "Assets/Cutscene/Period2/Textures/night_sky.png";

        internal static Classroom Build(string textureDir)
        {
            var fog = new Color(0.5f, 0.48f, 0.46f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.028f;
            RenderSettings.fogColor = fog;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.7f, 0.66f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.45f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.22f, 0.18f);
            RenderSettings.skybox = null;

            var env = new GameObject("Stone Hall").transform;
            var flag = TexturedLit("HallFlagstone", FlagstoneTexture(textureDir + "/hall_flagstone.png"), new Color(0.78f, 0.74f, 0.7f), 0.25f, new Vector2(HalfWidth, (Back - Front) / 2f));
            var ashlar = TexturedLit("HallAshlar", AshlarTexture(textureDir + "/hall_ashlar.png"), new Color(0.82f, 0.78f, 0.72f), 0.08f, new Vector2(7f, 3.2f));
            var ashlarSide = TexturedLit("HallAshlarSide", AssetDatabase.LoadAssetAtPath<Texture2D>(textureDir + "/hall_ashlar.png"), new Color(0.82f, 0.78f, 0.72f), 0.08f, new Vector2(8f, 3.2f));
            var stone = LitMaterial("HallStone", new Color(0.55f, 0.52f, 0.48f), 0.12f);
            var darkStone = LitMaterial("HallStoneDark", new Color(0.3f, 0.28f, 0.27f), 0.1f);
            var dark = LitMaterial("DarkWood", new Color(0.22f, 0.14f, 0.09f), 0.25f);
            var navy = LitMaterial("Navy", new Color(0.12f, 0.13f, 0.3f), 0.1f);
            var gold = LitMaterial("GoldTrim", new Color(0.85f, 0.65f, 0.28f), 0.6f);
            var runner = LitMaterial("HallRunner", new Color(0.32f, 0.08f, 0.1f), 0.1f);

            float depth = Back - Front, midZ = (Back + Front) / 2f;
            Block(env, "Floor", PrimitiveType.Cube, new Vector3(0f, -0.05f, midZ), new Vector3(HalfWidth * 2f, 0.1f, depth), flag);
            Block(env, "Ceiling", PrimitiveType.Cube, new Vector3(0f, WallHeight + 0.05f, midZ), new Vector3(HalfWidth * 2f, 0.1f, depth), darkStone);
            Block(env, "Wall Back", PrimitiveType.Cube, new Vector3(0f, WallHeight / 2f, Back + 0.1f), new Vector3(HalfWidth * 2f, WallHeight, 0.2f), ashlar);
            Block(env, "Wall Left", PrimitiveType.Cube, new Vector3(-HalfWidth - 0.1f, WallHeight / 2f, midZ), new Vector3(0.2f, WallHeight, depth), ashlarSide);
            Block(env, "Wall Right", PrimitiveType.Cube, new Vector3(HalfWidth + 0.1f, WallHeight / 2f, midZ), new Vector3(0.2f, WallHeight, depth), ashlarSide);
            Block(env, "Wall Behind", PrimitiveType.Cube, new Vector3(0f, WallHeight / 2f, Front - 0.1f), new Vector3(HalfWidth * 2f, WallHeight, 0.2f), ashlar);

            // Plinth course along the foot of every wall, a cornice along the top.
            Block(env, "Plinth Course Back", PrimitiveType.Cube, new Vector3(0f, 0.25f, Back - 0.05f), new Vector3(HalfWidth * 2f, 0.5f, 0.12f), darkStone);
            Block(env, "Plinth Course Left", PrimitiveType.Cube, new Vector3(-HalfWidth + 0.05f, 0.25f, midZ), new Vector3(0.12f, 0.5f, depth), darkStone);
            Block(env, "Plinth Course Right", PrimitiveType.Cube, new Vector3(HalfWidth - 0.05f, 0.25f, midZ), new Vector3(0.12f, 0.5f, depth), darkStone);
            Block(env, "Plinth Course Behind", PrimitiveType.Cube, new Vector3(0f, 0.25f, Front + 0.05f), new Vector3(HalfWidth * 2f, 0.5f, 0.12f), darkStone);
            Block(env, "Cornice Back", PrimitiveType.Cube, new Vector3(0f, WallHeight - 0.3f, Back - 0.05f), new Vector3(HalfWidth * 2f, 0.22f, 0.22f), stone);
            Block(env, "Cornice Left", PrimitiveType.Cube, new Vector3(-HalfWidth + 0.05f, WallHeight - 0.3f, midZ), new Vector3(0.22f, 0.22f, depth), stone);
            Block(env, "Cornice Right", PrimitiveType.Cube, new Vector3(HalfWidth - 0.05f, WallHeight - 0.3f, midZ), new Vector3(0.22f, 0.22f, depth), stone);

            // A red runner down the middle of the hall, gold-edged.
            Block(env, "Runner Edge", PrimitiveType.Cube, new Vector3(0f, 0.004f, 3.4f), new Vector3(1.9f, 0.004f, 14.6f), gold);
            Block(env, "Runner", PrimitiveType.Cube, new Vector3(0f, 0.008f, 3.4f), new Vector3(1.75f, 0.004f, 14.5f), runner);

            // The far wall: three tall arched windows onto the night (daylight before the blackout).
            var sky = AssetDatabase.LoadAssetAtPath<Texture2D>(SkyTexture);
            if (sky == null) sky = DrawnSky(textureDir + "/night_sky_drawn.png");
            var windows = new GameObject("Windows").transform;
            windows.SetParent(env);
            int pane = 0;
            foreach (float x in new[] { -3.6f, 0f, 3.6f })
            {
                float w = x == 0f ? 2.1f : 1.7f, h = x == 0f ? 3.6f : 3.1f, y = 1.4f + h / 2f;
                // Each pane shows its own slice of the sky, so the three read as one view. (A material each: the
                // timeline animates their colour, which would clobber a property block.)
                var glass = TexturedUnlit("HallWindow" + pane, sky, new Color(1.9f, 1.75f, 1.45f));
                float u0 = (x + 4.5f - w / 2f) / 9f, u1 = (x + 4.5f + w / 2f) / 9f;
                glass.SetTextureScale("_BaseMap", new Vector2(u1 - u0, h / 5.4f));
                glass.SetTextureOffset("_BaseMap", new Vector2(u0, (y - h / 2f - 1.4f) / 5.4f));
                Block(windows, "Window " + pane, PrimitiveType.Quad, new Vector3(x, y, Back - 0.01f), new Vector3(w, h, 1f), glass);
                // The arch above shows the top of the sky (the disc maps the whole texture, so zoom into it).
                var archGlass = TexturedUnlit("HallWindowArch" + pane, sky, new Color(1.9f, 1.75f, 1.45f));
                archGlass.SetTextureScale("_BaseMap", new Vector2(0.18f, 0.18f));
                archGlass.SetTextureOffset("_BaseMap", new Vector2(0.41f + x / 30f, 0.8f));
                Block(windows, "Arch " + pane, PrimitiveType.Cylinder, new Vector3(x, y + h / 2f, Back - 0.012f), new Vector3(w, 0.01f, w), archGlass, Quaternion.Euler(90f, 0f, 0f));
                Block(env, "Mullion V", PrimitiveType.Cube, new Vector3(x, y + 0.2f, Back - 0.05f), new Vector3(0.07f, h + 0.5f, 0.05f), darkStone);
                for (int k = 1; k <= 2; k++)
                    Block(env, "Mullion H", PrimitiveType.Cube, new Vector3(x, 1.4f + h * k / 3f, Back - 0.05f), new Vector3(w, 0.06f, 0.05f), darkStone);
                Block(env, "Sill", PrimitiveType.Cube, new Vector3(x, 1.35f, Back - 0.15f), new Vector3(w + 0.3f, 0.12f, 0.3f), stone);
                Block(env, "Jamb L", PrimitiveType.Cube, new Vector3(x - w / 2f - 0.1f, y + 0.3f, Back - 0.08f), new Vector3(0.2f, h + 0.6f, 0.16f), stone);
                Block(env, "Jamb R", PrimitiveType.Cube, new Vector3(x + w / 2f + 0.1f, y + 0.3f, Back - 0.08f), new Vector3(0.2f, h + 0.6f, 0.16f), stone);
                pane++;
            }

            // Two rows of columns down the hall; the back right one is broken, its drum lying across the floor.
            for (int i = 0; i < Columns.Length; i++)
            {
                var p = Columns[i];
                bool broken = i == 3;
                float h = broken ? 2.1f : WallHeight;
                Block(env, "Column", PrimitiveType.Cylinder, p + Vector3.up * (h / 2f), new Vector3(0.62f, h / 2f, 0.62f), stone);
                Block(env, "Column Base", PrimitiveType.Cube, p + Vector3.up * 0.15f, new Vector3(0.9f, 0.3f, 0.9f), darkStone);
                Block(env, "Column Base Step", PrimitiveType.Cube, p + Vector3.up * 0.38f, new Vector3(0.78f, 0.16f, 0.78f), stone);
                if (!broken)
                    Block(env, "Column Capital", PrimitiveType.Cube, p + Vector3.up * (WallHeight - 0.55f), new Vector3(0.9f, 0.3f, 0.9f), darkStone);
                else
                {
                    Block(env, "Fallen Drum", PrimitiveType.Cylinder, p + new Vector3(-1.1f, 0.3f, -0.9f), new Vector3(0.6f, 0.75f, 0.6f), stone, Quaternion.Euler(0f, 35f, 90f));
                    Block(env, "Broken Top", PrimitiveType.Cube, p + new Vector3(0.05f, 2.15f, 0f), new Vector3(0.5f, 0.18f, 0.45f), stone, Quaternion.Euler(8f, 20f, -12f));
                }
            }

            // Rubble heaps: chunks and slabs to hide behind.
            Rubble(env, new Vector3(-5.1f, 0f, 9.6f), 1.1f, darkStone, stone, 11);
            Rubble(env, new Vector3(4.9f, 0f, 3.1f), 0.8f, darkStone, stone, 23);
            Rubble(env, new Vector3(1.8f, 0f, 10.2f), 0.6f, darkStone, stone, 37);

            // Statues on plinths along the side walls, banners between them.
            foreach (var (x, z) in new[] { (-6.3f, 2.4f), (6.3f, 6.6f), (-6.3f, 6.6f), (6.3f, 10.0f) })
            {
                Block(env, "Statue Plinth", PrimitiveType.Cube, new Vector3(x, 0.45f, z), new Vector3(0.9f, 0.9f, 0.9f), darkStone);
                Block(env, "Statue Body", PrimitiveType.Capsule, new Vector3(x, 1.65f, z), new Vector3(0.55f, 0.75f, 0.45f), stone);
                Block(env, "Statue Head", PrimitiveType.Sphere, new Vector3(x, 2.55f, z), new Vector3(0.36f, 0.4f, 0.36f), stone);
                Block(env, "Statue Hood", PrimitiveType.Sphere, new Vector3(x, 2.62f, z + 0.03f), new Vector3(0.44f, 0.44f, 0.44f), darkStone);
            }
            foreach (var (x, z) in new[] { (-6.85f, 4.5f), (6.85f, 4.5f), (-6.85f, 8.4f), (6.85f, 8.4f) })
            {
                var rot = Quaternion.Euler(0f, x < 0f ? 90f : -90f, 0f);
                Block(env, "Banner", PrimitiveType.Cube, new Vector3(x, 3.6f, z), new Vector3(0.9f, 2.4f, 0.02f), navy, rot);
                Block(env, "Banner Stripe", PrimitiveType.Cube, new Vector3(x + (x < 0f ? 0.015f : -0.015f), 2.6f, z), new Vector3(0.9f, 0.08f, 0.01f), gold, rot);
                Block(env, "Banner Star", PrimitiveType.Cube, new Vector3(x + (x < 0f ? 0.015f : -0.015f), 3.9f, z), new Vector3(0.22f, 0.22f, 0.01f), gold, rot * Quaternion.Euler(0f, 0f, 45f));
            }

            // Behind the students: the chalkboard they were taught at, banners either side.
            var board = LitMaterial("Chalkboard", new Color(0.07f, 0.13f, 0.1f), 0.1f);
            var chalk = Unlit("Chalk", new Color(0.82f, 0.84f, 0.8f));
            Block(env, "Board Frame", PrimitiveType.Cube, new Vector3(0f, 1.85f, Front + 0.05f), new Vector3(4.0f, 1.75f, 0.06f), dark);
            Block(env, "Board", PrimitiveType.Cube, new Vector3(0f, 1.85f, Front + 0.09f), new Vector3(3.8f, 1.55f, 0.04f), board);
            Block(env, "Board Ledge", PrimitiveType.Cube, new Vector3(0f, 1.02f, Front + 0.13f), new Vector3(3.9f, 0.05f, 0.12f), dark);
            // Chalk: a lantern (a ring of rays round a dot) and notes.
            for (int i = 0; i < 8; i++)
                Block(env, "Chalk Ray", PrimitiveType.Cube, new Vector3(-1.0f, 1.9f, Front + 0.115f), new Vector3(0.03f, 0.55f, 0.005f), chalk, Quaternion.Euler(0f, 0f, i * 22.5f));
            for (int i = 0; i < 5; i++)
                Block(env, "Chalk Note", PrimitiveType.Cube, new Vector3(0.45f + (i % 2) * 0.12f, 2.3f - i * 0.2f, Front + 0.115f), new Vector3(1.2f - i * 0.15f, 0.02f, 0.005f), chalk);
            foreach (float x in new[] { -3.4f, 3.4f })
            {
                Block(env, "Banner Behind", PrimitiveType.Cube, new Vector3(x, 3.0f, Front + 0.04f), new Vector3(0.75f, 2.0f, 0.02f), navy);
                Block(env, "Banner Behind Stripe", PrimitiveType.Cube, new Vector3(x, 2.2f, Front + 0.055f), new Vector3(0.75f, 0.08f, 0.01f), gold);
                Block(env, "Banner Behind Star", PrimitiveType.Cube, new Vector3(x, 3.2f, Front + 0.055f), new Vector3(0.2f, 0.2f, 0.01f), gold, Quaternion.Euler(0f, 0f, 45f));
            }

            // Sun shafts from the windows, slanting down the hall toward the students.
            var shaftMat = Additive("SunShaft", ShaftTexture(textureDir + "/shaft.png"), new Color(1f, 0.9f, 0.72f, 0.07f));
            var shafts = new GameObject("Sun Shafts").transform;
            shafts.SetParent(env);
            foreach (float x in new[] { -3.6f, 0f, 3.6f })
            for (int k = 0; k < 2; k++)
            {
                var q = Block(shafts, "Shaft", PrimitiveType.Quad, new Vector3(x * 0.9f, 2.6f, 8.6f), new Vector3(1.6f, 7.5f, 1f), shaftMat,
                    Quaternion.Euler(-58f, 180f, 0f) * Quaternion.Euler(0f, 90f * k, 0f));
                q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // Shafts and flat runner strips need no colliders, and a flat cylinder's capsule would balloon.
            foreach (var col in env.GetComponentsInChildren<Collider>())
                if (col.name == "Shaft" || col.name.StartsWith("Runner") || col.name.StartsWith("Window") || col.name.StartsWith("Arch")
                    || (col is CapsuleCollider && col.transform.localScale.y < 0.2f))
                    Object.DestroyImmediate(col);
            return new Classroom { root = env, windows = windows, shafts = shafts, fog = fog };
        }

        static void Rubble(Transform parent, Vector3 at, float size, Material a, Material b, int seed)
        {
            var state = Random.state;
            Random.InitState(seed);
            var heap = new GameObject("Rubble").transform;
            heap.SetParent(parent);
            heap.position = at;
            for (int i = 0; i < 9; i++)
            {
                float s = size * Random.Range(0.25f, 0.6f) * (i < 3 ? 1.4f : 1f);
                var off = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * size * 0.8f;
                var scale = new Vector3(s * Random.Range(0.8f, 1.6f), s * Random.Range(0.5f, 1f), s * Random.Range(0.8f, 1.4f));
                var rot = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f));
                Block(heap, "Chunk", PrimitiveType.Cube, at + off + Vector3.up * (scale.y * 0.4f), scale, i % 3 == 0 ? b : a, rot);
            }
            Random.state = state;
        }

        static Material TexturedLit(string name, Texture2D tex, Color tint, float smoothness, Vector2 tiling)
        {
            var m = LitMaterial(name, tint, smoothness);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material TexturedUnlit(string name, Texture2D tex, Color color)
        {
            var m = Unlit(name, color);
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ drawn textures

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        // Tileable value noise on a period-p lattice.
        static float TileNoise(float u, float v, int p, int s)
        {
            float x = u * p, y = v * p;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float H(int a, int b) => Hash(((a % p) + p) % p, ((b % p) + p) % p, s);
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx), fy);
        }

        static float Fbm(float u, float v, int s) => TileNoise(u, v, 8, s) * 0.5f + TileNoise(u, v, 16, s + 1) * 0.3f + TileNoise(u, v, 32, s + 2) * 0.2f;

        /// <summary>Square-ish flagstones in staggered rows with dark joints; one tile = 2 m of floor.</summary>
        static Texture2D FlagstoneTexture(string path)
        {
            const int n = 512;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true);
            var px = new Color[n * n];
            const int rows = 4;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                int row = Mathf.FloorToInt(v * rows);
                float shift = (row % 2) * 0.5f / 3f + Hash(row, 0, 5) * 0.1f;
                float cu = (u + shift) * 3f;
                int col = Mathf.FloorToInt(cu);
                float fu = cu - col, fv = v * rows - row;
                float edge = Mathf.Min(Mathf.Min(fu, 1f - fu) * 3f / rows, Mathf.Min(fv, 1f - fv));
                float wobble = (Fbm(u, v, 9) - 0.5f) * 0.012f;
                float joint = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edge + wobble - 0.008f) / 0.012f));
                float tone = 0.55f + Hash(col % 3, row, 3) * 0.25f;
                float grain = Fbm(u, v, 17) * 0.35f + 0.8f;
                float c = tone * grain * Mathf.Lerp(0.25f, 1f, joint);
                var col0 = new Color(c * 1.0f, c * 0.96f, c * 0.9f);
                px[y * n + x] = col0;
            }
            return Save(tex, px, path);
        }

        /// <summary>Courses of dressed blocks of mixed length with recessed joints.</summary>
        static Texture2D AshlarTexture(string path)
        {
            const int n = 512;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true);
            var px = new Color[n * n];
            const int rows = 6;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                int row = Mathf.FloorToInt(v * rows);
                float fv = v * rows - row;
                // Blocks per row: 3 or 4, offset per row; tile across u.
                int count = row % 2 == 0 ? 3 : 4;
                float cu = (u + Hash(row, 1, 7) * 0.3f) * count;
                int col = Mathf.FloorToInt(cu);
                float fu = cu - col;
                float edge = Mathf.Min(Mathf.Min(fu, 1f - fu) / count * rows * 0.6f, Mathf.Min(fv, 1f - fv));
                float joint = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edge - 0.03f) / 0.04f));
                float tone = 0.6f + Hash(col % count, row, 11) * 0.22f;
                float grain = Fbm(u, v, 29) * 0.3f + 0.82f;
                float c = tone * grain * Mathf.Lerp(0.35f, 1f, joint);
                px[y * n + x] = new Color(c * 1.0f, c * 0.95f, c * 0.88f);
            }
            return Save(tex, px, path);
        }

        /// <summary>Stand-in night sky for the windows: deep blue gradient, stars, a moon and spires.</summary>
        static Texture2D DrawnSky(string path)
        {
            const int w = 512, h = 320;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, true);
            var px = new Color[w * h];
            var moon = new Vector2(0.68f, 0.72f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                var c = Color.Lerp(new Color(0.16f, 0.22f, 0.42f), new Color(0.03f, 0.05f, 0.14f), Mathf.Pow(v, 0.7f));
                if (Hash(x, y, 77) > 0.9965f) c += Color.white * Hash(x, y, 78) * 0.9f;
                float d = Vector2.Distance(new Vector2(u * w / h, v), new Vector2(moon.x * w / h, moon.y));
                c += new Color(0.9f, 0.95f, 1f) * (d < 0.07f ? 1f : Mathf.Exp(-(d - 0.07f) * 14f) * 0.35f);
                // Spires and roofs of the academy along the bottom.
                float roof = 0.18f + 0.06f * Mathf.Abs(Mathf.Sin(u * 9f)) + (Mathf.Repeat(u * 5f, 1f) < 0.06f ? 0.25f * (1f - Mathf.Abs(Mathf.Repeat(u * 5f, 1f) - 0.03f) / 0.03f) : 0f);
                if (v < roof) c = new Color(0.02f, 0.025f, 0.05f);
                px[y * w + x] = c;
            }
            return Save(tex, px, path);
        }

        static Texture2D Save(Texture2D tex, Color[] px, string path)
        {
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
