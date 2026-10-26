using UnityEngine;

namespace WarehouseASRS.Core
{
    /// <summary>Rigid transform (position + rotation). Named Tf to avoid clashing with UnityEngine.Pose.</summary>
    [System.Serializable]
    public struct Tf
    {
        public Vector3 p;
        public Quaternion r;

        public Tf(Vector3 p, Quaternion r) { this.p = p; this.r = r; }
        public Tf(Vector3 p, float yawDeg) { this.p = p; r = Quaternion.Euler(0f, yawDeg, 0f); }

        public static readonly Tf Identity = new Tf(Vector3.zero, Quaternion.identity);
        public static Tf operator *(Tf a, Tf b) => new Tf(a.p + a.r * b.p, a.r * b.r);
        public Tf Inverse() { var ir = Quaternion.Inverse(r); return new Tf(ir * -p, ir); }
        public Vector3 Point(Vector3 v) => p + r * v;
        public Vector3 Dir(Vector3 v) => r * v;
        public Tf Moved(Vector3 d) => new Tf(p + d, r);
        public Vector3 Forward => r * Vector3.forward;
        public static Tf Lerp(Tf a, Tf b, float t) => new Tf(Vector3.Lerp(a.p, b.p, t), Quaternion.Slerp(a.r, b.r, t));
        public override string ToString() => $"({p.x:0.00},{p.y:0.00},{p.z:0.00} | {r.eulerAngles.x:0},{r.eulerAngles.y:0},{r.eulerAngles.z:0})";
    }

    public static class MathX
    {
        public static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        public static float YawOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    /// <summary>Small deterministic RNG (xorshift) so runs are identical everywhere.</summary>
    public class Rng
    {
        uint s;
        public Rng(int seed) { s = (uint)(seed * 2654435761u) ^ 0x9E3779B9u; if (s == 0) s = 1; }
        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        public float Value => (Next() & 0xFFFFFF) / 16777216f;
        public int Range(int lo, int hi) => lo + (int)(Next() % (uint)(hi - lo));
    }
}
