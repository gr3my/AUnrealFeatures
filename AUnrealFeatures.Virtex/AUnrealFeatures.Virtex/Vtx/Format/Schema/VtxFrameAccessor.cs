using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>
    /// Name → (kind, index) resolver built once from a <see cref="VtxSchema"/> —
    /// the managed equivalent of VTX's PropertyAddressCache. Lets a consumer read
    /// <c>acc.GetFloat(entity, "Player", "Health")</c> instead of tracking raw
    /// container indices. Resolution is O(1) after construction.
    /// </summary>
    public sealed class VtxFrameAccessor
    {
        private readonly Dictionary<string, VtxPropertyDef> _byKey = new Dictionary<string, VtxPropertyDef>();

        public VtxSchema Schema { get; }

        public VtxFrameAccessor(VtxSchema schema)
        {
            Schema = schema;
            if (schema == null) return;
            foreach (var type in schema.EntityTypes)
                foreach (var prop in type.Properties)
                    _byKey[Key(type.Name, prop.Name)] = prop;
        }

        private static string Key(string typeName, string propName) => typeName + "" + propName;

        public VtxPropertyDef Resolve(string entityType, string propertyName) =>
            _byKey.TryGetValue(Key(entityType, propertyName), out var def) ? def : null;

        public bool TryGetFloat(PropertyContainer e, string type, string prop, out float value)
        {
            value = 0f;
            var d = Resolve(type, prop);
            if (d == null || d.Kind != VtxPropKind.Float || e.FloatProperties == null || d.Index >= e.FloatProperties.Count) return false;
            value = e.FloatProperties[d.Index];
            return true;
        }

        public float GetFloat(PropertyContainer e, string type, string prop, float fallback = 0f) =>
            TryGetFloat(e, type, prop, out var v) ? v : fallback;

        public bool TryGetInt32(PropertyContainer e, string type, string prop, out int value)
        {
            value = 0;
            var d = Resolve(type, prop);
            if (d == null || d.Kind != VtxPropKind.Int32 || e.Int32Properties == null || d.Index >= e.Int32Properties.Count) return false;
            value = e.Int32Properties[d.Index];
            return true;
        }

        public int GetInt32(PropertyContainer e, string type, string prop, int fallback = 0) =>
            TryGetInt32(e, type, prop, out var v) ? v : fallback;

        public bool TryGetString(PropertyContainer e, string type, string prop, out string value)
        {
            value = null;
            var d = Resolve(type, prop);
            if (d == null || d.Kind != VtxPropKind.String || e.StringProperties == null || d.Index >= e.StringProperties.Count) return false;
            value = e.StringProperties[d.Index];
            return true;
        }

        public string GetString(PropertyContainer e, string type, string prop, string fallback = null) =>
            TryGetString(e, type, prop, out var v) ? v : fallback;

        public bool TryGetVector(PropertyContainer e, string type, string prop, out VtxVector value)
        {
            value = default;
            var d = Resolve(type, prop);
            if (d == null || d.Kind != VtxPropKind.Vector || e.VectorProperties == null || d.Index >= e.VectorProperties.Count) return false;
            value = e.VectorProperties[d.Index];
            return true;
        }

        public VtxVector GetVector(PropertyContainer e, string type, string prop) =>
            TryGetVector(e, type, prop, out var v) ? v : default;

        public bool TryGetTransform(PropertyContainer e, string type, string prop, out VtxTransform value)
        {
            value = default;
            var d = Resolve(type, prop);
            if (d == null || d.Kind != VtxPropKind.Transform || e.TransformProperties == null || d.Index >= e.TransformProperties.Count) return false;
            value = e.TransformProperties[d.Index];
            return true;
        }

        public bool TryGetBool(PropertyContainer e, string type, string prop, out bool value)
        {
            value = false;
            var d = Resolve(type, prop);
            if (d == null || d.Kind != VtxPropKind.Bool || e.BoolProperties == null || d.Index >= e.BoolProperties.Count) return false;
            value = e.BoolProperties[d.Index];
            return true;
        }

        public bool GetBool(PropertyContainer e, string type, string prop, bool fallback = false) =>
            TryGetBool(e, type, prop, out var v) ? v : fallback;
    }
}
