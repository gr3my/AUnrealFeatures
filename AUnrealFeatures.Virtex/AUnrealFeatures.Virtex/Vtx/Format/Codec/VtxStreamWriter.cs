namespace VTX.Format
{
    /// <summary>Message kinds in a live VTX stream (stable on the wire).</summary>
    public enum VtxMessageType : byte
    {
        End = 0,
        Header = 1,        // gameId + targetStadium + schema (sent once, up front)
        Frame = 2,         // frameIndex + gameTime + utc + frame data
        Heartbeat = 3,     // keep-alive, no payload
        SchemaUpdate = 4,  // mid-stream schema replacement
    }

    /// <summary>
    /// Produces a live VTX byte stream: a fixed <c>VTXS</c> magic + version, then
    /// a sequence of length-prefixed messages. Each call returns a self-contained
    /// byte[] the transport sends as-is (over the backend socket, a pipe, etc.).
    /// The stream is self-describing — the <see cref="VtxMessageType.Header"/>
    /// carries the game's schema so any consumer can decode it cold.
    ///
    /// Used by the backend relay and (in phase 2) the injected native reader's
    /// managed shim. Transport-agnostic and allocation-light.
    /// </summary>
    public sealed class VtxStreamWriter
    {
        public static readonly byte[] Magic = { (byte)'V', (byte)'T', (byte)'X', (byte)'S' };
        public const ushort Version = 1;

        private readonly VtxSchema _schema;
        private readonly string _gameId;
        private readonly string _targetStadium;

        public VtxStreamWriter(VtxSchema schema, string targetStadium = null)
        {
            _schema = schema;
            _gameId = schema?.GameId ?? string.Empty;
            _targetStadium = targetStadium ?? string.Empty;
        }

        /// <summary>Stream preamble: magic + version + the Header message (game id, stadium, schema).</summary>
        public byte[] Open()
        {
            var payload = new VtxBufferWriter(256);
            payload.WriteString(_gameId);
            payload.WriteString(_targetStadium);
            VtxSchemaCodec.Write(payload, _schema);

            var w = new VtxBufferWriter(payload.Length + 16);
            w.WriteRaw(Magic);
            w.WriteU8((byte)(Version & 0xFF));
            w.WriteU8((byte)(Version >> 8));
            WriteMessage(w, VtxMessageType.Header, payload);
            return w.ToArray();
        }

        /// <summary>Encode one frame as a Frame message.</summary>
        public byte[] Frame(VtxFrame frame)
        {
            var payload = new VtxBufferWriter(512);
            payload.WriteI32(frame.FrameIndex);
            payload.WriteDouble(frame.GameTime);
            payload.WriteI64(frame.UtcTimestamp);
            VtxFrameCodec.WriteFrame(payload, frame);

            var w = new VtxBufferWriter(payload.Length + 8);
            WriteMessage(w, VtxMessageType.Frame, payload);
            return w.ToArray();
        }

        /// <summary>A mid-stream schema change (rare — schema version bump during a live session).</summary>
        public byte[] SchemaUpdate(VtxSchema schema)
        {
            var payload = new VtxBufferWriter(256);
            VtxSchemaCodec.Write(payload, schema);
            var w = new VtxBufferWriter(payload.Length + 8);
            WriteMessage(w, VtxMessageType.SchemaUpdate, payload);
            return w.ToArray();
        }

        public byte[] Heartbeat()
        {
            var w = new VtxBufferWriter(8);
            WriteMessage(w, VtxMessageType.Heartbeat, null);
            return w.ToArray();
        }

        public byte[] Close()
        {
            var w = new VtxBufferWriter(8);
            WriteMessage(w, VtxMessageType.End, null);
            return w.ToArray();
        }

        private static void WriteMessage(VtxBufferWriter w, VtxMessageType type, VtxBufferWriter payload)
        {
            w.WriteU8((byte)type);
            if (payload == null)
            {
                w.WriteU32(0);
            }
            else
            {
                byte[] bytes = payload.ToArray();
                w.WriteU32((uint)bytes.Length);
                w.WriteRaw(bytes);
            }
        }
    }
}
