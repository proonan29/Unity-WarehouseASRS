using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using WarehouseASRS.Core;
using WarehouseASRS.World;

namespace WarehouseASRS
{
    public class CompareRow
    {
        public SlotPolicy policy; public bool dual;
        public int stored, shipped;
        public float perHour, leadMin, craneUtil, dualRatio, amrUtil, fill, avgCraneTrip;
    }

    /// <summary>
    /// Owns the simulation, advances it in play mode at the chosen speed, and exposes the actions used by the UI and the CLI:
    /// configuration, run control, selection, camera presets and the slot-policy comparison (headless, in the background).
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public class AppController : MonoBehaviour
    {
        public WarehouseView view;
        public CameraRig rig;
        public WarehouseConfig config = new WarehouseConfig();
        public float speed = 20f;
        public bool running = true;
        public bool followSelected = true;

        [NonSerialized] public Warehouse sim;
        [NonSerialized] public CompareRow[] compareRows;
        [NonSerialized] public bool comparing;
        [NonSerialized] public float compareHours = 8f;
        public event Action onChanged;
        double acc;
        float lastStepMs;
        Task<CompareRow[]> compareTask;

        public bool Ready => sim != null && view != null && view.IsBuilt;
        public float StepMs => lastStepMs;

        void OnEnable() => EnsureBuilt();

        /// <summary>Builds the simulation once and (re)builds the generated view when it is missing (DontSave objects).</summary>
        public void EnsureBuilt()
        {
            if (view == null || rig == null) return;
            if (sim == null) sim = new Warehouse(config);
            if (!view.IsBuilt || view.sim != sim) view.Build(sim);
            rig.follow = FollowTarget;
        }

        void Update()
        {
            if (sim == null || view == null || !view.IsBuilt) EnsureBuilt();
            if (sim == null) return;
            if (compareTask != null && compareTask.IsCompleted)
            {
                compareRows = compareTask.Status == TaskStatus.RanToCompletion ? compareTask.Result : null;
                comparing = false; compareTask = null;
                onChanged?.Invoke();
            }
            float frac = 0f;
            if (Application.isPlaying && running)
            {
                acc += Time.unscaledDeltaTime * speed;
                int n = (int)(acc / Warehouse.Dt);
                const int max = 6000;
                if (n > max) { n = max; acc = 0; } else acc -= n * Warehouse.Dt;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < n; i++) sim.Step();
                lastStepMs = (float)sw.Elapsed.TotalMilliseconds;
                frac = Mathf.Min((float)acc, Warehouse.Dt);
            }
            view.Render(frac);
        }

        Vector3? FollowTarget()
        {
            if (!followSelected || sim == null) return null;
            if (view.selectedBox >= 0)
            {
                var b = sim.boxes.Find(x => x.id == view.selectedBox);
                return b != null ? sim.BoxPos(b, sim.t) : (Vector3?)null;
            }
            if (view.selectedAmr >= 0 && view.selectedAmr < sim.amrs.Count) return sim.amrs[view.selectedAmr].mv.pos;
            return null;
        }

        // ------------------------------------------------------------------ actions

        public void Rebuild(WarehouseConfig c)
        {
            config = c.Clone();
            sim = new Warehouse(config);
            acc = 0;
            view.selectedBox = view.selectedAmr = view.selectedStacker = view.selectedStation = -1; view.highlightSku = -1;
            view.Build(sim);
            rig.follow = FollowTarget;
            Overview();
            onChanged?.Invoke();
        }

        public void Restart() => Rebuild(config);

        public void Configure(Action<WarehouseConfig> edit) { var c = config.Clone(); edit(c); Sanitize(c); Rebuild(c); }

