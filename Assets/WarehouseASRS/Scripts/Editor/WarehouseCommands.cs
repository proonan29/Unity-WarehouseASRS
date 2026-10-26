using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using WarehouseASRS.Core;
using WarehouseASRS.UI;
using WarehouseASRS.World;

namespace WarehouseASRS.EditorTools
{
    /// <summary>Scene setup and app control commands: `unity command wa_*`.</summary>
    public static class WarehouseCommands
    {
        const string ScenePath = "Assets/WarehouseASRS/Scenes/WarehouseASRS.unity";
        const string ResFolder = "Assets/WarehouseASRS/Resources";
        const string UiFolder = "Assets/WarehouseASRS/UI";
        const string PanelSettingsPath = UiFolder + "/WarehousePanelSettings.asset";
        const string ThemePath = UiFolder + "/WarehouseTheme.tss";
        const string TextSettingsPath = UiFolder + "/WarehouseTextSettings.asset";
        const string FontPath = UiFolder + "/Fonts/NotoSansKR-Regular.otf";
        const string FontAssetPath = UiFolder + "/Fonts/NotoSansKR-Regular SDF.asset";

        static Material EnsureMaterial(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) { m.shader = shader; EditorUtility.SetDirty(m); }
            return m;
        }

        [CliCommand("wa_setup_scene", "Create the WarehouseASRS scene: camera rig, lights, warehouse view, app controller and UI. Saves the scene.")]
        public static string SetupScene([CliArg("advance", "Seconds to simulate before saving")] float advance = 1800f,
                                        [CliArg("scene_path", "Scene asset path")] string scenePath = ScenePath)
        {
            if (Application.isPlaying) return "error: exit play mode first";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Directory.CreateDirectory(ResFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            var overlayShader = Shader.Find("WarehouseASRS/Overlay");
            if (overlayShader == null) return "error: WarehouseASRS/Overlay shader not found";
            var lit = EnsureMaterial(ResFolder + "/WA_Lit.mat", Shader.Find("Universal Render Pipeline/Lit"));
            var overlay = EnsureMaterial(ResFolder + "/WA_Overlay.mat", overlayShader);
            PlayerSettings.runInBackground = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.3f; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.6f;
            sunGo.transform.rotation = Quaternion.Euler(52f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.48f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.27f, 0.26f);
            RenderSettings.skybox = null;
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.4f; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(40f, 150f, 0f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.10f, 0.12f, 0.15f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; camData.stopNaN = true;
            var rig = camGo.AddComponent<CameraRig>();

            var viewGo = new GameObject("WarehouseView");
            var view = viewGo.AddComponent<WarehouseView>(); view.litMaterial = lit; view.overlayMaterial = overlay;

            var appGo = new GameObject("App");
            appGo.SetActive(false);
            var app = appGo.AddComponent<AppController>();
            app.view = view; app.rig = rig;
            app.config = new WarehouseConfig();

            var uiGo = new GameObject("UI");
            uiGo.SetActive(false);
            var docu = uiGo.AddComponent<UIDocument>();
            docu.panelSettings = EnsurePanelSettings();
            var ui = uiGo.AddComponent<WarehouseUI>(); ui.app = app;
            SetupFont(false);
            appGo.SetActive(true);
            uiGo.SetActive(true);
            app.EnsureBuilt();
            app.Overview();
            if (advance > 0) app.Advance(advance);

            EditorSceneManager.SaveScene(scene, scenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != scenePath && !s.path.EndsWith("SampleScene.unity")).ToList();
            list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            return $"scene={scenePath} build_ms={sw.ElapsedMilliseconds}\n{app.sim.Report()}";
        }

        static PanelSettings EnsurePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null) return ps;
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            return ps;
        }

