using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static AiHackathon.Cutscene.Editor.CutsceneKit;

namespace AiHackathon.Game.Editor
{
    /// <summary>
    /// Turns every Tripo rune stone under Assets/TripoModels/rune-stone-* into a <see cref="RuneDefinition"/>
    /// (Assets/Game/Runes) and builds the shared stone prefab that shows them (Assets/Game/Prefabs/RuneStone.prefab).
    /// The rune is found in the base colour as the strongly saturated paint the plain stone never reaches, so any
    /// colour of rune works; that paint becomes the emission map and the glow colour, and is rasterised onto the
    /// stone face for scoring. Drop a new rune-stone folder in and bake again.
    /// </summary>
    public static class RuneStoneBaker
    {
        public const string RuneDir = "Assets/Game/Runes";
        public const string PrefabPath = "Assets/Game/Prefabs/RuneStone.prefab";
        const string ModelsDir = "Assets/TripoModels";
        public const float StoneHeight = 1.3f;
        const int MaskSize = 1024;
        const float Cell = 0.015f;

        [MenuItem("Tools/Game/Bake Rune Stones")]
        public static void BakeFromMenu()
        {
            var runes = BakeAll();
            BuildStonePrefab();
            Debug.Log("[Runes] Baked " + string.Join(", ", runes.Select(r => r.displayName)));
        }

        /// <summary>Bakes every rune-stone-* folder, sorted by name.</summary>
        public static List<RuneDefinition> BakeAll()
        {
            EnsureFolder(RuneDir);
            var list = new List<RuneDefinition>();
            foreach (var dir in AssetDatabase.GetSubFolders(ModelsDir).Where(d => Path.GetFileName(d).StartsWith("rune-stone")).OrderBy(d => d))
            {
                var fbx = AssetDatabase.FindAssets("t:Model", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
                var baseColor = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(f => f.ToLowerInvariant().Contains("basecolor") && !f.EndsWith(".meta"));
                if (fbx == null || baseColor == null)
                {
                    Debug.LogWarning("[Runes] Skipping " + dir + ": needs an FBX and a *basecolor* texture.");
                    continue;
                }
                list.Add(Bake(Path.GetFileName(dir), fbx, baseColor.Replace('\\', '/')));
            }
            AssetDatabase.SaveAssets();
            return list;
        }

        public static RuneDefinition Bake(string name, string fbx, string baseColor)
        {
            var mask = Mask(baseColor, out var maskPixels, out var paint);
            var maskPath = RuneDir + "/" + name + "_glow.png";
            File.WriteAllBytes(maskPath, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(maskPath);
            var maskTex = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);

            var defPath = RuneDir + "/" + name + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<RuneDefinition>(defPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<RuneDefinition>();
                AssetDatabase.CreateAsset(def, defPath);
            }
            def.displayName = name;
            def.model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            float max = Mathf.Max(paint.r, Mathf.Max(paint.g, paint.b, 0.001f));
            def.glowColor = new Color(paint.r / max, paint.g / max, paint.b / max) * 1.8f;

            // Place a copy at the origin to measure it: turn the painted face to -Z, scale to StoneHeight, stand it
            // on the origin centred in x/z.
            var temp = (GameObject)Object.Instantiate(def.model);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var filter = temp.GetComponentInChildren<MeshFilter>();
            var mesh = filter.sharedMesh;
            // The Tripo meshes are imported without Read/Write; once drawn, their CPU arrays can come back empty.
            // AcquireReadOnlyMeshData reads them either way.
            ReadMesh(mesh, out var verts, out var normals, out var uvs, out var tris);
            if (verts.Length == 0) throw new System.InvalidOperationException("[Runes] Could not read the mesh of " + fbx);
            Vector3 faceN = Vector3.zero, faceP = Vector3.zero;
            int count = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                if (Sample(maskPixels, uvs[i]) < 0.5f) continue;
                faceN += normals[i];
                faceP += verts[i];
                count++;
            }
            if (count == 0) { faceN = Vector3.back; faceP = mesh.bounds.center; }
            else faceP /= count;
            var flat = new Vector3(faceN.x, 0f, faceN.z);
            float yaw = flat.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(flat, Vector3.back, Vector3.up) : 0f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float scale = StoneHeight / mesh.bounds.size.y;
            // Bounds of the turned mesh, to centre it.
            var turned = new Bounds(rot * verts[0] * scale, Vector3.zero);
            foreach (var v in verts) turned.Encapsulate(rot * v * scale);
            def.modelRotation = rot;
            def.modelScale = scale;
            def.modelPosition = new Vector3(-turned.center.x, -turned.min.y, -turned.center.z);
            Vector3 Local(Vector3 v) => def.modelPosition + rot * v * scale;
            def.bounds = new Bounds(def.modelPosition + turned.center, turned.size);
            def.runeCenter = Local(faceP);

            // Rasterise the paint onto the face: sample every triangle, keep samples whose UV lands on the rune.
            var samples = new List<Vector3>();
            const int n = 8;
            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                for (int a = 0; a <= n; a++)
                for (int b = 0; b <= n - a; b++)
                {
                    float u = a / (float)n, v = b / (float)n, w = 1f - u - v;
                    if (Sample(maskPixels, uvs[i0] * w + uvs[i1] * u + uvs[i2] * v) < 0.5f) continue;
                    samples.Add(Local(verts[i0] * w + verts[i1] * u + verts[i2] * v));
                }
            }
            BakeGrid(def, samples);
            }
            finally
            {
                // A hidden, unsaved copy left behind by an exception would linger in the editor until restart.
                Object.DestroyImmediate(temp);
            }

