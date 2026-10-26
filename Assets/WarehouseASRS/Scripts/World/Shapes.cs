using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WarehouseASRS.Core;

namespace WarehouseASRS.World
{
    /// <summary>Many boxes merged into one mesh (rack frames, conveyor rollers, floor details): one draw call.</summary>
    public class BoxMerger
    {
        static Mesh cube;
        static Mesh Cube
        {
            get
            {
                if (cube == null) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); }
                return cube;
            }
        }

        readonly List<CombineInstance> ci = new List<CombineInstance>();
        public int Count => ci.Count;
        public void Add(Vector3 center, Vector3 size) => ci.Add(new CombineInstance { mesh = Cube, transform = Matrix4x4.TRS(center, Quaternion.identity, size) });
        public void AddRot(Vector3 center, Vector3 size, Quaternion rot) => ci.Add(new CombineInstance { mesh = Cube, transform = Matrix4x4.TRS(center, rot, size) });

        public GameObject Build(string name, Transform parent, Material m, bool shadows = true)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            mesh.CombineMeshes(ci.ToArray(), true, true);
            mesh.RecalculateBounds();
            var g = new GameObject(name) { hideFlags = HideFlags.DontSave };
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }
    }

    /// <summary>Procedural low-poly models: stacker crane, AMR, conveyors, loader, 6-axis picking robot with suction tool.</summary>
    public static class Shapes
    {
        // ------------------------------------------------------------------ stacker crane

        public class CraneVisual { public Transform root, mover, carriage, table; public Renderer lamp; }

        public static CraneVisual Crane(Transform parent, MatLib m, string name, float rackTop, float zc)
        {
            var v = new CraneVisual();
            var blue = m.Get("CraneBlue", new Color(0.12f, 0.38f, 0.78f), 0.5f, 0.3f);
            var dark = m.Get("CraneDark", new Color(0.16f, 0.17f, 0.19f), 0.4f, 0.5f);
            var steel = m.Get("CraneSteel", new Color(0.62f, 0.64f, 0.67f), 0.5f, 0.7f);
            v.root = Prims.Node(name, parent, new Vector3(0f, 0f, zc));
            v.mover = Prims.Node("Mover", v.root, Vector3.zero);
            // bottom truck + top guide + two masts
            Prims.Box("Truck", v.mover, new Vector3(0f, 0.12f, 0f), new Vector3(1.25f, 0.24f, 0.56f), dark);
            Prims.Box("Mast1", v.mover, new Vector3(-0.52f, rackTop * 0.5f, 0f), new Vector3(0.12f, rackTop, 0.16f), blue);
            Prims.Box("Mast2", v.mover, new Vector3(0.52f, rackTop * 0.5f, 0f), new Vector3(0.12f, rackTop, 0.16f), blue);
            Prims.Box("TopBeam", v.mover, new Vector3(0f, rackTop, 0f), new Vector3(1.25f, 0.12f, 0.30f), blue);
            Prims.Box("Cabinet", v.mover, new Vector3(0f, 0.55f, 0.18f), new Vector3(0.5f, 0.6f, 0.14f), steel);
            v.lamp = Prims.Box("Lamp", v.mover, new Vector3(0f, rackTop + 0.12f, 0f), new Vector3(0.18f, 0.12f, 0.18f), m.Emissive("CraneLampIdle", new Color(0.55f, 0.6f, 0.7f)), null, false).GetComponent<Renderer>();
            v.carriage = Prims.Node("Carriage", v.mover, Vector3.zero);
            Prims.Box("Frame", v.carriage, new Vector3(0f, 0f, 0f), new Vector3(0.96f, 0.10f, 0.20f), blue);
            v.table = Prims.Node("Table", v.carriage, Vector3.zero);
            Prims.Box("Plate", v.table, new Vector3(0f, 0.02f, 0f), new Vector3(0.62f, 0.035f, 0.40f), steel);
            Prims.Box("Fork1", v.table, new Vector3(-0.14f, 0.045f, 0f), new Vector3(0.05f, 0.02f, 0.44f), dark);
            Prims.Box("Fork2", v.table, new Vector3(0.14f, 0.045f, 0f), new Vector3(0.05f, 0.02f, 0.44f), dark);
            return v;
        }

        // ------------------------------------------------------------------ AMR

        public class AmrVisual { public Transform root, lift; public Renderer lamp; }

        public static AmrVisual Amr(Transform parent, MatLib m, string name)
        {
            var v = new AmrVisual();
            v.root = Prims.Node(name, parent, Vector3.zero);
            var body = m.Get("AmrBody", new Color(0.22f, 0.25f, 0.30f), 0.5f, 0.3f);
            var stripe = m.Get("AmrStripe", new Color(0.98f, 0.75f, 0.10f), 0.4f);
            Prims.Box("Body", v.root, new Vector3(0f, 0.16f, 0f), new Vector3(0.84f, 0.28f, 1.12f), body);
            Prims.Box("Stripe", v.root, new Vector3(0f, 0.20f, 0f), new Vector3(0.86f, 0.04f, 1.14f), stripe);
            Prims.Box("Bumper", v.root, new Vector3(0f, 0.10f, 0.57f), new Vector3(0.80f, 0.10f, 0.05f), m.Get("Bumper", new Color(0.08f, 0.08f, 0.08f), 0.2f));
            Prims.Cyl("Lidar", v.root, new Vector3(0f, 0.33f, 0.46f), 0.05f, 0.05f, m.Get("Lidar", new Color(0.05f, 0.05f, 0.05f), 0.6f));
            v.lift = Prims.Node("Lift", v.root, new Vector3(0f, 0.30f, 0f));
            Prims.Box("Roller", v.lift, new Vector3(0f, 0.02f, 0f), new Vector3(0.60f, 0.05f, 0.80f), m.Get("AmrDeck", new Color(0.55f, 0.57f, 0.60f), 0.5f, 0.6f));
            v.lamp = Prims.Box("Lamp", v.root, new Vector3(0f, 0.25f, -0.58f), new Vector3(0.5f, 0.04f, 0.04f), m.Emissive("AmrLampIdle", new Color(0.3f, 0.6f, 1f)), null, false).GetComponent<Renderer>();
            return v;
        }

        // ------------------------------------------------------------------ conveyors / loader

        /// <summary>Roller conveyor bed between two points at box-bottom height (a, b are x or z ends on the line, y is ignored).</summary>
        public static void Conveyor(BoxMerger frame, BoxMerger rollers, Vector3 a, Vector3 b, float width = 0.56f)
        {
            var d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.1f) return;
            var dir = d / len; var side = new Vector3(-dir.z, 0f, dir.x);
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            float yTop = WarehouseLayout.ConvY - WarehouseLayout.BoxH * 0.5f;   // box bottom
            var mid = (a + b) * 0.5f; mid.y = yTop - 0.06f;
            frame.AddRot(mid, new Vector3(width, 0.08f, len), rot);
            frame.AddRot(mid + side * (width * 0.5f + 0.015f) + Vector3.up * 0.07f, new Vector3(0.03f, 0.10f, len), rot);
            frame.AddRot(mid - side * (width * 0.5f + 0.015f) + Vector3.up * 0.07f, new Vector3(0.03f, 0.10f, len), rot);
            for (float s = 0.15f; s < len; s += 3.0f)
            {
                var p = a + dir * s; p.y = (yTop - 0.1f) * 0.5f;
                frame.Add(p + side * (width * 0.5f - 0.03f), new Vector3(0.05f, yTop - 0.1f, 0.05f));
                frame.Add(p - side * (width * 0.5f - 0.03f), new Vector3(0.05f, yTop - 0.1f, 0.05f));
            }
            for (float s = 0.1f; s < len; s += 0.22f)
            {
                var p = a + dir * s; p.y = yTop - 0.015f;
                rollers.AddRot(p, new Vector3(width - 0.04f, 0.035f, 0.05f), rot);
            }
        }

        public class LoaderVisual { public Transform pusher; }

        /// <summary>Fork loader at an inbound dock: portal frame over the infeed with a pusher bar that slides the box onto the AMR.</summary>
        public static LoaderVisual Loader(Transform parent, MatLib m, string name, float x, float z)
        {
            var v = new LoaderVisual();
            var yel = m.Get("LoaderYellow", new Color(0.95f, 0.72f, 0.12f), 0.4f, 0.2f);
            var dark = m.Get("CraneDark", new Color(0.16f, 0.17f, 0.19f), 0.4f, 0.5f);
            var root = Prims.Node(name, parent, new Vector3(x, 0f, z));
            float yb = WarehouseLayout.ConvY;
            Prims.Box("PostN", root, new Vector3(0f, 0.9f, 0.62f), new Vector3(0.14f, 1.8f, 0.14f), yel);
            Prims.Box("PostS", root, new Vector3(0f, 0.9f, -0.62f), new Vector3(0.14f, 1.8f, 0.14f), yel);
            Prims.Box("Beam", root, new Vector3(0f, 1.8f, 0f), new Vector3(0.16f, 0.14f, 1.4f), yel);
            Prims.Box("Rail", root, new Vector3(-0.6f, 1.55f, 0f), new Vector3(2.2f, 0.06f, 0.06f), dark);
            Prims.Box("Rail2", root, new Vector3(-0.6f, 0.30f, 0.43f), new Vector3(2.2f, 0.05f, 0.05f), dark);
            v.pusher = Prims.Node("Pusher", root, new Vector3(-0.9f, yb, 0f));
            Prims.Box("Bar", v.pusher, Vector3.zero, new Vector3(0.05f, 0.24f, 0.44f), yel);
            Prims.Box("Arm", v.pusher, new Vector3(-0.5f, 0.3f, 0f), new Vector3(1.0f, 0.05f, 0.05f), dark);
            return v;
        }

        // ------------------------------------------------------------------ 6-axis picking robot (suction)

        public class RobotVisual { public Transform root, flange; public Transform[] joints; public Renderer lamp; public GameObject item; public Renderer itemRenderer; }

        public static RobotVisual Robot(Transform parent, MatLib m, string name, Vector3 basePos)
        {
            var g = RobotGeom.Default;
            var orange = m.Get("RobotOrange", new Color(1.0f, 0.50f, 0.06f), 0.55f, 0.1f);
            var gray = m.Get("RobotGray", new Color(0.22f, 0.23f, 0.25f), 0.4f, 0.5f);
            var rv = new RobotVisual();
            rv.root = Prims.Node(name, parent, basePos, Quaternion.Euler(0f, 90f, 0f));
            Prims.Box("BasePlate", rv.root, new Vector3(0f, 0.03f, 0f), new Vector3(1.0f, 0.06f, 1.0f), gray);
            var baseT = Prims.Node("Base", rv.root, new Vector3(0f, 0f, 0f));
            Prims.Cyl("Plinth", baseT, new Vector3(0f, 0.08f, 0f), 0.36f, 0.16f, gray);
            var j1 = Prims.Node("J1", baseT, Vector3.zero);
            Prims.Cyl("Turret", j1, new Vector3(0f, 0.40f, 0.05f), 0.30f, 0.42f, orange);
            Prims.Box("Shoulder", j1, new Vector3(0f, g.d1 - 0.05f, g.a1 * 0.6f), new Vector3(0.50f, 0.38f, 0.58f), orange);
            var j2 = Prims.Node("J2", j1, new Vector3(0f, g.d1, g.a1));
            Prims.CylX("Motor2", j2, new Vector3(0.32f, 0f, 0f), 0.17f, 0.20f, gray);
            Prims.Box("UpperArm", j2, new Vector3(0.10f, 0f, g.L2 * 0.5f), new Vector3(0.24f, 0.34f, g.L2 + 0.2f), orange);
            var j3 = Prims.Node("J3", j2, new Vector3(0f, 0f, g.L2));
            Prims.CylX("Elbow", j3, Vector3.zero, 0.19f, 0.44f, orange);
            Prims.Box("Motor3", j3, new Vector3(-0.06f, 0.05f, -0.25f), new Vector3(0.20f, 0.20f, 0.28f), gray);
            Prims.CylZ("Forearm", j3, new Vector3(0f, 0f, g.wristSplit * 0.5f), 0.15f, g.wristSplit, orange);
            var j4 = Prims.Node("J4", j3, new Vector3(0f, 0f, g.wristSplit));
            Prims.CylZ("Roll", j4, new Vector3(0f, 0f, (g.L3 - g.wristSplit) * 0.5f - 0.05f), 0.11f, g.L3 - g.wristSplit - 0.1f, orange);
            var j5 = Prims.Node("J5", j4, new Vector3(0f, 0f, g.L3 - g.wristSplit));
            Prims.CylX("Wrist", j5, Vector3.zero, 0.10f, 0.24f, gray);
            var j6 = Prims.Node("J6", j5, Vector3.zero);
            Prims.CylZ("Flange", j6, new Vector3(0f, 0f, g.d6 * 0.5f), 0.08f, g.d6, orange);
            rv.flange = Prims.Node("ToolMount", j6, new Vector3(0f, 0f, g.d6));
            // suction tool: stem + bellows cup (TCP is Tool metres beyond the flange)
            var rubber = m.Get("SuctionCup", new Color(0.08f, 0.08f, 0.09f), 0.2f);
            Prims.CylZ("Stem", rv.flange, new Vector3(0f, 0f, PickRobotPlan.Tool * 0.5f - 0.02f), 0.018f, PickRobotPlan.Tool - 0.04f, gray);
            Prims.CylZ("Cup", rv.flange, new Vector3(0f, 0f, PickRobotPlan.Tool - 0.03f), 0.065f, 0.05f, rubber);
            rv.item = Prims.Box("HeldItem", rv.flange, new Vector3(0f, 0f, PickRobotPlan.Tool + 0.07f), new Vector3(0.14f, 0.14f, 0.12f), m.Get("ItemPlain", new Color(0.9f, 0.9f, 0.9f), 0.3f));
            rv.itemRenderer = rv.item.GetComponent<Renderer>();
            rv.item.SetActive(false);
            rv.joints = new[] { j1, j2, j3, j4, j5, j6 };
            rv.lamp = Prims.Cyl("Lamp", j1, new Vector3(0f, g.d1 + 0.25f, -0.15f), 0.05f, 0.10f, m.Emissive("LampIdle", new Color(0.5f, 0.55f, 0.6f))).GetComponent<Renderer>();
            return rv;
        }

        public static void SetPose(RobotVisual rv, float[] q)
        {
            rv.joints[0].localRotation = Quaternion.AngleAxis(q[0], Vector3.up);
            rv.joints[1].localRotation = Quaternion.AngleAxis(q[1], Vector3.right);
            rv.joints[2].localRotation = Quaternion.AngleAxis(q[2], Vector3.right);
            rv.joints[3].localRotation = Quaternion.AngleAxis(q[3], Vector3.forward);
            rv.joints[4].localRotation = Quaternion.AngleAxis(q[4], Vector3.right);
            rv.joints[5].localRotation = Quaternion.AngleAxis(q[5], Vector3.forward);
        }
    }
}
