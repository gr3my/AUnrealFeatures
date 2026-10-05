using System;

namespace VTX.Format
{
    /// <summary>
    /// Incremental decoder for a live VTX stream. Bytes arrive from a transport in
    /// arbitrary-sized chunks (TCP has no message boundaries); <see cref="Feed"/>
    /// buffers them and dispatches every complete message it can. Decoded frames
    /// have their bucket names restored from the stream's schema.
    ///
    /// Events fire on the thread that calls <see cref="Feed"/>; the Unity consumer
    /// feeds it from the socket's already-main-thread-marshalled callback.
    /// </summary>
    public sealed class VtxStreamReader
    {
        public event Action<VtxSchema, string, string> HeaderReceived; // schema, gameId, targetStadium
        public event Action<VtxSchema> SchemaUpdated;
        public event Action<VtxFrame> FrameReceived;
        public event Action Ended;

        public VtxSchema Schema { get; private set; }
        public string GameId { get; private set; }
        public string TargetStadium { get; private set; }

        private byte[] _buf = new byte[4096];
        private int _start;   // first unconsumed byte
        private int _len;     // one past last valid byte
        private bool _preambleParsed;

        public void Feed(byte[] data) => Feed(data, 0, data?.Length ?? 0);

        public void Feed(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            Append(data, offset, count);
            Drain();
        }

        public void Reset()
        {
            _start = _len = 0;
            _preambleParsed = false;
            Schema = null;
            GameId = TargetStadium = null;
        }

        private void Append(byte[] data, int offset, int count)
        {
            EnsureFree(count);
            Buffer.BlockCopy(data, offset, _buf, _len, count);
            _len += count;
        }

        private void EnsureFree(int extra)
        {
            // Compact consumed bytes to the front first.
            if (_start > 0)
            {
                int live = _len - _start;
                if (live > 0) Buffer.BlockCopy(_buf, _start, _buf, 0, live);
                _start = 0;
                _len = live;
            }
            if (_len + extra <= _buf.Length) return;
            int cap = _buf.Length;
            while (cap < _len + extra) cap *= 2;
            Array.Resize(ref _buf, cap);
        }

        private int Available => _len - _start;

        private void Drain()
        {
            if (!_preambleParsed)
            {
                // magic(4) + version(2)
                if (Available < 6) return;
                if (_buf[_start] != 'V' || _buf[_start + 1] != 'T' || _buf[_start + 2] != 'X' || _buf[_start + 3] != 'S')
                    throw new VtxDecodeException("VTX stream: bad magic (expected 'VTXS').");
                _start += 6;
                _preambleParsed = true;
            }

            while (true)
            {
                // message header: type(1) + len(4 LE)
                if (Available < 5) return;
                int p = _start;
                var type = (VtxMessageType)_buf[p];
                uint msgLen = (uint)(_buf[p + 1] | (_buf[p + 2] << 8) | (_buf[p + 3] << 16) | (_buf[p + 4] << 24));
                if (Available < 5 + msgLen) return; // wait for the full payload

                int payloadStart = p + 5;
                Dispatch(type, payloadStart, (int)msgLen);
                _start = payloadStart + (int)msgLen;
            }
        }

        private void Dispatch(VtxMessageType type, int payloadStart, int payloadLen)
        {
            switch (type)
            {
                case VtxMessageType.Header:
                {
                    var r = new VtxBufferReader(_buf, payloadStart, payloadLen);
                    GameId = r.ReadString();
                    TargetStadium = r.ReadString();
                    Schema = VtxSchemaCodec.Read(r);
                    if (string.IsNullOrEmpty(Schema.GameId)) Schema.GameId = GameId;
                    HeaderReceived?.Invoke(Schema, GameId, TargetStadium);
                    break;
                }
                case VtxMessageType.SchemaUpdate:
                {
                    var r = new VtxBufferReader(_buf, payloadStart, payloadLen);
                    Schema = VtxSchemaCodec.Read(r);
                    SchemaUpdated?.Invoke(Schema);
                    break;
                }
                case VtxMessageType.Frame:
                {
                    var r = new VtxBufferReader(_buf, payloadStart, payloadLen);
                    int frameIndex = r.ReadI32();
                    double gameTime = r.ReadDouble();
                    long utc = r.ReadI64();
                    var frame = VtxFrameCodec.ReadFrame(r, Schema);
                    frame.FrameIndex = frameIndex;
                    frame.GameTime = gameTime;
                    frame.UtcTimestamp = utc;
                    FrameReceived?.Invoke(frame);
                    break;
                }
                case VtxMessageType.Heartbeat:
                    break;
                case VtxMessageType.End:
                    Ended?.Invoke();
                    break;
                default:
                    // Unknown message type — skip its payload (forward-compat).
                    break;
            }
        }
    }
}
