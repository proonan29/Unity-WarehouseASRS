using System.Collections.Generic;
using UnityEngine;

namespace WarehouseASRS.Core
{
    /// <summary>A vehicle (AMR or self-driving car) following a route on the path network.</summary>
    public class Mover
    {
        public int id;
        public string name;
        public Lane cls;
        public Vector3 pos;
        public float yaw;
        public Vector3 vel;
        public float halfLen = 0.9f, speed = 1.5f, rotSpeed = 60f;
        public bool canRotate = true;
        public bool reverse;          // currently moving backwards
        public int node;              // last node reached (or parked at)

        public List<int> pathNodes;   // node ids, [0] = start
        public List<int> pathEdges;
        public float[] cum;
        public float along;
        public int seg;
        public bool Moving => pathNodes != null;
        public float waitRun, waitTotal, dist, moveTime, rotTime;
        public bool blocked;
        public int forcedCount, forceLevel;
        public float forceUntil = -1f;
        public readonly List<int> held = new List<int>();
        public readonly List<int> blockers = new List<int>();   // vehicles holding what this one waits for

        public float Remaining => pathNodes == null ? 0f : Mathf.Max(0f, cum[cum.Length - 1] - along);
        public int Destination => pathNodes == null ? node : pathNodes[pathNodes.Count - 1];
    }

    /// <summary>
    /// Zone control on the path network: a vehicle may only occupy path intervals whose nodes it holds; a node can be taken
    /// only if neither it nor any conflicting node is held by another vehicle. Nodes inside an intersection zone are taken atomically
    /// up to the next safe node, so vehicles only ever wait outside intersections (no blocking the box, no circular waits there).
    /// </summary>
    public class Traffic
    {
        public readonly Graph g;
        public readonly int[] owner;
        public readonly List<Mover> movers = new List<Mover>();
        const float Margin = 0.25f;
        public int deadlockBreaks;
        public float now;
        readonly List<int> runBuf = new List<int>();

        public Traffic(Graph g)
        {
            this.g = g;
            owner = new int[g.nodes.Count];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
        }

        public Mover Add(int id, string name, Lane cls, int node, float yaw)
        {
            var m = new Mover { id = id, name = name, cls = cls, node = node, pos = g.nodes[node].pos, yaw = yaw };
            movers.Add(m);
            Take(m, node);
            return m;
        }

        public void Remove(Mover m)
        {
            foreach (var n in m.held) if (owner[n] == m.id) owner[n] = -1;
            m.held.Clear();
            movers.Remove(m);
        }

        void Take(Mover m, int n) { if (owner[n] != m.id) { owner[n] = m.id; m.held.Add(n); } }
        void Release(Mover m, int n) { if (owner[n] == m.id) owner[n] = -1; m.held.Remove(n); }

        public bool CanTake(Mover m, int n)
        {
            bool forced = now < m.forceUntil;
            if (forced && m.forceLevel >= 2) return true;
            if (owner[n] >= 0 && owner[n] != m.id) return false;
            if (forced) return true;
            foreach (var c in g.nodes[n].conflicts)
                if (owner[c] >= 0 && owner[c] != m.id) return false;
            return true;
        }

        /// <summary>Plans a route to <paramref name="target"/>. Returns false if unreachable.</summary>
        public bool Go(Mover m, int target)
        {
            if (m.node == target) { m.pathNodes = null; m.pathEdges = null; return true; }
            var route = g.Route(m.node, target, m.cls);
            if (route == null) return false;
            m.pathEdges = route;
            m.pathNodes = new List<int> { m.node };
            foreach (var e in route) m.pathNodes.Add(g.edges[e].to);
            m.cum = new float[m.pathNodes.Count];
            for (int i = 1; i < m.cum.Length; i++) m.cum[i] = m.cum[i - 1] + g.edges[route[i - 1]].length;
            m.along = 0f; m.seg = 0;
            return true;
        }

