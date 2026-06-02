using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[CustomEditor(typeof(GameManager))]
public class GameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gm = (GameManager)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Map Switcher", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Map 0\nSmall", GUILayout.Height(40)))
            SetMap(0);
        if (GUILayout.Button("Map 1\nMedium", GUILayout.Height(40)))
            SetMap(1);
        if (GUILayout.Button("Map 2\nSalvage Run", GUILayout.Height(40)))
            SetMap(2);
        EditorGUILayout.EndHorizontal();
    }

    void SetMap(int index)
    {
        if (Application.isPlaying)
        {
            ((GameManager)target).RestartWithMap(index);
            return;
        }

        var prop = serializedObject.FindProperty("testMapIndex");
        prop.intValue = index;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        EditorSceneManager.MarkSceneDirty(((GameManager)target).gameObject.scene);
        Debug.Log($"[GameManager] Map set to {index}. Press Play to start.");
    }
}
