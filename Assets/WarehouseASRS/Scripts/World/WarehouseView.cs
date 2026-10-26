using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WarehouseASRS.Core;

namespace WarehouseASRS.World
{
    public struct Label3D
    {
        public Vector3 world;
        public string text;
        public Color color;
        public int kind;      // 0 stacker, 1 station, 2 amr, 3 inbound line
        public int id;
        public bool selected;
    }

    /// <summary>
    /// Builds the static hall (racks as merged meshes, conveyors, docks) and renders the dynamic state every frame:
    /// stacker cranes, AMRs, 6-axis robots, and every box as a GPU instance (one batch per colour category).
    /// </summary>
    [ExecuteAlways]
    public class WarehouseView : MonoBehaviour
    {
        public Material litMaterial, overlayMaterial;
        public bool showLanes = true, showPaths = true, showLabels = true, showZones = true;
        [NonSerialized] public Warehouse sim;
        [NonSerialized] public int selectedBox = -1, selectedAmr = -1, selectedStacker = -1, selectedStation = -1;
        [NonSerialized] public int highlightSku = -1;
        [NonSerialized] public readonly List<Label3D> labels = new List<Label3D>();

        public const int Cats = 7;   // 0 A, 1 B, 2 C, 3 empty, 4 reserved for an order, 5 order box, 6 highlighted SKU
        public static readonly Color[] CatColor =
        {
            new Color(0.93f, 0.50f, 0.15f), new Color(0.20f, 0.68f, 0.66f), new Color(0.62f, 0.50f, 0.78f),
            new Color(0.78f, 0.68f, 0.50f), new Color(0.98f, 0.85f, 0.20f), new Color(0.35f, 0.82f, 0.38f), new Color(1f, 0.25f, 0.55f)
        };

        Transform root;
        MatLib m;
        Mesh cubeMesh;
        Material[] boxMats;
        readonly List<Matrix4x4>[] inst = new List<Matrix4x4>[Cats];
        Matrix4x4[] chunk = new Matrix4x4[1023];
        Bounds worldBounds;
        Shapes.CraneVisual[] cranes;
        Shapes.AmrVisual[] amrViews;
        Shapes.RobotVisual[] robots;
        PickRobotPlan[] plans;
        Shapes.LoaderVisual[] loaders;
        Material lampOn, lampOff, lampBusy;
        Material[] amrLamp;
        LineBatch laneBatch, zoneBatch, pathBatch, selBatch;
        GameObject selBox;
        readonly float[] q = new float[6];
        readonly List<(int id, Vector3 p)> boxPts = new List<(int, Vector3)>();
        readonly List<Vector3> amrPos = new List<Vector3>();

        public bool IsBuilt => root != null && sim != null;
        public static Color LaneColor = new Color(0.35f, 0.72f, 1f, 0.5f);

        public void Clear()
        {
            if (root != null) DestroyImmediate(root.gameObject);
            root = null;
        }

        // ------------------------------------------------------------------ static build

        public void Build(Warehouse w)
        {
            Clear();
            sim = w;
            var L = w.L;
            m = new MatLib(litMaterial);
            var go = new GameObject("WarehouseRoot") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            root = go.transform;
            for (int i = 0; i < Cats; i++) inst[i] = new List<Matrix4x4>(4096);
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); cubeMesh = g.GetComponent<MeshFilter>().sharedMesh; DestroyImmediate(g);
            boxMats = new Material[Cats];
            for (int i = 0; i < Cats; i++) boxMats[i] = m.Get("Box" + i, CatColor[i], 0.25f);
            lampOn = m.Emissive("CraneLampOn", new Color(0.25f, 0.95f, 0.4f));
            lampOff = m.Emissive("CraneLampIdle", new Color(0.55f, 0.6f, 0.7f));
            lampBusy = m.Emissive("CraneLampBusy", new Color(1f, 0.7f, 0.15f));
            amrLamp = new[]
            {
                m.Emissive("AmrIdle", new Color(0.3f, 0.6f, 1f)), m.Emissive("AmrToPad", new Color(0.3f, 0.6f, 1f)), m.Emissive("AmrCharging", new Color(0.25f, 0.95f, 0.4f)),
                m.Emissive("AmrToStop", new Color(0.9f, 0.95f, 1f)), m.Emissive("AmrTransfer", new Color(1f, 0.7f, 0.15f)), m.Emissive("AmrLow", new Color(1f, 0.25f, 0.2f))
            };
            laneBatch = new LineBatch("Lanes", root, overlayMaterial);
            zoneBatch = new LineBatch("Zones", root, overlayMaterial);
            pathBatch = new LineBatch("Paths", root, overlayMaterial);
            selBatch = new LineBatch("Selection", root, overlayMaterial);
            BuildHall(L);
            BuildRacks(L);
            BuildFlow(L);
            BuildDynamic(L);
            DrawLanes(L);
            selBox = Prims.Box("SelBox", root, Vector3.zero, new Vector3(0.74f, 0.46f, 0.56f), m.Glass("SelGlass", new Color(1f, 1f, 0.3f, 0.35f)), null, false);
            selBox.SetActive(false);
            worldBounds = new Bounds(new Vector3(-4f, 4f, L.H * 0.5f), new Vector3(90f, 20f, L.H + 20f));
        }