        static float Lo(Mover m, int j) => j == 0 ? -999f : m.cum[j] - 0.5f * (m.cum[j] - m.cum[j - 1]);
        static float Hi(Mover m, int j) => j == m.cum.Length - 1 ? 999f : m.cum[j] + 0.5f * (m.cum[j + 1] - m.cum[j]);

        public void Step(Mover m, float dt)
        {
            m.vel = Vector3.zero;
            if (m.pathNodes == null) { ReleaseBehindParked(m); return; }
            var cum = m.cum;
            int last = cum.Length - 1;
            float front = m.along + m.halfLen + Margin;

            // ---- acquire nodes ahead
            float limit = cum[last];
            for (int j = m.seg + 1; j <= last; j++)
            {
                int n = m.pathNodes[j];
                if (owner[n] == m.id) continue;
                if (Lo(m, j) > front + m.speed * dt + 0.5f) { limit = Lo(m, j) - m.halfLen - Margin; break; }
                // take the intersection zone atomically, up to a safe node far enough past it for the whole body to clear the zone
                int k = j, lastUnsafe = -1;
                runBuf.Clear();
                while (true)
                {
                    runBuf.Add(m.pathNodes[k]);
                    bool safe = g.nodes[m.pathNodes[k]].safe;
                    if (!safe) lastUnsafe = k;
                    if (k == last) break;
                    if (safe && !g.nodes[m.pathNodes[k + 1]].spur && (lastUnsafe < 0 || cum[k] - cum[lastUnsafe] >= 2f * m.halfLen + 0.6f)) break;
                    k++;
                }
                bool ok = true;
                foreach (var n2 in runBuf) if (!CanTake(m, n2)) { ok = false; break; }
                if (!ok) { limit = Lo(m, j) - m.halfLen - Margin; CollectBlockers(m); break; }
                foreach (var n2 in runBuf) Take(m, n2);
                j = k;
            }
            limit = Mathf.Min(limit, cum[last]);

            // ---- rotate in place at the start of an edge (AMRs); cars steer through
            var e = g.edges[m.pathEdges[m.seg]];
            float wantYaw = e.reverse ? e.Yaw + 180f : e.Yaw;
            m.reverse = e.reverse;
            if (m.canRotate)
            {
                float dy = Mathf.DeltaAngle(m.yaw, wantYaw);
                if (Mathf.Abs(dy) > 1f && m.along - cum[m.seg] < 0.05f)
                {
                    float step = m.rotSpeed * dt;
                    m.yaw += Mathf.Clamp(dy, -step, step);
                    m.rotTime += dt; m.waitRun = 0f; m.blocked = false;
                    return;
                }
            }
            m.yaw = wantYaw;

            // ---- move
            float d = Mathf.Min(m.speed * dt, limit - m.along);
            if (d > 1e-4f)
            {
                m.along += d;
                m.dist += d; m.moveTime += dt;
                m.waitRun = 0f; m.blocked = false; m.blockers.Clear();
                if (m.forceUntil < now) m.forceLevel = 0;
                while (m.seg < last - 1 && m.along >= cum[m.seg + 1]) m.seg++;
                var a = g.nodes[m.pathNodes[m.seg]].pos; var b = g.nodes[m.pathNodes[m.seg + 1]].pos;
                float t = (m.along - cum[m.seg]) / Mathf.Max(1e-4f, cum[m.seg + 1] - cum[m.seg]);
                var np = Vector3.Lerp(a, b, Mathf.Clamp01(t));
                m.vel = (np - m.pos) / dt;
                m.pos = np;
                m.node = m.pathNodes[m.seg];
            }
            else Wait(m, dt);

            // ---- release nodes behind the body
            float rear = m.along - m.halfLen - Margin;
            for (int i = m.held.Count - 1; i >= 0; i--)
            {
                int n = m.held[i];
                int j = m.pathNodes.IndexOf(n);
                if (j < 0) { if ((g.nodes[n].pos - m.pos).magnitude > m.halfLen + 1.5f) Release(m, n); continue; }
                if (Hi(m, j) < rear) Release(m, n);
            }

            // ---- arrival
            if (m.along >= cum[last] - 1e-4f)
            {
                m.node = m.pathNodes[last];
                m.pos = g.nodes[m.node].pos;
                m.pathNodes = null; m.pathEdges = null;
                m.reverse = false;
            }
        }

