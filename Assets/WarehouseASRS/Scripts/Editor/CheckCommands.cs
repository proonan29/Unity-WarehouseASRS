using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;
using WarehouseASRS.Core;

namespace WarehouseASRS.EditorTools
{
    /// <summary>Structural invariants of the simulation (slot book-keeping, box containers, orders), checked while it runs.</summary>
    public static class CheckCommands
    {
        static List<string> Check(Warehouse w)
        {
            var err = new List<string>();
            int nonFree = 0;
            for (int s = 0; s < w.slotState.Length; s++)
            {
                var b = w.slotBox[s];
                if (w.slotState[s] == SlotState.Free) { if (b != null) err.Add($"slot {s} free but holds box {b.id}"); }
                else { nonFree++; if (b == null) err.Add($"slot {s} {w.slotState[s]} without box"); else if (b.slot != s) err.Add($"slot {s} box {b.id} points to slot {b.slot}"); }
            }
            if (nonFree != w.occ) err.Add($"occ {w.occ} != non-free slots {nonFree}");
            int freeSum = w.stackers.Sum(s => s.free);
            if (freeSum != w.cfg.Capacity - w.occ) err.Add($"sum(free) {freeSum} != capacity - occ {w.cfg.Capacity - w.occ}");
            var ids = new HashSet<int>();
            foreach (var b in w.boxes)
            {
                if (!ids.Add(b.id)) err.Add($"duplicate box id {b.id}");
                if (b.loc == BoxLoc.Stored && (b.slot < 0 || w.slotBox[b.slot] != b)) err.Add($"stored box {b.id} not in its slot");
                if (b.qty < 0) err.Add($"box {b.id} negative qty");
            }
            var seen = new Dictionary<Box, string>();
            void See(Box b, string where) { if (seen.TryGetValue(b, out var prev)) err.Add($"box {b.id} in two containers: {prev} and {where}"); else seen[b] = where; }
            foreach (var st in w.stackers) { foreach (var b in st.inQ) See(b, "inQ"); foreach (var b in st.outQ) See(b, "outQ"); if (st.crane.carrying != null) See(st.crane.carrying, "crane"); }
            foreach (var sn in w.stations) { foreach (var b in sn.inBuf) See(b, "stIn"); foreach (var b in sn.outBuf) See(b, "stOut"); if (sn.ordBox != null && sn.phase != StationPhase.Idle) See(sn.ordBox, "ord"); }
            foreach (var l in w.inlines) foreach (var b in l.q) See(b, "inbound");
            foreach (var a in w.amrs) if (a.load != null) See(a.load, "amr " + a.name);
            foreach (var st in w.stackers)
            {
                int n = 0;
                for (int i = 0; i < w.cfg.SlotsPerStacker; i++) { int slot = st.idx * w.cfg.SlotsPerStacker + i; if (w.slotState[slot] == SlotState.Stored || w.slotState[slot] == SlotState.ReservedOut) n++; }
                if (n != st.stored) err.Add($"stacker {st.idx} stored {st.stored} != slots {n}");
            }
            foreach (var o in w.orders)
                foreach (var p in o.picks)
                    if (!p.done && !w.boxes.Contains(p.box)) err.Add($"order {o.id} pick box {p.box.id} missing");
            foreach (var a in w.amrs)
                for (int i = 0; i < w.amrs.Count; i++)
                    if (a != w.amrs[i] && (a.mv.pos - w.amrs[i].mv.pos).magnitude < 0.55f) err.Add($"AMR collision {a.name}/{w.amrs[i].name} t={w.t:0}");
            return err;
        }

        [CliCommand("wa_check", "Run the simulation and verify invariants every simulated 20 minutes (slot book-keeping, containers, orders, AMR overlap).")]
        public static string Run([CliArg("hours", "Simulated hours")] float hours = 24f,
                                 [CliArg("stackers", "Stackers")] int stackers = 4, [CliArg("bays", "Bays")] int bays = 30, [CliArg("levels", "Levels")] int levels = 16,
                                 [CliArg("amrs", "AMRs")] int amrs = 10, [CliArg("stations", "Stations")] int stations = 2,
                                 [CliArg("in_per_hour", "Inbound/h")] float inPerHour = 100f, [CliArg("orders_per_hour", "Orders/h")] float ordersPerHour = 60f,
                                 [CliArg("fill", "Initial fill")] float fill = 0.4f, [CliArg("seed", "Seed")] int seed = 1)
        {
            var w = new Warehouse(SimCommands.Make(stackers, bays, levels, amrs, stations, "abc", true, inPerHour, ordersPerHour, fill, seed));
            var all = new List<string>();
            int fullStops = 0; bool wasOpen = true; int minOcc = int.MaxValue, maxOcc = 0;
            for (float t = 0; t < hours * 3600f; t += 1200f)
            {
                for (int i = 0; i < 12000; i++)
                {
                    w.Step();
                    if (wasOpen && !w.inboundOpen) fullStops++;
                    wasOpen = w.inboundOpen;
                }
                minOcc = System.Math.Min(minOcc, w.occ); maxOcc = System.Math.Max(maxOcc, w.occ);
                all.AddRange(Check(w).Select(e => $"t={w.t / 3600f:0.00}h {e}"));
                if (all.Count > 30) break;
            }
            var sb = new StringBuilder();
            sb.AppendLine(w.cfg.ToString());
            sb.AppendLine($"t={w.t / 3600f:0.0}h fill={w.Fill * 100f:0.0}% (min {minOcc * 100f / w.cfg.Capacity:0}% max {maxOcc * 100f / w.cfg.Capacity:0}%) full-stops={fullStops} shipped={w.ordersShipped} ({w.OrdersPerHour:0.0}/h) deadlock-breaks={w.traffic.deadlockBreaks}");
            sb.AppendLine(all.Count == 0 ? "INVARIANTS OK" : "VIOLATIONS:\n  " + string.Join("\n  ", all.Take(20)));
            return sb.ToString();
        }
    }
}
