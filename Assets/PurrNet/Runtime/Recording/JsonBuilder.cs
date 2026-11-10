using System;
using System.Globalization;
using System.Text;

namespace PurrNet.Recording
{
    /// <summary>
    /// Minimal JSON writer for the metadata records, writing UTF-8 straight into a reusable buffer: no strings, no
    /// garbage. Keeps the recorder free of serializer dependencies.
    /// </summary>
    public sealed class JsonBuilder
    {
        readonly RecordingBuffer _out = new RecordingBuffer(256);
        bool _needComma;

        public byte[] data => _out.data;
        public int length => _out.length;

        /// <summary>Starts over, keeping the buffer.</summary>
        public JsonBuilder Clear()
        {
            _out.Clear();
            _needComma = false;
            return this;
        }

        public JsonBuilder BeginObject() { Separator(); Ascii('{'); _needComma = false; return this; }
        public JsonBuilder EndObject() { Ascii('}'); _needComma = true; return this; }
        public JsonBuilder BeginArray() { Separator(); Ascii('['); _needComma = false; return this; }
        public JsonBuilder EndArray() { Ascii(']'); _needComma = true; return this; }

        public JsonBuilder Key(string key)
        {
            Separator();
            AppendString(key);
            Ascii(':');
            _needComma = false;
            return this;
        }

        public JsonBuilder Value(string value)
        {
            Separator();
            if (value == null) AsciiText("null");
            else AppendString(value);
            _needComma = true;
            return this;
        }

        public JsonBuilder Value(long value) { Separator(); Integer(value); _needComma = true; return this; }

        public JsonBuilder Value(ulong value)
        {
            Separator();
            if (value <= long.MaxValue)
                Integer((long)value);
            else
            {
                Integer((long)(value / 10));
                Ascii((char)('0' + (int)(value % 10)));
            }
            _needComma = true;
            return this;
        }

        public JsonBuilder Value(bool value) { Separator(); AsciiText(value ? "true" : "false"); _needComma = true; return this; }

        public JsonBuilder Value(float value)
        {
            Separator();
            if (float.IsNaN(value) || float.IsInfinity(value)) AsciiText("null");
            else Float(value);
            _needComma = true;
            return this;
        }

        public JsonBuilder Null() { Separator(); AsciiText("null"); _needComma = true; return this; }

        public JsonBuilder Prop(string key, string value) => Key(key).Value(value);
        public JsonBuilder Prop(string key, long value) => Key(key).Value(value);
        public JsonBuilder Prop(string key, ulong value) => Key(key).Value(value);
        public JsonBuilder Prop(string key, bool value) => Key(key).Value(value);
        public JsonBuilder Prop(string key, float value) => Key(key).Value(value);

        public JsonBuilder Floats(string key, float x, float y, float z) =>
            Key(key).BeginArray().Value(x).Value(y).Value(z).EndArray();

        public JsonBuilder Floats(string key, float x, float y, float z, float w) =>
            Key(key).BeginArray().Value(x).Value(y).Value(z).Value(w).EndArray();

        /// <summary>A string value built from parts: <c>BeginString().Part("e").Part(12).EndString()</c>.</summary>
        public JsonBuilder BeginString() { Separator(); Ascii('"'); return this; }
        public JsonBuilder Part(string text) { AppendEscaped(text); return this; }
        public JsonBuilder Part(long value) { Integer(value); return this; }
        public JsonBuilder EndString() { Ascii('"'); _needComma = true; return this; }

        /// <summary>Appends already-encoded JSON (a value) as is.</summary>
        public JsonBuilder RawValue(byte[] bytes, int count)
        {
            Separator();
            _out.WriteBytes(bytes, 0, count);
            _needComma = true;
            return this;
        }

        void Separator()
        {
            if (_needComma)
                Ascii(',');
        }

        void Ascii(char c) => _out.WriteByte((byte)c);

        void AsciiText(string text)
        {
            _out.Ensure(text.Length);
            foreach (char c in text)
                _out.data[_out.length++] = (byte)c;
        }

        void Integer(long value)
        {
            if (value < 0)
            {
                Ascii('-');
                if (value == long.MinValue)
                {
                    AsciiText("9223372036854775808");
                    return;
                }
                value = -value;
            }
            _out.Ensure(20);
            int start = _out.length;
            do
            {
                _out.data[_out.length++] = (byte)('0' + (int)(value % 10));
                value /= 10;
            } while (value != 0);
            Array.Reverse(_out.data, start, _out.length - start);
        }

        void Float(float value)
        {
            Span<char> chars = stackalloc char[32];
            if (!value.TryFormat(chars, out int written, "R", CultureInfo.InvariantCulture))
            {
                AsciiText("null");
                return;
            }
            _out.Ensure(written);
            for (int i = 0; i < written; i++)
                _out.data[_out.length++] = (byte)chars[i];
        }

        void AppendString(string s)
        {
            Ascii('"');
            AppendEscaped(s);
            Ascii('"');
        }

        void AppendEscaped(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': AsciiText("\\\""); break;
                    case '\\': AsciiText("\\\\"); break;
                    case '\n': AsciiText("\\n"); break;
                    case '\r': AsciiText("\\r"); break;
                    case '\t': AsciiText("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            AsciiText("\\u00");
                            Ascii(Hex(c >> 4));
                            Ascii(Hex(c & 15));
                        }
                        else if (c < 0x80)
                            Ascii(c);
                        else
                        {
                            // Non-ASCII: encode the whole run (keeps surrogate pairs together) without allocating.
                            int end = i + 1;
                            while (end < s.Length && s[end] >= 0x80)
                                end++;
                            _out.Ensure(Encoding.UTF8.GetMaxByteCount(end - i));
                            _out.length += Encoding.UTF8.GetBytes(s, i, end - i, _out.data, _out.length);
                            i = end - 1;
                        }
                        break;
                }
            }
        }

        static char Hex(int v) => (char)(v < 10 ? '0' + v : 'a' + v - 10);

        /// <summary>The JSON as a string. Allocates; for one-off records only.</summary>
        public override string ToString() => Encoding.UTF8.GetString(_out.data, 0, _out.length);
    }
}
