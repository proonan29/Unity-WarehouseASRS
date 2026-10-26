using System.Collections.Generic;
using UnityEngine;

namespace WarehouseASRS.Core
{
    /// <summary>Vehicle classes allowed on an edge.</summary>
    [System.Flags]
    public enum Lane { None = 0, Parts = 1, Carrier = 2, Drive = 4, Vehicles = Carrier | Drive, All = 7 }

    public class Node
    {
        public int id;
        public Vector3 pos;
        public bool junction;   // vehicles may turn / rotate here: larger conflict radius
        public bool safe;       // plain node clear of every junction zone: vehicles may wait here without blocking an intersection
        public float radius;
        public bool spur;       // node inside a dead-end spur (entered atomically together with the whole spur)
        public bool dead;       // spliced out (too close to an intersection to be a useful waiting point)
        public int[] conflicts = new int[0];
        public readonly List<int> outEdges = new List<int>();
        public readonly List<int> inEdges = new List<int>();
    }

    public class Edge
    {
        public int id, from, to;
        public float length;
        public Lane mask;
        public bool reverse;    // traversed backwards (vehicle keeps its heading): spur exits
        public float Yaw;       // travel direction
    }

    /// <summary>Directed path network with node conflict sets (zone control) and Dijkstra routing.</summary>
    public class Graph
    {
        public readonly List<Node> nodes = new List<Node>();
        public readonly List<Edge> edges = new List<Edge>();
        readonly Dictionary<long, int> index = new Dictionary<long, int>();
        public const float Spacing = 2.0f;
        public const float NormalRadius = 0.75f;   // half width of an AMR plus margin
        public const float JunctionRadius = 1.3f;  // unused (single AMR class)
        public const float PartsJunctionRadius = 1.15f; // swept radius of an AMR rotating in place

        static long Key(Vector3 p) => ((long)Mathf.RoundToInt(p.x * 20f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 20f);

        public int NodeAt(Vector3 p)
        {
            p.y = 0f;
            var k = Key(p);
            if (index.TryGetValue(k, out var i)) return i;
            var n = new Node { id = nodes.Count, pos = p };
            nodes.Add(n);
            index[k] = n.id;
            return n.id;
        }

        public bool TryFind(Vector3 p, out int id) { p.y = 0f; return index.TryGetValue(Key(p), out id); }

        public int AddEdge(int a, int b, Lane mask, bool reverse = false)
        {
            foreach (var ei in nodes[a].outEdges)
            {
                var e0 = edges[ei];
                if (e0.to == b && e0.reverse == reverse) { e0.mask |= mask; return ei; }
            }
            var d = nodes[b].pos - nodes[a].pos;
            var e = new Edge { id = edges.Count, from = a, to = b, length = d.magnitude, mask = mask, reverse = reverse, Yaw = MathX.YawOf(d) };
            edges.Add(e);
            nodes[a].outEdges.Add(e.id);
            nodes[b].inEdges.Add(e.id);
            return e.id;
        }

        /// <summary>One-way lane through the given stops; segments are subdivided so nodes are at most Spacing apart.</summary>
        public void AddLane(IList<Vector3> stops, Lane mask)
        {
            for (int i = 0; i + 1 < stops.Count; i++)
            {
                var a = stops[i]; var b = stops[i + 1];
                float len = (b - a).magnitude;
                if (len < 0.05f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / Spacing - 1e-3f));
                int prev = NodeAt(a);
                for (int k = 1; k <= n; k++)
                {
                    int cur = NodeAt(Vector3.Lerp(a, b, k / (float)n));
                    AddEdge(prev, cur, mask);
                    prev = cur;
                }
            }
        }

        /// <summary>Dead-end spur: entered forward, left in reverse (vehicle classes in revMask) or forward (Drive).</summary>
        public void AddSpur(IList<Vector3> pts, Lane inMask, Lane revMask, Lane fwdOutMask = Lane.None)
        {
            var ids = new int[pts.Count];
            for (int i = 0; i < pts.Count; i++) { ids[i] = NodeAt(pts[i]); if (i > 0) nodes[ids[i]].spur = true; }
            for (int i = 0; i + 1 < ids.Length; i++)
            {
                if (inMask != Lane.None) AddEdge(ids[i], ids[i + 1], inMask);
                if (revMask != Lane.None) AddEdge(ids[i + 1], ids[i], revMask, true);
                if (fwdOutMask != Lane.None) AddEdge(ids[i + 1], ids[i], fwdOutMask, false);
            }
        }