        public static void Sanitize(WarehouseConfig c)
        {
            c.stackers = Mathf.Clamp(c.stackers, 1, 6);
            c.bays = Mathf.Clamp(c.bays, 8, 40);
            c.levels = Mathf.Clamp(c.levels, 4, 24);
            c.amrs = Mathf.Clamp(c.amrs, 2, 16);
            c.stations = Mathf.Clamp(c.stations, 1, 2);
            c.inboundPerHour = Mathf.Clamp(c.inboundPerHour, 0f, 600f);
            c.ordersPerHour = Mathf.Clamp(c.ordersPerHour, 0f, 300f);
            c.initialFill = Mathf.Clamp(c.initialFill, 0f, 0.95f);
        }

        public void Advance(float seconds) { EnsureBuilt(); sim.Advance(seconds); view.Render(0f); }

        /// <summary>kind: 0 stacker, 1 station, 2 AMR, 3 inbound line, 4 box, other = clear.</summary>
        public void Select(int kind, int id)
        {
            view.selectedStacker = kind == 0 ? id : -1;
            view.selectedStation = kind == 1 ? id : -1;
            view.selectedAmr = kind == 2 ? id : -1;
            view.selectedBox = kind == 4 ? id : -1;
            view.highlightSku = -1;
            if (kind == 4)
            {
                var b = sim.boxes.Find(x => x.id == id);
                if (b != null && b.sku >= 0) view.highlightSku = b.sku;
            }
            rig.follow = FollowTarget;
            if (kind == 0 && id >= 0) FocusAisle(id);
            else if (kind == 1 && id >= 0) FocusStation(id);
            onChanged?.Invoke();
        }

        // ------------------------------------------------------------------ camera presets

        public void Overview()
        {
            var L = sim.L;
            rig.View(new Vector3(-3f, 0f, L.H * 0.5f - 2f), 0f, 52f, Mathf.Max(60f, (L.RackX1 + 38f) * 1.0f));
        }

        public void FocusInbound() => rig.View(new Vector3(-22f, 0f, sim.L.H - 4.5f), 20f, 42f, 20f);
        public void FocusStation(int k) => rig.View(new Vector3(-20f, 1f, sim.L.stationZc[Mathf.Clamp(k, 0, sim.L.stationZc.Length - 1)]), -35f, 36f, 15f);
        public void FocusAisle(int s) => rig.View(new Vector3(WarehouseLayout.RackX0 + 6f, 3f, sim.L.aisleZ[Mathf.Clamp(s, 0, sim.L.aisleZ.Length - 1)]), 90f, 22f, 20f);
        public void FocusRacks() => rig.View(new Vector3(WarehouseLayout.RackX0 + sim.cfg.bays * 0.35f, 3f, sim.L.H * 0.5f), 35f, 28f, 55f);
        public void FocusTop() => rig.View(new Vector3(-3f, 0f, sim.L.H * 0.5f - 2f), 0f, 88f, Mathf.Max(60f, (sim.L.RackX1 + 38f) * 1.05f));

        // ------------------------------------------------------------------ policy comparison (background)

        public void StartCompare(float hours)
        {
            if (comparing) return;
            comparing = true; compareRows = null; compareHours = hours;
            var cfg = config.Clone();
            compareTask = Task.Run(() => RunCompare(cfg, hours));
            onChanged?.Invoke();
        }

        public static CompareRow[] RunCompare(WarehouseConfig cfg, float hours)
        {
            var rows = new System.Collections.Generic.List<CompareRow>();
            foreach (SlotPolicy p in Enum.GetValues(typeof(SlotPolicy)))
                foreach (bool dual in new[] { false, true })
                {
                    var c = cfg.Clone(); c.policy = p; c.dualCommand = dual;
                    var w = new Warehouse(c);
                    w.Advance(hours * 3600f);
                    rows.Add(new CompareRow
                    {
                        policy = p, dual = dual, stored = w.inboundStored, shipped = w.ordersShipped, perHour = w.OrdersPerHour, leadMin = w.AvgLead / 60f,
                        craneUtil = w.CraneUtil, dualRatio = w.DualRatio, amrUtil = w.AmrUtil, fill = w.Fill
                    });
                }
            return rows.ToArray();
        }
    }
}
