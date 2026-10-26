using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using WarehouseASRS.Core;
using WarehouseASRS.World;

namespace WarehouseASRS.UI
{
    /// <summary>History graph (occupancy and shipped orders) drawn with Painter2D.</summary>
    public class SparkGraph : VisualElement
    {
        public List<HistoryPoint> data = new List<HistoryPoint>();
        public SparkGraph()
        {
            style.height = 70; style.backgroundColor = new Color(1f, 1f, 1f, 0.05f); style.marginBottom = 4;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (data.Count < 2 || r.width < 4) return;
            var p = ctx.painter2D;
            float t0 = data[0].t, t1 = data[data.Count - 1].t;
            float maxShip = Mathf.Max(1, data[data.Count - 1].shipped);
            void Line(Func<HistoryPoint, float> f, Color c)
            {
                p.strokeColor = c; p.lineWidth = 2f; p.BeginPath();
                for (int i = 0; i < data.Count; i++)
                {
                    float x = r.x + (data[i].t - t0) / Mathf.Max(1f, t1 - t0) * r.width, y = r.yMax - Mathf.Clamp01(f(data[i])) * (r.height - 4) - 2;
                    if (i == 0) p.MoveTo(new Vector2(x, y)); else p.LineTo(new Vector2(x, y));
                }
                p.Stroke();
            }
            Line(h => h.fill, new Color(1f, 0.72f, 0.25f));
            Line(h => h.shipped / maxShip, new Color(0.4f, 0.85f, 0.5f));
            Line(h => h.craneUtil, new Color(0.45f, 0.7f, 1f));
        }
    }

    /// <summary>UI Toolkit interface built in code: left panel (configuration, run, view, compare, legend), right panel (KPIs, stackers,
    /// AMRs, stations, selection details), event log and 3D labels.</summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(UIDocument))]
    public class WarehouseUI : MonoBehaviour
    {
        public AppController app;

        UIDocument doc;
        VisualElement root, left, right, bottom, labelLayer, listBox, detail, compareBox;
        Label timeLabel, stateLabel, kpiLabel, eventsLabel, hintLabel, perfLabel, gateLabel;
        VisualElement fillBar;
        SparkGraph graph;
        readonly List<Label> labelPool = new List<Label>();
        float nextRefresh;
        bool built;

        static readonly Color Panel = new Color(0.05f, 0.07f, 0.10f, 0.88f);
        static readonly Color Accent = new Color(1f, 0.72f, 0.25f);
        static readonly Color Dim = new Color(0.68f, 0.72f, 0.78f);

        void OnEnable()
        {
            doc = GetComponent<UIDocument>();
            Build();
            if (app != null && app.rig != null)
            {
                app.rig.isOverUI = IsOverUI;
                app.rig.onClick -= OnClick; app.rig.onClick += OnClick;
                app.onChanged -= OnAppChanged; app.onChanged += OnAppChanged;
            }
        }

        void OnDisable()
        {
            if (app != null && app.rig != null) app.rig.onClick -= OnClick;
            if (app != null) app.onChanged -= OnAppChanged;
        }

        void OnAppChanged() => Build();

        // ------------------------------------------------------------------ helpers

        static void Round(IStyle s, float r) { s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = r; }

        static VisualElement Box(Color bg, float pad = 10)
        {
            var v = new VisualElement();
            v.style.backgroundColor = bg;
            v.style.paddingLeft = v.style.paddingRight = pad; v.style.paddingTop = v.style.paddingBottom = pad;
            Round(v.style, 8);
            return v;
        }

        static Label Text(string s, int size = 13, Color? c = null, bool bold = false)
        {
            var l = new Label(s);
            l.style.fontSize = size; l.style.color = c ?? new Color(0.92f, 0.94f, 0.97f);
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            return l;
        }

        static VisualElement Row(bool wrap = true)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            if (wrap) r.style.flexWrap = Wrap.Wrap;
            r.style.marginBottom = 4; r.style.alignItems = Align.Center;
            return r;
        }

