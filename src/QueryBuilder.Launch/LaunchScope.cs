using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QueryBuilder.Launch
{
    /// <summary>
    /// Which rows the user may see: values per scope key, or <see cref="Unrestricted"/>. The two are separate
    /// instances, so a token can never carry both.
    /// </summary>
    public sealed class LaunchScope
    {
        public const int MaxKeys = 20;
        public const int MaxValuesPerKey = 1000;
        public const int MaxValueLength = 200;
        public const int MaxBytes = 6144;

        private static readonly Regex KeyPattern = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,127}\\z", RegexOptions.CultureInvariant);

        private readonly List<KeyValuePair<string, List<string>>>? _values;

        private LaunchScope(List<KeyValuePair<string, List<string>>>? values)
        {
            _values = values;
        }

        public static LaunchScope Unrestricted { get; } = new LaunchScope(null);

        public bool IsUnrestricted => _values == null;

        /// <summary>
        /// UTF-8 bytes of the scope as the hub stores it in the session, which is the number its 6 KB limit applies to:
        /// <c>System.Text.Json</c>'s default encoder escapes quotes, ampersands, angle brackets, apostrophes, plus, backticks
        /// and everything outside ASCII, so a value costs more than its length. Zero for <see cref="Unrestricted"/>.
        /// </summary>
        public int SerializedByteCount { get; private set; }

        public static LaunchScope For(IDictionary<string, IEnumerable<string>> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Count == 0)
            {
                throw new ArgumentException("A scope needs at least one key; use LaunchScope.Unrestricted for no restriction.", nameof(values));
            }

            if (values.Count > MaxKeys)
            {
                throw new ArgumentException("A scope may have at most " + MaxKeys + " keys.", nameof(values));
            }

            var list = new List<KeyValuePair<string, List<string>>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                if (pair.Key == null || !KeyPattern.IsMatch(pair.Key))
                {
                    throw new ArgumentException("Scope key '" + pair.Key + "' must start with a letter and use only letters, digits and underscores (up to 128).", nameof(values));
                }

                if (!seen.Add(pair.Key))
                {
                    throw new ArgumentException("Scope key '" + pair.Key + "' appears twice (keys are compared ignoring case).", nameof(values));
                }

                var distinct = new List<string>();
                var seenValues = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in pair.Value ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        throw new ArgumentException("Scope key '" + pair.Key + "' has an empty value.", nameof(values));
                    }

                    if (value.Length > MaxValueLength)
                    {
                        throw new ArgumentException("A value for scope key '" + pair.Key + "' is longer than " + MaxValueLength + " characters.", nameof(values));
                    }

                    if (seenValues.Add(value))
                    {
                        distinct.Add(value);
                    }

                    if (distinct.Count > MaxValuesPerKey)
                    {
                        throw new ArgumentException("Scope key '" + pair.Key + "' has more than " + MaxValuesPerKey + " values.", nameof(values));
                    }
                }

                if (distinct.Count == 0)
                {
                    throw new ArgumentException("Scope key '" + pair.Key + "' has no values; leave the key out instead.", nameof(values));
                }

                list.Add(new KeyValuePair<string, List<string>>(pair.Key, distinct));
            }

            var scope = new LaunchScope(list);
            scope.SerializedByteCount = Measure(list);
            if (scope.SerializedByteCount > MaxBytes)
            {
                throw new ArgumentException("The scope is larger than the hub accepts (6 KB): it takes " + scope.SerializedByteCount
                    + " bytes once serialised, the limit is " + MaxBytes + ". Send fewer values, or group them in your app's own data.", nameof(values));
            }

            return scope;
        }

        internal void Write(JsonWriter writer)
        {
            if (_values == null)
            {
                writer.Bool("unrestricted", true);
                return;
            }

            writer.StartObject("scope");
            foreach (var pair in _values)
            {
                writer.StartArray(pair.Key);
                foreach (var value in pair.Value)
                {
                    writer.StringItem(value);
                }

                writer.EndArray();
            }

            writer.EndObject();
        }

        // Mirrors JsonSerializer.Serialize of the hub's key-to-values map with the default JavaScriptEncoder; a test
        // compares it with the hub's own serialiser for every UTF-16 code unit.
        private static int Measure(List<KeyValuePair<string, List<string>>> values)
        {
            var bytes = 2;
            var firstKey = true;
            foreach (var pair in values)
            {
                if (!firstKey)
                {
                    bytes++;
                }

                firstKey = false;
                bytes += EscapedBytes(pair.Key) + 2 + 1 + 2;
                var firstValue = true;
                foreach (var value in pair.Value)
                {
                    if (!firstValue)
                    {
                        bytes++;
                    }

                    firstValue = false;
                    bytes += EscapedBytes(value) + 2;
                }
            }

            return bytes;
        }

        private static int EscapedBytes(string text)
        {
            var bytes = 0;
            foreach (var c in text)
            {
                switch (c)
                {
                    case '\\':
                    case '\b':
                    case '\t':
                    case '\n':
                    case '\f':
                    case '\r':
                        bytes += 2;
                        break;
                    case '"':
                    case '&':
                    case '\'':
                    case '+':
                    case '<':
                    case '>':
                    case '`':
                        bytes += 6;
                        break;
                    default:
                        bytes += c < 0x20 || c >= 0x7F ? 6 : 1;
                        break;
                }
            }

            return bytes;
        }
    }
}