        void Wait(Mover m, float dt)
        {
            m.waitRun += dt; m.waitTotal += dt; m.blocked = true;
            if ((m.waitRun > 15f && InCycle(m)) || m.waitRun > 600f)
            {
                // safety valve (reported): first ignore conflict zones, then also node owners, for a few seconds
                m.forceLevel = Mathf.Min(2, m.forceLevel + 1);
                m.forceUntil = now + 8f;
                m.waitRun = 0f;
                m.forcedCount++;
                deadlockBreaks++;
            }
        }

        void CollectBlockers(Mover m)
        {
            m.blockers.Clear();
            foreach (var n in runBuf)
            {
                if (owner[n] >= 0 && owner[n] != m.id && !m.blockers.Contains(owner[n])) m.blockers.Add(owner[n]);
                foreach (var c in g.nodes[n].conflicts)
                    if (owner[c] >= 0 && owner[c] != m.id && !m.blockers.Contains(owner[c])) m.blockers.Add(owner[c]);
            }
        }

        readonly Dictionary<int, Mover> byId = new Dictionary<int, Mover>();
        readonly HashSet<int> seen = new HashSet<int>();

        /// <summary>True if the wait-for graph starting at m loops back to m (a real deadlock, not just a queue).</summary>
        bool InCycle(Mover m)
        {
            byId.Clear();
            foreach (var x in movers) byId[x.id] = x;
            seen.Clear();
            var stack = new Stack<int>();
            foreach (var b in m.blockers) stack.Push(b);
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                if (id == m.id) return true;
                if (!seen.Add(id) || !byId.TryGetValue(id, out var o)) continue;
                if (!o.blocked) continue;
                foreach (var b in o.blockers) stack.Push(b);
            }
            return false;
        }

        void ReleaseBehindParked(Mover m)
        {
            for (int i = m.held.Count - 1; i >= 0; i--)
            {
                int n = m.held[i];
                if (n == m.node) continue;
                if ((g.nodes[n].pos - m.pos).magnitude > m.halfLen + Margin + 0.2f) Release(m, n);
            }
        }

        /// <summary>Changes the destination of a moving vehicle: keeps its route up to the next node and re-routes from there.</summary>
        public bool Retarget(Mover m, int target)
        {
            if (m.pathNodes == null) return Go(m, target);
            int next = m.pathNodes[m.seg + 1];
            var route = g.Route(next, target, m.cls);
            if (route == null) return false;
            var nodes = m.pathNodes.GetRange(0, m.seg + 2);
            var edges = m.pathEdges.GetRange(0, m.seg + 1);
            foreach (var e in route) { edges.Add(e); nodes.Add(g.edges[e].to); }
            m.pathNodes = nodes; m.pathEdges = edges;
            m.cum = new float[nodes.Count];
            for (int i = 1; i < m.cum.Length; i++) m.cum[i] = m.cum[i - 1] + g.edges[edges[i - 1]].length;
            return true;
        }

        public void StepAll(float dt, float t)
        {
            now = t;
            for (int i = 0; i < movers.Count; i++) Step(movers[i], dt);
        }

        /// <summary>True if the mover holds none of the given nodes (it has left a spur).</summary>
        public static bool Clear(Mover m, int[] nodes)
        {
            if (m == null) return true;
            foreach (var n in nodes) if (m.held.Contains(n)) return false;
            return true;
        }
    }
}
