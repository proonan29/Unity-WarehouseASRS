using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarehouseASRS.World
{
    /// <summary>Materials cloned from one URP Lit asset (so the shader is always included), cached by key.</summary>
    public class MatLib
    {
        readonly Material baseLit;
        readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        public MatLib(Material baseLit) { this.baseLit = baseLit; }

        public Material Get(string key, Color c, float smooth = 0.3f, float metal = 0f)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = baseLit != null ? new Material(baseLit) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = key;
            m.hideFlags = HideFlags.DontSave;
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            m.enableInstancing = true;
            cache[key] = m;
            return m;
        }

        public Material Glass(string key, Color c)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = Get(key, c, 0.9f, 0f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) { m.SetInt("_SrcBlendAlpha", (int)BlendMode.One); m.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha); }
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public Material Emissive(string key, Color c)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = Get(key, c, 0.5f, 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 1.5f);
            return m;
        }
    }

    /// <summary>Primitive helpers (no colliders – nothing here uses physics).</summary>
    public static class Prims
    {
        static Mesh cube, cyl;
        static Mesh CubeMesh { get { if (cube == null) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); } return cube; } }
        static Mesh CylMesh { get { if (cyl == null) { var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cyl = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); } return cyl; } }

        static GameObject Make(string name, Mesh mesh, Transform parent, Material m, bool shadows)
        {
            var g = new GameObject(name);
            g.hideFlags = HideFlags.DontSave;
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        public static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m, Quaternion? rot = null, bool shadows = true)
        {
            var g = Make(name, CubeMesh, parent, m, shadows);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.transform.localScale = size;
            return g;
        }

        /// <summary>Cylinder whose axis is the local Y axis of <paramref name="rot"/>.</summary>
        public static GameObject Cyl(string name, Transform parent, Vector3 pos, float radius, float height, Material m, Quaternion? rot = null, bool shadows = true)
        {
            var g = Make(name, CylMesh, parent, m, shadows);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            return g;
        }

        public static GameObject CylX(string name, Transform parent, Vector3 pos, float radius, float length, Material m) => Cyl(name, parent, pos, radius, length, m, Quaternion.Euler(0f, 0f, 90f));
        public static GameObject CylZ(string name, Transform parent, Vector3 pos, float radius, float length, Material m) => Cyl(name, parent, pos, radius, length, m, Quaternion.Euler(90f, 0f, 0f));

        /// <summary>Thin box from a to b (beams, pillars).</summary>
        public static GameObject Beam(string name, Transform parent, Vector3 a, Vector3 b, float w, float h, Material m)
        {
            var d = b - a;
            var rot = Quaternion.LookRotation(d.normalized, Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up);
            return Box(name, parent, (a + b) * 0.5f, new Vector3(w, h, d.magnitude), m, rot);
        }

        public static Transform Node(string name, Transform parent, Vector3 pos, Quaternion? rot = null)
        {
            var t = new GameObject(name) { hideFlags = HideFlags.DontSave }.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = rot ?? Quaternion.identity;
            return t;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
        }
    }

    /// <summary>Flat line segments on the floor (or any height) batched into one mesh; rebuilt when needed.</summary>
    public class LineBatch
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<int> idx = new List<int>();
        public readonly Mesh mesh;
        public readonly MeshRenderer renderer;

        public LineBatch(string name, Transform parent, Material mat)
        {
            var g = new GameObject(name) { hideFlags = HideFlags.DontSave };
            g.transform.SetParent(parent, false);
            mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.MarkDynamic();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = g.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void Clear() { v.Clear(); c.Clear(); idx.Clear(); }

        /// <summary>Horizontal ribbon from a to b of width w.</summary>
        public void Line(Vector3 a, Vector3 b, float w, Color col)
        {
            var d = b - a; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return;
            var n = new Vector3(-d.z, 0f, d.x).normalized * (w * 0.5f);
            int i = v.Count;
            v.Add(a - n); v.Add(a + n); v.Add(b + n); v.Add(b - n);
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            idx.Add(i); idx.Add(i + 1); idx.Add(i + 2); idx.Add(i); idx.Add(i + 2); idx.Add(i + 3);
        }

        public void Dashed(Vector3 a, Vector3 b, float w, Color col, float dash = 1f, float gap = 0.7f)
        {
            float len = (b - a).magnitude;
            for (float s = 0f; s < len; s += dash + gap) Line(Vector3.Lerp(a, b, s / len), Vector3.Lerp(a, b, Mathf.Min(len, s + dash) / len), w, col);
        }

        public void Ring(Vector3 center, float r, float w, Color col, int seg = 24)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Line(center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r, w, col);
            }
        }

        public void Rect(Vector3 center, float hx, float hz, float yaw, float w, Color col)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            var p = new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz) };
            for (int i = 0; i < 4; i++) Line(center + q * p[i], center + q * p[(i + 1) % 4], w, col);
        }

        public void Apply()
        {
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateBounds();
        }
    }
}
