using System;

namespace VTX.Format
{
    /// <summary>
    /// Backend-side relay splitter. Consumes a live VTX stream and surfaces each
    /// message as its RAW bytes (no per-frame decode) so the router can forward
    /// verbatim to subscribers — the efficient fan-out path. It decodes only the
    /// <see cref="VtxMessageType.Header"/> to learn the gameId/stadium/schema for
    /// routing, and caches the stream <see cref="Preamble"/> (magic + version +
    /// header message) so a late-joining consumer can be brought up to speed with
    /// one write before live frames start flowing.
    /// </summary>
    public sealed class VtxStreamRelay
    {
        /// <summary>schema, gameId, targetStadium, preambleBytes.</summary>
        public event Action<VtxSchema, string, string, byte[]> HeaderReady;

        /// <summary>type, rawMessageBytes (the full [type][len][payload] slice).</summary>
        public event Action<VtxMessageType, byte[]> MessageReady;

        /// <summary>magic + version + Header message — replay to new subscribers. Null until the header arrives.</summary>
        public byte[] Preamble { get; private set; }

        public VtxSchema Schema { get; private set; }
        public string GameId { get; private set; }
        public string TargetStadium { get; private set; }

        private byte[] _buf = new byte[4096];
        private int _start;
        private int _len;
        private bool _preambleParsed;
        private int _streamStartCapture = -1; // index in _buf where the preamble began

        public void Feed(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            Append(data, offset, count);
            Drain();
        }

        public void Feed(byte[] data) => Feed(data, 0, data?.Length ?? 0);

        private void Append(byte[] data, int offset, int count)
        {
            if (_start > 0)
            {
                int live = _len - _start;
                if (live > 0) Buffer.BlockCopy(_buf, _start, _buf, 0, live);
                if (_streamStartCapture >= 0) _streamStartCapture -= _start;
                _start = 0;
                _len = live;
            }
            if (_len + count > _buf.Length)
            {
                int cap = _buf.Length;
                while (cap < _len + count) cap *= 2;
                Array.Resize(ref _buf, cap);
            }
            Buffer.BlockCopy(data, offset, _buf, _len, count);
            _len += count;
        }

        private int Available => _len - _start;

        private void Drain()
        {
            if (!_preambleParsed)
            {
                if (Available < 6) return;
                if (_buf[_start] != 'V' || _buf[_start + 1] != 'T' || _buf[_start + 2] != 'X' || _buf[_start + 3] != 'S')
                    throw new VtxDecodeException("VTX relay: bad magic (expected 'VTXS').");
                _streamStartCapture = _start; // preamble starts at magic
                _start += 6;
                _preambleParsed = true;
            }

            while (true)
            {
                if (Available < 5) return;
                int p = _start;
                var type = (VtxMessageType)_buf[p];
                uint msgLen = (uint)(_buf[p + 1] | (_buf[p + 2] << 8) | (_buf[p + 3] << 16) | (_buf[p + 4] << 24));
                int total = 5 + (int)msgLen;
                if (Available < total) return;

                var raw = new byte[total];
                Buffer.BlockCopy(_buf, p, raw, 0, total);

                if (type == VtxMessageType.Header && Preamble == null)
                {
                    var r = new VtxBufferReader(_buf, p + 5, (int)msgLen);
                    GameId = r.ReadString();
                    TargetStadium = r.ReadString();
                    Schema = VtxSchemaCodec.Read(r);
                    if (string.IsNullOrEmpty(Schema.GameId)) Schema.GameId = GameId;

                    // Preamble = magic(4)+version(2) + this whole header message.
                    int preambleLen = (p + total) - _streamStartCapture;
                    Preamble = new byte[preambleLen];
                    Buffer.BlockCopy(_buf, _streamStartCapture, Preamble, 0, preambleLen);

                    HeaderReady?.Invoke(Schema, GameId, TargetStadium, Preamble);
                }
                else
                {
                    MessageReady?.Invoke(type, raw);
                }

                _start += total;
            }
        }
    }
}
