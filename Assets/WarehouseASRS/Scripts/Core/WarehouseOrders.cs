using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WarehouseASRS.Core
{
    public partial class Warehouse
    {
        public Order OrderOf(Box b) => b.orderId >= 0 && orderMap.TryGetValue(b.orderId, out var o) ? o : null;

        // ------------------------------------------------------------------ order generation

        void GenerateOrders()
        {
            if (t < nextOrder) return;
            nextOrder = t + Exp(3600f / Mathf.Max(1f, cfg.ordersPerHour));
            if (orders.Count >= MaxBacklog) return;
            TryCreateOrder();
        }

        void TryCreateOrder()
        {
            var avail = new int[skus.Count];
            foreach (var b in boxes) if (b.loc == BoxLoc.Stored && !b.reserved && b.sku >= 0) avail[b.sku] += b.qty;
            int total = avail.Sum();
            if (total < 20)
            {
                if (!orderPaused) { orderPaused = true; Log("Stock depleted: orders wait for the next inbound", "재고 소진: 다음 입고까지 주문 대기"); }
                return;
            }
            if (orderPaused) { orderPaused = false; Log("Stock available: orders resumed", "재고 확보: 주문 재개"); }

            float r = rng.Value;
            int lines = 1 + (r < 0.3f ? 1 : 0) + (r < 0.1f ? 1 : 0);
            var order = new Order { id = nextOrderId++, tCreated = t };
            var used = new HashSet<int>();
            for (int li = 0; li < lines; li++)
            {
                int sku = PickWeighted(k => !used.Contains(k) && avail[k] > 0 ? skus[k].pop : 0f);
                if (sku < 0) break;
                used.Add(sku);
                int qty = Mathf.Min(avail[sku], 1 + rng.Range(0, 6));
                Allocate(order, sku, qty);
            }
            if (order.picks.Count == 0) return;
            order.lines = used.Count;
            order.totalQty = order.picks.Sum(p => p.qty);
            order.state = OrderState.Waiting;
            Station best = stations[0];
            foreach (var s in stations) if (s.queue.Count < best.queue.Count) best = s;
            order.station = best.idx;
            best.queue.Add(order);
            orders.Add(order); orderMap[order.id] = order;
            ordersCreated++;
            Log($"Order #{order.id}: {order.lines} line(s), {order.totalQty} items, {order.picks.Count} box(es) -> PK{best.idx + 1}",
                $"주문 #{order.id}: {order.lines}개 품목, {order.totalQty}개, 박스 {order.picks.Count}개 → PK{best.idx + 1}");
        }

        void Allocate(Order order, int sku, int qty)
        {
            var cand = boxes.Where(b => b.loc == BoxLoc.Stored && !b.reserved && b.sku == sku).ToList();
            int need = qty;
            while (need > 0 && cand.Count > 0)
            {
                Box best = null;
                foreach (var c in cand)
                    if (c.qty >= need && (best == null || c.qty < best.qty || (c.qty == best.qty && slotTimeLocal[c.slot % cfg.SlotsPerStacker] < slotTimeLocal[best.slot % cfg.SlotsPerStacker]))) best = c;
                if (best == null) foreach (var c in cand) if (best == null || c.qty > best.qty) best = c;
                int take = Mathf.Min(best.qty, need);
                order.picks.Add(new Pick { box = best, sku = sku, qty = take });
                best.reserved = true; best.orderId = order.id;
                slotState[best.slot] = SlotState.ReservedOut;
                cand.Remove(best);
                need -= take;
            }
        }

        // ------------------------------------------------------------------ fetching (retrieval commands)

        int Rank(Box b)
        {
            var o = OrderOf(b);
            if (o == null) return 99;
            int r = stations[o.station].queue.IndexOf(o);
            return r < 0 ? 99 : r;
        }

        /// <summary>Next retrieval for a crane. Boxes of the order at the head of a station queue go first, so out queues can never be
        /// clogged by boxes of later orders while the active order still waits for a box from the same stacker.</summary>
        public RetrieveCmd PickRetrieval(Stacker st, float fx, float fy)
        {
            if (st.retr.Count == 0) return null;
            int minRank = int.MaxValue;
            foreach (var c in st.retr) minRank = Mathf.Min(minRank, Rank(c.box));
            int used = st.outQ.Count + st.outResv;
            RetrieveCmd best = null; float bt = float.MaxValue; int seen = 0;
            foreach (var c in st.retr)
            {
                if (Rank(c.box) > minRank && used >= Stacker.OutCap - 1) continue;
                if (++seen > 4) break;
                var p = L.SlotXY(c.slot);
                float tt = Crane.TravelBetween(fx, fy, p.x, p.y) + Crane.TravelBetween(p.x, p.y, L.IoX, L.IoY) - seen * 0.5f;
                if (tt < bt) { bt = tt; best = c; }
            }
            return best;
        }

        void RequestRetrieve(Box b)
        {
            int s = b.slot / cfg.SlotsPerStacker;
            stackers[s].retr.Add(new RetrieveCmd { box = b, slot = b.slot, tCreated = t });
        }

        bool AllOut(Order o) => o.picks.All(p => p.box.loc != BoxLoc.Stored && p.box.loc != BoxLoc.OnCrane || p.done) && !(o.orderBox != null && o.orderBox.loc == BoxLoc.Stored);

        void FetchOrders()
        {
            foreach (var sn in stations)
            {
                for (int i = 0; i < Mathf.Min(2, sn.queue.Count); i++)
                {
                    var o = sn.queue[i];
                    if (!o.fetchRequested)
                    {
                        o.fetchRequested = true;
                        foreach (var p in o.picks) RequestRetrieve(p.box);
                        o.state = OrderState.Fetching;
                    }
                    if (o.orderBox == null) AssignOrderBox(o, sn);
                }
            }
        }

        bool AllOutOfStorage(Order o)
        {
            if (o.orderBox == null) return false;
            if (!o.fetchRequested) return false;
            if (o.orderBox.loc == BoxLoc.Stored || o.orderBox.loc == BoxLoc.OnCrane && o.orderBox.slot >= 0) return false;
            foreach (var p in o.picks) if (!p.done && (p.box.loc == BoxLoc.Stored)) return false;
            return true;
        }

        void AssignOrderBox(Order o, Station sn)
        {
            Box best = null; float bs = float.MaxValue;
            float zc = L.stationZc[sn.idx];
            foreach (var b in boxes)
            {
                if (!b.Empty || b.reserved || b.loc != BoxLoc.Stored) continue;
                float sc = Mathf.Abs(stackers[b.owner].aisleZ - zc) + slotTimeLocal[b.slot % cfg.SlotsPerStacker] * 0.3f;
                if (sc < bs) { bs = sc; best = b; }
            }
            if (best == null) return;
            stackers[best.owner].empties--;
            best.reserved = true; best.orderId = o.id; best.orderBox = true;
            slotState[best.slot] = SlotState.ReservedOut;
            o.orderBox = best;
            RequestRetrieve(best);
        }

        // ------------------------------------------------------------------ picking station

        void RemoveFromStationIn(Station sn, Box b)
        {
            sn.inBuf.Remove(b);
            ShiftQueue(sn.inBuf, i => L.StationInPos(sn.idx, i));
        }

        void StepStation(Station sn)
        {
            sn.blend = Mathf.MoveTowards(sn.blend, sn.phase == StationPhase.Picking ? 1f : 0f, Dt / 0.7f);
            switch (sn.phase)
            {
                case StationPhase.Idle:
                    if (sn.queue.Count > 0) { var o = sn.queue[0]; o.tStart = t; sn.phase = StationPhase.WaitOrderBox; }
                    break;

                case StationPhase.WaitOrderBox:
                    {
                        var o = sn.Active; var ob = o.orderBox;
                        if (ob != null && ob.loc == BoxLoc.StationBuf && ob.owner == sn.idx && ob.availT <= t)
                        {
                            int idx = ob.queueIdx;
                            RemoveFromStationIn(sn, ob);
                            ob.Move(L.StationToOrd(sn.idx, idx), t, Station.XferT);
                            ob.loc = BoxLoc.StationWork; ob.availT = t;
                            sn.ordBox = ob; sn.timer = Station.XferT; sn.phase = StationPhase.MoveOrd;
                        }
                        break;
                    }

                case StationPhase.MoveOrd:
                    sn.timer -= Dt;
                    if (sn.timer <= 0f) { sn.ordBox.xp = null; sn.phase = StationPhase.WaitSource; }
                    break;

                case StationPhase.WaitSource:
                    {
                        var o = sn.Active;
                        if (o.picks.All(p => p.done))
                        {
                            sn.ordBox.Move(L.ShipPath(sn.idx), t, 5f);
                            sn.timer = 5f; sn.phase = StationPhase.Shipping;
                            break;
                        }
                        foreach (var p in o.picks)
                        {
                            var b = p.box;
                            if (p.done || b.loc != BoxLoc.StationBuf || b.owner != sn.idx || b.availT > t) continue;
                            int idx = b.queueIdx;
                            RemoveFromStationIn(sn, b);
                            b.Move(L.StationToSrc(sn.idx, idx), t, Station.XferT);
                            b.loc = BoxLoc.StationWork; b.availT = t;
                            sn.srcBox = b; sn.curPick = p; sn.timer = Station.XferT; sn.phase = StationPhase.MoveSrc;
                            break;
                        }
                        break;
                    }

                case StationPhase.MoveSrc:
                    sn.timer -= Dt;
                    if (sn.timer <= 0f) { sn.srcBox.xp = null; sn.itemsLeft = sn.curPick.qty; sn.rt = 0f; sn.phase = StationPhase.Picking; }
                    break;

                case StationPhase.Picking:
                    sn.busyTime += Dt;
                    sn.rt += Dt;
                    if (sn.rt >= Station.ItemT)
                    {
                        sn.rt = 0f; sn.itemsLeft--;
                        sn.srcBox.qty--; sn.ordBox.qty++; sn.itemsPicked++;
                        if (sn.itemsLeft <= 0) { sn.phase = StationPhase.MoveOut; sn.timer = -1f; }
                    }
                    break;

                case StationPhase.MoveOut:
                    if (sn.timer < 0f)
                    {
                        if (sn.outBuf.Count >= Station.OutCap) break;
                        var b = sn.srcBox;
                        int idx = sn.outBuf.Count;
                        b.Move(L.StationSrcToOut(sn.idx, idx), t, Station.XferT);
                        b.loc = BoxLoc.StationOut; b.owner = sn.idx; b.queueIdx = idx;
                        b.availT = t + Station.XferT; b.readyT = b.availT;
                        b.reserved = false; b.orderId = -1; b.taken = false;
                        if (b.qty <= 0) { b.qty = 0; b.sku = -1; }
                        sn.outBuf.Add(b);
                        sn.curPick.done = true;
                        sn.srcBox = null; sn.curPick = null;
                        sn.timer = Station.XferT;
                    }
                    else
                    {
                        sn.timer -= Dt;
                        if (sn.timer <= 0f) sn.phase = StationPhase.WaitSource;
                    }
                    break;

                case StationPhase.Shipping:
                    sn.timer -= Dt;
                    if (sn.timer <= 0f)
                    {
                        var o = sn.Active; var ob = sn.ordBox;
                        ob.loc = BoxLoc.Shipped; ob.xp = null; ob.owner = sn.idx;
                        boxes.Remove(ob);
                        o.state = OrderState.Shipped; o.tDone = t;
                        ordersShipped++; itemsShipped += ob.qty; sn.ordersDone++;
                        lastLead = t - o.tCreated; leadSum += lastLead;
                        Log($"Order #{o.id} shipped ({ob.qty} items, {lastLead / 60f:0.0} min)", $"주문 #{o.id} 출고 완료 ({ob.qty}개, {lastLead / 60f:0.0}분)");
                        sn.queue.RemoveAt(0); orders.Remove(o); orderMap.Remove(o.id);
                        sn.ordBox = null; sn.phase = StationPhase.Idle;
                    }
                    break;
            }
        }
    }
}
