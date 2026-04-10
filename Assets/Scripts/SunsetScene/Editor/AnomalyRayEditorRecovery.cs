// Setup:
// Editor-only recovery hook for the anomaly ray scene.
// It clears saved LineRenderer/TrailRenderer selections on editor load so Unity's built-in shape gizmo tool does not reopen into a broken state.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TrulyAnYauMoment.SunsetScene.Editor
{
    [InitializeOnLoad]
    public static class AnomalyRayEditorRecovery
    {
        static AnomalyRayEditorRecovery()
        {
            EditorApplication.delayCall += RecoverEditorState;
            EditorSceneManager.sceneOpened += (_, _) => EditorApplication.delayCall += RecoverEditorState;
        }

        private static void RecoverEditorState()
        {
            Object activeObject = Selection.activeObject;
            if (activeObject == null)
            {
                Tools.current = Tool.Move;
                return;
            }

            GameObject selectedGameObject = null;
            if (activeObject is GameObject gameObject)
            {
                selectedGameObject = gameObject;
            }
            else if (activeObject is Component component)
            {
                selectedGameObject = component.gameObject;
            }

            if (selectedGameObject == null)
            {
                return;
            }

            if (selectedGameObject.GetComponent<LineRenderer>() == null &&
                selectedGameObject.GetComponent<TrailRenderer>() == null)
            {
                return;
            }

            Selection.activeObject = null;
            Tools.current = Tool.Move;
            SceneView.RepaintAll();
        }
    }
}
