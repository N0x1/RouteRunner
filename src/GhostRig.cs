using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using RouteRunner.Core;
using Object = UnityEngine.Object;

namespace RouteRunner
{
    internal sealed class CaptureRig : IDisposable
    {
        internal readonly FirstPersonController Player;
        internal readonly Transform[] Bones;
        internal readonly string[] Paths;
        internal readonly SkinnedMeshRenderer[] Renderers;
        readonly Animator animator;
        readonly AnimatorCullingMode oldCulling;
        readonly bool[] oldOffscreen;
        readonly bool evaluateAnimation;

        internal CaptureRig(FirstPersonController player, bool evaluateAnimation = true)
        {
            Player = player;
            this.evaluateAnimation = evaluateAnimation;
            var health = player.GetComponent<PlayerHealth>();
            if (health == null || health.graphics == null) throw new InvalidOperationException("Player graphics are not ready.");
            var setup = player.GetComponent<PlayerSetup>();
            var arms = GameBridge.ArmsField?.GetValue(setup) as GameObject[] ?? Array.Empty<GameObject>();
            Renderers = health.graphics.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.bones.Length > 0 && !arms.Any(a => a != null && (r.transform == a.transform || r.transform.IsChildOf(a.transform))))
                .ToArray();
            if (Renderers.Length == 0) throw new InvalidOperationException("No full-body skinned meshes found. See BepInEx/LogOutput.log.");
            var bones = new HashSet<Transform>();
            foreach (var r in Renderers)
            {
                bones.Add(r.transform);
                if (r.rootBone != null) bones.Add(r.rootBone);
                foreach (var b in r.bones) if (b != null) bones.Add(b);
            }
            if (bones.Any(b => b != player.transform && !b.IsChildOf(player.transform)))
                throw new InvalidOperationException("The player rig contains external bones.");
            Bones = bones.OrderBy(b => PathOf(b, player.transform), StringComparer.Ordinal).ToArray();
            Paths = Bones.Select(b => PathOf(b, player.transform)).ToArray();
            if (Bones.Length > RouteCodec.MaxBones) throw new InvalidOperationException("This character rig exceeds the supported bone count.");
            // The local full body is hidden. Evaluate its animation while recording without making it visible.
            animator = GameBridge.AnimatorField?.GetValue(player) as Animator;
            if (animator == null || !animator.enabled || !animator.gameObject.activeInHierarchy)
                throw new InvalidOperationException("The full-body animator is not active yet.");
            oldCulling = animator.cullingMode;
            if (evaluateAnimation)
            {
                oldOffscreen = Renderers.Select(r => r.updateWhenOffscreen).ToArray();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var r in Renderers) r.updateWhenOffscreen = true;
            }
        }

        internal Frame Capture(float time)
        {
            var root = Player.transform;
            var rootPosition = root.position;
            var rootRotation = root.rotation;
            var values = new float[7 + Bones.Length * 10];
            Put(values, 0, rootPosition); Put(values, 3, rootRotation);
            var inverse = Quaternion.Inverse(rootRotation);
            for (int i = 0; i < Bones.Length; i++)
            {
                var bone = Bones[i];
                if (bone == null) throw new InvalidOperationException("Character rig changed during recording.");
                int j = 7 + i * 10;
                // Flat, root-relative world poses also capture procedural lean, crouch, and IK.
                Put(values, j, inverse * (bone.position - rootPosition));
                Put(values, j + 3, inverse * bone.rotation);
                Put(values, j + 7, bone.lossyScale);
            }
            return new Frame { Time = time, State = GameBridge.State(Player), Pose = values };
        }

        public void Dispose()
        {
            if (!evaluateAnimation) return;
            if (animator != null) animator.cullingMode = oldCulling;
            for (int i = 0; i < Renderers.Length; i++) if (Renderers[i] != null) Renderers[i].updateWhenOffscreen = oldOffscreen[i];
        }

        internal static string PathOf(Transform t, Transform root)
        {
            if (t == root) return "$root";
            var parts = new List<string>();
            while (t != null && t != root)
            {
                int ordinal = 0;
                if (t.parent != null)
                    for (int i = 0; i < t.GetSiblingIndex(); i++) if (t.parent.GetChild(i).name == t.name) ordinal++;
                parts.Add(Uri.EscapeDataString(t.name) + ":" + ordinal);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
        internal static Vector3 V(float[] a, int i) => new Vector3(a[i], a[i + 1], a[i + 2]);
        internal static Quaternion Q(float[] a, int i) => new Quaternion(a[i], a[i + 1], a[i + 2], a[i + 3]);
        static void Put(float[] a, int i, Vector3 v) { a[i] = v.x; a[i + 1] = v.y; a[i + 2] = v.z; }
        static void Put(float[] a, int i, Quaternion q) { a[i] = q.x; a[i + 1] = q.y; a[i + 2] = q.z; a[i + 3] = q.w; }
    }

    internal sealed class GhostRig : IDisposable
    {
        readonly GameObject root;
        readonly Transform[] bones;
        readonly Material material;
        readonly Texture2D colourTexture;
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly Route sourceRoute;
        readonly Transform[] sourceBones;
        readonly string[] sourcePaths;
        readonly SkinnedMeshRenderer[] sourceRenderers;
        readonly Mesh[] sourceMeshes;
        readonly Transform[][] sourceSkinBones;
        readonly Transform[] sourceRootBones;
        Color currentColour;
        Route shownRoute;
        float shownTime = float.NaN;

        internal bool CanReuse(CaptureRig source, Route route)
        {
            if (!ReferenceEquals(sourceRoute, route) || !sourcePaths.SequenceEqual(source.Paths) ||
                !sourceBones.SequenceEqual(source.Bones) || !sourceRenderers.SequenceEqual(source.Renderers)) return false;
            for (int i = 0; i < sourceMeshes.Length; i++)
                if (source.Renderers[i].sharedMesh != sourceMeshes[i] || source.Renderers[i].rootBone != sourceRootBones[i] ||
                    !source.Renderers[i].bones.SequenceEqual(sourceSkinBones[i])) return false;
            return true;
        }

        internal void SetColour(Color colour)
        {
            if (material == null || currentColour.Equals(colour)) return;
            if (colourTexture != null)
            {
                colourTexture.SetPixel(0, 0, colour); colourTexture.Apply(false, false);
            }
            else material.color = colour;
            currentColour = colour;
        }

        internal GhostRig(CaptureRig source, Route route, Color colour)
        {
            sourceRoute = route; sourceBones = source.Bones; sourcePaths = source.Paths; sourceRenderers = source.Renderers;
            sourceMeshes = source.Renderers.Select(r => r.sharedMesh).ToArray();
            sourceSkinBones = source.Renderers.Select(r => r.bones).ToArray();
            sourceRootBones = source.Renderers.Select(r => r.rootBone).ToArray();
            var byPath = new Dictionary<string, Transform>(source.Paths.Length, StringComparer.Ordinal);
            for (int i = 0; i < source.Paths.Length; i++) byPath.Add(source.Paths[i], source.Bones[i]);
            if (route.Bones.Length != byPath.Count || route.Bones.Any(p => !byPath.ContainsKey(p)))
                throw new InvalidOperationException("This route uses a different character rig. Record/import with matching game versions.");
            root = new GameObject("Route Runner (visual only)");
            try
            {
                // Construct only renderers and transforms. Never instantiate a player prefab or its scripts.
                var shader = Shader.Find("Unlit/Transparent");
                bool texturedColour = shader != null;
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) throw new InvalidOperationException("No supported unlit ghost shader was found.");
                material = new Material(shader) { name = "Route Runner Ghost" };
                if (texturedColour)
                {
                    // Unlit/Transparent has no colour uniform: a uniform texture supplies RGBA.
                    colourTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Route Runner Tint", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                    colourTexture.SetPixel(0, 0, colour); colourTexture.Apply(false, false);
                    material.mainTexture = colourTexture;
                }
                else { material.mainTexture = Texture2D.whiteTexture; material.color = colour; }
                currentColour = colour;
                material.renderQueue = 3000;
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
                if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                bones = new Transform[route.Bones.Length];
                var mapping = new Dictionary<Transform, Transform>();
                for (int i = 0; i < bones.Length; i++)
                {
                    var go = new GameObject("Pose " + i); go.transform.SetParent(root.transform, false);
                    bones[i] = go.transform; mapping[byPath[route.Bones[i]]] = bones[i];
                }
                for (int i = 0; i < source.Renderers.Length; i++)
                {
                    var original = source.Renderers[i];
                    var renderer = mapping[original.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                    if (texturedColour) renderer.sharedMesh = original.sharedMesh;
                    else
                    {
                        // The sprite fallback multiplies vertex colours. Make only our mesh copy
                        // white so dark source vertex colours cannot turn the chosen tint black.
                        var mesh = Object.Instantiate(original.sharedMesh);
                        ownedMeshes.Add(mesh);
                        var colours = new Color32[mesh.vertexCount];
                        for (int vertex = 0; vertex < colours.Length; vertex++) colours[vertex] = new Color32(255, 255, 255, 255);
                        mesh.colors32 = colours;
                        renderer.sharedMesh = mesh;
                    }
                    renderer.bones = sourceSkinBones[i].Select(b => b == null ? null : mapping[b]).ToArray();
                    renderer.rootBone = original.rootBone == null ? null : mapping[original.rootBone];
                    renderer.localBounds = original.localBounds;
                    renderer.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, original.sharedMesh.subMeshCount)).ToArray();
                    renderer.updateWhenOffscreen = true;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                Show(route, 0);
            }
            catch { Dispose(); throw; }
        }

        internal void Show(Route route, float time)
        {
            if (ReferenceEquals(shownRoute, route) && shownTime == time) return;
            int index = Timeline.FindFrame(route.Frames, time);
            var a = route.Frames[index]; var b = route.Frames[Math.Min(index + 1, route.Frames.Count - 1)];
            float blend = Timeline.Blend(a, b, time);
            root.transform.SetPositionAndRotation(Vector3.Lerp(CaptureRig.V(a.Pose, 0), CaptureRig.V(b.Pose, 0), blend), Quaternion.Slerp(CaptureRig.Q(a.Pose, 3), CaptureRig.Q(b.Pose, 3), blend));
            for (int i = 0; i < bones.Length; i++)
            {
                int j = 7 + i * 10;
                bones[i].localPosition = Vector3.Lerp(CaptureRig.V(a.Pose, j), CaptureRig.V(b.Pose, j), blend);
                bones[i].localRotation = Quaternion.Slerp(CaptureRig.Q(a.Pose, j + 3), CaptureRig.Q(b.Pose, j + 3), blend);
                bones[i].localScale = Vector3.Lerp(CaptureRig.V(a.Pose, j + 7), CaptureRig.V(b.Pose, j + 7), blend);
            }
            shownRoute = route; shownTime = time;
        }
        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
            if (material != null) Object.Destroy(material);
            if (colourTexture != null) Object.Destroy(colourTexture);
            foreach (var mesh in ownedMeshes) if (mesh != null) Object.Destroy(mesh);
            ownedMeshes.Clear();
        }
    }
}