            var matPath = RuneDir + "/" + name + ".mat";
            var source = def.model.GetComponentInChildren<Renderer>().sharedMaterial;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source);
            }
            mat.SetTexture("_EmissionMap", maskTex);
            // URP drops the emission keyword from a material whose emission colour is black, which would leave the
            // rune unlit whatever RuneStoneFx sets. Keep a barely visible base; RuneStoneFx drives the real glow.
            mat.SetColor("_EmissionColor", new Color(0.02f, 0.02f, 0.02f));
            mat.EnableKeyword("_EMISSION");
            // Marked emissive for realtime GI: URP's material validation keeps _EMISSION only on a material
            // flagged emissive, and stripped it from these when they were inspected or re-imported.
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            def.material = mat;
            EditorUtility.SetDirty(def);
            return def;
        }

        static void ReadMesh(Mesh mesh, out Vector3[] verts, out Vector3[] normals, out Vector2[] uvs, out int[] tris)
        {
            using (var all = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var data = all[0];
                var v = new Unity.Collections.NativeArray<Vector3>(data.vertexCount, Unity.Collections.Allocator.Temp);
                var n = new Unity.Collections.NativeArray<Vector3>(data.vertexCount, Unity.Collections.Allocator.Temp);
                var u = new Unity.Collections.NativeArray<Vector2>(data.vertexCount, Unity.Collections.Allocator.Temp);
                data.GetVertices(v);
                data.GetNormals(n);
                data.GetUVs(0, u);
                verts = v.ToArray();
                normals = n.ToArray();
                uvs = u.ToArray();
                v.Dispose(); n.Dispose(); u.Dispose();
                var list = new List<int>();
                for (int sub = 0; sub < data.subMeshCount; sub++)
                {
                    var desc = data.GetSubMesh(sub);
                    var idx = new Unity.Collections.NativeArray<int>(desc.indexCount, Unity.Collections.Allocator.Temp);
                    data.GetIndices(idx, sub);
                    list.AddRange(idx);
                    idx.Dispose();
                }
                tris = list.ToArray();
            }
        }

        static float Sample(float[] mask, Vector2 uv)
        {
            int x = Mathf.Clamp((int)(Mathf.Repeat(uv.x, 1f) * MaskSize), 0, MaskSize - 1);
            int y = Mathf.Clamp((int)(Mathf.Repeat(uv.y, 1f) * MaskSize), 0, MaskSize - 1);
            return mask[y * MaskSize + x];
        }

        static void BakeGrid(RuneDefinition def, List<Vector3> samples)
        {
            if (samples.Count == 0)
            {
                def.cells = new bool[0];
                def.gridWidth = def.gridHeight = 0;
                return;
            }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            float z = 0f;
            foreach (var p in samples)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.y));
                max = Vector2.Max(max, new Vector2(p.x, p.y));
                z += p.z;
            }
            const float pad = 0.03f;
            min -= Vector2.one * pad;
            max += Vector2.one * pad;
            int gw = Mathf.CeilToInt((max.x - min.x) / Cell), gh = Mathf.CeilToInt((max.y - min.y) / Cell);
            var cells = new bool[gw * gh];
            foreach (var p in samples)
            {
                int x = Mathf.Clamp(Mathf.FloorToInt((p.x - min.x) / Cell), 0, gw - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt((p.y - min.y) / Cell), 0, gh - 1);
                cells[y * gw + x] = true;
            }
            // Close one-cell gaps left between samples on long thin triangles.
            def.cells = Erode(Dilate(cells, gw, gh), gw, gh);
            def.gridWidth = gw;
            def.gridHeight = gh;
            def.gridMin = min;
            def.gridMax = new Vector2(min.x + gw * Cell, min.y + gh * Cell);
            def.faceZ = z / samples.Count;
        }

        /// <summary>
        /// Rune mask from the base colour: strongly saturated, reasonably bright paint, keeping only blobs at least 30% of
        /// the largest (drops stray saturated specks in the stone), softened by one texel. Also returns the mask and the
        /// average paint colour.
        /// </summary>
        static Texture2D Mask(string baseColor, out float[] pixels, out Color paint)
        {
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(baseColor));
            int step = Mathf.Max(1, src.width / MaskSize);
            var px = src.GetPixels32();
            var raw = new float[MaskSize * MaskSize];
            var rgb = new Color[MaskSize * MaskSize];
            for (int y = 0; y < MaskSize; y++)
            for (int x = 0; x < MaskSize; x++)
            {
                Color c = px[Mathf.Min(src.height - 1, y * step + step / 2) * src.width + Mathf.Min(src.width - 1, x * step + step / 2)];
                Color.RGBToHSV(c, out _, out float sat, out float val);
                raw[y * MaskSize + x] = sat > 0.72f && val > 0.45f ? 1f : 0f;
                rgb[y * MaskSize + x] = c;
            }
            Object.DestroyImmediate(src);
            // The rune is one painted blob; small saturated islands (seams, edge smears) are not part of it.
            KeepLargeRegions(raw, MaskSize, 0.3f);

            Color sum = Color.black;
            int n = 0;
            for (int i = 0; i < raw.Length; i++)
                if (raw[i] > 0.5f) { sum += rgb[i]; n++; }
            paint = n > 0 ? sum / n : new Color(1f, 0.72f, 0.28f);

            pixels = new float[raw.Length];
            var colors = new Color[raw.Length];
            for (int y = 0; y < MaskSize; y++)
            for (int x = 0; x < MaskSize; x++)
            {
                float s = 0f;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    s += raw[Mathf.Clamp(y + dy, 0, MaskSize - 1) * MaskSize + Mathf.Clamp(x + dx, 0, MaskSize - 1)];
                float v = Mathf.Max(raw[y * MaskSize + x], s / 9f);
                pixels[y * MaskSize + x] = v;
                colors[y * MaskSize + x] = new Color(v, v, v);
            }
            var tex = new Texture2D(MaskSize, MaskSize, TextureFormat.RGB24, true);
            tex.SetPixels(colors);
            tex.Apply();
            return tex;
        }

        /// <summary>Clears every 4-connected blob smaller than <paramref name="share"/> of the largest one.</summary>
        static void KeepLargeRegions(float[] mask, int size, float share)
        {
            var label = new int[mask.Length];
            var sizes = new List<int> { 0 };
            var stack = new Stack<int>();
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] < 0.5f || label[i] != 0) continue;
                int id = sizes.Count, count = 0;
                stack.Push(i);
                label[i] = id;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    count++;
                    int x = k % size, y = k / size;
                    if (x > 0) Visit(k - 1);
                    if (x < size - 1) Visit(k + 1);
                    if (y > 0) Visit(k - size);
                    if (y < size - 1) Visit(k + size);
                }
                sizes.Add(count);

                void Visit(int j)
                {
                    if (mask[j] < 0.5f || label[j] != 0) return;
                    label[j] = id;
                    stack.Push(j);
                }
            }
            int largest = sizes.Max();
            for (int i = 0; i < mask.Length; i++)
                if (label[i] != 0 && sizes[label[i]] < largest * share) mask[i] = 0f;
        }

        static bool[] Dilate(bool[] c, int w, int h)
        {
            var o = new bool[c.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool any = false;
                for (int dy = -1; dy <= 1 && !any; dy++)
                for (int dx = -1; dx <= 1 && !any; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    any = xx >= 0 && yy >= 0 && xx < w && yy < h && c[yy * w + xx];
                }
                o[y * w + x] = any;
            }
            return o;
        }

        static bool[] Erode(bool[] c, int w, int h)
        {
            var o = new bool[c.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool all = c[y * w + x];
                for (int dy = -1; dy <= 1 && all; dy++)
                for (int dx = -1; dx <= 1 && all; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    all = xx >= 0 && yy >= 0 && xx < w && yy < h && c[yy * w + xx];
                }
                o[y * w + x] = all;
            }
            return o;
        }

        // ------------------------------------------------------------------ the stone prefab

        /// <summary>
        /// Shared stone: <see cref="RuneStoneFx"/> + <see cref="RuneTrace"/> + a hit box for the lasers, a spinning
        /// holder for the model, the rune light, and the result effects (a soft light column and floor ring tinted to
        /// the rune for success, a puff of grey dust for failure). The rune model itself is added by SetRune.
        /// </summary>
        public static GameObject BuildStonePrefab()
        {
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            var root = new GameObject("RuneStone");
            var fx = root.AddComponent<RuneStoneFx>();
            var trace = root.AddComponent<RuneTrace>();
            trace.stone = fx;
            fx.trace = trace;
            fx.hitBox = root.AddComponent<BoxCollider>();
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            fx.spin = spin;
            var light = Light(spin, "RuneLight", LightType.Point, Color.white, 0f, 2.4f);
            light.shadows = LightShadows.None;
            fx.runeLight = light;

            // Success, in warm gold whatever the rune's colour: a burst of light streaks and a ring off the
            // rune, a soft column of light and a floor ring.
            var gold = new Color(1f, 0.82f, 0.45f);
            var success = new GameObject("SuccessFx");
            success.transform.SetParent(root.transform, false);
            var sAtRune = new GameObject("AtRune").transform;
            sAtRune.SetParent(success.transform, false);
            Show(Tint(Vfx(sAtRune, "HCFX_Orb_03_Get", Vector3.zero, 0.12f, 0.75f), gold));
            Show(Tint(Vfx(sAtRune, "HCFX_Orb_05_Get", Vector3.zero, 0.16f, 0.7f), gold));
            Show(Vfx(success.transform, "HCFX_Blessed_01_Small", Vector3.zero, 0.42f, 0.55f));
            Show(FloorRing(success.transform, 0.24f, gold));
            success.SetActive(false);
            fx.successFx = success;

            // Failure: the rune's magic bursts into a dark, smoky fizzle with black shards cracking off, and dust
            // rolls off the dais.
            var fail = new GameObject("FailFx");
            fail.transform.SetParent(root.transform, false);
            var fAtRune = new GameObject("AtRune").transform;
            fAtRune.SetParent(fail.transform, false);
            var fizzle = Tint(Vfx(fAtRune, "HCFX_Eye_01_Die", Vector3.zero, 0.2f, 0.9f), new Color(0.36f, 0.3f, 0.42f));
            SecondColor(fizzle, new Color(0.3f, 0.26f, 0.34f));
            Show(fizzle);
            Show(Vfx(fAtRune, "HCFX_Hit_01", Vector3.zero, 0.13f, 0.9f));
            var dust = Tint(Vfx(fAtRune, "HCFX_Smoke_01_Air", Vector3.back * 0.05f, 0.2f, 0.75f), new Color(0.5f, 0.47f, 0.46f));
            Mute(dust, "glow_view", "glow_floor", "smoke_mesh");
            SecondColor(dust, new Color(0.36f, 0.34f, 0.34f));
            Show(dust);
            var floorDust = Tint(Vfx(fail.transform, "HCFX_Smoke_01_Floor", new Vector3(0f, 0.02f, 0f), 0.22f, 0.6f), new Color(0.48f, 0.45f, 0.42f));
            Mute(floorDust, "glow_view", "glow_floor", "smoke_mesh");
            SecondColor(floorDust, new Color(0.36f, 0.34f, 0.32f));
            Show(floorDust);
            fail.SetActive(false);
            fx.failFx = fail;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void Show(GameObject go) => go.SetActive(true);

        /// <summary>A stone in the open scene showing <paramref name="rune"/>.</summary>
        public static RuneStoneFx PlaceStone(string name, Vector3 position, RuneDefinition rune)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? BuildStonePrefab();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // Unpacked: the builders re-save the prefab each run, and when it re-imports, children added to a
            // linked instance (the rune model) were dropped from the scene.
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.position = position;
            var fx = go.GetComponent<RuneStoneFx>();
            fx.SetRune(rune);
            return fx;
        }

        /// <summary>
        /// The Klaus particle shader tints with a second colour fed through custom vertex data (pink on the dust);
        /// CutsceneKit.Tint only reaches the start colour, so recolour the custom streams too.
        /// </summary>
        static void SecondColor(GameObject vfx, Color color)
        {
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var custom = ps.customData;
                if (!custom.enabled) continue;
                foreach (var stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 })
                    if (custom.GetMode(stream) == ParticleSystemCustomDataMode.Color)
                        custom.SetColor(stream, new ParticleSystem.MinMaxGradient(color));
            }
        }
    }
}
