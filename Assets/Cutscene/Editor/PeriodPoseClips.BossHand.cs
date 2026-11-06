using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AiHackathon.Cutscene.Editor
{
    public static partial class PeriodPoseClips
    {
        /// <summary>
        /// The boss's wand is part of his body mesh, rigidly skinned to the right hand, and Tripo left it lying along
        /// an open hand. On a mesh copy (<paramref name="mesh"/>, same bones and bind poses as <paramref name="fbx"/>):
        /// sets the wand in the palm the way the casters' grips hold theirs (<see cref="GripLocal"/>,
        /// <see cref="AxisLocal"/>; the shaft slides along its own line and tilts only a little, so the clips still
        /// aim it), closes the fingers and thumb around it and skins them rigidly to the hand, so every clip shows a
        /// fist on the wand whatever it does with the fingers.
        /// </summary>
        internal static void CloseBossHandOnWand(Mesh mesh, string fbx)
        {
            using (var rig = new PoseRig(fbx))
            {
                var bones = rig.Root.GetComponentInChildren<SkinnedMeshRenderer>().bones;
                var bind = mesh.bindposes;
                int hand = Array.FindIndex(bones, b => b.name == R + "Hand");
                var fingers = new HashSet<int>(Enumerable.Range(0, bones.Length).Where(i => bones[i].name.StartsWith(R)
                    && new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }.Any(bones[i].name.Contains)));
                Matrix4x4 Skin(int b) => bones[b].localToWorldMatrix * bind[b];

                var verts = mesh.vertices;
                var normals = mesh.normals;
                var tangents = mesh.tangents;
                var weights = mesh.boneWeights;
                var handSkin = Skin(hand);
                var toHand = handSkin.inverse;

                // ---- The wand: the mesh island holding the vertex farthest from the hand among those rigid on it ----
                var island = WandIsland(mesh, verts, weights, hand, bind[hand].inverse.MultiplyPoint3x4(Vector3.zero));
                var world = island.ToDictionary(i => i, i => handSkin.MultiplyPoint3x4(verts[i]));
                var tip = island.OrderByDescending(i => (world[i] - bones[hand].position).sqrMagnitude).First();
                var butt = island.OrderByDescending(i => (world[i] - world[tip]).sqrMagnitude).First();
                ShaftNearButt(island.Select(i => world[i]).ToList(), world[butt], world[tip], out var linePoint, out var lineAxis, out var radius);

                // Hold the shaft just above the butt (Tripo had it lying along the forearm, the butt by the elbow),
                // tilted onto the grip axis.
                var handBone = rig[R + "Hand"];
                var grip = handBone.position + handBone.rotation * (GripLocal(rig, R) - rig.FingerLocal(R) * BossGripBack);
                var gripAxis = handBone.rotation * AxisLocal(rig, R);
                var pivot = linePoint + lineAxis * (Vector3.Dot(world[butt] - linePoint, lineAxis) + BossGripFromButt);
                var turn = Quaternion.FromToRotation(lineAxis, gripAxis);
                foreach (var i in island)
                {
                    verts[i] = toHand.MultiplyPoint3x4(grip + turn * (world[i] - pivot));
                    if (normals.Length > 0) normals[i] = toHand.MultiplyVector(turn * handSkin.MultiplyVector(normals[i])).normalized;
                    if (tangents.Length == 0) continue;
                    var t = toHand.MultiplyVector(turn * handSkin.MultiplyVector(tangents[i])).normalized;
                    tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                }

                // ---- The fist: wrap the fingers on the new shaft, then bake them onto the hand ----
                var before = Enumerable.Range(0, bones.Length).Select(Skin).ToArray();
                rig.Curl(R, 0.15f, BossThumbCurl);
                // Shaft radius plus half a finger (the boss's hand is about the casters' size).
                rig.Wrap(R, grip, gripAxis, radius + 0.005f);
                var after = Enumerable.Range(0, bones.Length).Select(Skin).ToArray();
                var islandSet = new HashSet<int>(island);
                for (int v = 0; v < verts.Length; v++)
                {
                    if (islandSet.Contains(v)) continue;
                    var w = weights[v];
                    var pairs = new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) };
                    if (!pairs.Any(p => p.Item2 > 0f && fingers.Contains(p.Item1))) continue;
                    var posed = Blend(after, pairs);
                    var merged = new Dictionary<int, float>();
                    foreach (var (b, ww) in pairs)
                    {
                        if (ww <= 0f) continue;
                        int to = fingers.Contains(b) ? hand : b;
                        merged[to] = merged.TryGetValue(to, out var a) ? a + ww : ww;
                    }
                    var top = merged.OrderByDescending(kv => kv.Value).Take(4).ToArray();
                    float total = top.Sum(kv => kv.Value);
                    var nw = new BoneWeight();
                    var np = new (int, float)[4];
                    for (int k = 0; k < top.Length; k++) np[k] = (top[k].Key, top[k].Value / total);
                    (nw.boneIndex0, nw.weight0) = np[0]; (nw.boneIndex1, nw.weight1) = np[1];
                    (nw.boneIndex2, nw.weight2) = np[2]; (nw.boneIndex3, nw.weight3) = np[3];
                    weights[v] = nw;
                    // Same place and facing as the wrapped pose, now under the new weights (all unwrapped bones).
                    var undo = Blend(before, np).inverse;
                    verts[v] = undo.MultiplyPoint3x4(posed.MultiplyPoint3x4(verts[v]));
                    if (normals.Length > 0) normals[v] = undo.MultiplyVector(posed.MultiplyVector(normals[v])).normalized;
                    if (tangents.Length == 0) continue;
                    var t = undo.MultiplyVector(posed.MultiplyVector(tangents[v])).normalized;
                    tangents[v] = new Vector4(t.x, t.y, t.z, tangents[v].w);
                }
                mesh.vertices = verts;
                if (normals.Length > 0) mesh.normals = normals;
                if (tangents.Length > 0) mesh.tangents = tangents;
                mesh.boneWeights = weights;
                mesh.RecalculateBounds();
            }
        }

        // How far up the shaft from its butt the fist closes (model units; the boss's hand is about 8 cm long).
        const float BossGripFromButt = 0.06f;
        // How far toward the wrist the fist sits from the casters' grip point. Their wands cross the index knuckle;
        // on the boss's long, thin fingers that wedged the shaft between index and middle, so it goes deeper into the
        // palm and leaves the fist between thumb and index.
        const float BossGripBack = 0.016f;
        // Thumb curl before the wrap, so the thumb closes over the shaft instead of standing off it.
        const float BossThumbCurl = 0.6f;

        static Matrix4x4 Blend(Matrix4x4[] skin, (int bone, float weight)[] pairs)
        {
            var m = new Matrix4x4();
            foreach (var (b, w) in pairs)
            {
                if (w <= 0f) continue;
                for (int k = 0; k < 16; k++) m[k] += skin[b][k] * w;
            }
            return m;
        }

        // Vertices of the mesh island (welded across UV seams) that holds the vertex rigid on the hand farthest from it.
        static List<int> WandIsland(Mesh mesh, Vector3[] verts, BoneWeight[] weights, int hand, Vector3 handBind)
        {
            var parent = Enumerable.Range(0, verts.Length).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            var seen = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < verts.Length; i++)
            {
                var key = Vector3Int.RoundToInt(verts[i] * 1e5f);
                if (seen.TryGetValue(key, out var j)) Union(i, j); else seen[key] = i;
            }
            var tris = mesh.triangles;
            for (int t = 0; t < tris.Length; t += 3) { Union(tris[t], tris[t + 1]); Union(tris[t + 1], tris[t + 2]); }
            bool Rigid(int i) => weights[i].boneIndex0 == hand && weights[i].weight0 > 0.95f;
            int seed = Enumerable.Range(0, verts.Length).Where(Rigid).OrderByDescending(i => (verts[i] - handBind).sqrMagnitude).First();
            int root = Find(seed);
            var island = Enumerable.Range(0, verts.Length).Where(i => Find(i) == root).ToList();
            if (!island.All(Rigid))
                throw new Exception("The boss wand is no longer a separate mesh island rigid on the right hand.");
            return island;
        }

        // The straight-ish run of the shaft where the hand holds it (3 to 30 cm from the butt): the line through its
        // cross-section centres, pointing toward the tip, and its mean radius.
        static void ShaftNearButt(List<Vector3> points, Vector3 butt, Vector3 tip, out Vector3 point, out Vector3 axis, out float radius)
        {
            var centres = new List<Vector3>();
            var slabs = new List<List<Vector3>>();
            for (float s = 0.03f; s <= 0.3f; s += 0.01f)
            {
                var slab = points.Where(p => Mathf.Abs((p - butt).magnitude - s) < 0.006f).ToList();
                if (slab.Count < 4) continue;
                slabs.Add(slab);
                centres.Add(slab.Aggregate(Vector3.zero, (a, p) => a + p) / slab.Count);
            }
            point = centres.Aggregate(Vector3.zero, (a, p) => a + p) / centres.Count;
            // Principal direction of the centres (power iteration from butt to tip).
            axis = (tip - butt).normalized;
            for (int it = 0; it < 32; it++)
            {
                var next = Vector3.zero;
                foreach (var c in centres) { var d = c - point; next += d * Vector3.Dot(d, axis); }
                if (next.sqrMagnitude < 1e-12f) break;
                axis = next.normalized;
            }
            if (Vector3.Dot(axis, tip - butt) < 0f) axis = -axis;
            var p0 = point;
            var a0 = axis;
            radius = slabs.SelectMany(s => s).Average(p => Vector3.ProjectOnPlane(p - p0, a0).magnitude);
        }
    }
}
