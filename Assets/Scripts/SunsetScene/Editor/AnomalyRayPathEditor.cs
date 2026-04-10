// Setup:
// Lightweight custom inspector for anomaly rays so you can refresh generated segments, validate setup, and preview traversal without scene-level managers.
using UnityEditor;
using UnityEngine;

namespace TrulyAnYauMoment.SunsetScene.Editor
{
    [CustomEditor(typeof(AnomalyRayPath))]
    public sealed class AnomalyRayPathEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Validate Rays"))
            {
                serializedObject.ApplyModifiedProperties();
                AnomalyRayValidator.ValidateActiveScene(true);
            }

            if (GUILayout.Button("Refresh Path Cache"))
            {
                serializedObject.ApplyModifiedProperties();
                foreach (object targetObject in targets)
                {
                    if (targetObject is AnomalyRayPath path)
                    {
                        Undo.RecordObject(path, "Refresh Anomaly Ray Cache");
                        path.RefreshPathCache();
                        path.RebuildGeneratedSegments();
                        EditorUtility.SetDirty(path);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Preview Start"))
            {
                SetPreview(0f);
            }

            if (GUILayout.Button("Preview Mid"))
            {
                SetPreview(0.5f);
            }

            if (GUILayout.Button("Preview End"))
            {
                SetPreview(1f);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SetPreview(float progress)
        {
            serializedObject.ApplyModifiedProperties();
            foreach (object targetObject in targets)
            {
                if (targetObject is AnomalyRayPath path)
                {
                    Undo.RecordObject(path, "Preview Anomaly Ray");
                    path.SetPreviewProgress(progress);
                    path.RebuildGeneratedSegments();
                    EditorUtility.SetDirty(path);
                }
            }
        }
    }
}
