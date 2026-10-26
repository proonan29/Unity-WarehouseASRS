using System.Collections.Generic;
using UnityEngine;

namespace WarehouseASRS.Core
{
    /// <summary>
    /// Pick-and-place cycle of the station's 6-axis robot with a suction gripper: source box -> order box, one item per cycle.
    /// Waypoints are solved once with the analytic IK (CellFactory/WeldingBot solver); poses are blended joint-space moves.
    /// </summary>
    public class PickRobotPlan
    {
        public const float Tool = 0.25f;
        static readonly float[] Seg = { 0.5f, 0.2f, 0.4f, 0.8f, 0.5f, 0.2f, 0.4f, 0.8f };   // sums to Station.ItemT (3.8 s)

        public readonly RobotGeom geom = RobotGeom.Default;
        public readonly Tf baseTf;
        readonly float[][] wp = new float[8][];   // waypoint per segment end
        public float[] qHome = (float[])RobotGeom.Home.Clone();
        public readonly bool ok;
        public readonly string info;
        public Vector3 srcWorld, ordWorld;

        public PickRobotPlan(Vector3 basePos, Vector3 src, Vector3 ord)
        {
            baseTf = new Tf(basePos, 90f);
            srcWorld = src; ordWorld = ord;
            float top = WarehouseLayout.BoxH * 0.5f;
            var sGrasp = src + Vector3.up * top; var sHover = sGrasp + Vector3.up * 0.55f;
            var oRel = ord + Vector3.up * (top + 0.05f); var oHover = oRel + Vector3.up * 0.55f;
            var qSH = Solve(sHover, qHome); var qSG = Solve(sGrasp, qSH); var qOH = Solve(oHover, qSH); var qOR = Solve(oRel, qOH);
            ok = qSH != null && qSG != null && qOH != null && qOR != null;
            if (!ok) { info = $"IK failed: srcHover={qSH != null} srcGrasp={qSG != null} ordHover={qOH != null} ordRel={qOR != null}"; return; }
            info = "ok";
            // segment end poses: 0 srcHover->srcGrasp, 1 hold, 2 up, 3 ->ordHover, 4 ->ordRel, 5 hold, 6 up, 7 ->srcHover
            wp[0] = qSG; wp[1] = qSG; wp[2] = qSH; wp[3] = qOH; wp[4] = qOR; wp[5] = qOR; wp[6] = qOH; wp[7] = qSH;
            start = qSH;
        }

        float[] start;

        float[] Solve(Vector3 world, float[] reference)
        {
            var local = baseTf.Inverse().Point(world);
            var rot = Quaternion.LookRotation(Vector3.down, new Vector3(local.x, 0f, local.z));
            var res = new float[6];
            return RobotKin.Solve(geom, Tool, new Tf(local, rot), reference, false, res, out _) ? res : null;
        }

        /// <summary>Joint angles at time rt (0..ItemT) of the item cycle, blended with the home pose.</summary>
        public float[] Pose(float rt, float blend, float[] q)
        {
            if (!ok) { System.Array.Copy(qHome, q, 6); return q; }
            float t = Mathf.Clamp(rt, 0f, Station.ItemT - 1e-3f);
            int s = 0;
            while (s < Seg.Length - 1 && t >= Seg[s]) { t -= Seg[s]; s++; }
            var a = s == 0 ? start : wp[s - 1]; var b = wp[s];
            float u = MathX.Smooth(t / Seg[s]);
            float k = Mathf.SmoothStep(0f, 1f, blend);
            for (int i = 0; i < 6; i++) q[i] = Mathf.Lerp(qHome[i], Mathf.Lerp(a[i], b[i], u), k);
            return q;
        }

        /// <summary>True while an item is held by the suction cup (grasped until released over the order box).</summary>
        public static bool Holding(float rt) => rt > 0.6f && rt < 2.6f;

        public Vector3 TcpWorld(float[] q) => baseTf.Point(RobotKin.FK(geom, Tool, q).p);
        public Tf FlangeWorld(float[] q) { var f = RobotKin.FK(geom, 0f, q); return baseTf * f; }

        /// <summary>Reach test of the waypoints.</summary>
        public static string Test(WarehouseLayout L)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < L.stationDocks.Length; k++)
            {
                var p = new PickRobotPlan(L.StationRobotBase(k), L.StationSrcPos(k), L.StationOrdPos(k));
                sb.AppendLine($"station {k + 1}: {p.info}");
                if (p.ok)
                {
                    var q = new float[6];
                    float maxErr = 0f;
                    foreach (var t in new[] { 0.3f, 0.6f, 1.4f, 2.0f, 2.9f, 3.5f })
                    {
                        p.Pose(t, 1f, q);
                        var tcp = p.TcpWorld(q);
                        maxErr = Mathf.Max(maxErr, 0f * tcp.x);
                    }
                    sb.AppendLine($"  joints at grasp: {string.Join(",", System.Array.ConvertAll(p.wp[0], v => v.ToString("0")))}  at release: {string.Join(",", System.Array.ConvertAll(p.wp[4], v => v.ToString("0")))}");
                    var chk = p.TcpWorld(p.wp[0]);
                    sb.AppendLine($"  TCP at grasp {chk} (target {p.srcWorld + Vector3.up * WarehouseLayout.BoxH * 0.5f})");
                }
            }
            return sb.ToString();
        }
    }
}
