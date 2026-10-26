using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WarehouseASRS.Core
{
    public partial class Warehouse
    {
        struct Cand { public int type; public Box box; public float prio; public Dock src; }

        const float MoveSpeed = 1.5f, LoadedSpeed = 1.2f, XferWait = Station.XferT + 0.5f;

        // ------------------------------------------------------------------ dispatch (every second)

        void Dispatch()
        {
            FetchOrders();
            SpawnEmptyIfNeeded();
            var idle = amrs.Where(a => a.Available).ToList();
            if (idle.Count == 0) return;
            var cands = new List<Cand>();
            foreach (var sn in stations)
                foreach (var b in sn.outBuf)
                    if (!b.taken && b.availT <= t) cands.Add(new Cand { type = 0, box = b, prio = (t - b.readyT) * 1.2f + 5f, src = sn.dock });
            foreach (var s in stackers)
                foreach (var b in s.outQ)
                    if (!b.taken && b.availT <= t) cands.Add(new Cand { type = 1, box = b, prio = (t - b.readyT) * 1.5f + 3f + (Rank(b) == 0 ? 20f : 0f), src = s.dock });
            foreach (var l in inlines)
                if (l.q.Count > 0 && !l.q[0].taken && l.q[0].availT <= t)
                    cands.Add(new Cand { type = 2, box = l.q[0], prio = (t - l.q[0].readyT), src = l.dock });
            cands.Sort((a, b) => b.prio.CompareTo(a.prio));
            foreach (var c in cands)
            {
                if (idle.Count == 0) break;
                TryAssign(c, idle);
            }
        }

        int Undelivered(Order o)
        {
            int n = 0;
            if (o.orderBox != null && !o.orderBox.taken && !AtStation(o.orderBox)) n++;
            foreach (var p in o.picks) if (!p.done && !p.box.taken && !AtStation(p.box)) n++;
            return n;
        }

        static bool AtStation(Box b) => b.loc == BoxLoc.StationBuf || b.loc == BoxLoc.StationWork || b.loc == BoxLoc.StationOut;

        float RouteLen(Amr a, int node)
        {
            var r = L.graph.Route(a.mv.node, node, Lane.Parts);
            if (r == null) return float.MaxValue;
            return L.graph.RouteLength(r) + (100f - a.battery) * 0.1f;
        }

        bool TryAssign(Cand c, List<Amr> idle)
        {
            if (c.src.load >= (c.src.kind == DockKind.Station ? 4 : 2)) return false;
            Dock dst; Stacker dstSt = null; Station dstSn = null; int slot = -1;
            if (c.type == 1)
            {
                var o = OrderOf(c.box);
                if (o == null) return false;
                var sn = stations[o.station];
                int room = Station.InCap - (sn.inBuf.Count + sn.inResv);
                if (room <= 0) return false;
                if (sn.queue.IndexOf(o) != 0 && room <= Undelivered(sn.queue[0])) return false;
                if (sn.dock.load >= 4) return false;
                dst = sn.dock; dstSn = sn;
            }
            else
            {
                if (!SelectSlot(c.box, false, out dstSt, out slot)) return false;
                dst = dstSt.dock;
                if (dst.load >= 2) { Release(slot); return false; }
            }
            Amr best = null; float bl = float.MaxValue;
            foreach (var a in idle)
            {
                float len = RouteLen(a, c.src.node);
                if (len < bl) { bl = len; best = a; }
            }
            if (best == null || bl >= float.MaxValue) { if (slot >= 0) Release(slot); return false; }

            c.box.taken = true;
            if (dstSt != null) dstSt.inResv++; else dstSn.inResv++;
            best.stops.Add(new Stop { dock = c.src, pick = true, box = c.box });
            best.stops.Add(new Stop { dock = dst, pick = false, box = c.box, destSlot = slot });
            c.src.load++; dst.load++;
            best.jobs++;
            idle.Remove(best);
            return true;
        }

        // ------------------------------------------------------------------ AMR behaviour

        void StepAmr(Amr a)
        {
            var mv = a.mv;
            bool moving = mv.Moving;
            if (moving) a.battery = Mathf.Max(0f, a.battery - 0.012f * Dt);
            else if (a.state == AmrState.Charging) a.battery = Mathf.Min(100f, a.battery + 0.1f * Dt);
            if (a.battery < 22f) a.lowBat = true; else if (a.battery >= 70f) a.lowBat = false;
            if (a.state == AmrState.ToStop || a.state == AmrState.Transfer || a.load != null) a.busyTime += Dt;

            switch (a.state)
            {
                case AmrState.Idle:
                case AmrState.Charging:
                    if (a.stops.Count > 0) BeginStops(a);
                    break;
                case AmrState.ToPad:
                    if (a.stops.Count > 0) { BeginStops(a); break; }
                    if (!moving)
                    {
                        if (mv.node == a.pad.node) a.state = AmrState.Charging;
                        else traffic.Go(mv, a.pad.node);
                    }
                    break;
                case AmrState.ToStop:
                    if (!moving)
                    {
                        var d = a.stops[0].dock;
                        if (mv.node == d.node) StartTransfer(a);
                        else traffic.Go(mv, d.node);
                    }
                    break;
                case AmrState.Transfer:
                    a.timer -= Dt;
                    if (a.timer <= 0f) CompleteStop(a);
                    break;
            }
        }

        void BeginStops(Amr a)
        {
            a.state = AmrState.ToStop;
            var d = a.stops[0].dock;
            if (a.mv.Moving) traffic.Retarget(a.mv, d.node); else traffic.Go(a.mv, d.node);
        }

        void StartTransfer(Amr a)
        {
            var s = a.stops[0]; var d = s.dock; var b = s.box;
            Vector3[] path;
            if (s.pick)
            {
                switch (d.kind)
                {
                    case DockKind.Inbound: path = (Vector3[])L.InboundPickPath(d.owner).Clone(); break;
                    case DockKind.Stacker: path = (Vector3[])L.StackerOutPath(d.owner, b.queueIdx).Clone(); break;
                    default: path = (Vector3[])L.StationOutPickPath(d.owner).Clone(); break;
                }
                path[0] = BoxPos(b, t);
            }
            else if (d.kind == DockKind.Stacker)
            {
                int idx = Mathf.Min(stackers[d.owner].inQ.Count, Stacker.InCap - 1);
                path = L.StackerInPath(d.owner, idx);
            }
            else
            {
                int idx = Mathf.Min(stations[d.owner].inBuf.Count, Station.InCap - 1);
                path = L.StationDropPath(d.owner, idx);
            }
            b.Move(path, t, Station.XferT);
            a.timer = XferWait;
            a.state = AmrState.Transfer;
        }

        void CompleteStop(Amr a)
        {
            var s = a.stops[0]; var d = s.dock; var b = s.box;
            if (s.pick)
            {
                switch (d.kind)
                {
                    case DockKind.Inbound:
                        {
                            var line = inlines[d.owner];
                            line.q.Remove(b);
                            ShiftQueue(line.q, i => L.InboundPos(line.idx, i));
                            break;
                        }
                    case DockKind.Stacker:
                        {
                            var st = stackers[d.owner];
                            st.outQ.Remove(b);
                            ShiftQueue(st.outQ, i => L.OutSlotPos(st.idx, i));
                            break;
                        }
                    default:
                        {
                            var sn = stations[d.owner];
                            sn.outBuf.Remove(b);
                            ShiftQueue(sn.outBuf, i => L.StationOutPos(sn.idx, i));
                            break;
                        }
                }
                b.loc = BoxLoc.OnAmr; b.owner = a.id; b.xp = null;
                a.load = b;
                a.mv.speed = LoadedSpeed;
            }
            else
            {
                a.load = null; b.xp = null; b.taken = false;
                a.mv.speed = MoveSpeed;
                if (d.kind == DockKind.Stacker)
                {
                    var st = stackers[d.owner];
                    st.inResv--;
                    b.loc = BoxLoc.InQueue; b.owner = st.idx; b.queueIdx = st.inQ.Count; b.availT = t;
                    st.inQ.Add(b);
                }
                else
                {
                    var sn = stations[d.owner];
                    sn.inResv--;
                    b.loc = BoxLoc.StationBuf; b.owner = sn.idx; b.queueIdx = sn.inBuf.Count; b.availT = t;
                    sn.inBuf.Add(b);
                }
            }
            d.load--;
            a.stops.RemoveAt(0);
            a.moves++;
            if (a.stops.Count == 0 && !s.pick) TryChain(a, d);
            if (a.stops.Count > 0) BeginStops(a);
            else { a.state = AmrState.ToPad; traffic.Go(a.mv, a.pad.node); }
        }

        /// <summary>After a drop at a stacker or station dock the AMR picks up what is waiting right there (no empty trip).</summary>
        void TryChain(Amr a, Dock d)
        {
            if (a.lowBat) return;
            if (d.kind == DockKind.Stacker)
            {
                var st = stackers[d.owner];
                foreach (var b in st.outQ)
                {
                    if (b.taken || b.availT > t) continue;
                    var o = OrderOf(b);
                    if (o == null) continue;
                    var sn = stations[o.station];
                    int room = Station.InCap - (sn.inBuf.Count + sn.inResv);
                    if (room <= 0 || sn.dock.load >= 4) continue;
                    if (sn.queue.IndexOf(o) != 0 && room <= Undelivered(sn.queue[0])) continue;
                    b.taken = true; sn.inResv++;
                    a.stops.Add(new Stop { dock = d, pick = true, box = b });
                    a.stops.Add(new Stop { dock = sn.dock, pick = false, box = b });
                    d.load++; sn.dock.load++;
                    return;
                }
            }
            else if (d.kind == DockKind.Station)
            {
                var sn = stations[d.owner];
                foreach (var b in sn.outBuf)
                {
                    if (b.taken || b.availT > t) continue;
                    if (!SelectSlot(b, false, out var dst, out var slot)) continue;
                    if (dst.dock.load >= 2) { Release(slot); continue; }
                    b.taken = true; dst.inResv++;
                    a.stops.Add(new Stop { dock = d, pick = true, box = b });
                    a.stops.Add(new Stop { dock = dst.dock, pick = false, box = b, destSlot = slot });
                    d.load++; dst.dock.load++;
                    return;
                }
            }
        }
    }
}