        static Button Btn(string s, Action a, bool on = false, float minW = 0)
        {
            var b = new Button(a) { text = s };
            b.style.fontSize = 12;
            b.style.marginLeft = 0; b.style.marginRight = 4; b.style.marginTop = 2; b.style.marginBottom = 2;
            b.style.paddingLeft = 7; b.style.paddingRight = 7; b.style.paddingTop = 3; b.style.paddingBottom = 3;
            b.style.backgroundColor = on ? new Color(0.78f, 0.45f, 0.08f) : new Color(0.15f, 0.19f, 0.25f);
            b.style.color = Color.white;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            Round(b.style, 5);
            if (minW > 0) b.style.minWidth = minW;
            return b;
        }

        static void Section(VisualElement parent, string title)
        {
            var l = Text(title, 12, Accent, true);
            l.style.marginTop = 10; l.style.marginBottom = 4;
            l.style.borderBottomWidth = 1; l.style.borderBottomColor = new Color(1f, 0.72f, 0.25f, 0.25f);
            parent.Add(l);
        }

        static void Check(VisualElement parent, string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(Loc.T(label)) { value = get() };
            t.style.fontSize = 12; t.style.color = Color.white; t.style.marginBottom = 0; t.style.marginTop = 0;
            t.labelElement.style.minWidth = 160; t.labelElement.style.color = new Color(0.86f, 0.9f, 0.95f);
            t.RegisterValueChangedCallback(e => set(e.newValue));
            parent.Add(t);
        }

        static VisualElement Chip(string s, Color c)
        {
            var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; r.style.marginRight = 8; r.style.marginBottom = 2;
            var sw = new VisualElement(); sw.style.width = 10; sw.style.height = 10; sw.style.backgroundColor = c; sw.style.marginRight = 4; Round(sw.style, 2);
            r.Add(sw); r.Add(Text(s, 11, new Color(0.86f, 0.9f, 0.95f)));
            return r;
        }

