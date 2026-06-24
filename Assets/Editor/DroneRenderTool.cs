using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor batch tool: builds the hauler drone mesh directly and renders preview
/// PNGs via a Unity camera. Run headless with:
///   Unity.exe -batchmode -projectPath ... -executeMethod DroneRenderTool.Render -quit
/// Env vars:
///   DRONE_OUT   output PNG path (default project root)
///   DRONE_CARGO "1" to also build a cargo crate in the bay
/// </summary>
public static class DroneRenderTool
{
    const int Size = 640;

    public static void Render()
    {
        string outPath = Environment.GetEnvironmentVariable("DRONE_OUT");
        if (string.IsNullOrEmpty(outPath))
            outPath = Path.Combine(Directory.GetCurrentDirectory(), "drone_preview.png");
        bool withCargo = Environment.GetEnvironmentVariable("DRONE_CARGO") == "1";

        var root = new GameObject("HaulerPreview");
        BuildHauler(root.transform, withCargo);

        // Lighting
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
        var lightGO = new GameObject("KeyLight");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.color = new Color(1f, 0.97f, 0.92f);
        lightGO.transform.rotation = Quaternion.Euler(46f, 35f, 0f);

        var fillGO = new GameObject("FillLight");
        var fill2 = fillGO.AddComponent<Light>();
        fill2.type = LightType.Directional;
        fill2.intensity = 0.6f;
        fill2.color = new Color(0.7f, 0.8f, 1f);
        fillGO.transform.rotation = Quaternion.Euler(20f, -130f, 0f);

        Bounds b = ComputeBounds(root);
        float radius = Mathf.Max(b.extents.magnitude, 0.1f);

        var threeQuarter = RenderView(b, radius, new Vector3(1.1f, 0.7f, 1.4f), 28f);
        var side = RenderView(b, radius, new Vector3(1.0f, 0.18f, 0.04f), 26f);
        var top = RenderView(b, radius, new Vector3(0.0f, 1f, 0.0001f), 28f);

        var combined = Composite(threeQuarter, side, top);
        File.WriteAllBytes(outPath, combined.EncodeToPNG());
        Debug.Log($"[DroneRenderTool] wrote {outPath} cargo={withCargo} bounds={b.size}");
    }

    static void BuildHauler(Transform parent, bool withCargo)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        var matHull = new Material(sh) { color = new Color(0.31f, 0.29f, 0.25f) };
        matHull.SetFloat("_Smoothness", 0.3f);
        var matArm = new Material(sh) { color = new Color(0.12f, 0.12f, 0.14f) };
        matArm.SetFloat("_Smoothness", 0.18f);

        Color glowCol = Palette.DroneIdle;
        var matGlow = new Material(sh) { color = glowCol };
        matGlow.EnableKeyword("_EMISSION");
        matGlow.SetColor("_EmissionColor", glowCol * 1.8f);
        matGlow.SetFloat("_Smoothness", 0.9f);

        var result = HaulerDroneMesh.Build(parent, 0.55f, 0.28f, matHull, matArm, matGlow);

        if (withCargo)
        {
            var cargo = new GameObject("CargoItem");
            cargo.transform.SetParent(parent, false);
            cargo.transform.localPosition = result.cargoAnchor;
            cargo.transform.localScale = Vector3.one * result.cargoScale;
            var cBody = new Material(sh) { color = new Color(0.25f, 0.22f, 0.15f) };
            Color cGlow = new Color(1f, 0.6f, 0.05f);
            var cGlowMat = new Material(sh) { color = cGlow };
            cGlowMat.EnableKeyword("_EMISSION");
            cGlowMat.SetColor("_EmissionColor", cGlow * 4f);
            LootBarrelMesh.Build(cargo.transform, cBody, cBody, cGlowMat);
        }
    }

    static Bounds ComputeBounds(GameObject root)
    {
        var rends = root.GetComponentsInChildren<MeshRenderer>();
        if (rends.Length == 0) return new Bounds(root.transform.position, Vector3.one);
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        return b;
    }

    static Texture2D RenderView(Bounds b, float radius, Vector3 dir, float fov)
    {
        var camGO = new GameObject("PreviewCam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 100f;

        float dist = radius / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad) * 1.2f;
        Vector3 pos = b.center + dir.normalized * dist;
        camGO.transform.position = pos;
        camGO.transform.LookAt(b.center, Vector3.up);

        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;

        bool rendered = false;
        try
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req))
            {
                RenderPipeline.SubmitRenderRequest(cam, req);
                rendered = true;
            }
        }
        catch (Exception e) { Debug.LogWarning("SubmitRenderRequest failed: " + e.Message); }

        if (!rendered)
        {
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
        }

        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        rt.Release();
        UnityEngine.Object.DestroyImmediate(camGO);
        return tex;
    }

    static Texture2D Composite(params Texture2D[] views)
    {
        int gap = 8;
        int h = 0, w = 0;
        foreach (var v in views) { h = Mathf.Max(h, v.height); w += v.width; }
        w += gap * (views.Length - 1);

        var outTex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var fill = new Color(0.05f, 0.05f, 0.06f);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = fill;
        outTex.SetPixels(px);

        int x = 0;
        foreach (var v in views)
        {
            outTex.SetPixels(x, 0, v.width, v.height, v.GetPixels());
            x += v.width + gap;
        }
        outTex.Apply();
        return outTex;
    }
}
