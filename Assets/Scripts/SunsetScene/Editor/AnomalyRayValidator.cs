// Setup:
// Use this from the anomaly-ray inspector or the Tools menu to validate the active god-ray anomaly setup before look-dev.
// The validator only reports setup risks; it does not move, color, or compose rays for you.
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrulyAnYauMoment.SunsetScene.Editor
{
    public static class AnomalyRayValidator
    {
        [MenuItem("Tools/Sunset Scene/Validate Anomaly Rays")]
        public static void ValidateActiveSceneMenu()
        {
            ValidateActiveScene(true);
        }

        public static IReadOnlyList<AnomalyRayValidationMessage> ValidateActiveScene(bool logToConsole)
        {
            List<AnomalyRayValidationMessage> messages = new List<AnomalyRayValidationMessage>();
            Scene activeScene = SceneManager.GetActiveScene();

            AnomalyRayPath[] paths = Object.FindObjectsByType<AnomalyRayPath>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (paths.Length == 0)
            {
                messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Warning, "No AnomalyRayPath components were found in the active scene.", null));
            }

            Collect(messages, paths);

            if (logToConsole)
            {
                LogSummary(activeScene.name, messages);
            }

            return messages;
        }

        private static void Collect<T>(List<AnomalyRayValidationMessage> messages, T[] targets) where T : Object
        {
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] is IAnomalyRayValidatable validatable)
                {
                    validatable.CollectValidation(messages);
                }
            }
        }

        private static void LogSummary(string sceneName, IReadOnlyList<AnomalyRayValidationMessage> messages)
        {
            int infoCount = 0;
            int warningCount = 0;
            int errorCount = 0;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Anomaly Ray Validation Summary for '{sceneName}'");

            for (int index = 0; index < messages.Count; index++)
            {
                AnomalyRayValidationMessage message = messages[index];

                switch (message.Severity)
                {
                    case AnomalyRayValidationSeverity.Error:
                        errorCount++;
                        break;
                    case AnomalyRayValidationSeverity.Warning:
                        warningCount++;
                        break;
                    default:
                        infoCount++;
                        break;
                }

                builder.AppendLine($"[{message.Severity}] {message.Message}");
            }

            builder.AppendLine($"Totals: {errorCount} error(s), {warningCount} warning(s), {infoCount} info message(s).");

            if (errorCount > 0)
            {
                Debug.LogError(builder.ToString());
            }
            else if (warningCount > 0)
            {
                Debug.LogWarning(builder.ToString());
            }
            else
            {
                Debug.Log(builder.ToString());
            }
        }
    }
}
