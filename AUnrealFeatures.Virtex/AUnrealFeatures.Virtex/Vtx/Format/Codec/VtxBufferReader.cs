using System;
using System.Text;

namespace VTX.Format
{
    /// <summary>Thrown when a buffer is truncated or malformed during decode.</summary>
    public sealed class VtxDecodeException : Exception
    {
        public VtxDecodeException(string message) : base(message) { }
    }

    /// <summary>
    /// Little-endian cursor over a byte buffer — the decode counterpart to
    /// <see cref="VtxBufferWriter"/>. Bounds-checked: every read that would run
    /// past the end throws <see cref="VtxDecodeException"/> rather than reading
    /// garbage, so a torn live frame fails cleanly.
    /// </summary>
    public sealed class VtxBufferReader
    {
        private readonly byte[] _buffer;
        private readonly int _end;
        private int _pos;

        public VtxBufferReader(byte[] buffer) : this(buffer, 0, buffer?.Length ?? 0) { }

        public VtxBufferReader(byte[] buffer, int offset, int length)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _pos = offset;
            _end = offset + length;
        }

        public int Position => _pos;
        public int Remaining => _end - _pos;
        public bool AtEnd => _pos >= _end;

        private void Need(int n)
        {
            if (_pos + n > _end)
                throw new VtxDecodeException($"VTX decode: need {n} bytes at {_pos}, only {_end - _pos} remain.");
        }

        public byte ReadU8()
        {
            Need(1);
            return _buffer[_pos++];
        }

        public bool ReadBool() => ReadU8() != 0;

        public byte[] ReadRaw(int length)
        {
            if (length < 0) throw new VtxDecodeException("VTX decode: negative length.");
            Need(length);
            var result = new byte[length];
            Buffer.BlockCopy(_buffer, _pos, result, 0, length);
            _pos += length;
            return result;
        }

        /// <summary>Advance without copying (used to skip an unknown message body).</summary>
        public void Skip(int length)
        {
            Need(length);
            _pos += length;
        }

        public ulong ReadVarUInt()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                Need(1);
                byte b = _buffer[_pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 63) throw new VtxDecodeException("VTX decode: varint too long.");
            }
            return result;
        }

        public long ReadVarInt()
        {
            ulong raw = ReadVarUInt();
            return (long)(raw >> 1) ^ -(long)(raw & 1);
        }

        public uint ReadU32()
        {
            Need(4);
            uint v = (uint)(_buffer[_pos]
                            | (_buffer[_pos + 1] << 8)
                            | (_buffer[_pos + 2] << 16)
                            | (_buffer[_pos + 3] << 24));
            _pos += 4;
            return v;
        }

        public int ReadI32() => (int)ReadVarInt();
        public long ReadI64() => ReadVarInt();

        public ulong ReadU64()
        {
            Need(8);
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)_buffer[_pos++] << (i * 8);
            return v;
        }

        public float ReadFloat() => VtxBits.UInt32ToFloat(ReadU32());

        public double ReadDouble() => BitConverter.Int64BitsToDouble(unchecked((long)ReadU64()));

        public string ReadString()
        {
            int len = (int)ReadVarUInt();
            if (len == 0) return string.Empty;
            Need(len);
            string s = Encoding.UTF8.GetString(_buffer, _pos, len);
            _pos += len;
            return s;
        }
    }
}
