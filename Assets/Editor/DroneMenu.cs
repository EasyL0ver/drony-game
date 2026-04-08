using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu items for spawning procedural objects.
/// </summary>
public static class ProceduralMenu
{
    [MenuItem("GameObject/3D Object/Sci-Fi Drone", false, 10)]
    static void CreateDrone()
    {
        GameObject go = new GameObject("SciFi_Drone");
        go.AddComponent<ProceduralDrone>();
        go.transform.position = new Vector3(0, 1.0f, 0);

        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create Sci-Fi Drone");
    }

    [MenuItem("Tools/Setup RTS Camera", false, 20)]
    static void SetupRTSCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("RTS Camera");
            cam = camGO.AddComponent<Camera>();
            camGO.tag = "MainCamera";
        }

        if (cam.GetComponent<RTSCamera>() == null)
            Undo.AddComponent<RTSCamera>(cam.gameObject);

        // Good RTS starting transform: looking down at the map origin
        cam.transform.position = new Vector3(0f, 35f, -20f);
        cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);

        Selection.activeGameObject = cam.gameObject;
        Debug.Log("RTS Camera ready — WASD pan, scroll zoom, MMB orbit, Q/E rotate");
    }

    [MenuItem("GameObject/3D Object/Low-Poly Drone", false, 11)]
    static void CreateLowPolyDrone()
    {
        GameObject go = new GameObject("LowPoly_Drone");
        go.AddComponent<LowPolyDrone>();
        go.transform.position = new Vector3(0, 1.0f, 0);

        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create Low-Poly Drone");
    }

    [MenuItem("GameObject/3D Object/Hex Map", false, 12)]
    static void CreateHexMap()
    {
        GameObject go = new GameObject("HexMap");
        go.AddComponent<HexMapGenerator>();
        go.transform.position = Vector3.zero;

        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create Hex Map");

        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
            // Switch to a top-down view for the RTS perspective
            SceneView.lastActiveSceneView.LookAt(
                Vector3.zero,
                Quaternion.Euler(90f, 0f, 0f),
                40f);
        }
    }
}