        float X0 => -38f;
        float X1(WarehouseLayout L) => L.RackX1 + 4f;

        void BuildHall(WarehouseLayout L)
        {
            float x0 = X0, x1 = X1(L), z0 = -3f, z1 = L.H + 4f;
            var floor = m.Get("Floor", new Color(0.40f, 0.41f, 0.43f), 0.15f);
            Prims.Box("Floor", root, new Vector3((x0 + x1) * 0.5f, -0.06f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.12f, z1 - z0), floor, null, false);
            var glass = m.Glass("WallGlass", new Color(0.55f, 0.7f, 0.85f, 0.14f));
            float wh = 2.2f;
            Prims.Box("WallS", root, new Vector3((x0 + x1) * 0.5f, wh * 0.5f, z0), new Vector3(x1 - x0, wh, 0.1f), glass, null, false);
            Prims.Box("WallN", root, new Vector3((x0 + x1) * 0.5f, wh * 0.5f, z1), new Vector3(x1 - x0, wh, 0.1f), glass, null, false);
            Prims.Box("WallW", root, new Vector3(x0, wh * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.1f, wh, z1 - z0), glass, null, false);
            Prims.Box("WallE", root, new Vector3(x1, wh * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.1f, wh, z1 - z0), glass, null, false);
            var col = new BoxMerger();
            for (float x = x0; x <= x1 + 0.1f; x += 8f) { col.Add(new Vector3(x, 3f, z0), new Vector3(0.3f, 6f, 0.3f)); col.Add(new Vector3(x, 3f, z1), new Vector3(0.3f, 6f, 0.3f)); }
            for (float z = z0 + 8f; z < z1; z += 8f) { col.Add(new Vector3(x0, 3f, z), new Vector3(0.3f, 6f, 0.3f)); col.Add(new Vector3(x1, 3f, z), new Vector3(0.3f, 6f, 0.3f)); }
            col.Build("Columns", root, m.Get("Column", new Color(0.55f, 0.57f, 0.6f), 0.3f, 0.4f));
            var grid = new LineBatch("Grid", root, overlayMaterial);
            var gc = new Color(1f, 1f, 1f, 0.05f);
            for (float x = x0; x <= x1; x += 5f) grid.Line(new Vector3(x, 0.01f, z0), new Vector3(x, 0.01f, z1), 0.05f, gc);
            for (float z = z0; z <= z1; z += 5f) grid.Line(new Vector3(x0, 0.01f, z), new Vector3(x1, 0.01f, z), 0.05f, gc);
            grid.Apply();
        }

