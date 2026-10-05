using System;
using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>
    /// Validates that a schema and its frames satisfy exactly what the Unity
    /// consumer (<c>VtxStadiumFeed</c>) requires to render an entity:
    ///   1. every entity has a non-empty, stable unique id,
    ///   2. its <c>EntityTypeId</c> resolves in the schema,
    ///   3. that type exposes a <c>Pose</c> (Transform) or <c>Position</c> (Vector)
    ///      property at a valid container index,
    ///   4. a Pose scale that isn't ~0 (zero scale renders invisible).
    /// A producer runs this so it fails LOUD instead of streaming data that
    /// silently draws nothing on the client.
    /// </summary>
    public static class VtxRenderContract
    {
        public const string PoseProperty = "Pose";
        public const string PositionProperty = "Position";

        public sealed class Result
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public bool Ok => Errors.Count == 0;

            public string Summary()
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(Ok ? "PASS" : "FAIL");
                sb.Append($" ({Errors.Count} error(s), {Warnings.Count} warning(s))");
                foreach (var e in Errors) sb.Append("\n  ERROR: ").Append(e);
                foreach (var w in Warnings) sb.Append("\n  warn:  ").Append(w);
                return sb.ToString();
            }
        }

        private static VtxPropertyDef Find(VtxEntityType t, string name)
        {
            for (int i = 0; i < t.Properties.Count; i++)
                if (t.Properties[i].Name == name) return t.Properties[i];
            return null;
        }

        /// <summary>Static check: is this schema capable of rendering anything on the client?</summary>
        public static Result ValidateSchema(VtxSchema schema)
        {
            var r = new Result();
            if (schema == null) { r.Errors.Add("schema is null"); return r; }
            if (string.IsNullOrEmpty(schema.GameId)) r.Warnings.Add("schema.GameId is empty (router will fall back to the header gameId)");
            if (schema.EntityTypes.Count == 0) r.Errors.Add("schema declares no entity types");
            if (schema.Buckets.Count == 0) r.Warnings.Add("schema declares no buckets (bucket names won't be restored on the client)");

            int renderable = 0;
            foreach (var t in schema.EntityTypes)
            {
                var pose = Find(t, PoseProperty);
                var pos = Find(t, PositionProperty);
                if (pose != null && pose.Kind != VtxPropKind.Transform)
                    r.Errors.Add($"entity type '{t.Name}': '{PoseProperty}' must be Transform, is {pose.Kind}");
                if (pos != null && pos.Kind != VtxPropKind.Vector)
                    r.Errors.Add($"entity type '{t.Name}': '{PositionProperty}' must be Vector, is {pos.Kind}");

                bool hasPose = pose != null && pose.Kind == VtxPropKind.Transform;
                bool hasPos = pos != null && pos.Kind == VtxPropKind.Vector;
                if (hasPose || hasPos) renderable++;
                else r.Warnings.Add($"entity type '{t.Name}' has neither {PoseProperty}(Transform) nor {PositionProperty}(Vector) — its entities won't render");
            }
            if (renderable == 0)
                r.Errors.Add($"no renderable entity type (need a '{PoseProperty}':Transform or '{PositionProperty}':Vector property)");
            return r;
        }

        /// <summary>Instance check: does this concrete frame render on the client?</summary>
        public static Result ValidateFrame(VtxSchema schema, VtxFrame frame, int maxIssues = 25)
        {
            var r = new Result();
            if (schema == null) { r.Errors.Add("schema is null"); return r; }
            if (frame == null) { r.Errors.Add("frame is null"); return r; }

            int entityCount = 0;
            var seen = new HashSet<string>();
            for (int b = 0; b < frame.Buckets.Count; b++)
            {
                var bucket = frame.Buckets[b];
                for (int i = 0; i < bucket.Entities.Count; i++)
                {
                    if (r.Errors.Count + r.Warnings.Count >= maxIssues) { r.Warnings.Add("... (further issues truncated)"); return r; }
                    entityCount++;
                    var e = bucket.Entities[i];
                    string where = $"bucket {b} entity {i}";
                    string uid = i < bucket.UniqueIds.Count ? bucket.UniqueIds[i] : null;

                    if (string.IsNullOrEmpty(uid)) { r.Errors.Add($"{where}: empty unique id (client skips it)"); continue; }
                    if (!seen.Add(uid)) r.Warnings.Add($"{where}: duplicate unique id '{uid}' (client keeps only one)");

                    var t = schema.GetEntityType(e.EntityTypeId);
                    if (t == null) { r.Errors.Add($"{where} '{uid}': EntityTypeId {e.EntityTypeId} not in schema (client skips it)"); continue; }

                    var pose = Find(t, PoseProperty);
                    var pos = Find(t, PositionProperty);
                    if (pose != null && pose.Kind == VtxPropKind.Transform)
                    {
                        int have = e.TransformProperties != null ? e.TransformProperties.Count : 0;
                        if (pose.Index >= have)
                            r.Errors.Add($"{where} '{uid}': schema maps {PoseProperty}->Transform[{pose.Index}] but entity carries {have} transform(s)");
                        else
                        {
                            var scale = e.TransformProperties[pose.Index].Scale;
                            if (IsZero(scale)) r.Errors.Add($"{where} '{uid}': {PoseProperty} scale is ~0 → invisible (set scale to 1,1,1)");
                            else if (AnyZero(scale)) r.Warnings.Add($"{where} '{uid}': {PoseProperty} scale has a zero axis (renders flat)");
                        }
                    }
                    else if (pos != null && pos.Kind == VtxPropKind.Vector)
                    {
                        int have = e.VectorProperties != null ? e.VectorProperties.Count : 0;
                        if (pos.Index >= have)
                            r.Errors.Add($"{where} '{uid}': schema maps {PositionProperty}->Vector[{pos.Index}] but entity carries {have} vector(s)");
                    }
                    else
                    {
                        r.Errors.Add($"{where} '{uid}': entity type '{t.Name}' exposes no {PoseProperty}/{PositionProperty} — won't render");
                    }
                }
            }
            if (entityCount == 0) r.Warnings.Add("frame has no entities (nothing to render this tick)");
            return r;
        }

        private static bool IsZero(VtxVector s) => (Math.Abs(s.X) + Math.Abs(s.Y) + Math.Abs(s.Z)) < 1e-6;
        private static bool AnyZero(VtxVector s) => Math.Abs(s.X) < 1e-6 || Math.Abs(s.Y) < 1e-6 || Math.Abs(s.Z) < 1e-6;
    }
}