        /// <summary>Marks junctions (3+ neighbours or a turn) and computes conflict sets.</summary>
        public void Finish()
        {
            foreach (var n in nodes)
            {
                var nb = new HashSet<int>();
                foreach (var e in n.outEdges) nb.Add(edges[e].to);
                foreach (var e in n.inEdges) nb.Add(edges[e].from);
                bool turn = false;
                foreach (var ie in n.inEdges)
                    foreach (var oe in n.outEdges)
                    {
                        var a = edges[ie]; var b = edges[oe];
                        if (a.from == b.to) continue;
                        if (Mathf.Abs(Mathf.DeltaAngle(a.Yaw, b.Yaw)) > 20f) turn = true;
                    }
                n.junction = nb.Count >= 3 || turn;
            }
            // splice out plain lane nodes that sit inside an intersection's clearance: nobody should wait there
            for (int pass = 0; pass < 3; pass++)
                foreach (var n in nodes)
                {
                    if (n.dead || n.junction || n.inEdges.Count != 1 || n.outEdges.Count != 1) continue;
                    var ein = edges[n.inEdges[0]]; var eout = edges[n.outEdges[0]];
                    if (ein.reverse || eout.reverse || ein.from == eout.to) continue;
                    bool close = false;
                    foreach (var m in nodes)
                    {
                        if (m.dead || !m.junction) continue;
                        float r = RadiusOf(m) + NormalRadius;
                        if ((m.pos - n.pos).sqrMagnitude < r * r) { close = true; break; }
                    }
                    if (!close) continue;
                    nodes[ein.from].outEdges.Remove(ein.id); n.inEdges.Clear();
                    nodes[eout.to].inEdges.Remove(eout.id); n.outEdges.Clear();
                    AddEdge(ein.from, eout.to, ein.mask & eout.mask);
                    n.dead = true;
                }
            foreach (var n in nodes)
            {
                bool car = false;
                foreach (var e in n.outEdges) if ((edges[e].mask & Lane.Vehicles) != 0) car = true;
                foreach (var e in n.inEdges) if ((edges[e].mask & Lane.Vehicles) != 0) car = true;
                n.radius = n.junction ? (car ? JunctionRadius : PartsJunctionRadius) : NormalRadius;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                var list = new List<int>();
                var a = nodes[i];
                if (a.dead) { a.conflicts = new int[0]; continue; }
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (i == j) continue;
                    var b = nodes[j];
                    if (b.dead) continue;
                    float r = a.radius + b.radius;
                    if ((a.pos - b.pos).sqrMagnitude < r * r) list.Add(j);
                }
                a.conflicts = list.ToArray();
            }
            foreach (var n in nodes)
            {
                // a vehicle may wait at a node that does not overlap any (other) intersection node,
                // e.g. plain lane nodes and isolated dock entries; clustered junctions form zones crossed atomically
                n.safe = !n.dead;
                foreach (var c in n.conflicts) if (nodes[c].junction) n.safe = false;
            }
        }
        float RadiusOf(Node n)
        {
            bool car = false;
            foreach (var e in n.outEdges) if ((edges[e].mask & Lane.Vehicles) != 0) car = true;
            foreach (var e in n.inEdges) if ((edges[e].mask & Lane.Vehicles) != 0) car = true;
            return car ? JunctionRadius : PartsJunctionRadius;
        }

        // ------------------------------------------------------------------ routing

        float[] dist; int[] prevEdge; bool[] done;

        /// <summary>Shortest route (edge ids) from node a to node b for a vehicle class. Null if unreachable.</summary>
        public List<int> Route(int a, int b, Lane cls)
        {
            if (a == b) return new List<int>();
            int n = nodes.Count;
            if (dist == null || dist.Length != n) { dist = new float[n]; prevEdge = new int[n]; done = new bool[n]; }
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prevEdge[i] = -1; done[i] = false; }
            var heap = new SortedSet<(float, int)>();
            dist[a] = 0f; heap.Add((0f, a));
            while (heap.Count > 0)
            {
                var (d, u) = heap.Min; heap.Remove(heap.Min);
                if (done[u]) continue;
                done[u] = true;
                if (u == b) break;
                foreach (var ei in nodes[u].outEdges)
                {
                    var e = edges[ei];
                    if ((e.mask & cls) == 0) continue;
                    // a vehicle that arrived on a reverse edge may not continue with another reverse edge into a different spur
                    float w = e.length + (nodes[e.to].junction ? 0.5f : 0f) + (e.reverse ? 0.5f : 0f);
                    float nd = d + w;
                    if (nd < dist[e.to])
                    {
                        if (dist[e.to] < float.MaxValue) heap.Remove((dist[e.to], e.to));
                        dist[e.to] = nd; prevEdge[e.to] = ei;
                        heap.Add((nd, e.to));
                    }
                }
            }
            if (prevEdge[b] < 0) return null;
            var path = new List<int>();
            int cur = b;
            while (cur != a) { int ei = prevEdge[cur]; path.Add(ei); cur = edges[ei].from; }
            path.Reverse();
            return path;
        }

        public float RouteLength(List<int> route)
        {
            float s = 0f;
            if (route != null) foreach (var e in route) s += edges[e].length;
            return s;
        }
    }
}
