using System.Collections.Generic;
using UnityEngine;

namespace WarehouseASRS.Core
{
    /// <summary>
    /// Floor-mounted 6-axis assembly robot with a spherical wrist (heavy-payload class, ~2.7 m reach),
    /// expressed in the robot base frame using Unity axes (+Y up, +Z forward at J1 = 0).
    /// Joint axes: J1 up, J2 right, J3 right, J4 forward (forearm roll), J5 right (bend), J6 forward (flange).
    /// J2 &lt; 0 raises the upper arm. Adapted from the WeldingBot analytic solver.
    /// </summary>
    public class RobotGeom
    {
        public float d1 = 0.75f;        // base -> shoulder height
        public float a1 = 0.35f;        // shoulder forward offset
        public float L2 = 1.30f;        // upper arm
        public float L3 = 1.40f;        // elbow -> wrist centre
        public float wristSplit = 0.55f;// J4 location along the forearm (visual)
        public float d6 = 0.22f;        // wrist centre -> flange
        public float[] qMin = { -185f, -140f, -110f, -350f, -125f, -350f };
        public float[] qMax = { 185f, 45f, 165f, 350f, 125f, 350f };
        public float[] vMax = { 105f, 95f, 105f, 165f, 160f, 230f };   // deg/s

        public static readonly float[] Home = { 0f, -75f, 95f, 0f, 70f, 0f };
        public float Reach => a1 + L2 + L3;
        public static readonly RobotGeom Default = new RobotGeom();
    }

    public static class RobotKin
    {
        static readonly Vector3[] Axes = { Vector3.up, Vector3.right, Vector3.right, Vector3.forward, Vector3.right, Vector3.forward };
        public static Vector3 Axis(int i) => Axes[i];
        static readonly float[] ContW = { 1f, 1f, 1f, 0.6f, 0.6f, 0.3f };
        [System.ThreadStatic] static List<float[]> bufTS;
        static List<float[]> buf => bufTS ??= new List<float[]>(8);

        public static Tf FK(RobotGeom g, float tool, float[] q)
        {
            Quaternion r1 = Quaternion.AngleAxis(q[0], Vector3.up);
            Vector3 j2 = r1 * new Vector3(0f, g.d1, g.a1);
            Quaternion r2 = r1 * Quaternion.AngleAxis(q[1], Vector3.right);
            Vector3 j3 = j2 + r2 * new Vector3(0f, 0f, g.L2);
            Quaternion r3 = r2 * Quaternion.AngleAxis(q[2], Vector3.right);
            Vector3 wrist = j3 + r3 * new Vector3(0f, 0f, g.L3);
            Quaternion r6 = r3 * Quaternion.AngleAxis(q[3], Vector3.forward) * Quaternion.AngleAxis(q[4], Vector3.right) *
                            Quaternion.AngleAxis(q[5], Vector3.forward);
            return new Tf(wrist + r6 * new Vector3(0f, 0f, g.d6 + tool), r6);
        }