        void BuildRacks(WarehouseLayout L)
        {
            var posts = new BoxMerger(); var beams = new BoxMerger(); var rails = new BoxMerger();
            float top = L.RackTopY;
            for (int s = 0; s < L.cfg.stackers; s++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float zr = L.aisleZ[s] + side * WarehouseLayout.RowOff;
                    for (int b = 0; b <= L.cfg.bays; b++)
                    {
                        float x = WarehouseLayout.RackX0 + b * WarehouseLayout.BayPitch;
                        foreach (float dz in new[] { -0.21f, 0.21f }) posts.Add(new Vector3(x, top * 0.5f, zr + dz), new Vector3(0.06f, top, 0.06f));
                    }
                    for (int k = 0; k <= L.cfg.levels; k++)
                    {
                        float y = WarehouseLayout.Level0Y + k * WarehouseLayout.LevelPitch - WarehouseLayout.BoxH * 0.5f - 0.04f;
                        foreach (float dz in new[] { -0.21f, 0.21f }) beams.Add(new Vector3((WarehouseLayout.RackX0 + L.RackX1) * 0.5f, y, zr + dz), new Vector3(L.RackX1 - WarehouseLayout.RackX0, 0.05f, 0.05f));
                    }
                }
                float zc = L.aisleZ[s];
                foreach (float dz in new[] { -0.32f, 0.32f })
                    rails.Add(new Vector3((WarehouseLayout.RackX0 - 1.2f + L.RackX1) * 0.5f, 0.03f, zc + dz), new Vector3(L.RackX1 - WarehouseLayout.RackX0 + 1.2f, 0.06f, 0.06f));
                rails.Add(new Vector3((WarehouseLayout.RackX0 - 1.2f + L.RackX1) * 0.5f, top + 0.18f, zc), new Vector3(L.RackX1 - WarehouseLayout.RackX0 + 1.2f, 0.08f, 0.10f));
            }
            posts.Build("RackPosts", root, m.Get("RackPost", new Color(0.13f, 0.30f, 0.62f), 0.4f, 0.4f));
            beams.Build("RackBeams", root, m.Get("RackBeam", new Color(0.95f, 0.45f, 0.10f), 0.4f, 0.3f));
            rails.Build("CraneRails", root, m.Get("CraneRail", new Color(0.7f, 0.72f, 0.75f), 0.5f, 0.7f));
        }

        void BuildFlow(WarehouseLayout L)
        {
            var frame = new BoxMerger(); var rollers = new BoxMerger();
            var frameMat = m.Get("ConvFrame", new Color(0.30f, 0.34f, 0.38f), 0.4f, 0.5f);
            var rollMat = m.Get("ConvRoller", new Color(0.72f, 0.74f, 0.77f), 0.6f, 0.8f);
            var deckMat = m.Get("DeckPlate", new Color(0.24f, 0.26f, 0.29f), 0.3f, 0.3f);
            loaders = new Shapes.LoaderVisual[L.inboundZ.Length];
            for (int j = 0; j < L.inboundZ.Length; j++)
            {
                float z = L.inboundZ[j];
                Shapes.Conveyor(frame, rollers, new Vector3(WarehouseLayout.InboundSpawnX, 0f, z), new Vector3(WarehouseLayout.InboundHeadX + 0.5f, 0f, z));
                loaders[j] = Shapes.Loader(root, m, "Loader" + (j + 1), WarehouseLayout.InboundHeadX + 0.2f, z);
            }
            for (int k = 0; k < L.cfg.stations; k++)
            {
                float zc = L.stationZc[k];
                Shapes.Conveyor(frame, rollers, new Vector3(-14.8f, 0f, zc), new Vector3(-19.9f, 0f, zc));
                Shapes.Conveyor(frame, rollers, new Vector3(-14.9f, 0f, zc + 1.2f), new Vector3(-19.7f, 0f, zc + 1.2f));
                Shapes.Conveyor(frame, rollers, new Vector3(-18.5f, 0f, zc - 1.2f), new Vector3(-19.6f, 0f, zc - 1.2f));
                Shapes.Conveyor(frame, rollers, new Vector3(-19f, 0f, zc - 1.2f), new Vector3(-19f, 0f, zc - 3f));
                Shapes.Conveyor(frame, rollers, new Vector3(-19f, 0f, zc - 3f), new Vector3(-28.5f, 0f, zc - 3f));
            }
            frame.Build("ConvFrames", root, frameMat);
            rollers.Build("ConvRollers", root, rollMat, false);
            // AMR transfer decks: a small plate on every dock end so docks read clearly
            var plates = new BoxMerger();
            foreach (var d in L.docks)
            {
                if (d.kind == DockKind.Pad) continue;
                plates.Add(new Vector3(d.pos.x, 0.01f, d.pos.z), new Vector3(1.5f, 0.02f, 1.5f));
            }
            plates.Build("DockPads", root, deckMat, false);
            // chargers
            for (int i = 0; i < L.padDocks.Length; i++)
            {
                var d = L.padDocks[i];
                float side = d.yaw < 0f ? 1f : -1f;
                Prims.Box("Charger" + (i + 1), root, new Vector3(d.pos.x + side * 0.0f, 0.45f, d.pos.z + 0.75f), new Vector3(0.25f, 0.9f, 0.18f), m.Emissive("ChargerCol", new Color(0.2f, 0.8f, 0.4f)), null, false);
            }
        }

        void BuildDynamic(WarehouseLayout L)
        {
            int S = L.cfg.stackers;
            cranes = new Shapes.CraneVisual[S];
            for (int s = 0; s < S; s++) cranes[s] = Shapes.Crane(root, m, "Crane" + (s + 1), L.RackTopY + 0.1f, L.aisleZ[s]);
            amrViews = new Shapes.AmrVisual[sim.amrs.Count];
            for (int i = 0; i < amrViews.Length; i++) amrViews[i] = Shapes.Amr(root, m, sim.amrs[i].name);
            int K = L.cfg.stations;
            robots = new Shapes.RobotVisual[K]; plans = new PickRobotPlan[K];
            for (int k = 0; k < K; k++)
            {
                robots[k] = Shapes.Robot(root, m, "Robot" + (k + 1), L.StationRobotBase(k));
                plans[k] = new PickRobotPlan(L.StationRobotBase(k), L.StationSrcPos(k), L.StationOrdPos(k));
            }
        }

        void DrawLanes(WarehouseLayout L)
        {
            laneBatch.Clear(); zoneBatch.Clear();
            var g = L.graph;
            foreach (var e in g.edges)
            {
                var a = g.nodes[e.from].pos; var b = g.nodes[e.to].pos;
                a.y = b.y = 0.03f;
                var col = e.reverse ? new Color(0.35f, 0.72f, 1f, 0.22f) : LaneColor;
                laneBatch.Line(a, b, 0.32f, col);
                var d = (b - a); if (d.sqrMagnitude < 0.01f) continue;
                d.Normalize();
                var mid = (a + b) * 0.5f; var n = new Vector3(-d.z, 0f, d.x);
                if (!e.reverse)
                {
                    laneBatch.Line(mid - d * 0.35f + n * 0.3f, mid + d * 0.25f, 0.1f, col);
                    laneBatch.Line(mid - d * 0.35f - n * 0.3f, mid + d * 0.25f, 0.1f, col);
                }
            }
            foreach (var d in L.docks)
            {
                var c = d.kind == DockKind.Stacker ? new Color(1f, 0.8f, 0.25f, 0.8f) : d.kind == DockKind.Inbound ? new Color(0.4f, 0.9f, 1f, 0.8f)
                      : d.kind == DockKind.Station ? new Color(0.6f, 1f, 0.5f, 0.8f) : new Color(0.3f, 0.95f, 0.45f, 0.7f);
                zoneBatch.Rect(new Vector3(d.pos.x, 0.035f, d.pos.z), 0.75f, 0.75f, 0f, 0.07f, c);
            }
            laneBatch.Apply(); zoneBatch.Apply();
        }

        // ------------------------------------------------------------------ per frame

        int Cat(Box b)
        {
            if (b.orderBox) return 5;
            if (b.sku < 0) return 3;
            if (highlightSku >= 0 && b.sku == highlightSku) return 6;
            if (b.reserved) return 4;
            return sim.skus[b.sku].cls;
        }

        Vector3 BoxPosV(Box b, float tt, float frac)
        {
            if (b.loc == BoxLoc.OnAmr && (b.xp == null || tt >= b.xt0 + b.xdur) && b.owner >= 0 && b.owner < amrPos.Count)
            {
                var p = amrPos[b.owner]; return new Vector3(p.x, WarehouseLayout.DeckY, p.z);
            }
            return sim.BoxPos(b, tt);
        }

        public void Render(float frac)
        {
            if (!IsBuilt) return;
            var w = sim; var L = w.L;
            float tt = w.t + frac;
            labels.Clear(); boxPts.Clear(); amrPos.Clear();
            laneBatch.renderer.enabled = showLanes;
            zoneBatch.renderer.enabled = showZones;

            // ---- AMRs
            for (int i = 0; i < w.amrs.Count; i++)
            {
                var a = w.amrs[i]; var v = amrViews[i]; var mv = a.mv;
                var pos = mv.pos + mv.vel * Mathf.Min(frac, Warehouse.Dt);
                amrPos.Add(pos);
                v.root.position = pos;
                v.root.rotation = Quaternion.Euler(0f, mv.yaw, 0f);
                int li = a.lowBat ? 5 : (int)a.state;
                v.lamp.sharedMaterial = amrLamp[li];
                if (showLabels) labels.Add(new Label3D { world = pos + Vector3.up * 1.1f, text = $"{a.name} {a.battery:0}%", color = a.lowBat ? new Color(1f, 0.5f, 0.45f) : new Color(0.7f, 0.88f, 1f), kind = 2, id = i, selected = i == selectedAmr });
            }

            // ---- cranes
            for (int s = 0; s < cranes.Length; s++)
            {
                var c = w.stackers[s].crane; var v = cranes[s];
                v.mover.localPosition = new Vector3(c.x, 0f, 0f);
                v.carriage.localPosition = new Vector3(0f, c.y - WarehouseLayout.BoxH * 0.5f - 0.07f, 0f);
                v.table.localPosition = new Vector3(0f, 0f, c.forkSide * c.forkExt * WarehouseLayout.RowOff);
                v.lamp.sharedMaterial = c.action == "idle" ? lampOff : c.carrying != null ? lampBusy : lampOn;
                if (showLabels) labels.Add(new Label3D { world = new Vector3(WarehouseLayout.RackX0 - 2.6f, 3.4f, L.aisleZ[s]), text = $"S{s + 1}  {w.stackers[s].stored}", color = new Color(1f, 0.85f, 0.4f), kind = 0, id = s, selected = s == selectedStacker });
            }

            // ---- picking robots
            for (int k = 0; k < robots.Length; k++)
            {
                var sn = w.stations[k]; var rv = robots[k]; var plan = plans[k];
                bool picking = sn.phase == StationPhase.Picking;
                plan.Pose(picking ? sn.rt : 0f, sn.blend, q);
                Shapes.SetPose(rv, q);
                bool hold = picking && PickRobotPlan.Holding(sn.rt) && sn.curPick != null;
                rv.item.SetActive(hold);
                if (hold && sn.curPick.sku >= 0) rv.itemRenderer.sharedMaterial = boxMats[Mathf.Clamp(w.skus[sn.curPick.sku].cls, 0, 2)];
                rv.lamp.sharedMaterial = picking ? lampOn : sn.phase == StationPhase.Idle ? lampOff : lampBusy;
                if (showLabels) labels.Add(new Label3D { world = new Vector3(-20.8f, 3.1f, L.stationZc[k]), text = $"PK{k + 1}  {sn.phase}", color = new Color(0.6f, 1f, 0.6f), kind = 1, id = k, selected = k == selectedStation });
            }
            if (showLabels)
                for (int j = 0; j < L.inboundZ.Length; j++)
                    labels.Add(new Label3D { world = new Vector3(-27f, 2.0f, L.inboundZ[j]), text = $"IN{j + 1}  {w.inlines[j].q.Count}", color = new Color(0.55f, 0.9f, 1f), kind = 3, id = j });

            // ---- loader pushers (follow a box being pushed onto the AMR)
            var pushX = new float[loaders.Length];
            for (int j = 0; j < loaders.Length; j++) pushX[j] = -0.9f;
            foreach (var b in w.boxes)
            {
                if (b.xp == null || b.loc != BoxLoc.OnAmr || tt >= b.xt0 + b.xdur) continue;
                for (int j = 0; j < loaders.Length; j++)
                {
                    var end = b.xp[b.xp.Length - 1];
                    if (Mathf.Abs(end.z - L.inboundZ[j]) < 0.05f && Mathf.Abs(end.x - WarehouseLayout.StationDockX) < 0.05f)
                        pushX[j] = Mathf.Clamp(BoxPosV(b, tt, frac).x - (WarehouseLayout.InboundHeadX + 0.2f) - 0.38f, -0.9f, 1.4f);
                }
            }
            for (int j = 0; j < loaders.Length; j++)
            {
                var lp = loaders[j].pusher.localPosition;
                loaders[j].pusher.localPosition = new Vector3(Mathf.Lerp(lp.x, pushX[j], Application.isPlaying ? 0.4f : 1f), lp.y, lp.z);
            }

            // ---- boxes (GPU instancing, one batch per category)
            for (int i = 0; i < Cats; i++) inst[i].Clear();
            Vector3? selPos = null;
            var scale = new Vector3(WarehouseLayout.BoxL, WarehouseLayout.BoxH, WarehouseLayout.BoxW);
            foreach (var b in w.boxes)
            {
                if (b.loc == BoxLoc.Supplier) continue;
                var p = BoxPosV(b, tt, frac);
                inst[Cat(b)].Add(Matrix4x4.TRS(p, Quaternion.identity, scale * 0.96f));
                boxPts.Add((b.id, p));
                if (b.id == selectedBox) selPos = p;
            }
            for (int c = 0; c < Cats; c++) DrawInstances(c);

            if (selBox != null)
            {
                selBox.SetActive(selPos.HasValue);
                if (selPos.HasValue) selBox.transform.position = selPos.Value;
            }
            DrawOverlays(L);
        }

        void DrawInstances(int c)
        {
            var list = inst[c];
            if (list.Count == 0) return;
            var rp = new RenderParams(boxMats[c]) { worldBounds = worldBounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            for (int s = 0; s < list.Count; s += 1023)
            {
                int n = Mathf.Min(1023, list.Count - s);
                list.CopyTo(s, chunk, 0, n);
                Graphics.RenderMeshInstanced(rp, cubeMesh, 0, chunk, n);
            }
        }

        void DrawOverlays(WarehouseLayout L)
        {
            pathBatch.Clear(); selBatch.Clear();
            var g = L.graph;
            if (showPaths)
            {
                for (int i = 0; i < sim.amrs.Count; i++)
                {
                    var mv = sim.amrs[i].mv;
                    if (mv.pathNodes == null) continue;
                    var prev = new Vector3(mv.pos.x, 0.05f, mv.pos.z);
                    bool sel = i == selectedAmr;
                    var col = sel ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(1f, 0.75f, 0.25f, 0.45f);
                    for (int k = Mathf.Max(1, mv.seg + 1); k < mv.pathNodes.Count; k++)
                    {
                        var p = g.nodes[mv.pathNodes[k]].pos; p.y = 0.05f;
                        pathBatch.Line(prev, p, sel ? 0.28f : 0.16f, col);
                        prev = p;
                    }
                }
            }
            if (selectedAmr >= 0 && selectedAmr < amrPos.Count) selBatch.Ring(new Vector3(amrPos[selectedAmr].x, 0.06f, amrPos[selectedAmr].z), 1.0f, 0.12f, new Color(1f, 0.9f, 0.3f, 0.95f));
            if (selectedStacker >= 0 && selectedStacker < cranes.Length)
            {
                var c = sim.stackers[selectedStacker].crane; float zc = L.aisleZ[selectedStacker];
                selBatch.Rect(new Vector3((WarehouseLayout.RackX0 + L.RackX1) * 0.5f, 0.06f, zc), (L.RackX1 - WarehouseLayout.RackX0) * 0.5f + 0.5f, 1.6f, 0f, 0.12f, new Color(1f, 0.9f, 0.3f, 0.9f));
            }
            if (selectedStation >= 0 && selectedStation < robots.Length)
                selBatch.Rect(new Vector3(-17.5f, 0.06f, L.stationZc[selectedStation] - 0.2f), 3.8f, 2.4f, 0f, 0.12f, new Color(0.5f, 1f, 0.5f, 0.9f));
            pathBatch.Apply(); selBatch.Apply();
        }

        // ------------------------------------------------------------------ picking

        /// <summary>Nearest thing to a screen point: box (3), AMR (2), stacker (0), station (1) or inbound line (4 -> kind 3).</summary>
        public (int kind, int id) Pick(Camera cam, Vector2 screen)
        {
            float best = 14f * 14f; (int, int) res = (-1, -1);
            foreach (var (id, p) in boxPts)
            {
                var sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0) continue;
                float d = ((Vector2)sp - screen).sqrMagnitude;
                if (d < best) { best = d; res = (4, id); }
            }
            float lb = 44f * 44f;
            foreach (var l in labels)
            {
                var sp = cam.WorldToScreenPoint(l.world);
                if (sp.z <= 0) continue;
                float d = ((Vector2)sp - screen).sqrMagnitude;
                if (d < lb && (res.Item1 < 0 || l.kind == 2 || d < 30f * 30f)) { lb = d; res = (l.kind, l.id); }
            }
            return res;
        }
    }
}