        [CliCommand("wa_setup_font", "Create the Noto Sans KR dynamic font asset and register it as a UI fallback font.")]
        public static string SetupFont([CliArg("force", "Recreate the font asset")] bool force = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) return $"error: font not found: {FontPath}";
            var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fa != null && force) { AssetDatabase.DeleteAsset(FontAssetPath); fa = null; }
            if (fa == null)
            {
                fa = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (fa == null) return "error: CreateFontAsset failed";
                fa.name = "NotoSansKR-Regular SDF";
                fa.isMultiAtlasTexturesEnabled = true;
                AssetDatabase.CreateAsset(fa, FontAssetPath);
                if (fa.atlasTextures != null) foreach (var tex in fa.atlasTextures) if (tex != null) { tex.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); }
                if (fa.material != null) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
                EditorUtility.SetDirty(fa);
            }
            var ts = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (ts == null) { ts = ScriptableObject.CreateInstance<PanelTextSettings>(); AssetDatabase.CreateAsset(ts, TextSettingsPath); }
            if (ts.fallbackFontAssets == null) ts.fallbackFontAssets = new List<FontAsset>();
            if (!ts.fallbackFontAssets.Contains(fa)) ts.fallbackFontAssets.Add(fa);
            ts.clearDynamicDataOnBuild = true;
            EditorUtility.SetDirty(ts);
            var ps = EnsurePanelSettings();
            ps.textSettings = ts;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return $"font={FontAssetPath} hangul={fa.HasCharacter('한', true, true)}";
        }

        // ------------------------------------------------------------------ app control

        static AppController App(out string error)
        {
            error = null;
            var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
            if (app == null) { error = "error: no AppController in the active scene (run wa_setup_scene)"; return null; }
            app.EnsureBuilt();
            if (!app.Ready) { error = "error: app not ready"; return null; }
            return app;
        }

        static void MarkDirty(AppController app) { if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(app.gameObject.scene); }
        static void RefreshUI() { var ui = UnityEngine.Object.FindAnyObjectByType<WarehouseUI>(); if (ui != null) ui.Build(); }

        [CliCommand("wa_status", "Simulation state and KPIs of the running app (edit or play mode).")]
        public static string Status()
        {
            var app = App(out var err); if (app == null) return err;
            return $"play={Application.isPlaying} running={app.running} speed={app.speed}\n" + app.sim.Report();
        }

        [CliCommand("wa_load", "Rebuild the warehouse with a configuration and optionally advance it.")]
        public static string Load([CliArg("stackers", "Stacker cranes 1-6")] int stackers = 4,
                                  [CliArg("bays", "Bays per rack row")] int bays = 30,
                                  [CliArg("levels", "Shelf levels")] int levels = 16,
                                  [CliArg("amrs", "AMRs")] int amrs = 10,
                                  [CliArg("stations", "Picking stations 1-2")] int stations = 2,
                                  [CliArg("policy", "random|nearest|abc")] string policy = "abc",
                                  [CliArg("dual", "Dual command")] bool dual = true,
                                  [CliArg("in_per_hour", "Inbound boxes per hour")] float inPerHour = 100f,
                                  [CliArg("orders_per_hour", "Orders per hour")] float ordersPerHour = 60f,
                                  [CliArg("fill", "Initial fill 0-0.95")] float fill = 0.4f,
                                  [CliArg("seed", "Seed")] int seed = 1,
                                  [CliArg("advance", "Seconds to simulate right away")] float advance = 0f)
        {
            var app = App(out var err); if (app == null) return err;
            var c = SimCommands.Make(stackers, bays, levels, amrs, stations, policy, dual, inPerHour, ordersPerHour, fill, seed);
            AppController.Sanitize(c);
            try { app.Rebuild(c); } catch (Exception e) { return "error: " + e.Message; }
            if (advance > 0) app.Advance(advance);
            RefreshUI(); MarkDirty(app);
            return app.sim.Report();
        }

        [CliCommand("wa_advance", "Advance the simulation by N seconds.")]
        public static string Advance([CliArg("seconds", "Simulated seconds")] float seconds = 60f)
        {
            var app = App(out var err); if (app == null) return err;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            app.Advance(seconds);
            RefreshUI();
            return $"advanced {seconds}s in {sw.ElapsedMilliseconds} ms\n" + app.sim.Report();
        }

        [CliCommand("wa_set", "Set run state, display options, selection and language. Empty = unchanged.")]
        public static string Set([CliArg("running", "true/false")] string running = "",
                                 [CliArg("speed", "Simulation speed (x)")] float speed = -1,
                                 [CliArg("toggles", "Comma list, e.g. lanes=1,paths=1,zones=0,labels=1")] string toggles = "",
                                 [CliArg("select", "box:ID | amr:NAME | stacker:N | station:N | none")] string select = "",
                                 [CliArg("lang", "ko or en")] string lang = "")
        {
            var app = App(out var err); if (app == null) return err;
            if (running == "true") app.running = true; else if (running == "false") app.running = false;
            if (speed > 0) app.speed = speed;
            if (lang == "ko") Loc.Korean = true; else if (lang == "en") Loc.Korean = false;
            var v = app.view;
            foreach (var kv in toggles.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = kv.Split('='); bool on = p.Length < 2 || p[1] == "1" || p[1] == "true";
                switch (p[0].Trim())
                {
                    case "lanes": v.showLanes = on; break; case "paths": v.showPaths = on; break; case "zones": v.showZones = on; break;
                    case "labels": v.showLabels = on; break;
                }
            }
            if (select != "")
            {
                var p = select.Split(':');
                if (p[0] == "box") app.Select(4, int.Parse(p[1]));
                else if (p[0] == "amr") { int i = app.sim.amrs.FindIndex(x => x.name.Equals(p[1], StringComparison.OrdinalIgnoreCase)); app.Select(2, i); }
                else if (p[0] == "stacker") app.Select(0, int.Parse(p[1]) - 1);
                else if (p[0] == "station") app.Select(1, int.Parse(p[1]) - 1);
                else app.Select(-1, -1);
            }
            app.view.Render(0f);
            RefreshUI(); MarkDirty(app);
            return $"running={app.running} speed={app.speed} lanes={v.showLanes} paths={v.showPaths} sel=box{v.selectedBox}/amr{v.selectedAmr}/stk{v.selectedStacker}/stn{v.selectedStation}";
        }

        [CliCommand("wa_camera", "Place the camera: preset overview|top|racks|inbound|stationN|aisleN, or focus x,z + yaw/pitch/distance.")]
        public static string CameraCmd([CliArg("preset", "overview, top, racks, inbound, station1.., aisle1..")] string preset = "",
                                       [CliArg("focus", "x,z (empty = keep)")] string focus = "",
                                       [CliArg("yaw", "deg")] float yaw = float.NaN,
                                       [CliArg("pitch", "deg")] float pitch = float.NaN,
                                       [CliArg("distance", "m")] float distance = float.NaN)
        {
            var app = App(out var err); if (app == null) return err;
            var rig = app.rig;
            if (preset == "overview") app.Overview();
            else if (preset == "top") app.FocusTop();
            else if (preset == "racks") app.FocusRacks();
            else if (preset == "inbound") app.FocusInbound();
            else if (preset.StartsWith("station")) app.FocusStation(int.Parse(preset.Substring(7)) - 1);
            else if (preset.StartsWith("aisle")) app.FocusAisle(int.Parse(preset.Substring(5)) - 1);
            if (focus != "") { var p = focus.Split(','); rig.focus = new Vector3(float.Parse(p[0]), 0f, float.Parse(p[1])); rig.follow = null; }
            if (!float.IsNaN(yaw)) rig.yaw = yaw;
            if (!float.IsNaN(pitch)) rig.pitch = pitch;
            if (!float.IsNaN(distance)) rig.distance = distance;
            rig.Place();
            MarkDirty(app);
            return $"focus={rig.focus} yaw={rig.yaw} pitch={rig.pitch} distance={rig.distance}";
        }

        static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            return tex;
        }

        [CliCommand("wa_capture", "Render the main camera (3D only, no UI panels) to Screenshots/<name>.png.")]
        public static string Capture([CliArg("name", "File name")] string name = "shot", [CliArg("width", "Width")] int width = 1600, [CliArg("height", "Height")] int height = 1000)
        {
            var app = App(out var err); if (app == null) return err;
            var rig = app.rig; var cam = rig.Cam;
            float pl = rig.panelLeft, pr = rig.panelRight;
            rig.panelLeft = rig.panelRight = 0;
            rig.Place();
            app.view.Render(0f);
            var tex = Render(cam, width, height);
            rig.panelLeft = pl; rig.panelRight = pr;
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name.EndsWith(".png") ? name : name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rig.Place();
            return $"saved {path}";
        }
    }
}
