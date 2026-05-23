using UnityEditor;
using UnityEngine;
using Hintway;

namespace Hintway.Editor
{
    [CustomEditor(typeof(AnalyticsManager))]
    internal class AnalyticsManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Open Documentation"))
                Application.OpenURL("https://documentation.hintway.app");
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Menu items
        // ──────────────────────────────────────────────────────────────────────

        [MenuItem("Tools/Hintway/Add Analytics Manager to Scene", false, 10)]
        private static void AddToScene()
        {
#if UNITY_2022_2_OR_NEWER
            var existing = Object.FindFirstObjectByType<AnalyticsManager>();
#else
            var existing = Object.FindObjectOfType<AnalyticsManager>();
#endif
            if (existing != null)
            {
                Debug.LogWarning("[Hintway] An AnalyticsManager already exists in the scene.");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            var go = new GameObject("AnalyticsManager");
            go.AddComponent<AnalyticsManager>();
            Undo.RegisterCreatedObjectUndo(go, "Add Analytics Manager");
            Selection.activeGameObject = go;
            Debug.Log("[Hintway] AnalyticsManager added to the scene.");
        }

        [MenuItem("Tools/Hintway/Open Documentation", false, 30)]
        private static void OpenDocumentation()
        {
            Application.OpenURL("https://documentation.hintway.app");
        }
    }
}
