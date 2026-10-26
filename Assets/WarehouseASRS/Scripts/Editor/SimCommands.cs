using System;
using System.Linq;
using System.Text;
using WarehouseASRS.Core;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace WarehouseASRS.EditorTools
{
    /// <summary>Headless simulation commands (no scene needed): `unity command wa_*`.</summary>
    public static class SimCommands
    {
        public static SlotPolicy ParsePolicy(string p) =>
            p.StartsWith("r", StringComparison.OrdinalIgnoreCase) ? SlotPolicy.Random : p.StartsWith("n", StringComparison.OrdinalIgnoreCase) ? SlotPolicy.Nearest : SlotPolicy.Abc;

        public static WarehouseConfig Make(int stackers, int bays, int levels, int amrs, int stations, string policy, bool dual,
                                           float inPerHour, float ordersPerHour, float initFill, int seed) =>
            new WarehouseConfig
            {
                stackers = Mathf.Clamp(stackers, 2, 6), bays = Mathf.Clamp(bays, 8, 60), levels = Mathf.Clamp(levels, 4, 30),
                amrs = Mathf.Clamp(amrs, 2, 16), stations = Mathf.Clamp(stations, 1, 2), policy = ParsePolicy(policy), dualCommand = dual,
                inboundPerHour = inPerHour, ordersPerHour = ordersPerHour, initialFill = Mathf.Clamp(initFill, 0f, 0.9f), seed = seed
            };

        [CliCommand("wa_sim_run", "Run the warehouse headless and print KPIs.")]
        public static string SimRun([CliArg("stackers", "Stacker cranes (2-6)")] int stackers = 4,
                                    [CliArg("bays", "Columns per rack row")] int bays = 30,
                                    [CliArg("levels", "Shelf levels")] int levels = 16,
                                    [CliArg("amrs", "AMRs (2-16)")] int amrs = 10,
                                    [CliArg("stations", "Picking stations (1-2)")] int stations = 2,
                                    [CliArg("policy", "random | nearest | abc")] string policy = "abc",
                                    [CliArg("dual", "Dual command cycles")] bool dual = true,
                                    [CliArg("in_per_hour", "Supplier boxes per hour")] float inPerHour = 100f,
                                    [CliArg("orders_per_hour", "Orders per hour")] float ordersPerHour = 60f,
                                    [CliArg("fill", "Initial fill 0..0.9")] float fill = 0.4f,
                                    [CliArg("hours", "Simulated hours")] float hours = 2f,
                                    [CliArg("seed", "Seed")] int seed = 1,
                                    [CliArg("events", "Print the last N events")] int events = 0)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var w = new Warehouse(Make(stackers, bays, levels, amrs, stations, policy, dual, inPerHour, ordersPerHour, fill, seed));
            long buildMs = sw.ElapsedMilliseconds;
            w.Advance(hours * 3600f);
            var sb = new StringBuilder();
            sb.AppendLine(w.cfg.ToString());
            sb.Append(w.Report());
            sb.AppendLine($"build={buildMs} ms  wall={sw.ElapsedMilliseconds} ms (x{hours * 3600f * 1000f / Mathf.Max(1, sw.ElapsedMilliseconds - buildMs):0})");
            if (events > 0) foreach (var e in w.events.Skip(Math.Max(0, w.events.Count - events))) sb.AppendLine($"  {e.t / 60f,7:0.0}m {e.en}");
            return sb.ToString();
        }

        [CliCommand("wa_compare", "Compare slot policies (random / nearest / abc) with and without dual command.")]
        public static string Compare([CliArg("stackers", "Stackers")] int stackers = 4,
                                     [CliArg("amrs", "AMRs")] int amrs = 10,
                                     [CliArg("stations", "Stations")] int stations = 2,
                                     [CliArg("in_per_hour", "Supplier boxes per hour")] float inPerHour = 100f,
                                     [CliArg("orders_per_hour", "Orders per hour")] float ordersPerHour = 60f,
                                     [CliArg("fill", "Initial fill")] float fill = 0.4f,
                                     [CliArg("hours", "Simulated hours")] float hours = 6f,
                                     [CliArg("seed", "Seed")] int seed = 1)
        {
            var sb = new StringBuilder();
            sb.AppendLine("policy   dual  stored shipped ord/h lead(min) crane% dual% amr% amrWait% station% fill% deadlocks");
            foreach (var pol in new[] { "random", "nearest", "abc" })
                foreach (var dual in new[] { false, true })
                {
                    var w = new Warehouse(Make(stackers, 30, 16, amrs, stations, pol, dual, inPerHour, ordersPerHour, fill, seed));
                    w.Advance(hours * 3600f);
                    sb.AppendLine($"{pol,-8} {(dual ? "yes" : "no"),-4} {w.inboundStored,6} {w.ordersShipped,7} {w.OrdersPerHour,5:0.0} {w.AvgLead / 60f,9:0.0} {w.CraneUtil * 100f,6:0} {w.DualRatio * 100f,5:0} {w.AmrUtil * 100f,4:0} {w.amrs.Sum(a => a.mv.waitTotal) / Mathf.Max(1f, w.t * w.amrs.Count) * 100f,8:0} {w.StationUtil * 100f,8:0} {w.Fill * 100f,5:0} {w.traffic.deadlockBreaks,9}");
                }
            return sb.ToString();
        }

        [CliCommand("wa_trace", "Run N seconds and print what every AMR is doing (traffic diagnosis).")]
        public static string Trace([CliArg("seconds", "Seconds to simulate")] float seconds = 1800f,
                                   [CliArg("amrs", "AMRs")] int amrs = 10,
                                   [CliArg("stations", "Stations")] int stations = 2,
                                   [CliArg("seed", "Seed")] int seed = 1)
        {
            var w = new Warehouse(Make(4, 30, 16, amrs, stations, "abc", true, 100f, 60f, 0.4f, seed));
            var sb = new StringBuilder();
            float next = 60f;
            while (w.t < seconds)
            {
                w.Step();
                if (w.t >= next) { next += 60f; if (w.amrs.Any(a => a.mv.blocked && a.mv.waitRun > 8f)) { sb.AppendLine($"--- t={w.t:0} blocked AMRs"); foreach (var a in w.amrs.Where(a => a.mv.blocked && a.mv.waitRun > 8f)) sb.AppendLine(Line(w, a)); if (sb.Length > 3500) break; } }
            }
            sb.AppendLine("=== final"); foreach (var a in w.amrs) sb.AppendLine(Line(w, a));
            sb.AppendLine($"deadlock breaks {w.traffic.deadlockBreaks}");
            return sb.ToString();
        }

        static string Line(Warehouse w, Amr a)
        {
            var mv = a.mv; var g = w.L.graph;
            string dest = a.stops.Count > 0 ? a.stops[0].dock.name + (a.stops[0].pick ? "(pick)" : "(drop)") : "-";
            string blk = string.Join(",", mv.blockers.Select(id => w.amrs[id].name));
            return $"  {a.name} {a.state,-8} node {mv.node} @({mv.pos.x:0.0},{mv.pos.z:0.0}) moving={mv.Moving} blocked={mv.blocked} wait={mv.waitRun:0} -> {dest} held=[{string.Join(" ", mv.held)}] blockedBy=[{blk}] dest={mv.Destination}";
        }

        [CliCommand("wa_ik_test", "Solve the picking robot's waypoints at every station and report reachability.")]
        public static string IkTest([CliArg("stations", "Stations")] int stations = 2)
        {
            var L = new WarehouseLayout(new WarehouseConfig { stations = stations });
            return PickRobotPlan.Test(L);
        }

        [CliCommand("wa_layout", "Print the warehouse layout, path network and dock reachability.")]
        public static string LayoutInfo([CliArg("stackers", "Stackers")] int stackers = 4,
                                        [CliArg("amrs", "AMRs")] int amrs = 10,
                                        [CliArg("stations", "Stations")] int stations = 2)
        {
            var cfg = new WarehouseConfig { stackers = stackers, amrs = amrs, stations = stations };
            var L = new WarehouseLayout(cfg);
            var g = L.graph;
            var sb = new StringBuilder();
            sb.AppendLine($"hall z=0..{L.H:0.0}  racks x={WarehouseLayout.RackX0}..{L.RackX1:0.0} height={L.RackTopY:0.0} m  slots={cfg.Capacity}");
            sb.AppendLine($"nodes={g.nodes.Count} edges={g.edges.Count} junctions={g.nodes.Count(n => n.junction)}");
            foreach (var d in L.docks) sb.AppendLine($"  dock {d.id,3} {d.kind,-8} {d.name,-4} node {d.node,4} {g.nodes[d.node].pos} yaw {d.yaw:0} conflicts={g.nodes[d.node].conflicts.Length}");
            int bad = 0;
            foreach (var a in L.docks) foreach (var b in L.docks)
                {
                    if (a == b) continue;
                    if (g.Route(a.node, b.node, Lane.Parts) == null) { bad++; if (bad < 8) sb.AppendLine($"  NO ROUTE {a.name} -> {b.name}"); }
                }
            sb.AppendLine($"unreachable pairs={bad}");
            var r = g.Route(L.padDocks[0].node, L.stackerDocks[stackers - 1].node, Lane.Parts);
            sb.AppendLine($"route {L.padDocks[0].name} -> {L.stackerDocks[stackers - 1].name}: {g.RouteLength(r):0.0} m");
            float t0 = L.SlotTime(L.Encode(0, 0, 0, 0)), t1 = L.SlotTime(L.Encode(0, 0, cfg.bays - 1, cfg.levels - 1));
            sb.AppendLine($"crane time from I/O: nearest slot {t0:0.0}s, farthest {t1:0.0}s");
            return sb.ToString();
        }
    }
}
