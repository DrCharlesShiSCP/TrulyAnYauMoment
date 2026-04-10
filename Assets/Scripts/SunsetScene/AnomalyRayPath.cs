// Setup:
// Add this to a stylized god-ray root, assign waypoint transforms in order, then choose either generated LineRenderer segments or manually authored segment objects.
// You control gaps, darkening, reroutes, and convergence in the Inspector; the script only evaluates the path and drives the linked visuals.
using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TrulyAnYauMoment.SunsetScene
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AnomalyRayPath : MonoBehaviour, IAnomalyRayValidatable
    {
        public enum InterpolationMode
        {
            Linear,
            Curved
        }

        public enum PathMode
        {
            Static,
            AnimatedFlow,
            PingPong,
            Loop
        }

        public enum VisualMode
        {
            GeneratedLineRenderers,
            SegmentObjects
        }

        [Serializable]
        public struct VisibleSection
        {
            public bool Enabled;
            [Range(0f, 1f)] public float Start;
            [Range(0f, 1f)] public float End;
            [Min(0f)] public float IntensityMultiplier;
            [Min(0.001f)] public float WidthMultiplier;
        }

        [Serializable]
        public struct DarkeningZone
        {
            public bool Enabled;
            [Range(0f, 1f)] public float Start;
            [Range(0f, 1f)] public float End;
            [Min(0f)] public float IntensityMultiplier;
            [Min(0.001f)] public float WidthMultiplier;
        }

        private struct EvaluatedSpan
        {
            public float Start;
            public float End;
            public float Intensity;
            public float Width;
        }

        [Header("Path")]
        [SerializeField] private Transform[] waypoints = Array.Empty<Transform>();
        [SerializeField] private InterpolationMode interpolationMode = InterpolationMode.Curved;
        [SerializeField, Min(2)] private int samplesPerSegment = 10;

        [Header("Traversal")]
        [SerializeField] private PathMode pathMode = PathMode.Static;
        [SerializeField, Min(0f)] private float travelSpeed = 0.15f;
        [SerializeField, Range(0.02f, 1f)] private float flowWindowSize = 1f;
        [SerializeField, Min(0f)] private float startDelay = 0f;
        [SerializeField, Range(0f, 1f)] private float previewProgress = 0f;
        [SerializeField] private bool previewInEditMode = true;

        [Header("Visible Sections")]
        [SerializeField] private VisibleSection[] visibleSections = Array.Empty<VisibleSection>();
        [SerializeField] private DarkeningZone[] darkeningZones = Array.Empty<DarkeningZone>();

        [Header("Convergence")]
        [SerializeField] private bool useConvergenceTarget = false;
        [SerializeField] private Transform convergenceTarget;
        [SerializeField, Range(0f, 1f)] private float convergenceStart = 0.7f;
        [SerializeField] private AnimationCurve convergenceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Range(0.05f, 1f)] private float widthAtConvergence = 0.35f;

        [Header("Visual Style")]
        [SerializeField] private VisualMode visualMode = VisualMode.GeneratedLineRenderers;
        [SerializeField, Min(0f)] private float intensityMultiplier = 1f;
        [SerializeField, Min(0.001f)] private float widthMultiplier = 1f;
        [SerializeField] private bool useColorOverride = false;
        [SerializeField] private Color colorOverride = Color.white;
        [SerializeField] private Gradient colorGradient;
        [SerializeField] private string emissionColorProperty = "_EmissionColor";

        [Header("Line Renderer Visuals")]
        [SerializeField] private LineRenderer lineRendererTemplate;
        [SerializeField] private Transform generatedLineRoot;

        [Header("Segment Object Visuals")]
        [SerializeField] private Transform[] segmentObjects = Array.Empty<Transform>();
        [SerializeField] private bool orientSegmentObjectsToPath = true;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color pathGizmoColor = new Color(1f, 0.58f, 0.14f, 0.9f);
        [SerializeField] private Color gapGizmoColor = new Color(0.95f, 0.2f, 0.2f, 0.9f);
        [SerializeField] private Color darkZoneGizmoColor = new Color(0.25f, 0.75f, 1f, 0.9f);
        [SerializeField] private Color convergenceGizmoColor = new Color(1f, 0.92f, 0.35f, 0.95f);

        private const string GeneratedRendererPrefix = "AnomalyRaySegment_";
        private const float MinimumSpanLength = 0.001f;

        private readonly List<Vector3> cachedPoints = new List<Vector3>();
        private readonly List<float> cachedDistances = new List<float>();
        private readonly List<EvaluatedSpan> evaluatedSpans = new List<EvaluatedSpan>();

        private float cachedPathLength;
        private bool cacheReady;
        private Vector3[] segmentObjectBaseScales = Array.Empty<Vector3>();
        private Renderer[][] segmentObjectRenderers = Array.Empty<Renderer[]>();
        private Color[][] segmentObjectBaseEmissionColors = Array.Empty<Color[]>();

        private void Reset()
        {
            colorGradient = BuildDefaultGradient();
            convergenceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            previewInEditMode = true;
            widthMultiplier = 1f;
            intensityMultiplier = 1f;
        }

        private void OnEnable()
        {
            CacheSegmentObjectState();
            RefreshPathCache();
            ApplyVisuals();
        }

        private void OnValidate()
        {
            samplesPerSegment = Mathf.Max(2, samplesPerSegment);
            travelSpeed = Mathf.Max(0f, travelSpeed);
            flowWindowSize = Mathf.Clamp(flowWindowSize, 0.02f, 1f);
            widthMultiplier = Mathf.Max(0.001f, widthMultiplier);
            intensityMultiplier = Mathf.Max(0f, intensityMultiplier);
            widthAtConvergence = Mathf.Clamp(widthAtConvergence, 0.05f, 1f);

            if (!AnomalyRayUtility.HasUsableGradient(colorGradient))
            {
                colorGradient = BuildDefaultGradient();
            }

            if (!AnomalyRayUtility.HasUsableCurve(convergenceCurve))
            {
                convergenceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }

            CacheSegmentObjectState();
            RefreshPathCache();
            ApplyVisuals();
        }

        private void Update()
        {
            if (!Application.isPlaying && !previewInEditMode)
            {
                DisableUnusedVisuals();
                return;
            }

            ApplyVisuals();
        }

        public void RefreshPathCache()
        {
            cachedPoints.Clear();
            cachedDistances.Clear();
            cachedPathLength = 0f;
            cacheReady = false;

            List<Transform> validWaypoints = GetValidWaypoints();
            if (validWaypoints.Count < 2)
            {
                return;
            }

            Vector3 previousPoint = validWaypoints[0].position;
            cachedPoints.Add(previousPoint);
            cachedDistances.Add(0f);

            for (int segmentIndex = 0; segmentIndex < validWaypoints.Count - 1; segmentIndex++)
            {
                Vector3 p0 = validWaypoints[Mathf.Max(segmentIndex - 1, 0)].position;
                Vector3 p1 = validWaypoints[segmentIndex].position;
                Vector3 p2 = validWaypoints[segmentIndex + 1].position;
                Vector3 p3 = validWaypoints[Mathf.Min(segmentIndex + 2, validWaypoints.Count - 1)].position;

                for (int sampleIndex = 1; sampleIndex <= samplesPerSegment; sampleIndex++)
                {
                    float t = sampleIndex / (float)samplesPerSegment;
                    Vector3 currentPoint = interpolationMode == InterpolationMode.Curved
                        ? CatmullRom(p0, p1, p2, p3, t)
                        : Vector3.Lerp(p1, p2, t);

                    if ((currentPoint - previousPoint).sqrMagnitude < 0.000001f)
                    {
                        continue;
                    }

                    cachedPathLength += Vector3.Distance(previousPoint, currentPoint);
                    cachedPoints.Add(currentPoint);
                    cachedDistances.Add(cachedPathLength);
                    previousPoint = currentPoint;
                }
            }

            cacheReady = cachedPoints.Count >= 2 && cachedPathLength > MinimumSpanLength;
        }

        public void RebuildGeneratedSegments()
        {
            if (visualMode != VisualMode.GeneratedLineRenderers || lineRendererTemplate == null)
            {
                return;
            }

            RefreshPathCache();
            ApplyVisuals();
        }

        public void SetPreviewProgress(float normalizedProgress)
        {
            previewProgress = Mathf.Clamp01(normalizedProgress);
            ApplyVisuals();
        }

        public void CollectValidation(List<AnomalyRayValidationMessage> messages)
        {
            if (waypoints == null || waypoints.Length < 2)
            {
                messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Error, "At least two waypoint transforms are required.", this));
                return;
            }

            AnomalyRayUtility.AddNullEntries(waypoints, "Waypoints", messages, this, AnomalyRayValidationSeverity.Error);

            if (!cacheReady)
            {
                RefreshPathCache();
            }

            if (!cacheReady)
            {
                messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Error, "The path length is zero or too short to evaluate.", this));
            }

            if (visualMode == VisualMode.GeneratedLineRenderers && lineRendererTemplate == null)
            {
                messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Error, "Generated Line Renderers mode requires a LineRenderer template.", this));
            }
            else if (visualMode == VisualMode.GeneratedLineRenderers && lineRendererTemplate != null)
            {
                BuildEvaluatedSpans();
                int availableRenderers = GetManagedLineRenderers().Count;
                if (availableRenderers < evaluatedSpans.Count)
                {
                    messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Warning, $"Only {availableRenderers} LineRenderer segment(s) are precreated for {evaluatedSpans.Count} visible span(s). Extra spans will stay hidden until more line children are added.", this));
                }
            }

            if (visualMode == VisualMode.SegmentObjects)
            {
                if (segmentObjects == null || segmentObjects.Length == 0)
                {
                    messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Error, "Segment Objects mode requires at least one segment object transform.", this));
                }

                AnomalyRayUtility.AddNullEntries(segmentObjects, "Segment objects", messages, this, AnomalyRayValidationSeverity.Error);

                for (int index = 0; index < segmentObjects.Length; index++)
                {
                    if (segmentObjects[index] == null)
                    {
                        continue;
                    }

                    for (int compareIndex = index + 1; compareIndex < segmentObjects.Length; compareIndex++)
                    {
                        if (segmentObjects[index] == segmentObjects[compareIndex])
                        {
                            messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Warning, $"Segment objects {index} and {compareIndex} reference the same transform.", this));
                        }
                    }
                }
            }

            if (useConvergenceTarget && convergenceTarget == null)
            {
                messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Warning, "Convergence is enabled but no convergence target is assigned.", this));
            }

            ValidateRanges(visibleSections, "Visible section", messages);
            ValidateRanges(darkeningZones, "Darkening zone", messages);

            for (int index = 0; index < waypoints.Length - 1; index++)
            {
                Transform current = waypoints[index];
                Transform next = waypoints[index + 1];

                if (current == null || next == null)
                {
                    continue;
                }

                if (current == next)
                {
                    messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Warning, $"Waypoint {index} and {index + 1} reference the same transform.", this));
                }
                else if ((current.position - next.position).sqrMagnitude < 0.000001f)
                {
                    messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Warning, $"Waypoint {index} and {index + 1} occupy the same position.", this));
                }
            }
        }

        private void ApplyVisuals()
        {
            if (!cacheReady)
            {
                RefreshPathCache();
            }

            if (!cacheReady)
            {
                DisableUnusedVisuals();
                return;
            }

            BuildEvaluatedSpans();

            switch (visualMode)
            {
                case VisualMode.GeneratedLineRenderers:
                    ApplyGeneratedLineRenderers();
                    DisableUnusedSegmentObjects();
                    break;
                case VisualMode.SegmentObjects:
                    ApplySegmentObjects();
                    DisableUnusedLineRenderers();
                    break;
            }
        }

        private void BuildEvaluatedSpans()
        {
            evaluatedSpans.Clear();

            List<EvaluatedSpan> baseSpans = BuildBaseVisibleSpans();
            List<Vector2> flowWindows = BuildFlowWindows();
            List<EvaluatedSpan> clippedSpans = new List<EvaluatedSpan>();

            for (int spanIndex = 0; spanIndex < baseSpans.Count; spanIndex++)
            {
                if (flowWindows.Count == 0)
                {
                    clippedSpans.Add(baseSpans[spanIndex]);
                    continue;
                }

                for (int windowIndex = 0; windowIndex < flowWindows.Count; windowIndex++)
                {
                    if (TryIntersect(baseSpans[spanIndex].Start, baseSpans[spanIndex].End, flowWindows[windowIndex].x, flowWindows[windowIndex].y, out float start, out float end))
                    {
                        EvaluatedSpan clipped = baseSpans[spanIndex];
                        clipped.Start = start;
                        clipped.End = end;
                        clippedSpans.Add(clipped);
                    }
                }
            }

            for (int spanIndex = 0; spanIndex < clippedSpans.Count; spanIndex++)
            {
                List<float> boundaries = new List<float> { clippedSpans[spanIndex].Start, clippedSpans[spanIndex].End };

                for (int darkIndex = 0; darkIndex < darkeningZones.Length; darkIndex++)
                {
                    if (!darkeningZones[darkIndex].Enabled)
                    {
                        continue;
                    }

                    Vector2 range = AnomalyRayUtility.NormalizeRange(darkeningZones[darkIndex].Start, darkeningZones[darkIndex].End);
                    if (range.y <= clippedSpans[spanIndex].Start || range.x >= clippedSpans[spanIndex].End)
                    {
                        continue;
                    }

                    boundaries.Add(Mathf.Clamp(range.x, clippedSpans[spanIndex].Start, clippedSpans[spanIndex].End));
                    boundaries.Add(Mathf.Clamp(range.y, clippedSpans[spanIndex].Start, clippedSpans[spanIndex].End));
                }

                if (useConvergenceTarget && convergenceTarget != null && convergenceStart > clippedSpans[spanIndex].Start && convergenceStart < clippedSpans[spanIndex].End)
                {
                    boundaries.Add(convergenceStart);
                }

                boundaries.Sort();
                for (int boundaryIndex = boundaries.Count - 1; boundaryIndex > 0; boundaryIndex--)
                {
                    if (Mathf.Abs(boundaries[boundaryIndex] - boundaries[boundaryIndex - 1]) < 0.0001f)
                    {
                        boundaries.RemoveAt(boundaryIndex);
                    }
                }

                for (int boundaryIndex = 0; boundaryIndex < boundaries.Count - 1; boundaryIndex++)
                {
                    float subStart = boundaries[boundaryIndex];
                    float subEnd = boundaries[boundaryIndex + 1];
                    if (subEnd - subStart < MinimumSpanLength)
                    {
                        continue;
                    }

                    float midpoint = (subStart + subEnd) * 0.5f;
                    float intensity = clippedSpans[spanIndex].Intensity;
                    float width = clippedSpans[spanIndex].Width;

                    for (int darkIndex = 0; darkIndex < darkeningZones.Length; darkIndex++)
                    {
                        if (!darkeningZones[darkIndex].Enabled)
                        {
                            continue;
                        }

                        Vector2 range = AnomalyRayUtility.NormalizeRange(darkeningZones[darkIndex].Start, darkeningZones[darkIndex].End);
                        if (midpoint >= range.x && midpoint <= range.y)
                        {
                            intensity *= Mathf.Max(0f, darkeningZones[darkIndex].IntensityMultiplier);
                            width *= Mathf.Max(0.001f, darkeningZones[darkIndex].WidthMultiplier);
                        }
                    }

                    width *= EvaluateConvergenceWidth(midpoint);

                    if (intensity <= 0.0001f || width <= 0.0001f)
                    {
                        continue;
                    }

                    evaluatedSpans.Add(new EvaluatedSpan
                    {
                        Start = subStart,
                        End = subEnd,
                        Intensity = intensity,
                        Width = width
                    });
                }
            }
        }

        private List<EvaluatedSpan> BuildBaseVisibleSpans()
        {
            List<EvaluatedSpan> spans = new List<EvaluatedSpan>();

            if (visibleSections == null || visibleSections.Length == 0)
            {
                spans.Add(new EvaluatedSpan
                {
                    Start = 0f,
                    End = 1f,
                    Intensity = 1f,
                    Width = 1f
                });
                return spans;
            }

            for (int index = 0; index < visibleSections.Length; index++)
            {
                if (!visibleSections[index].Enabled)
                {
                    continue;
                }

                Vector2 range = AnomalyRayUtility.NormalizeRange(visibleSections[index].Start, visibleSections[index].End);
                if (range.y - range.x < MinimumSpanLength)
                {
                    continue;
                }

                spans.Add(new EvaluatedSpan
                {
                    Start = range.x,
                    End = range.y,
                    Intensity = Mathf.Max(0f, visibleSections[index].IntensityMultiplier <= 0f ? 1f : visibleSections[index].IntensityMultiplier),
                    Width = Mathf.Max(0.001f, visibleSections[index].WidthMultiplier <= 0f ? 1f : visibleSections[index].WidthMultiplier)
                });
            }

            if (spans.Count == 0)
            {
                spans.Add(new EvaluatedSpan
                {
                    Start = 0f,
                    End = 1f,
                    Intensity = 1f,
                    Width = 1f
                });
            }

            return spans;
        }

        private List<Vector2> BuildFlowWindows()
        {
            List<Vector2> windows = new List<Vector2>();
            if (pathMode != PathMode.Static && Application.isPlaying && Time.timeSinceLevelLoad < startDelay)
            {
                return windows;
            }

            if (pathMode == PathMode.Static || flowWindowSize >= 0.999f)
            {
                windows.Add(new Vector2(0f, 1f));
                return windows;
            }

            float progress = ResolveTraversalProgress();
            switch (pathMode)
            {
                case PathMode.AnimatedFlow:
                case PathMode.PingPong:
                {
                    float start = progress * Mathf.Max(0f, 1f - flowWindowSize);
                    windows.Add(new Vector2(start, start + flowWindowSize));
                    break;
                }
                case PathMode.Loop:
                {
                    float start = progress;
                    float end = progress + flowWindowSize;
                    if (end <= 1f)
                    {
                        windows.Add(new Vector2(start, end));
                    }
                    else
                    {
                        windows.Add(new Vector2(start, 1f));
                        windows.Add(new Vector2(0f, end - 1f));
                    }

                    break;
                }
            }

            return windows;
        }

        private float ResolveTraversalProgress()
        {
            if (!Application.isPlaying)
            {
                return Mathf.Clamp01(previewProgress);
            }

            float elapsed = Mathf.Max(0f, Time.timeSinceLevelLoad - startDelay);
            float raw = elapsed * travelSpeed;

            return pathMode switch
            {
                PathMode.AnimatedFlow => Mathf.Clamp01(raw),
                PathMode.PingPong => Mathf.PingPong(raw, 1f),
                PathMode.Loop => Mathf.Repeat(raw, 1f),
                _ => Mathf.Clamp01(previewProgress)
            };
        }

        private float EvaluateConvergenceWidth(float normalizedDistance)
        {
            if (!useConvergenceTarget || convergenceTarget == null || normalizedDistance <= convergenceStart)
            {
                return 1f;
            }

            float local = Mathf.InverseLerp(convergenceStart, 1f, normalizedDistance);
            float curveValue = AnomalyRayUtility.HasUsableCurve(convergenceCurve) ? convergenceCurve.Evaluate(local) : local;
            return Mathf.Lerp(1f, widthAtConvergence, curveValue);
        }

        private void ApplyGeneratedLineRenderers()
        {
            if (lineRendererTemplate == null)
            {
                return;
            }

            List<LineRenderer> renderers = GetManagedLineRenderers();
            int visibleRendererCount = Mathf.Min(renderers.Count, evaluatedSpans.Count);

            for (int index = 0; index < renderers.Count; index++)
            {
                bool shouldShow = index < visibleRendererCount;
                renderers[index].gameObject.SetActive(shouldShow);
                if (!shouldShow)
                {
                    continue;
                }

                EvaluatedSpan span = evaluatedSpans[index];
                List<Vector3> positions = BuildPositionsForSpan(span.Start, span.End);
                renderers[index].positionCount = positions.Count;
                renderers[index].SetPositions(positions.ToArray());

                float midpoint = (span.Start + span.End) * 0.5f;
                Color color = EvaluateColor(midpoint, span.Intensity * intensityMultiplier);
                renderers[index].widthMultiplier = lineRendererTemplate.widthMultiplier * widthMultiplier * span.Width;
                renderers[index].startColor = color;
                renderers[index].endColor = color;
            }
        }

        private void ApplySegmentObjects()
        {
            int visibleCount = Mathf.Min(segmentObjects.Length, evaluatedSpans.Count);

            for (int index = 0; index < visibleCount; index++)
            {
                Transform segmentObject = segmentObjects[index];
                if (segmentObject == null)
                {
                    continue;
                }

                EvaluatedSpan span = evaluatedSpans[index];
                Vector3 start = EvaluatePosition(span.Start);
                Vector3 end = EvaluatePosition(span.End);
                Vector3 direction = end - start;
                float length = direction.magnitude;
                if (length < MinimumSpanLength)
                {
                    segmentObject.gameObject.SetActive(false);
                    continue;
                }

                segmentObject.gameObject.SetActive(true);
                segmentObject.position = (start + end) * 0.5f;

                if (orientSegmentObjectsToPath)
                {
                    segmentObject.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }

                Vector3 baseScale = segmentObjectBaseScales.Length > index ? segmentObjectBaseScales[index] : Vector3.one;
                float widthScale = widthMultiplier * span.Width;
                segmentObject.localScale = new Vector3(baseScale.x * widthScale, baseScale.y * widthScale, baseScale.z * length);

                Color color = EvaluateColor((span.Start + span.End) * 0.5f, span.Intensity * intensityMultiplier);
                ApplySegmentObjectColor(index, color);
            }

            for (int index = visibleCount; index < segmentObjects.Length; index++)
            {
                if (segmentObjects[index] != null)
                {
                    segmentObjects[index].gameObject.SetActive(false);
                }
            }
        }

        private void ApplySegmentObjectColor(int segmentIndex, Color color)
        {
            if (segmentObjectRenderers == null || segmentIndex >= segmentObjectRenderers.Length)
            {
                return;
            }

            Renderer[] renderers = segmentObjectRenderers[segmentIndex];
            if (renderers == null)
            {
                return;
            }

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Color baseEmission = segmentObjectBaseEmissionColors[segmentIndex][rendererIndex];
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(emissionColorProperty, AnomalyRayUtility.MultiplyColors(baseEmission, color));
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void CacheSegmentObjectState()
        {
            segmentObjectBaseScales = new Vector3[segmentObjects?.Length ?? 0];
            segmentObjectRenderers = new Renderer[segmentObjects?.Length ?? 0][];
            segmentObjectBaseEmissionColors = new Color[segmentObjects?.Length ?? 0][];

            for (int index = 0; index < segmentObjectBaseScales.Length; index++)
            {
                if (segmentObjects[index] == null)
                {
                    segmentObjectBaseScales[index] = Vector3.one;
                    segmentObjectRenderers[index] = Array.Empty<Renderer>();
                    segmentObjectBaseEmissionColors[index] = Array.Empty<Color>();
                    continue;
                }

                segmentObjectBaseScales[index] = segmentObjects[index].localScale;
                Renderer[] renderers = segmentObjects[index].GetComponentsInChildren<Renderer>(true);
                segmentObjectRenderers[index] = renderers;
                segmentObjectBaseEmissionColors[index] = new Color[renderers.Length];

                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    segmentObjectBaseEmissionColors[index][rendererIndex] = ReadRendererEmission(renderers[rendererIndex], emissionColorProperty, Color.white);
                }
            }
        }

        private void DisableUnusedVisuals()
        {
            DisableUnusedLineRenderers();
            DisableUnusedSegmentObjects();
        }

        private void DisableUnusedLineRenderers()
        {
            List<LineRenderer> renderers = GetManagedLineRenderers();
            for (int index = 0; index < renderers.Count; index++)
            {
                if (renderers[index] != null)
                {
                    renderers[index].gameObject.SetActive(false);
                }
            }
        }

        private void DisableUnusedSegmentObjects()
        {
            for (int index = 0; index < segmentObjects.Length; index++)
            {
                if (segmentObjects[index] != null)
                {
                    segmentObjects[index].gameObject.SetActive(false);
                }
            }
        }

        private List<LineRenderer> GetManagedLineRenderers()
        {
            List<LineRenderer> result = new List<LineRenderer>();
            if (lineRendererTemplate == null)
            {
                return result;
            }

            result.Add(lineRendererTemplate);

            Transform root = generatedLineRoot != null ? generatedLineRoot : transform;
            List<LineRenderer> clones = new List<LineRenderer>();

            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                Transform child = root.GetChild(childIndex);
                if (child == lineRendererTemplate.transform)
                {
                    continue;
                }

                if (!child.name.StartsWith(GeneratedRendererPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                LineRenderer clone = child.GetComponent<LineRenderer>();
                if (clone != null)
                {
                    clones.Add(clone);
                }
            }

            clones.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            result.AddRange(clones);
            return result;
        }

        private List<Vector3> BuildPositionsForSpan(float start, float end)
        {
            List<Vector3> positions = new List<Vector3> { EvaluatePosition(start) };

            for (int index = 1; index < cachedPoints.Count - 1; index++)
            {
                float normalized = cachedPathLength > 0f ? cachedDistances[index] / cachedPathLength : 0f;
                if (normalized > start && normalized < end)
                {
                    positions.Add(cachedPoints[index]);
                }
            }

            positions.Add(EvaluatePosition(end));

            if (positions.Count < 2)
            {
                positions.Add(EvaluatePosition(end));
            }

            return positions;
        }

        private Vector3 EvaluatePosition(float normalizedDistance)
        {
            EvaluateDistance(normalizedDistance, out int lowerIndex, out int upperIndex, out float segmentT);
            Vector3 point = Vector3.Lerp(cachedPoints[lowerIndex], cachedPoints[upperIndex], segmentT);

            if (!useConvergenceTarget || convergenceTarget == null || normalizedDistance <= convergenceStart)
            {
                return point;
            }

            float local = Mathf.InverseLerp(convergenceStart, 1f, normalizedDistance);
            float curveValue = AnomalyRayUtility.HasUsableCurve(convergenceCurve) ? convergenceCurve.Evaluate(local) : local;
            return Vector3.Lerp(point, convergenceTarget.position, curveValue);
        }

        private void EvaluateDistance(float normalizedDistance, out int lowerIndex, out int upperIndex, out float segmentT)
        {
            float targetDistance = Mathf.Clamp01(normalizedDistance) * cachedPathLength;

            lowerIndex = 0;
            upperIndex = Mathf.Min(1, cachedDistances.Count - 1);

            for (int index = 1; index < cachedDistances.Count; index++)
            {
                if (cachedDistances[index] >= targetDistance)
                {
                    lowerIndex = Mathf.Max(0, index - 1);
                    upperIndex = index;
                    break;
                }
            }

            float lowerDistance = cachedDistances[lowerIndex];
            float upperDistance = cachedDistances[upperIndex];
            segmentT = Mathf.InverseLerp(lowerDistance, upperDistance, targetDistance);
        }

        private List<Transform> GetValidWaypoints()
        {
            List<Transform> validWaypoints = new List<Transform>(waypoints.Length);
            for (int index = 0; index < waypoints.Length; index++)
            {
                if (waypoints[index] != null)
                {
                    validWaypoints.Add(waypoints[index]);
                }
            }

            return validWaypoints;
        }

        private Color EvaluateColor(float normalizedDistance, float alphaScale)
        {
            Color color = useColorOverride
                ? colorOverride
                : (AnomalyRayUtility.HasUsableGradient(colorGradient) ? colorGradient.Evaluate(normalizedDistance) : Color.white);
            color.a *= Mathf.Clamp01(alphaScale);
            return color;
        }

        private Color ReadRendererEmission(Renderer targetRenderer, string propertyName, Color fallback)
        {
            if (targetRenderer == null || targetRenderer.sharedMaterial == null || !targetRenderer.sharedMaterial.HasProperty(propertyName))
            {
                return fallback;
            }

            return targetRenderer.sharedMaterial.GetColor(propertyName);
        }

        private void ValidateRanges<T>(T[] ranges, string label, List<AnomalyRayValidationMessage> messages) where T : struct
        {
            for (int index = 0; index < ranges.Length; index++)
            {
                float start;
                float end;
                bool enabled;

                if (typeof(T) == typeof(VisibleSection))
                {
                    VisibleSection section = (VisibleSection)(object)ranges[index];
                    start = section.Start;
                    end = section.End;
                    enabled = section.Enabled;
                }
                else
                {
                    DarkeningZone zone = (DarkeningZone)(object)ranges[index];
                    start = zone.Start;
                    end = zone.End;
                    enabled = zone.Enabled;
                }

                if (!enabled)
                {
                    continue;
                }

                if (Mathf.Abs(start - end) < MinimumSpanLength)
                {
                    messages.Add(new AnomalyRayValidationMessage(AnomalyRayValidationSeverity.Warning, $"{label} {index} has no usable length.", this));
                }
            }
        }

        private bool TryIntersect(float aStart, float aEnd, float bStart, float bEnd, out float start, out float end)
        {
            start = Mathf.Max(aStart, bStart);
            end = Mathf.Min(aEnd, bEnd);
            return end - start > MinimumSpanLength;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static Gradient BuildDefaultGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.46f, 0.14f), 0f),
                    new GradientColorKey(new Color(1f, 0.78f, 0.42f), 0.55f),
                    new GradientColorKey(new Color(1f, 0.95f, 0.72f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            return gradient;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawGizmos)
            {
                return;
            }

            if (!cacheReady)
            {
                RefreshPathCache();
            }

            if (!cacheReady)
            {
                return;
            }

            Gizmos.color = pathGizmoColor;
            for (int index = 0; index < cachedPoints.Count - 1; index++)
            {
                Gizmos.DrawLine(EvaluatePosition(cachedDistances[index] / cachedPathLength), EvaluatePosition(cachedDistances[index + 1] / cachedPathLength));
            }

            for (int index = 0; index < waypoints.Length; index++)
            {
                if (waypoints[index] == null)
                {
                    continue;
                }

                float size = HandleUtility.GetHandleSize(waypoints[index].position) * 0.04f;
                Gizmos.DrawSphere(waypoints[index].position, size);
                Handles.Label(waypoints[index].position + Vector3.up * size * 2.5f, index.ToString());
            }

            DrawRangeGizmos(darkeningZones, darkZoneGizmoColor, "Dark");
            DrawGapGizmos();

            if (useConvergenceTarget && convergenceTarget != null)
            {
                Gizmos.color = convergenceGizmoColor;
                float size = HandleUtility.GetHandleSize(convergenceTarget.position) * 0.08f;
                Gizmos.DrawWireSphere(convergenceTarget.position, size);
                Handles.Label(convergenceTarget.position + Vector3.up * size, "Convergence");
            }
        }

        private void DrawRangeGizmos(DarkeningZone[] zones, Color color, string label)
        {
            Handles.color = color;
            for (int index = 0; index < zones.Length; index++)
            {
                if (!zones[index].Enabled)
                {
                    continue;
                }

                Vector2 range = AnomalyRayUtility.NormalizeRange(zones[index].Start, zones[index].End);
                Vector3 start = EvaluatePosition(range.x);
                Vector3 end = EvaluatePosition(range.y);
                Handles.DrawDottedLine(start, end, 4f);
                Handles.Label((start + end) * 0.5f, $"{label} {index}");
            }
        }

        private void DrawGapGizmos()
        {
            List<EvaluatedSpan> baseSpans = BuildBaseVisibleSpans();
            List<float> boundaries = new List<float> { 0f, 1f };

            for (int index = 0; index < baseSpans.Count; index++)
            {
                boundaries.Add(baseSpans[index].Start);
                boundaries.Add(baseSpans[index].End);
            }

            boundaries.Sort();
            Handles.color = gapGizmoColor;

            for (int index = 0; index < boundaries.Count - 1; index++)
            {
                float start = boundaries[index];
                float end = boundaries[index + 1];
                if (end - start < MinimumSpanLength)
                {
                    continue;
                }

                bool insideVisibleSection = false;
                float midpoint = (start + end) * 0.5f;
                for (int spanIndex = 0; spanIndex < baseSpans.Count; spanIndex++)
                {
                    if (midpoint >= baseSpans[spanIndex].Start && midpoint <= baseSpans[spanIndex].End)
                    {
                        insideVisibleSection = true;
                        break;
                    }
                }

                if (insideVisibleSection)
                {
                    continue;
                }

                Handles.DrawDottedLine(EvaluatePosition(start), EvaluatePosition(end), 5f);
            }
        }
#endif
    }
}