        static float Norm180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a <= -180f) a += 360f;
            return a;
        }

        static bool Wrap(RobotGeom g, float[] q, float[] reference)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = Norm180(q[i]);
                float target = reference != null ? reference[i] : 0f;
                float best = float.NaN, bestD = float.MaxValue;
                for (int k = -2; k <= 2; k++)
                {
                    float c = a + 360f * k;
                    if (c < g.qMin[i] || c > g.qMax[i]) continue;
                    float d = Mathf.Abs(c - target);
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (float.IsNaN(best)) return false;
                q[i] = best;
            }
            return true;
        }

        /// <summary>All (up to 8) analytic solutions inside the joint limits for a TCP pose in the base frame.</summary>
        public static int SolveAll(RobotGeom g, float tool, Vector3 p, Quaternion r, float[] reference, List<float[]> sols)
        {
            sols.Clear();
            Vector3 wc = p - r * new Vector3(0f, 0f, g.d6 + tool);
            float hd = Mathf.Sqrt(wc.x * wc.x + wc.z * wc.z);
            float qa = Mathf.Atan2(wc.x, wc.z) * Mathf.Rad2Deg;
            float h = wc.y - g.d1;
            for (int bi = 0; bi < 2; bi++)
            {
                if (hd < 1e-4f && bi == 1) break;
                float q0 = bi == 0 ? qa : qa + 180f;
                float rr = (bi == 0 ? hd : -hd) - g.a1;
                float D = (rr * rr + h * h - g.L2 * g.L2 - g.L3 * g.L3) / (2f * g.L2 * g.L3);
                if (D > 1f || D < -1f) continue;
                float relBase = Mathf.Acos(D);
                for (int ei = 0; ei < 2; ei++)
                {
                    float rel = ei == 0 ? relBase : -relBase;
                    float phi2 = Mathf.Atan2(h, rr) - Mathf.Atan2(g.L3 * Mathf.Sin(rel), g.L2 + g.L3 * Mathf.Cos(rel));
                    float q1 = -phi2 * Mathf.Rad2Deg, q2 = -rel * Mathf.Rad2Deg;
                    Quaternion r03 = Quaternion.AngleAxis(q0, Vector3.up) * Quaternion.AngleAxis(q1, Vector3.right) *
                                     Quaternion.AngleAxis(q2, Vector3.right);
                    Quaternion rw = Quaternion.Inverse(r03) * r;
                    Vector3 f = rw * Vector3.forward;
                    float b = Mathf.Acos(Mathf.Clamp(f.z, -1f, 1f));
                    for (int wi = 0; wi < 2; wi++)
                    {
                        float q4r = wi == 0 ? b : -b;
                        float s5 = Mathf.Sin(q4r);
                        bool singular = Mathf.Abs(s5) < 1e-4f;
                        float q3 = singular ? (reference != null ? reference[3] : 0f)
                                            : Mathf.Atan2(f.x / s5, -f.y / s5) * Mathf.Rad2Deg;
                        float q4 = q4r * Mathf.Rad2Deg;
                        Quaternion rz6 = Quaternion.Inverse(Quaternion.AngleAxis(q3, Vector3.forward) *
                                                            Quaternion.AngleAxis(q4, Vector3.right)) * rw;
                        Vector3 v = rz6 * Vector3.right;
                        float q5 = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                        var q = new[] { q0, q1, q2, q3, q4, q5 };
                        if (Wrap(g, q, reference)) sols.Add(q);
                        if (singular) break;
                    }
                }
            }
            return sols.Count;
        }

        /// <summary>Penalty for awkward postures: wrist singularity, joint-limit proximity, elbow-down, stretched elbow.</summary>
        public static float PostureCost(RobotGeom g, float[] q)
        {
            float c = 0f;
            float a5 = Mathf.Abs(q[4]);
            if (a5 < 12f) c += (12f - a5) * 3f;
            for (int i = 0; i < 6; i++)
            {
                float m = Mathf.Min(q[i] - g.qMin[i], g.qMax[i] - q[i]);
                if (m < 10f) c += (10f - m) * 2f;
            }
            if (q[2] < 5f) c += 40f + (5f - q[2]);          // elbow-down / stretched
            if (q[1] < -115f) c += (-115f - q[1]) * 2f;     // reaching over the back
            c += 0.01f * (Mathf.Abs(q[1] - RobotGeom.Home[1]) + Mathf.Abs(q[2] - RobotGeom.Home[2]) + Mathf.Abs(q[3]) * 0.5f);
            return c;
        }

        public static float Continuity(float[] a, float[] b)
        {
            float s = 0f;
            for (int i = 0; i < 6; i++) s += ContW[i] * Mathf.Abs(a[i] - b[i]);
            return s;
        }

        /// <summary>
        /// Best solution. follow=true: closest to <paramref name="reference"/> (short linear moves);
        /// otherwise the most comfortable posture, using the reference only to pick joint aliases.
        /// </summary>
        public static bool Solve(RobotGeom g, float tool, Tf target, float[] reference, bool follow, float[] result, out float cost)
        {
            cost = float.MaxValue;
            SolveAll(g, tool, target.p, target.r, reference, buf);
            int best = -1;
            for (int i = 0; i < buf.Count; i++)
            {
                var q = buf[i];
                float c = PostureCost(g, q);
                if (reference != null) c += (follow ? 1f : 0.004f) * Continuity(q, reference);
                if (c < cost) { cost = c; best = i; }
            }
            if (best < 0) return false;
            System.Array.Copy(buf[best], result, 6);
            cost = PostureCost(g, result);
            return true;
        }

        /// <summary>Time for a coordinated joint move (all joints arrive together) at a speed fraction, plus acceleration overhead.</summary>
        public static float JointMoveTime(RobotGeom g, float[] a, float[] b, float speedFrac)
        {
            float t = 0f;
            for (int i = 0; i < 6; i++) t = Mathf.Max(t, Mathf.Abs(a[i] - b[i]) / (g.vMax[i] * speedFrac));
            return t < 1e-3f ? 0f : t + 0.5f;
        }
    }
}
