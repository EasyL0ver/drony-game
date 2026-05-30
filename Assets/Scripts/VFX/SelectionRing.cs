using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Creates and manages a selection ring visual underneath a drone.
/// </summary>
public class SelectionRing : MonoBehaviour
{
    GameObject ringGO;
    Material ringMat;

    public void Init(float hoverY)
    {
        ringGO = new GameObject("SelectionRing");
        ringGO.transform.SetParent(transform, false);
        ringGO.transform.localPosition = new Vector3(0f, -hoverY + 0.05f, 0f);

        var mf = ringGO.AddComponent<MeshFilter>();
        var mr = ringGO.AddComponent<MeshRenderer>();

        mf.sharedMesh = MakeRingMesh(0.45f, 0.35f, 12);

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        ringMat = new Material(sh);
        Color col = Palette.WithAlpha(Palette.SelectionRing, 0.8f);
        ringMat.color = col;
        ringMat.SetColor("_BaseColor", col);
        ringMat.SetFloat("_Surface", 1f);
        ringMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ringMat.SetOverrideTag("RenderType", "Transparent");
        ringMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        ringMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ringMat.SetInt("_ZWrite", 0);
        ringMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mr.sharedMaterial = ringMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ringGO.SetActive(false);
    }

    public void SetVisible(bool visible)
    {
        if (ringGO != null)
            ringGO.SetActive(visible);
    }

    public void UpdatePulse()
    {
        if (ringGO == null || !ringGO.activeSelf || ringMat == null) return;
        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 4f);
        Color col = Palette.WithAlpha(Palette.SelectionRing, pulse);
        ringMat.color = col;
        ringMat.SetColor("_BaseColor", col);
    }

    static Mesh MakeRingMesh(float outerR, float innerR, int segments)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            verts.Add(new Vector3(Mathf.Cos(a) * outerR, 0f, Mathf.Sin(a) * outerR));
            verts.Add(new Vector3(Mathf.Cos(a) * innerR, 0f, Mathf.Sin(a) * innerR));
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int o = i * 2, n = i * 2 + 1;
            int no = next * 2, nn = next * 2 + 1;
            tris.Add(o);  tris.Add(no); tris.Add(n);
            tris.Add(n);  tris.Add(no); tris.Add(nn);
        }

        var m = new Mesh { name = "SelectionRing" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        return m;
    }
}
