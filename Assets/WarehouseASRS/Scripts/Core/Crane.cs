using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarehouseASRS.Core
{
    /// <summary>Trapezoidal motion profile helpers.</summary>
    public static class Kin
    {
        public static float TravelTime(float d, float v, float a)
        {
            d = Mathf.Abs(d);
            if (d < 1e-4f) return 0f;
            float dAcc = v * v / a;
            return d <= dAcc ? 2f * Mathf.Sqrt(d / a) : d / v + v / a;
        }

        /// <summary>Distance covered after time t on a move of length d (accelerate, cruise, decelerate).</summary>
        public static float Dist(float t, float d, float v, float a)
        {
            if (d <= 1e-4f) return 0f;
            float T = TravelTime(d, v, a);
            t = Mathf.Clamp(t, 0f, T);
            float dAcc = v * v / a;
            if (d <= dAcc) return t <= T * 0.5f ? 0.5f * a * t * t : d - 0.5f * a * (T - t) * (T - t);
            float ta = v / a;
            if (t < ta) return 0.5f * a * t * t;
            if (t > T - ta) return d - 0.5f * a * (T - t) * (T - t);
            return 0.5f * a * ta * ta + v * (t - ta);
        }
    }

    /// <summary>
    /// Miniload ASRS stacker crane: travels along the aisle (x) while the carriage lifts (y) at the same time; a telescopic fork
    /// reaches into the rack rows on both sides. Works through a plan of legs; decides its next command when the plan is empty.
    /// </summary>
    public class Crane
    {
        public const float Vx = 3.2f, Ax = 1.0f, Vy = 1.2f, Ay = 0.9f, ForkT = 2.2f;

        public readonly Stacker st;
        public float x, y;
        public float forkExt;        // 0..1 amplitude of the fork travel
        public int forkSide;         // -1 south row / +1 north row
        public Box carrying;
        readonly List<Leg> plan = new List<Leg>();
        int li;
        float legT;
        bool lastWasStore;
        public float busyTime;
        public int commands, chainedCommands, dualCycles;
        public string action = "idle";

        public bool Busy => plan.Count > 0;

        public Crane(Stacker s, float x0, float y0) { st = s; x = x0; y = y0; }

        public static float TravelBetween(float x0, float y0, float x1, float y1) =>
            Mathf.Max(Kin.TravelTime(x1 - x0, Vx, Ax), Kin.TravelTime(y1 - y0, Vy, Ay));

        static Leg TravelLeg(float x0, float y0, float x1, float y1)
        {
            float dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            return new Leg { kind = Leg.Kind.Travel, x0 = x0, y0 = y0, x1 = x1, y1 = y1, dx = dx, dy = dy, dur = Mathf.Max(Kin.TravelTime(dx, Vx, Ax), Kin.TravelTime(dy, Vy, Ay)) };
        }

        void AddTravel(float x1, float y1)
        {
            var last = plan.Count > 0 ? plan[plan.Count - 1] : null;
            float fx = last != null ? last.x1 : x, fy = last != null ? last.y1 : y;
            if (Mathf.Abs(fx - x1) < 1e-3f && Mathf.Abs(fy - y1) < 1e-3f) return;
            plan.Add(TravelLeg(fx, fy, x1, y1));
        }

        void AddFork(int side, Action mid)
        {
            var last = plan.Count > 0 ? plan[plan.Count - 1] : null;
            float fx = last != null ? last.x1 : x, fy = last != null ? last.y1 : y;
            plan.Add(new Leg { kind = Leg.Kind.Fork, x0 = fx, y0 = fy, x1 = fx, y1 = fy, dur = ForkT, side = side, onMid = mid });
        }

        public void Step(Warehouse w)
        {
            if (plan.Count == 0)
            {
                Decide(w);
                if (plan.Count == 0) { action = "idle"; return; }
                li = 0; legT = 0f;
            }
            busyTime += Warehouse.Dt;
            var leg = plan[li];
            legT += Warehouse.Dt;
            float p = leg.dur < 1e-4f ? 1f : Mathf.Clamp01(legT / leg.dur);
            if (leg.kind == Leg.Kind.Travel)
            {
                x = leg.x0 + Mathf.Sign(leg.x1 - leg.x0) * Kin.Dist(legT, leg.dx, Vx, Ax);
                y = leg.y0 + Mathf.Sign(leg.y1 - leg.y0) * Kin.Dist(legT, leg.dy, Vy, Ay);
            }
            else
            {
                forkSide = leg.side;
                forkExt = Mathf.Sin(p * Mathf.PI);
                if (!leg.midDone && p >= 0.5f) { leg.midDone = true; leg.onMid?.Invoke(); }
            }
            if (legT >= leg.dur)
            {
                if (leg.kind == Leg.Kind.Travel) { x = leg.x1; y = leg.y1; }
                else { forkExt = 0f; if (!leg.midDone) { leg.midDone = true; leg.onMid?.Invoke(); } }
                leg.onEnd?.Invoke();
                li++; legT = 0f;
                if (li >= plan.Count) { plan.Clear(); li = 0; }
            }
        }

        // ------------------------------------------------------------------ decisions

        void Decide(Warehouse w)
        {
            var L = w.L;
            float ioX = L.IoX, ioY = L.IoY;
            bool atIo = Mathf.Abs(x - ioX) < 0.05f && Mathf.Abs(y - ioY) < 0.05f;
            var head = st.inQ.Count > 0 && st.inQ[0].availT <= w.t ? st.inQ[0] : null;
            bool canRetr = st.retr.Count > 0 && st.outQ.Count + st.outResv < Stacker.OutCap && w.PickRetrieval(st, lastWasStore ? x : ioX, lastWasStore ? y : ioY) != null;

            // dual command: a retrieval right after a store, straight from the slot the crane just filled
            if (lastWasStore && w.cfg.dualCommand && canRetr)
            {
                StartRetrieve(w, w.PickRetrieval(st, x, y), true);
                return;
            }
            bool store;
            if (head != null && (st.inQ.Count >= Stacker.InCap || !canRetr)) store = true;
            else if (canRetr) store = false;
            else store = head != null;
            if (store && head != null) { StartStore(w, head, !lastWasStore && atIo && commands > 0); return; }
            if (canRetr) { StartRetrieve(w, w.PickRetrieval(st, ioX, ioY), false); return; }
            lastWasStore = false;
            if (!atIo) { AddTravel(ioX, ioY); action = "return"; }
        }

        RetrieveCmd NearestRetrieve(Warehouse w, float fx, float fy)
        {
            RetrieveCmd best = null; float bt = float.MaxValue;
            int n = Mathf.Min(4, st.retr.Count);
            for (int i = 0; i < n; i++)
            {
                var c = st.retr[i];
                var p = w.L.SlotXY(c.slot);
                float tt = TravelBetween(fx, fy, p.x, p.y) + TravelBetween(p.x, p.y, w.L.IoX, w.L.IoY) - i * 0.5f;
                if (tt < bt) { bt = tt; best = c; }
            }
            return best;
        }

        void StartStore(Warehouse w, Box b, bool chain)
        {
            var L = w.L;
            int slot = b.slot;
            var sp = L.SlotXY(slot);
            commands++; if (chain) chainedCommands++;
            action = "store";
            AddTravel(L.IoX, L.IoY);
            AddFork(-1, () => w.CranePickFromIn(this, b));
            AddTravel(sp.x, sp.y);
            AddFork(L.SlotSide(slot), () => w.CranePutInSlot(this, b, slot));
            lastWasStore = true;
        }

        void StartRetrieve(Warehouse w, RetrieveCmd c, bool chain)
        {
            var L = w.L;
            st.retr.Remove(c);
            st.outResv++;
            var sp = L.SlotXY(c.slot);
            commands++; if (chain) { chainedCommands++; dualCycles++; }
            action = "retrieve";
            AddTravel(sp.x, sp.y);
            AddFork(L.SlotSide(c.slot), () => w.CranePickFromSlot(this, c));
            AddTravel(L.IoX, L.IoY);
            AddFork(1, () => w.CranePutInOut(this, c.box));
            lastWasStore = false;
        }
    }
}
