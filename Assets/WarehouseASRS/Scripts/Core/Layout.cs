using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WarehouseASRS.Core
{
    /// <summary>
    /// Warehouse geometry (metres, +Y up). Racks run along +X; aisle i sits at z = aisleZ[i] with a rack row on each side.
    /// The AMR ring is one-way: lane A (x = -1, northbound) passes the stacker docks, lane B (x = -10, southbound) passes the
    /// inbound loaders and picking stations. Charging pads sit inside the ring.
    /// </summary>
    public class WarehouseLayout
    {
        public const float BoxL = 0.6f, BoxW = 0.4f, BoxH = 0.32f;
        public const float BayPitch = 0.7f, LevelPitch = 0.45f, Level0Y = 0.72f, RowOff = 0.925f, RackX0 = 6f;
        public const float AisleWidth = 1.2f, AislePitch = 2.5f;
        public const float RingXA = -1f, RingXB = -10f, DockXStack = 2.4f, LaneJogX = 3.3f;
        public const float DeckY = 0.51f, ConvY = 0.96f;
        public const float StationDockX = -14f, InboundHeadX = -16f, InboundSpawnX = -32f;

        public readonly WarehouseConfig cfg;
        public readonly Graph graph = new Graph();
        public readonly List<Dock> docks = new List<Dock>();
        public float H;
        public float[] aisleZ, stationZc, inboundZ;
        public Dock[] stackerDocks, stationDocks, inboundDocks, padDocks;

        public float IoX => RackX0 - 0.35f;
        public float IoY => Level0Y;
        public float RackX1 => RackX0 + cfg.bays * BayPitch;
        public float RackTopY => Level0Y + (cfg.levels - 1) * LevelPitch + 0.5f;
        public int PerStacker => cfg.SlotsPerStacker;

        public WarehouseLayout(WarehouseConfig c)
        {
            cfg = c;
            int S = c.stackers, K = c.stations;
            H = Mathf.Max(AislePitch * S + 8f, 18f + 9f * K);
            aisleZ = new float[S];
            for (int i = 0; i < S; i++) aisleZ[i] = H * 0.5f + (i - (S - 1) * 0.5f) * AislePitch;
            inboundZ = new[] { H - 3f, H - 6f };
            stationZc = new float[K];
            for (int k = 0; k < K; k++) stationZc[k] = H - 15f - 9f * k;

            // charging pads (inside the ring): column A on lane A, column B on lane B, away from other spurs
            var padA = new List<float>(); var padB = new List<float>();
            for (float z = 3f; z <= H - 3f + 0.01f; z += 2.6f)
            {
                if (!aisleZ.Any(a => Mathf.Abs(a - z) < 2.4f)) padA.Add(z);
                if (!inboundZ.Any(a => Mathf.Abs(a - z) < 2.4f) && !stationZc.Any(a => Mathf.Abs(a - z) < 2.4f)) padB.Add(z);
            }
            var padPick = new List<(bool a, float z)>();
            int ia = 0, ib = 0;
            for (int i = 0; i < c.amrs; i++)
            {
                bool wantA = i % 2 == 0;
                if (wantA && ia >= padA.Count) wantA = false;
                if (!wantA && ib >= padB.Count) wantA = true;
                if (wantA && ia < padA.Count) padPick.Add((true, padA[ia++]));
                else if (!wantA && ib < padB.Count) padPick.Add((false, padB[ib++]));
                else throw new System.Exception("not enough charging pads");
            }

            // ring lane stops
            var za = new List<float> { 0f, H };
            za.AddRange(aisleZ); za.AddRange(padPick.Where(p => p.a).Select(p => p.z));
            za.Sort();
            var zb = new List<float> { 0f, H };
            zb.AddRange(inboundZ); zb.AddRange(stationZc); zb.AddRange(padPick.Where(p => !p.a).Select(p => p.z));
            zb.Sort(); zb.Reverse();
            var ring = new List<Vector3>();
            foreach (var z in za) ring.Add(new Vector3(RingXA, 0f, z));
            foreach (var z in zb) ring.Add(new Vector3(RingXB, 0f, z));
            ring.Add(new Vector3(RingXA, 0f, 0f));
            graph.AddLane(ring, Lane.Parts);

            stackerDocks = new Dock[S];
            for (int i = 0; i < S; i++)
                stackerDocks[i] = MakeDock("S" + (i + 1), DockKind.Stacker, i, 90f, RingXA, aisleZ[i], new[] { 0.7f, DockXStack });
            inboundDocks = new Dock[inboundZ.Length];
            for (int j = 0; j < inboundZ.Length; j++)
                inboundDocks[j] = MakeDock("IN" + (j + 1), DockKind.Inbound, j, -90f, RingXB, inboundZ[j], new[] { -12f, StationDockX });
            stationDocks = new Dock[K];
            for (int k = 0; k < K; k++)
                stationDocks[k] = MakeDock("PK" + (k + 1), DockKind.Station, k, -90f, RingXB, stationZc[k], new[] { -12f, StationDockX });
            padDocks = new Dock[c.amrs];
            for (int i = 0; i < c.amrs; i++)
            {
                var (a, z) = padPick[i];
                padDocks[i] = a ? MakeDock("C" + (i + 1), DockKind.Pad, i, -90f, RingXA, z, new[] { -4f })
                                : MakeDock("C" + (i + 1), DockKind.Pad, i, 90f, RingXB, z, new[] { -7f });
            }
            graph.Finish();
        }

        Dock MakeDock(string name, DockKind kind, int owner, float yaw, float ringX, float z, float[] xs)
        {
            var pts = new List<Vector3> { new Vector3(ringX, 0f, z) };
            foreach (var x in xs) pts.Add(new Vector3(x, 0f, z));
            graph.AddSpur(pts, Lane.Parts, Lane.Parts);
            var d = new Dock
            {
                id = docks.Count, name = name, kind = kind, owner = owner, yaw = yaw,
                entry = graph.NodeAt(pts[0]), node = graph.NodeAt(pts[pts.Count - 1]), pos = pts[pts.Count - 1],
                spurNodes = pts.Skip(1).Select(p => graph.NodeAt(p)).ToArray()
            };
            docks.Add(d);
            return d;
        }

        // ------------------------------------------------------------------ rack slots

        public (int s, int side, int bay, int level) Decode(int slot)
        {
            int s = slot / PerStacker, local = slot % PerStacker;
            int per = cfg.bays * cfg.levels;
            return (s, local / per, (local % per) / cfg.levels, local % cfg.levels);
        }

        public int Encode(int s, int side, int bay, int level) => s * PerStacker + (side * cfg.bays + bay) * cfg.levels + level;

        public Vector3 SlotPos(int slot)
        {
            var (s, side, bay, level) = Decode(slot);
            return new Vector3(RackX0 + (bay + 0.5f) * BayPitch, Level0Y + level * LevelPitch, aisleZ[s] + (side == 0 ? -RowOff : RowOff));
        }

        public Vector2 SlotXY(int slot)
        {
            var (_, _, bay, level) = Decode(slot);
            return new Vector2(RackX0 + (bay + 0.5f) * BayPitch, Level0Y + level * LevelPitch);
        }

        public int SlotSide(int slot) => Decode(slot).side == 0 ? -1 : 1;

        /// <summary>Crane time from the I/O point to a slot (used to rank slots: shorter = better).</summary>
        public float SlotTime(int slot)
        {
            var p = SlotXY(slot);
            return Mathf.Max(Kin.TravelTime(p.x - IoX, Crane.Vx, Crane.Ax), Kin.TravelTime(p.y - IoY, Crane.Vy, Crane.Ay));
        }

        // ------------------------------------------------------------------ stacker I/O queues and AMR paths

        public Vector3 InSlotPos(int s, int k) => new Vector3(IoX - 0.8f * k, Level0Y, aisleZ[s] - RowOff);
        public Vector3 OutSlotPos(int s, int k) => new Vector3(IoX - 0.8f * k, Level0Y, aisleZ[s] + RowOff);
        public Vector3 StackerDeck(int s) => new Vector3(DockXStack, DeckY, aisleZ[s]);

        public Vector3[] StackerInPath(int s, int k) => new[]
        {
            StackerDeck(s), new Vector3(LaneJogX, Level0Y, aisleZ[s]), new Vector3(LaneJogX, Level0Y, aisleZ[s] - RowOff), InSlotPos(s, k)
        };

        public Vector3[] StackerOutPath(int s, int k) => new[]
        {
            OutSlotPos(s, k), new Vector3(LaneJogX, Level0Y, aisleZ[s] + RowOff), new Vector3(LaneJogX, Level0Y, aisleZ[s]), StackerDeck(s)
        };

        // ------------------------------------------------------------------ inbound lines

        public Vector3 InboundDeck(int j) => new Vector3(StationDockX, DeckY, inboundZ[j]);
        public Vector3 InboundPos(int j, int k) => new Vector3(InboundHeadX - 0.8f * k, ConvY, inboundZ[j]);
        public Vector3[] InboundSpawnPath(int j, int k) => new[] { new Vector3(InboundSpawnX, ConvY, inboundZ[j]), InboundPos(j, k) };
        public Vector3[] InboundPickPath(int j) => new[] { InboundPos(j, 0), new Vector3(StationDockX - 1f, ConvY, inboundZ[j]), InboundDeck(j) };

        // ------------------------------------------------------------------ picking stations

        public Vector3 StationRobotBase(int k) => new Vector3(-20.8f, 0f, stationZc[k]);
        public Vector3 StationDeck(int k) => new Vector3(StationDockX, DeckY, stationZc[k]);
        public Vector3 StationInPos(int k, int i) => new Vector3(-15.4f - 0.8f * i, ConvY, stationZc[k]);
        public Vector3 StationOutPos(int k, int i) => new Vector3(-16f - 0.8f * i, ConvY, stationZc[k] + 1.2f);
        public Vector3 StationSrcPos(int k) => new Vector3(-19f, ConvY, stationZc[k] + 1.2f);
        public Vector3 StationOrdPos(int k) => new Vector3(-19f, ConvY, stationZc[k] - 1.2f);
        public Vector3[] StationDropPath(int k, int i) => new[] { StationDeck(k), new Vector3(-14.8f, ConvY, stationZc[k]), StationInPos(k, i) };
        public Vector3[] StationToSrc(int k, int i) => new[] { StationInPos(k, i), new Vector3(-18.4f, ConvY, stationZc[k]), StationSrcPos(k) };
        public Vector3[] StationToOrd(int k, int i) => new[] { StationInPos(k, i), new Vector3(-18.4f, ConvY, stationZc[k]), StationOrdPos(k) };
        public Vector3[] StationSrcToOut(int k, int i) => new[] { StationSrcPos(k), StationOutPos(k, i) };
        public Vector3[] StationOutPickPath(int k) => new[] { StationOutPos(k, 0), new Vector3(-15f, ConvY, stationZc[k] + 1.2f), new Vector3(-15f, ConvY, stationZc[k]), StationDeck(k) };
        public Vector3[] ShipPath(int k) => new[] { StationOrdPos(k), new Vector3(-19f, ConvY, stationZc[k] - 3f), new Vector3(-28f, ConvY, stationZc[k] - 3f) };

        public Vector3 DockDeck(Dock d) => d.kind == DockKind.Stacker ? StackerDeck(d.owner) : d.kind == DockKind.Inbound ? InboundDeck(d.owner) : StationDeck(d.owner);

        public static Vector3 EvalPath(Vector3[] pts, float u)
        {
            u = Mathf.Clamp01(u);
            if (pts.Length == 1) return pts[0];
            float total = 0f;
            for (int i = 0; i + 1 < pts.Length; i++) total += (pts[i + 1] - pts[i]).magnitude;
            float d = u * total;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                float len = (pts[i + 1] - pts[i]).magnitude;
                if (d <= len || i == pts.Length - 2) return Vector3.Lerp(pts[i], pts[i + 1], len < 1e-4f ? 1f : Mathf.Clamp01(d / len));
                d -= len;
            }
            return pts[pts.Length - 1];
        }
    }
}
