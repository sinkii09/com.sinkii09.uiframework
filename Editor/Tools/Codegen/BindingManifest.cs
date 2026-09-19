using System;
using System.Collections.Generic;
using System.Text;

namespace Sinkii09.UIFramework.Editor
{
    /// <summary>
    /// One serialized field the generator emits, pointing at a prefab child.
    /// <see cref="Component"/> is wizard-side only — the manifest and the emitter read the three
    /// strings and nothing else, which is what keeps both of them testable without a prefab.
    /// </summary>
    public sealed class BindingCandidate
    {
        public string FieldName;
        public string ChildPath;
        public string TypeFullName;
        public UnityEngine.Component Component;
    }

    /// <summary>
    /// The ONE definition of the generated header format. The emitter formats through it and the
    /// scanner parses through it — two independent definitions of one format is exactly how this
    /// package's older wizards diverged, so this type owns both directions.
    /// </summary>
    public static class BindingManifest
    {
        /// Presence of this line is what licenses the generator to overwrite a file.
        public const string Marker = MarkerPrefix + "1";

        // Matched by prefix, not equality: bumping the format to v2 must not make every file
        // written by v1 un-overwritable ("was not written by this generator" on our own output).
        private const string MarkerPrefix = "// uifw-bindings-manifest v";

        private const string PrefabPrefix = "// prefab: ";
        private const string FieldPrefix = "// field: ";
        private const string Separator = " | ";
        private const string PipeEscape = "%7C";

        // The header sits at the top of the file; scanning further would start matching comments
        // inside generated field declarations.
        private const int HeaderScanLines = 32;

        public static string Format(string prefabGuid, IReadOnlyList<BindingCandidate> candidates)
        {
            var sb = new StringBuilder();
            sb.Append(Marker).Append('\n');
            sb.Append(PrefabPrefix).Append(prefabGuid ?? string.Empty).Append('\n');
            foreach (var c in candidates)
            {
                sb.Append(FieldPrefix)
                  .Append(c.FieldName).Append(Separator)
                  .Append(Escape(c.ChildPath)).Append(Separator)
                  .Append(c.TypeFullName).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// True only when the generator itself wrote this file. Anything else — a hand-written file
        /// that happens to share the name, an empty file, a near-miss comment — is refused, because
        /// the generator overwrites without prompting.
        /// </summary>
        public static bool HasMarker(string fileText)
        {
            if (string.IsNullOrEmpty(fileText)) return false;
            foreach (var line in HeaderLines(fileText))
                if (line.StartsWith(MarkerPrefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Identity of a binding: the (child path, component type) pair, NOT the field name. The
        /// field name is an output that can legally change; this pair is what stays the same across
        /// regenerations, so it is the key both the scanner and the wizard match on. Defined here
        /// because it reuses the same pipe-escaping as the header — a second key format built with a
        /// raw '|' elsewhere is how a path containing '|' starts aliasing onto another binding.
        /// </summary>
        public static string PairKey(string childPath, string typeFullName) =>
            Escape(childPath) + Separator + (typeFullName ?? string.Empty);

        public static string PairKey(BindingCandidate candidate) =>
            candidate == null ? string.Empty : PairKey(candidate.ChildPath, candidate.TypeFullName);

        /// <summary>
        /// Reads back what the previous generation emitted, so the wizard can re-tick the same
        /// children. Returns false when the file is not ours; an unparseable field line is skipped
        /// rather than failing the whole read — a partial restore beats none.
        /// </summary>
        public static bool TryParse(string fileText, out string prefabGuid, out List<BindingCandidate> candidates)
        {
            prefabGuid = null;
            candidates = new List<BindingCandidate>();
            if (!HasMarker(fileText)) return false;

            foreach (var line in HeaderLines(fileText))
            {
                if (line.StartsWith(PrefabPrefix, StringComparison.Ordinal))
                {
                    prefabGuid = line.Substring(PrefabPrefix.Length).Trim();
                    continue;
                }
                if (!line.StartsWith(FieldPrefix, StringComparison.Ordinal)) continue;

                var parts = line.Substring(FieldPrefix.Length).Split(new[] { Separator }, 3, StringSplitOptions.None);
                if (parts.Length != 3) continue;

                candidates.Add(new BindingCandidate
                {
                    FieldName = parts[0].Trim(),
                    ChildPath = Unescape(parts[1].Trim()),
                    TypeFullName = parts[2].Trim()
                });
            }

            return true;
        }

        // A GameObject may legitimately be named "a | b", which would break the separator. Field
        // names are identifier-sanitised and type names cannot contain '|', so only the path needs it.
        private static string Escape(string path) => path?.Replace("|", PipeEscape) ?? string.Empty;

        private static string Unescape(string path) => path?.Replace(PipeEscape, "|") ?? string.Empty;

        private static IEnumerable<string> HeaderLines(string fileText)
        {
            var lines = fileText.Split('\n');
            var count = Math.Min(lines.Length, HeaderScanLines);
            for (var i = 0; i < count; i++)
            {
                // Tolerates CRLF checkouts and a UTF-8 BOM on the first line — neither should
                // decide whether we are allowed to overwrite a file.
                yield return lines[i].Trim('\r', '﻿', ' ', '\t');
            }
        }
    }
}
