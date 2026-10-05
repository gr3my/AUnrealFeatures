using System.Collections.Generic;

namespace VTX.Format
{
    /// <summary>
    /// Structure-of-Arrays container matching VTX <c>FlatArray&lt;T&gt;</c>: all
    /// elements live contiguously in <see cref="Data"/>, with <see cref="Offsets"/>
    /// marking where each sub-array starts. Sub-array <c>i</c> spans
    /// <c>[Offsets[i], Offsets[i+1])</c> (the last runs to <c>Data.Count</c>).
    /// Avoids the per-entity allocation churn of <c>List&lt;List&lt;T&gt;&gt;</c>.
    /// </summary>
    public sealed class FlatArray<T>
    {
        public List<T> Data { get; } = new List<T>();
        public List<int> Offsets { get; } = new List<int>();

        public bool IsEmpty => Data.Count == 0 && Offsets.Count == 0;
        public int SubArrayCount => Offsets.Count;

        /// <summary>Append a sub-array, recording its start offset.</summary>
        public void Add(IReadOnlyList<T> subArray)
        {
            Offsets.Add(Data.Count);
            if (subArray == null) return;
            for (int i = 0; i < subArray.Count; i++) Data.Add(subArray[i]);
        }

        /// <summary>Materialize sub-array <paramref name="index"/> as a fresh list.</summary>
        public List<T> GetSubArray(int index)
        {
            int start = Offsets[index];
            int end = index + 1 < Offsets.Count ? Offsets[index + 1] : Data.Count;
            var result = new List<T>(end - start);
            for (int i = start; i < end; i++) result.Add(Data[i]);
            return result;
        }
    }
}
