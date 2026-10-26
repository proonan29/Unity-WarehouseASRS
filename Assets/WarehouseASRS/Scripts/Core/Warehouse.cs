using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WarehouseASRS.Core
{
    public struct HistoryPoint { public float t, fill, craneUtil, amrUtil; public int stored, shipped; }

    /// <summary>
    /// Deterministic (0.1 s step) simulation of an automated warehouse: supplier boxes arrive on inbound conveyors, are loaded onto AMRs,
    /// stored by miniload stacker cranes, retrieved for randomly generated orders, delivered to a robot picking station
    /// and the leftovers go back into storage. Slot assignment, order allocation, AMR dispatch and the stacker cranes live here.
    /// </summary>
    public partial class Warehouse
    {
        public const float Dt = 0.1f;
        public readonly WarehouseConfig cfg;
        public readonly WarehouseLayout L;
        public readonly Traffic traffic;
        public float t;
        public readonly Rng rng;

        public readonly List<Sku> skus = new List<Sku>();
        float[] skuCum;
        public readonly List<Box> boxes = new List<Box>();
        public SlotState[] slotState;
        public Box[] slotBox;
        public Stacker[] stackers;
        public Station[] stations;
        public InboundLine[] inlines;
        public readonly List<Amr> amrs = new List<Amr>();
        public readonly List<Order> orders = new List<Order>();
        readonly Dictionary<int, Order> orderMap = new Dictionary<int, Order>();
        public readonly List<SimEvent> events = new List<SimEvent>();
        public readonly List<HistoryPoint> history = new List<HistoryPoint>();
        int[][] skuCount;
        float[] slotTimeLocal;

        int nextBoxId = 1, nextOrderId = 1;
        public int occ;
        public bool inboundOpen = true, orderPaused;
        float nextInbound, nextOrder, nextDispatch, nextHistory, nextEmpty;
        const int MaxBacklog = 12;

        // statistics
        public int inboundSpawned, inboundStored, ordersCreated, ordersShipped, itemsShipped, emptiesSpawned, returns, fullEvents;
        public double leadSum;
        public float lastLead;

        public float Fill => occ / (float)cfg.Capacity;

        public Warehouse(WarehouseConfig c)
        {
            cfg = c.Clone();
            rng = new Rng(cfg.seed);
            L = new WarehouseLayout(cfg);
            traffic = new Traffic(L.graph);
            BuildSkus();

            int S = cfg.stackers, per = cfg.SlotsPerStacker;
            slotState = new SlotState[S * per];
            slotBox = new Box[S * per];
            skuCount = new int[S][];
            slotTimeLocal = new float[per];
            for (int i = 0; i < per; i++) slotTimeLocal[i] = L.SlotTime(i);
            var order = Enumerable.Range(0, per).OrderBy(i => slotTimeLocal[i]).ThenBy(i => i).ToArray();

            stackers = new Stacker[S];
            for (int i = 0; i < S; i++)
            {
                var st = new Stacker { idx = i, dock = L.stackerDocks[i], aisleZ = L.aisleZ[i], free = per, byTime = order };
                st.crane = new Crane(st, L.IoX, L.IoY);
                stackers[i] = st;
                skuCount[i] = new int[cfg.skuCount];
            }
            inlines = L.inboundDocks.Select((d, i) => new InboundLine { idx = i, dock = d }).ToArray();
            stations = L.stationDocks.Select((d, i) => new Station { idx = i, dock = d }).ToArray();
            for (int i = 0; i < cfg.amrs; i++)
            {
                var pad = L.padDocks[i];
                var mv = traffic.Add(i, "A" + (i + 1), Lane.Parts, pad.node, pad.yaw);
                mv.halfLen = 0.55f; mv.speed = 1.5f; mv.rotSpeed = 110f;
                amrs.Add(new Amr { id = i, name = mv.name, mv = mv, pad = pad, state = AmrState.Charging, battery = 60f + rng.Value * 40f });
            }
            InitialFill();
            nextInbound = t + Exp(3600f / Mathf.Max(1f, cfg.inboundPerHour));
            nextOrder = t + Exp(3600f / Mathf.Max(1f, cfg.ordersPerHour));
        }

        float Exp(float mean) => -Mathf.Log(Mathf.Max(1e-4f, 1f - rng.Value)) * mean;

        // ------------------------------------------------------------------ catalogue

        static readonly string[] Cat = { "Fastener", "Bearing", "Sensor", "Cable", "Valve", "Gasket", "Motor", "Switch" };
        static readonly string[] CatKo = { "체결구", "베어링", "센서", "케이블", "밸브", "개스킷", "모터", "스위치" };

        void BuildSkus()
        {
            int n = cfg.skuCount; float sum = 0f;
            skuCum = new float[n];
            for (int i = 0; i < n; i++)
            {
                float pop = 1f / Mathf.Pow(i + 1, 0.9f);
                sum += pop; skuCum[i] = sum;
                skus.Add(new Sku
                {
                    id = i, pop = pop, cap = 12 + (i * 7) % 13,
                    cls = i < n * 0.2f ? 0 : i < n * 0.5f ? 1 : 2,
                    name = $"{Cat[i % Cat.Length]} {(i / Cat.Length) + 1}", nameKo = $"{CatKo[i % Cat.Length]} {(i / Cat.Length) + 1}"
                });
            }
        }

        int PickWeighted(Func<int, float> w)
        {
            float tot = 0f;
            for (int i = 0; i < skus.Count; i++) tot += Mathf.Max(0f, w(i));
            if (tot <= 0f) return -1;
            float r = rng.Value * tot;
            for (int i = 0; i < skus.Count; i++) { r -= Mathf.Max(0f, w(i)); if (r <= 0f) return i; }
            return skus.Count - 1;
        }

        Box NewBox(int sku, int qty)
        {
            var b = new Box { id = nextBoxId++, sku = sku, qty = qty, tCreated = t };
            boxes.Add(b);
            return b;
        }

        public void Log(string en, string ko)
        {
            events.Add(new SimEvent { t = t, en = en, ko = ko });
            if (events.Count > 400) events.RemoveRange(0, 100);
        }

        // ------------------------------------------------------------------ slots

        void Reserve(Stacker st, int slot, Box b)
        {
            slotState[slot] = SlotState.ReservedIn; slotBox[slot] = b;
            st.free--; occ++;
            b.slot = slot;
            if (b.Empty) st.empties++;
        }

        void Release(int slot)
        {
            var b = slotBox[slot];
            int s = slot / cfg.SlotsPerStacker;
            slotState[slot] = SlotState.Free; slotBox[slot] = null;
            stackers[s].free++; occ--;
            if (b != null && b.Empty) stackers[s].empties--;
        }

        int FirstFree(Stacker st, int from, int dir)
        {
            var o = st.byTime; int per = o.Length;
            int baseId = st.idx * per;
            for (int i = from; i >= 0 && i < per; i += dir)
                if (slotState[baseId + o[i]] == SlotState.Free) return baseId + o[i];
            return -1;
        }

        int RandomFree(Stacker st)
        {
            int per = st.byTime.Length, baseId = st.idx * per;
            for (int k = 0; k < 60; k++)
            {
                int s = baseId + rng.Range(0, per);
                if (slotState[s] == SlotState.Free) return s;
            }
            return FirstFree(st, 0, 1);
        }

        /// <summary>Chooses stacker and slot for a box according to the policy and reserves the slot.</summary>
        public bool SelectSlot(Box b, bool ignoreQueue, out Stacker st, out int slot)
        {
            st = null; slot = -1;
            var cands = new List<Stacker>(stackers.Length);
            foreach (var s in stackers) if (s.free > 0 && (ignoreQueue || s.inQ.Count + s.inResv < Stacker.InCap)) cands.Add(s);
            if (cands.Count == 0) return false;
            int per = cfg.SlotsPerStacker;
            if (b.Empty)
            {
                float zin = L.inboundZ[0];
                cands.Sort((a, c) => Mathf.Abs(a.aisleZ - zin).CompareTo(Mathf.Abs(c.aisleZ - zin)));
                st = cands[0];
                foreach (var s in cands) if (s.empties < 0.3f * per) { st = s; break; }
                slot = FirstFree(st, 0, 1);
            }
            else
            {
                switch (cfg.policy)
                {
                    case SlotPolicy.Random:
                        st = cands[rng.Range(0, cands.Count)];
                        slot = RandomFree(st);
                        break;
                    case SlotPolicy.Nearest:
                        st = cands.OrderBy(s => s.Workload).First();
                        slot = FirstFree(st, 0, 1);
                        break;
                    default:
                        st = cands.OrderBy(s => s.Workload + 0.3f * skuCount[s.idx][b.sku]).First();
                        int cls = skus[b.sku].cls;
                        if (cls == 0) slot = FirstFree(st, 0, 1);
                        else if (cls == 2) slot = FirstFree(st, per - 1, -1);
                        else
                        {
                            int mid = (int)(per * 0.35f);
                            slot = FirstFree(st, mid, 1);
                            if (slot < 0) slot = FirstFree(st, mid, -1);
                        }
                        break;
                }
            }
            if (slot < 0) return false;
            Reserve(st, slot, b);
            return true;
        }

        void SetStored(Box b, int slot)
        {
            int s = slot / cfg.SlotsPerStacker;
            slotState[slot] = SlotState.Stored; slotBox[slot] = b;
            b.slot = slot; b.loc = BoxLoc.Stored; b.owner = s; b.xp = null; b.taken = false; b.availT = t;
            stackers[s].stored++;
            if (b.sku >= 0) skuCount[s][b.sku]++;
        }

        void InitialFill()
        {
            int n = (int)(cfg.initialFill * cfg.Capacity);
            for (int i = 0; i < n; i++)
            {
                bool empty = rng.Value < 0.08f;
                Box b;
                if (empty) b = NewBox(-1, 0);
                else
                {
                    int sku = PickWeighted(k => skus[k].pop);
                    b = NewBox(sku, Mathf.Max(1, Mathf.RoundToInt(skus[sku].cap * (0.5f + 0.5f * rng.Value))));
                }
                if (!SelectSlot(b, true, out var st, out var slot)) { boxes.Remove(b); break; }
                SetStored(b, slot);
            }
        }

        // ------------------------------------------------------------------ positions

        public Vector3 BoxPos(Box b, float tt)
        {
            if (b.xp != null && tt < b.xt0 + b.xdur)
            {
                float u = Mathf.Clamp01((tt - b.xt0) / b.xdur);
                return WarehouseLayout.EvalPath(b.xp, u * u * (3f - 2f * u));
            }
            switch (b.loc)
            {
                case BoxLoc.Stored: return L.SlotPos(b.slot);
                case BoxLoc.InQueue: return L.InSlotPos(b.owner, b.queueIdx);
                case BoxLoc.OutQueue: return L.OutSlotPos(b.owner, b.queueIdx);
                case BoxLoc.OnCrane:
                    {
                        var c = stackers[b.owner].crane;
                        return new Vector3(c.x, c.y, c.st.aisleZ + c.forkSide * c.forkExt * WarehouseLayout.RowOff);
                    }
                case BoxLoc.OnAmr:
                    {
                        var p = amrs[b.owner].mv.pos; return new Vector3(p.x, WarehouseLayout.DeckY, p.z);
                    }
                case BoxLoc.InboundConv: return L.InboundPos(b.owner, b.queueIdx);
                case BoxLoc.StationBuf: return L.StationInPos(b.owner, b.queueIdx);
                case BoxLoc.StationWork: return b.orderBox ? L.StationOrdPos(b.owner) : L.StationSrcPos(b.owner);
                case BoxLoc.StationOut: return L.StationOutPos(b.owner, b.queueIdx);
                case BoxLoc.Shipped: { var p = L.ShipPath(b.owner); return p[p.Length - 1]; }
                default: return Vector3.zero;
            }
        }

        void ShiftQueue(List<Box> q, Func<int, Vector3> posOf)
        {
            for (int i = 0; i < q.Count; i++)
            {
                var b = q[i];
                var p0 = BoxPos(b, t); var p1 = posOf(i);
                b.queueIdx = i;
                if ((p0 - p1).sqrMagnitude > 1e-4f) { b.Move(new[] { p0, p1 }, t, 0.9f); b.availT = Mathf.Max(b.availT, t + 0.9f); }
            }
        }

        // ------------------------------------------------------------------ crane callbacks

        public void CranePickFromIn(Crane c, Box b)
        {
            var s = c.st;
            s.inQ.Remove(b);
            b.loc = BoxLoc.OnCrane; b.owner = s.idx; b.xp = null;
            c.carrying = b;
            ShiftQueue(s.inQ, i => L.InSlotPos(s.idx, i));
        }

        public void CranePutInSlot(Crane c, Box b, int slot)
        {
            c.carrying = null;
            SetStored(b, slot);
            c.st.stores++;
            inboundStored++;
        }

        public void CranePickFromSlot(Crane c, RetrieveCmd cmd)
        {
            var b = cmd.box; var s = c.st;
            Release(cmd.slot);
            s.stored--; s.retrievals++;
            if (b.sku >= 0) skuCount[s.idx][b.sku]--;
            b.loc = BoxLoc.OnCrane; b.owner = s.idx; b.slot = -1; b.xp = null;
            c.carrying = b;
        }

        public void CranePutInOut(Crane c, Box b)
        {
            var s = c.st;
            s.outResv--;
            c.carrying = null;
            b.loc = BoxLoc.OutQueue; b.owner = s.idx; b.queueIdx = s.outQ.Count;
            var p0 = new Vector3(c.x, c.y, s.aisleZ + WarehouseLayout.RowOff);
            s.outQ.Add(b);
            b.Move(new[] { p0, L.OutSlotPos(s.idx, b.queueIdx) }, t, 1.0f);
            b.availT = t + 1.0f; b.readyT = b.availT;
        }

        // ------------------------------------------------------------------ main step

        public void Step()
        {
            t += Dt;
            GenerateInbound();
            GenerateOrders();
            if (t >= nextDispatch) { nextDispatch = t + 1f; Dispatch(); }
            foreach (var s in stackers) s.crane.Step(this);
            foreach (var s in stations) StepStation(s);
            foreach (var a in amrs) StepAmr(a);
            traffic.StepAll(Dt, t);
            if (t >= nextHistory)
            {
                nextHistory = t + 120f;
                history.Add(new HistoryPoint { t = t, fill = Fill, craneUtil = CraneUtil, amrUtil = AmrUtil, stored = inboundStored, shipped = ordersShipped });
            }
        }

        public void Advance(float seconds)
        {
            int n = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < n; i++) Step();
        }

        // ------------------------------------------------------------------ generators

        void GenerateInbound()
        {
            if (inboundOpen && Fill >= cfg.fullStop) { inboundOpen = false; fullEvents++; Log("Warehouse is full: inbound stopped", "창고 만재: 입고 중단"); }
            else if (!inboundOpen && Fill < cfg.fullResume) { inboundOpen = true; Log("Space available: inbound resumed", "여유 확보: 입고 재개"); }
            if (t < nextInbound) return;
            if (!inboundOpen) { nextInbound = t + 5f; return; }
            InboundLine line = null;
            foreach (var l in inlines) if (l.q.Count < InboundLine.Cap && (line == null || l.q.Count < line.q.Count)) line = l;
            if (line == null) { nextInbound = t + 5f; return; }
            nextInbound = t + Exp(3600f / Mathf.Max(1f, cfg.inboundPerHour));
            var counts = new int[skus.Count];
            foreach (var b in boxes) if (b.sku >= 0 && b.loc != BoxLoc.Shipped) counts[b.sku]++;
            int sku = PickWeighted(k => Mathf.Pow(skus[k].pop, 0.6f) / (1f + counts[k] * 0.12f));
            var box = NewBox(sku, Mathf.Max(1, Mathf.RoundToInt(skus[sku].cap * (0.6f + 0.4f * rng.Value))));
            inboundSpawned++;
            Spawn(box, line);
            Log($"Inbound {box.Rfid}: {skus[sku].name} x{box.qty}", $"입고 {box.Rfid}: {skus[sku].nameKo} x{box.qty}");
        }

        void Spawn(Box box, InboundLine line)
        {
            box.loc = BoxLoc.InboundConv; box.owner = line.idx; box.queueIdx = line.q.Count;
            var path = L.InboundSpawnPath(line.idx, box.queueIdx);
            float len = (path[1] - path[0]).magnitude, dur = len / 0.8f;
            box.Move(path, t, dur);
            box.availT = t + dur; box.readyT = box.availT;
            line.q.Add(box);
            line.spawned++;
        }

        void SpawnEmptyIfNeeded()
        {
            int freeSlots = 0; foreach (var s0 in stackers) freeSlots += s0.free;
            if (t < nextEmpty || freeSlots < 12) return;
            int avail = 0, need = 0;
            foreach (var b in boxes) if (b.Empty && !b.reserved && b.loc != BoxLoc.Shipped) avail++;
            foreach (var o in orders) if (o.orderBox == null) need++;
            if (avail >= 2 + need) return;
            InboundLine line = null;
            foreach (var l in inlines) if (l.q.Count < InboundLine.Cap && (line == null || l.q.Count < line.q.Count)) line = l;
            if (line == null) return;
            nextEmpty = t + 15f;
            var box = NewBox(-1, 0);
            emptiesSpawned++;
            Spawn(box, line);
            Log($"Empty box {box.Rfid} supplied", $"빈박스 공급 {box.Rfid}");
        }

        // ------------------------------------------------------------------ statistics

        public float CraneUtil => t < 1f ? 0f : stackers.Average(s => s.crane.busyTime) / t;
        public float AmrUtil => t < 1f ? 0f : amrs.Average(a => a.busyTime) / t;
        public float StationUtil => t < 1f || stations.Length == 0 ? 0f : stations.Average(s => s.busyTime) / t;
        public float DualRatio { get { int c = stackers.Sum(s => s.crane.commands); return c == 0 ? 0f : stackers.Sum(s => s.crane.chainedCommands) / (float)c; } }
        public float OrdersPerHour => t < 600f ? 0f : ordersShipped / (t / 3600f);
        public float AvgLead => ordersShipped == 0 ? 0f : (float)(leadSum / ordersShipped);
        public int StoredBoxes => stackers.Sum(s => s.stored);
        public int EmptyBoxes => boxes.Count(b => b.Empty && b.loc == BoxLoc.Stored);
        public int Units => boxes.Where(b => b.loc == BoxLoc.Stored && b.sku >= 0).Sum(b => b.qty);

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"t={t / 3600f:0.00}h  fill={Fill * 100f:0.0}% ({occ}/{cfg.Capacity})  stored={StoredBoxes} empties={EmptyBoxes} units={Units}  inbound={(inboundOpen ? "open" : "FULL-STOP")} orders={(orderPaused ? "paused(no stock)" : "running")}");
            sb.AppendLine($"inbound spawned={inboundSpawned} stored-ops={inboundStored}  orders created={ordersCreated} shipped={ordersShipped} ({OrdersPerHour:0.0}/h) items={itemsShipped} avgLead={AvgLead / 60f:0.0}min active={orders.Count}");
            sb.AppendLine($"crane util={CraneUtil * 100f:0}% dual={DualRatio * 100f:0}%  AMR util={AmrUtil * 100f:0}% wait={amrs.Sum(a => a.mv.waitTotal) / Mathf.Max(1f, t * amrs.Count) * 100f:0}% deadlock-breaks={traffic.deadlockBreaks}  station util={StationUtil * 100f:0}% empties-supplied={emptiesSpawned}");
            foreach (var s in stackers)
                sb.AppendLine($"  S{s.idx + 1}: stored={s.stored} free={s.free} util={(t < 1f ? 0 : s.crane.busyTime / t * 100f):0}% cmds={s.crane.commands} (chained {s.crane.chainedCommands}) inQ={s.inQ.Count}+{s.inResv} outQ={s.outQ.Count}+{s.outResv} retr={s.retr.Count} {s.crane.action}");
            foreach (var s in stations)
                sb.AppendLine($"  PK{s.idx + 1}: phase={s.phase} queue={s.queue.Count} inBuf={s.inBuf.Count}+{s.inResv} outBuf={s.outBuf.Count} done={s.ordersDone} items={s.itemsPicked}");
            sb.AppendLine("  AMR: " + string.Join(" ", amrs.Select(a => $"{a.name}:{a.state}{(a.load != null ? "*" : "")}{a.battery:0}%")));
            return sb.ToString();
        }
    }
}
