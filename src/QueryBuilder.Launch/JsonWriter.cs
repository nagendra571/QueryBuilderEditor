using System;
using System.Globalization;
using System.Text;

namespace QueryBuilder.Launch
{
    /// <summary>Writes compact JSON in the order calls are made. Only what a launch token needs; no System.Text.Json, which netstandard2.0 lacks.</summary>
    internal sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private bool _first = true;

        public JsonWriter StartObject(string? name = null)
        {
            Prefix(name);
            _sb.Append('{');
            _first = true;
            return this;
        }

        public JsonWriter EndObject()
        {
            _sb.Append('}');
            _first = false;
            return this;
        }

        public JsonWriter StartArray(string name)
        {
            Prefix(name);
            _sb.Append('[');
            _first = true;
            return this;
        }

        public JsonWriter EndArray()
        {
            _sb.Append(']');
            _first = false;
            return this;
        }

        public JsonWriter String(string name, string value)
        {
            Prefix(name);
            AppendString(_sb, value);
            return this;
        }

        public JsonWriter StringItem(string value)
        {
            Prefix(null);
            AppendString(_sb, value);
            return this;
        }

        public JsonWriter Long(string name, long value)
        {
            Prefix(name);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Bool(string name, bool value)
        {
            Prefix(name);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        public override string ToString() => _sb.ToString();

        private void Prefix(string? name)
        {
            if (!_first)
            {
                _sb.Append(',');
            }

            _first = false;
            if (name != null)
            {
                AppendString(_sb, name);
                _sb.Append(':');
            }
        }

        internal static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(c).Append(value[++i]);
                    continue;
                }

                if (char.IsSurrogate(c))
                {
                    throw new ArgumentException("Text contains an unpaired surrogate and cannot be sent as UTF-8.");
                }

                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }
    }
}