        static VisualElement Bar(float frac, Color c, float width = 120, float h = 6)
        {
            var bg = new VisualElement(); bg.style.width = width; bg.style.height = h; bg.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f); Round(bg.style, 3);
            var fg = new VisualElement(); fg.style.width = new Length(Mathf.Clamp01(frac) * 100f, LengthUnit.Percent); fg.style.height = h; fg.style.backgroundColor = c; Round(fg.style, 3);
            bg.Add(fg);
            return bg;
        }

        void Stepper(VisualElement parent, string name, string value, Action dec, Action inc)
        {
            var r = Row();
            var l = Text(Loc.T(name), 12, Dim); l.style.width = 130; r.Add(l);
            r.Add(Btn("−", dec, false, 26));
            var v = Text(value, 13, Color.white, true); v.style.minWidth = 38; v.style.unityTextAlign = TextAnchor.MiddleCenter; r.Add(v);
            r.Add(Btn("+", inc, false, 26));
            parent.Add(r);
        }

        // ------------------------------------------------------------------ build

        public void Build()
        {
            if (doc == null) doc = GetComponent<UIDocument>();
            root = doc != null ? doc.rootVisualElement : null;
            if (root == null || app == null || app.sim == null) return;
            var sim = app.sim; var cfg = app.config; var v = app.view;
            root.Clear();
            labelPool.Clear();
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;

            labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            labelLayer.style.position = Position.Absolute; labelLayer.style.left = 0; labelLayer.style.top = 0; labelLayer.style.right = 0; labelLayer.style.bottom = 0;
            root.Add(labelLayer);

            // ---------------- left panel
            left = Box(Panel, 12);
            left.style.position = Position.Absolute; left.style.left = 10; left.style.top = 10; left.style.bottom = 10; left.style.width = 340;
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1;
            left.Add(scroll); root.Add(left);
            var lc = scroll.contentContainer;

            lc.Add(Text("WarehouseASRS", 22, Color.white, true));
            lc.Add(Text(Loc.T("Automated warehouse digital twin"), 12, Dim));

            Section(lc, Loc.T("Warehouse"));
            Stepper(lc, "Stackers", cfg.stackers.ToString(), () => app.Configure(c => c.stackers--), () => app.Configure(c => c.stackers++));
            Stepper(lc, "Levels", cfg.levels.ToString(), () => app.Configure(c => c.levels -= 2), () => app.Configure(c => c.levels += 2));
            Stepper(lc, "Bays", cfg.bays.ToString(), () => app.Configure(c => c.bays -= 2), () => app.Configure(c => c.bays += 2));
            Stepper(lc, "AMRs", cfg.amrs.ToString(), () => app.Configure(c => c.amrs--), () => app.Configure(c => c.amrs++));
            Stepper(lc, "Stations", cfg.stations.ToString(), () => app.Configure(c => c.stations--), () => app.Configure(c => c.stations++));
            Stepper(lc, "Inbound / hour", cfg.inboundPerHour.ToString("0"), () => app.Configure(c => c.inboundPerHour -= 20), () => app.Configure(c => c.inboundPerHour += 20));
            Stepper(lc, "Orders / hour", cfg.ordersPerHour.ToString("0"), () => app.Configure(c => c.ordersPerHour -= 10), () => app.Configure(c => c.ordersPerHour += 10));
            lc.Add(Text(Loc.T("Slot policy"), 12, Dim));
            var pr = Row();
            foreach (SlotPolicy p in Enum.GetValues(typeof(SlotPolicy))) { var pp = p; pr.Add(Btn(Loc.Policy(p), () => app.Configure(c => c.policy = pp), cfg.policy == p)); }
            lc.Add(pr);
            Check(lc, "Dual command", () => cfg.dualCommand, x => app.Configure(c => c.dualCommand = x));
            var rr0 = Row(); rr0.Add(Btn(Loc.T("Restart"), () => app.Restart())); lc.Add(rr0);

            Section(lc, Loc.T("Run"));
            var rr = Row();
            rr.Add(Btn(app.running ? "❚❚ " + Loc.T("Pause") : "▶ " + Loc.T("Start"), () => { app.running = !app.running; Build(); }, app.running, 80));
            foreach (var s in new[] { 1, 5, 20, 60, 200, 600 }) { int ss = s; rr.Add(Btn($"{s}x", () => { app.speed = ss; Build(); }, Mathf.Approximately(app.speed, s))); }
            lc.Add(rr);
            timeLabel = Text("", 15, Color.white, true); lc.Add(timeLabel);
            stateLabel = Text("", 11, Dim); lc.Add(stateLabel);

            Section(lc, Loc.T("View"));
            var vr = Row();
            vr.Add(Btn(Loc.T("Overview"), () => app.Overview()));
            vr.Add(Btn(Loc.T("Top"), () => app.FocusTop()));
            vr.Add(Btn(Loc.T("Racks"), () => app.FocusRacks()));
            vr.Add(Btn(Loc.T("Inbound"), () => app.FocusInbound()));
            lc.Add(vr);
            var vr2 = Row();
            for (int k = 0; k < cfg.stations; k++) { int kk = k; vr2.Add(Btn("PK" + (k + 1), () => app.Select(1, kk), v.selectedStation == k)); }
            for (int s = 0; s < cfg.stackers; s++) { int ss = s; vr2.Add(Btn("S" + (s + 1), () => app.Select(0, ss), v.selectedStacker == s)); }
            lc.Add(vr2);
            Check(lc, "Lanes", () => v.showLanes, x => v.showLanes = x);
            Check(lc, "Planned paths", () => v.showPaths, x => v.showPaths = x);
            Check(lc, "Docks", () => v.showZones, x => v.showZones = x);
            Check(lc, "Labels", () => v.showLabels, x => v.showLabels = x);
            Check(lc, "Follow selection", () => app.followSelected, x => app.followSelected = x);

            Section(lc, Loc.T("Compare"));
            lc.Add(Text(Loc.T("Same warehouse and demand, simulated headless."), 11, Dim));
            var cpr = Row();
            foreach (var h in new[] { 4f, 12f, 24f }) { float hh = h; cpr.Add(Btn($"{Loc.T("Compare policies")} {h:0}h", () => app.StartCompare(hh))); }
            lc.Add(cpr);
            compareBox = new VisualElement(); lc.Add(compareBox);
            BuildCompare();

            Section(lc, Loc.T("Legend"));
            var lg = Row();
            string[] names = { "Fast mover (A)", "Medium (B)", "Slow (C)", "Empty box", "Reserved for order", "Order box", "Selected SKU" };
            for (int i = 0; i < WarehouseView.Cats; i++) lg.Add(Chip(Loc.T(names[i]), WarehouseView.CatColor[i]));
            lc.Add(lg);

            Section(lc, Loc.T("Settings"));
            var lr = Row();
            lr.Add(Btn("한국어", () => { Loc.Korean = true; Build(); }, Loc.Korean));
            lr.Add(Btn("English", () => { Loc.Korean = false; Build(); }, !Loc.Korean));
            lc.Add(lr);
            perfLabel = Text("", 10, Dim); lc.Add(perfLabel);

            // ---------------- right panel
            right = Box(Panel, 10);
            right.style.position = Position.Absolute; right.style.right = 10; right.style.top = 10; right.style.bottom = 10; right.style.width = 370;
            root.Add(right);
            right.Add(Text(Loc.T("Warehouse KPIs"), 14, Accent, true));
            fillBar = Bar(0, Accent, 340, 8); fillBar.style.marginBottom = 4; right.Add(fillBar);
            kpiLabel = Text("", 12); right.Add(kpiLabel);
            gateLabel = Text("", 11, new Color(1f, 0.8f, 0.4f)); right.Add(gateLabel);
            graph = new SparkGraph(); right.Add(graph);
            var listScroll = new ScrollView(ScrollViewMode.Vertical);
            listScroll.style.flexGrow = 1; listScroll.style.flexShrink = 1;
            listBox = listScroll.contentContainer;
            right.Add(listScroll);
            var dScroll = new ScrollView(ScrollViewMode.Vertical);
            dScroll.style.height = new Length(30, LengthUnit.Percent); dScroll.style.flexShrink = 0; dScroll.style.marginTop = 6;
            dScroll.style.borderTopWidth = 1; dScroll.style.borderTopColor = new Color(1f, 0.72f, 0.25f, 0.25f);
            detail = dScroll.contentContainer;
            right.Add(dScroll);

            // ---------------- bottom: events + hint
            bottom = Box(new Color(0.05f, 0.07f, 0.10f, 0.78f), 8);
            bottom.style.position = Position.Absolute; bottom.style.left = 362; bottom.style.right = 392; bottom.style.bottom = 10;
            bottom.Add(Text(Loc.T("Events"), 12, Accent, true));
            eventsLabel = Text("", 11); bottom.Add(eventsLabel);
            root.Add(bottom);
            hintLabel = Text(Loc.T("Drag: rotate · Right-drag: pan · Wheel: zoom · Click: select box / AMR / stacker / station"), 11, Dim);
            hintLabel.style.position = Position.Absolute; hintLabel.style.left = 362; hintLabel.style.top = 12;
            hintLabel.pickingMode = PickingMode.Ignore;
            root.Add(hintLabel);

            built = true;
            nextRefresh = 0;
            Refresh();
        }

        void BuildCompare()
        {
            compareBox.Clear();
            if (app.comparing) { compareBox.Add(Text(Loc.T("Running comparison…"), 12, Accent)); return; }
            var rows = app.compareRows;
            if (rows == null) return;
            compareBox.Add(Text($"{app.compareHours:0} h", 11, Dim));
            var t = Text("policy      dual  orders/h  lead   crane  AMR\n" + string.Join("\n", rows.Select(r =>
                $"{Loc.Policy(r.policy),-9} {(r.dual ? "O" : "X"),-4}  {r.perHour,6:0.0}  {r.leadMin,5:0.0}m  {r.craneUtil * 100f,3:0}%  {r.amrUtil * 100f,3:0}%")), 11);
            compareBox.Add(t);
        }

        // ------------------------------------------------------------------ dynamic

        void Update()
        {
            if (!built || app == null || !app.Ready) return;
            if (Time.realtimeSinceStartup >= nextRefresh) { nextRefresh = Time.realtimeSinceStartup + 0.4f; Refresh(); }
        }

        void LateUpdate()
        {
            if (!built || app == null || !app.Ready) return;
            UpdatePanelsForCamera();
            PlaceLabels();
        }

        static string Clock(double t) { var ts = TimeSpan.FromSeconds(t); return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"; }

        public void Refresh()
        {
            if (!built || app == null || !app.Ready) return;
            var w = app.sim; var cfg = app.config;
            if (app.compareRows != null || app.comparing) BuildCompare();
            timeLabel.text = $"{Loc.T("Sim time")}  {Clock(w.t)}   {app.speed:0}x";
            stateLabel.text = $"{cfg.stackers}×{cfg.bays}×{cfg.levels} · {cfg.Capacity} slots · {Loc.Policy(cfg.policy)} · {(app.running ? Loc.T("running") : Loc.T("paused"))}";
            fillBar.Q<VisualElement>().style.width = new Length(Mathf.Clamp01(w.Fill) * 100f, LengthUnit.Percent);
            float wait = w.amrs.Sum(a => a.mv.waitTotal) / Mathf.Max(1f, w.t * w.amrs.Count);
            kpiLabel.text =
                $"{Loc.T("Occupancy")} {w.Fill * 100f:0.0}%  ({w.occ}/{cfg.Capacity})\n" +
                $"{Loc.T("Boxes stored")} {w.StoredBoxes} · {Loc.T("Empty boxes")} {w.EmptyBoxes} · {Loc.T("Units in stock")} {w.Units}\n" +
                $"{Loc.T("Throughput")} {w.OrdersPerHour:0.0} /h · {Loc.T("Orders")} {w.ordersShipped}/{w.ordersCreated}\n" +
                $"{Loc.T("Lead time")} {w.AvgLead / 60f:0.0} min\n" +
                $"{Loc.T("Crane utilisation")} {w.CraneUtil * 100f:0}% · {Loc.T("Dual-command share")} {w.DualRatio * 100f:0}%\n" +
                $"{Loc.T("AMR utilisation")} {w.AmrUtil * 100f:0}% · {Loc.T("Station utilisation")} {w.StationUtil * 100f:0}%\n" +
                $"{Loc.T("Traffic wait")} {wait * 100f:0}%";
            gateLabel.text = $"{Loc.T("Inbound gate")}: {(w.inboundOpen ? Loc.T("open") : "⛔ " + Loc.T("FULL STOP"))}" + (w.orderPaused ? $"  ·  {Loc.T("Orders")}: {Loc.T("waiting for stock")}" : "");
            gateLabel.style.color = w.inboundOpen && !w.orderPaused ? Dim : new Color(1f, 0.6f, 0.35f);
            graph.data = w.history; graph.MarkDirtyRepaint();
            perfLabel.text = $"sim {app.StepMs:0.0} ms/frame · nodes {w.L.graph.nodes.Count} · boxes {w.boxes.Count}";

            listBox.Clear();
            listBox.Add(Text(Loc.T("Stacker cranes"), 12, Accent, true));
            for (int s = 0; s < w.stackers.Length; s++)
            {
                var st = w.stackers[s]; int ss = s;
                var row = Row(false);
                row.style.backgroundColor = app.view.selectedStacker == s ? new Color(0.78f, 0.45f, 0.08f, 0.35f) : new Color(1f, 1f, 1f, 0.04f);
                var n = Text($"S{s + 1}", 12, Color.white, true); n.style.width = 26; row.Add(n);
                row.Add(Bar(st.stored / (float)cfg.SlotsPerStacker, Accent, 70));
                var info = Text($" {st.stored} · {Loc.T(st.crane.action)} · {st.inQ.Count}/{st.outQ.Count} · {st.crane.busyTime / Mathf.Max(1f, w.t) * 100f:0}%", 11, Dim); info.style.marginLeft = 4; row.Add(info);
                row.RegisterCallback<ClickEvent>(_ => app.Select(0, ss));
                listBox.Add(row);
            }
            var hs = Text(Loc.T("Picking stations"), 12, Accent, true); hs.style.marginTop = 6; listBox.Add(hs);
            for (int k = 0; k < w.stations.Length; k++)
            {
                var sn = w.stations[k]; int kk = k;
                var row = Row(false);
                row.style.backgroundColor = app.view.selectedStation == k ? new Color(0.78f, 0.45f, 0.08f, 0.35f) : new Color(1f, 1f, 1f, 0.04f);
                row.Add(Text($"PK{k + 1}  {Loc.T(sn.phase.ToString())}  ·  {Loc.T("Queue")} {sn.queue.Count} · {sn.inBuf.Count}/{sn.outBuf.Count} · {sn.ordersDone}", 11, new Color(0.7f, 1f, 0.7f)));
                row.RegisterCallback<ClickEvent>(_ => app.Select(1, kk));
                listBox.Add(row);
            }
            var ha = Text(Loc.T("AMR fleet"), 12, Accent, true); ha.style.marginTop = 6; listBox.Add(ha);
            for (int i = 0; i < w.amrs.Count; i++)
            {
                var a = w.amrs[i]; int ii = i;
                var row = Row(false);
                row.style.backgroundColor = app.view.selectedAmr == i ? new Color(0.78f, 0.45f, 0.08f, 0.35f) : new Color(1f, 1f, 1f, 0.04f);
                var n = Text(a.name, 11, new Color(0.7f, 0.88f, 1f), true); n.style.width = 30; row.Add(n);
                row.Add(Bar(a.battery / 100f, a.lowBat ? new Color(1f, 0.3f, 0.25f) : new Color(0.3f, 0.9f, 0.45f), 50));
                var info = Text($" {Loc.T(a.state.ToString())}{(a.load != null ? " · " + a.load.Rfid : "")}", 11, Dim); info.style.marginLeft = 4; row.Add(info);
                row.RegisterCallback<ClickEvent>(_ => app.Select(2, ii));
                listBox.Add(row);
            }

            detail.Clear();
            var v = app.view;
            if (v.selectedBox >= 0) { var b = w.boxes.Find(x => x.id == v.selectedBox); if (b != null) BoxDetail(b); else detail.Add(Text(Loc.T("Shipped"), 12, Dim)); }
            else if (v.selectedAmr >= 0 && v.selectedAmr < w.amrs.Count) AmrDetail(w.amrs[v.selectedAmr]);
            else if (v.selectedStacker >= 0 && v.selectedStacker < w.stackers.Length) StackerDetail(w.stackers[v.selectedStacker]);
            else if (v.selectedStation >= 0 && v.selectedStation < w.stations.Length) StationDetail(w.stations[v.selectedStation]);

            var lines = new List<string>();
            for (int i = w.events.Count - 1; i >= 0 && lines.Count < 6; i--) lines.Add($"{Clock(w.events[i].t)}  {Loc.Event(w.events[i])}");
            eventsLabel.text = string.Join("\n", lines);
        }

        void KV(string k, string v)
        {
            var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row;
            var a = Text(k, 12, Dim); a.style.width = 110; a.style.flexShrink = 0;
            var b = Text(v, 12); b.style.flexShrink = 1;
            r.Add(a); r.Add(b);
            detail.Add(r);
        }

        void Deselect() { var r = Row(); r.Add(Btn(Loc.T("Deselect"), () => app.Select(-1, -1))); detail.Add(r); }

        string SlotName(int slot)
        {
            var (s, side, bay, level) = app.sim.L.Decode(slot);
            return $"S{s + 1}-{(side == 0 ? "S" : "N")}-{bay + 1:00}-{level + 1:00}";
        }

        void BoxDetail(WarehouseASRS.Core.Box b)
        {
            var w = app.sim;
            detail.Add(Text($"{Loc.T("Box")} {b.Rfid}", 16, Color.white, true));
            if (b.sku >= 0)
            {
                var sku = w.skus[b.sku];
                KV(Loc.T("SKU"), $"{sku.id:00} {Loc.SkuName(sku)}");
                KV(Loc.T("Class"), sku.ClassName);
                KV(Loc.T("Quantity"), $"{b.qty} / {sku.cap}");
            }
            else KV(Loc.T("SKU"), b.orderBox ? Loc.T("Order box") : Loc.T("empty"));
            KV(Loc.T("Location"), Loc.T(b.loc.ToString()) + (b.slot >= 0 && (b.loc == BoxLoc.Stored || b.loc == BoxLoc.OnCrane) ? " · " + SlotName(b.slot) : ""));
            KV(Loc.T("Order"), b.orderId >= 0 ? "#" + b.orderId : Loc.T("none"));
            if (b.orderBox) KV(Loc.T("Quantity"), b.qty.ToString());
            Deselect();
        }

        void AmrDetail(Amr a)
        {
            detail.Add(Text($"AMR {a.name}", 16, new Color(0.7f, 0.88f, 1f), true));
            KV(Loc.T("State"), Loc.T(a.state.ToString()) + (a.lowBat ? " (low)" : ""));
            KV(Loc.T("Battery"), $"{a.battery:0}%");
            KV(Loc.T("Load"), a.load != null ? a.load.Rfid : "-");
            KV(Loc.T("Job"), a.stops.Count > 0 ? string.Join(" → ", a.stops.Select(s => (s.pick ? "▲" : "▼") + s.dock.name)) : "-");
            KV(Loc.T("Distance"), $"{a.mv.dist / 1000f:0.00} km");
            KV(Loc.T("Waiting"), $"{a.mv.waitTotal / 60f:0.0} min");
            Deselect();
        }

        void StackerDetail(Stacker st)
        {
            detail.Add(Text($"{Loc.T("Stacker")} S{st.idx + 1}", 16, Accent, true));
            KV(Loc.T("State"), Loc.T(st.crane.action));
            KV(Loc.T("Slots free"), $"{st.free} / {app.config.SlotsPerStacker}");
            KV(Loc.T("Boxes stored"), $"{st.stored} ({Loc.T("Empty boxes")} {st.empties})");
            KV(Loc.T("Commands"), $"{st.crane.commands} ({Loc.T("Chained")} {st.crane.chainedCommands})");
            KV(Loc.T("In queue"), $"{st.inQ.Count} (+{st.inResv})");
            KV(Loc.T("Out queue"), $"{st.outQ.Count} (+{st.outResv}) · {Loc.T("Retrievals")} {st.retr.Count}");
            Deselect();
        }

        void StationDetail(Station sn)
        {
            detail.Add(Text($"{Loc.T("Station")} PK{sn.idx + 1}", 16, new Color(0.7f, 1f, 0.7f), true));
            KV(Loc.T("State"), Loc.T(sn.phase.ToString()));
            var o = sn.Active;
            KV(Loc.T("Active order"), o != null ? $"#{o.id} · {o.lines} / {o.totalQty} · {o.picks.Count(p => p.done)}/{o.picks.Count}" : "-");
            KV(Loc.T("Queue"), sn.queue.Count.ToString());
            KV(Loc.T("Orders done"), sn.ordersDone.ToString());
            KV(Loc.T("Items picked"), sn.itemsPicked.ToString());
            Deselect();
        }

        // ------------------------------------------------------------------ interaction

        bool IsOverUI(Vector2 screen)
        {
            if (root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            return (left != null && left.worldBound.Contains(p)) || (right != null && right.worldBound.Contains(p)) || (bottom != null && bottom.worldBound.Contains(p));
        }

        void OnClick(Vector2 screen)
        {
            if (app == null || !app.Ready) return;
            var (kind, id) = app.view.Pick(app.rig.Cam, screen);
            app.Select(kind, id);
        }

        void UpdatePanelsForCamera()
        {
            if (root?.panel == null || left == null) return;
            float scale = Screen.width / Mathf.Max(1f, root.layout.width);
            if (float.IsNaN(scale) || scale <= 0) return;
            app.rig.panelLeft = (left.layout.width + 10) * scale;
            app.rig.panelRight = (right.layout.width + 10) * scale;
        }

        void PlaceLabels()
        {
            var view = app.view;
            var cam = app.rig.Cam;
            if (root?.panel == null || cam == null) return;
            int n = 0;
            foreach (var l in view.labels)
            {
                var sp = cam.WorldToScreenPoint(l.world);
                if (sp.z <= 0 || sp.x < 0 || sp.y < 0 || sp.x > Screen.width || sp.y > Screen.height) continue;
                if (l.kind == 2 && app.rig.distance > 70f && !l.selected) continue;
                var pp = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sp.x, Screen.height - sp.y));
                if (n >= labelPool.Count)
                {
                    var nl = new Label { pickingMode = PickingMode.Ignore };
                    nl.style.position = Position.Absolute;
                    nl.style.unityTextOutlineWidth = 0.15f; nl.style.unityTextOutlineColor = new Color(0, 0, 0, 0.9f);
                    labelLayer.Add(nl); labelPool.Add(nl);
                }
                var lb = labelPool[n++];
                lb.style.display = DisplayStyle.Flex;
                lb.text = l.text;
                lb.style.color = l.color;
                bool big = l.kind == 0 || l.kind == 1;
                lb.style.fontSize = big ? 13 : l.selected ? 13 : 10;
                lb.style.unityFontStyleAndWeight = big || l.selected ? FontStyle.Bold : FontStyle.Normal;
                lb.style.backgroundColor = l.selected ? new Color(0.78f, 0.45f, 0.08f, 0.7f) : big ? new Color(0f, 0f, 0f, 0.45f) : new Color(0, 0, 0, 0);
                lb.style.left = pp.x - (big ? 50 : 12);
                lb.style.top = pp.y - 10;
            }
            for (int i = n; i < labelPool.Count; i++) labelPool[i].style.display = DisplayStyle.None;
        }
    }
}
