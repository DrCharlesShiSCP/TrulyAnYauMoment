// Setup:
// Shared support types for the stylized anomaly-ray scripts.
// Runtime components report setup issues through these helpers so editor tools can keep validation concise.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrulyAnYauMoment.SunsetScene
{
    public enum AnomalyRayValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public struct AnomalyRayValidationMessage
    {
        public AnomalyRayValidationSeverity Severity;
        public string Message;
        public UnityEngine.Object Context;

        public AnomalyRayValidationMessage(AnomalyRayValidationSeverity severity, string message, UnityEngine.Object context)
        {
            Severity = severity;
            Message = message;
            Context = context;
        }
    }

    public interface IAnomalyRayValidatable
    {
        void CollectValidation(List<AnomalyRayValidationMessage> messages);
    }

    public static class AnomalyRayUtility
    {
        public static bool HasUsableCurve(AnimationCurve curve)
        {
            return curve != null && curve.length > 0;
        }

        public static bool HasUsableGradient(Gradient gradient)
        {
            return gradient != null && gradient.colorKeys != null && gradient.colorKeys.Length > 0;
        }

        public static Gradient CloneGradient(Gradient source)
        {
            if (source == null)
            {
                return null;
            }

            Gradient clone = new Gradient();
            clone.SetKeys(source.colorKeys, source.alphaKeys);
            clone.mode = source.mode;
            return clone;
        }

        public static Color MultiplyColors(Color a, Color b)
        {
            return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        }

        public static void AddNullEntries<T>(
            IReadOnlyList<T> values,
            string label,
            List<AnomalyRayValidationMessage> messages,
            UnityEngine.Object context,
            AnomalyRayValidationSeverity severity = AnomalyRayValidationSeverity.Warning)
            where T : UnityEngine.Object
        {
            if (values == null)
            {
                return;
            }

            for (int index = 0; index < values.Count; index++)
            {
                if (values[index] == null)
                {
                    messages.Add(new AnomalyRayValidationMessage(severity, $"{label} contains a null entry at index {index}.", context));
                }
            }
        }

        public static Vector2 NormalizeRange(float start, float end)
        {
            return start <= end ? new Vector2(start, end) : new Vector2(end, start);
        }
    }
}
