using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    public sealed class GenerateResult
    {
        public bool Success;
        public string Message;
        public string Path;
    }

    /// <summary>
    /// Writes "{View}.Bindings.g.cs" next to the hand-written "{View}.cs". Every decision that can
    /// be made without touching disk is a pure static below, so the guards are testable without
    /// driving an EditorWindow.
    /// </summary>
    public static class PrefabBindingGenerator
    {
        public const string GeneratedSuffix = ".Bindings.g.cs";

        private static readonly Regex IdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
        private static readonly Regex NamespacePattern = new(@"^\s*namespace\s+([A-Za-z_][\w.]*)", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex CommentOrStringPattern =
            new(@"//[^\n]*|/\*.*?\*/|""(?:\\.|[^""\\\n])*""", RegexOptions.Singleline | RegexOptions.Compiled);

        // C# keywords that pass the identifier regex but cannot name a class. Contextual keywords
        // (var, record, ...) are legal type names, so only the reserved ones are listed.
        private static readonly HashSet<string> ReservedKeywords = new()
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
            "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
            "using", "virtual", "void", "volatile", "while"
        };

        /// <summary>
        /// Blanks out comments and string literals, preserving length and newlines so every offset
        /// still lines up with the original. Without this, a "namespace Foo" inside a block comment
        /// or a class name mentioned in a doc-comment silently steers the generator.
        /// </summary>
        public static string StripCommentsAndStrings(string fileText)
        {
            if (string.IsNullOrEmpty(fileText)) return fileText ?? string.Empty;
            return CommentOrStringPattern.Replace(fileText,
                m => Regex.Replace(m.Value, @"[^\n]", " "));
        }

        /// <summary>
        /// Rejects names that are not legal C# identifiers and folders that escape the project.
        /// Kept separate from ViewViewModelCreatorWizard.GetExistingTargets so that method — and the
        /// four regression tests pinning it — stay byte-identical.
        /// </summary>
        public static string ValidateTarget(string viewName, string folder)
        {
            if (string.IsNullOrWhiteSpace(viewName) || !IdentifierPattern.IsMatch(viewName))
                return $"\"{viewName}\" is not a legal C# class name.";

            if (ReservedKeywords.Contains(viewName))
                return $"\"{viewName}\" is a C# keyword and cannot be a class name.";

            if (string.IsNullOrWhiteSpace(folder))
                return "Output folder is empty.";

            var normalized = folder.Replace('\\', '/').Trim();

            if (normalized.Split('/').Any(segment => segment == ".."))
                return "Output folder may not contain \"..\".";

            if (Path.IsPathRooted(normalized) || normalized.StartsWith("//"))
                return "Output folder must be a project-relative path.";

            if (!normalized.StartsWith("Assets/") && normalized != "Assets" &&
                !normalized.StartsWith("Packages/") && normalized != "Packages")
                return "Output folder must be under Assets/ or Packages/.";

            return null;
        }

        public static bool DeclaresPartialClass(string fileText, string className) =>
            !string.IsNullOrEmpty(fileText) &&
            Regex.IsMatch(StripCommentsAndStrings(fileText), $@"\bpartial\s+class\s+{Regex.Escape(className)}\b");

        /// <summary>
        /// The generated part must sit in the SAME namespace as the hand-written part or the two are
        /// different types. Taken from the hand file, never from the wizard's namespace field.
        /// </summary>
        public static string ExtractNamespace(string fileText, string className = null)
        {
            if (string.IsNullOrEmpty(fileText)) return string.Empty;

            var code = StripCommentsAndStrings(fileText);

            // With a class name, take the LAST namespace opened before the class declaration: a file
            // with two namespace blocks would otherwise put the generated part in the first one,
            // producing a second, unrelated type and a baffling "no such member".
            var limit = code.Length;
            if (!string.IsNullOrEmpty(className))
            {
                var classMatch = Regex.Match(code, $@"\bpartial\s+class\s+{Regex.Escape(className)}\b");
                if (classMatch.Success) limit = classMatch.Index;
            }

            var best = string.Empty;
            foreach (Match match in NamespacePattern.Matches(code))
            {
                if (match.Index >= limit) break;
                best = match.Groups[1].Value;
            }

            return best;
        }

        /// <summary>
        /// Best-effort: a field already DECLARED by hand would be a duplicate-member compile error.
        /// Matching must be declaration-shaped, not "the file mentions this name" — the whole point
        /// of the feature is that "{View}.cs" uses these fields, so a bare name match makes every
        /// run after the first refuse with a message that blames fields nobody wrote.
        /// A declaration is a type token, whitespace, the name, then ';', '=' or ','.
        /// </summary>
        public static string[] FindDeclaredFieldCollisions(string handFileText, IEnumerable<string> fieldNames)
        {
            if (string.IsNullOrEmpty(handFileText)) return new string[0];

            var code = StripCommentsAndStrings(handFileText);
            return fieldNames
                .Where(f => !string.IsNullOrEmpty(f) && Regex.IsMatch(code, DeclarationPattern(f)))
                .ToArray();
        }

        private static string DeclarationPattern(string fieldName) =>
            // (boundary)(modifiers)*(type)(space)(name)(terminator)
            @"(?:^|[;{}\[\]\s])(?:(?:public|private|protected|internal|static|readonly|volatile|new)\s+)*" +
            // A statement keyword in the type position means this is a usage, not a declaration:
            // "return _x = y;", "await _x;", "throw _x;" would otherwise read as declarations and
            // refuse generation with a message blaming a field nobody declared.
            @"(?!(?:return|await|throw|yield|else|do|using|lock|case|in|out|ref)\s)" +
            @"[A-Za-z_][\w.]*(?:<[^<>;]*>)?(?:\[\s*[,\s]*\])?\s+" +
            Regex.Escape(fieldName) + @"\s*(?:[;=,]|=>)";

        /// Project-relative path of the single "{viewName}.cs" MonoScript, or null when it is
        /// missing or ambiguous.
        public static string FindViewScriptPath(string viewName)
        {
            var matches = AssetDatabase.FindAssets($"{viewName} t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p) == viewName + ".cs")
                .Distinct()
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        public static GenerateResult Generate(string viewName, GameObject prefab,
                                              IReadOnlyList<BindingCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return Fail("Nothing selected — tick at least one child to bind.");

            // Last line of defence: nothing downstream checks this, and a duplicate would emit two
            // identical field declarations, i.e. a generated file that does not compile.
            var duplicates = candidates.GroupBy(c => c.FieldName)
                                       .Where(g => g.Count() > 1)
                                       .Select(g => g.Key)
                                       .ToArray();
            if (duplicates.Length > 0)
                return Fail($"Two selected children resolved to the same field name: " +
                            $"{string.Join(", ", duplicates)}. Rename one of the prefab children.");

            // Name is checked before the lookup so an illegal name reports why, instead of the
            // misleading "could not find exactly one <garbage>.cs".
            var badName = ValidateTarget(viewName, "Assets");
            if (badName != null) return Fail(badName);

            var scriptPath = FindViewScriptPath(viewName);
            if (scriptPath == null)
                return Fail($"Could not find exactly one {viewName}.cs in the project. " +
                            "Create the View first, or rename so only one file matches.");

            // Derived from the located script, never from the wizard's folder field: a partial split
            // across two assemblies does not compile.
            var folder = (Path.GetDirectoryName(scriptPath) ?? string.Empty).Replace('\\', '/');
            var invalid = ValidateTarget(viewName, folder);
            if (invalid != null) return Fail(invalid);

            if (!TryReadAllText(scriptPath, out var handText, out var readError))
                return Fail(readError);

            if (!DeclaresPartialClass(handText, viewName))
                return Fail($"{scriptPath} does not declare \"partial class {viewName}\". " +
                            "Add the partial keyword, then generate again.");

            var collisions = FindDeclaredFieldCollisions(handText, candidates.Select(c => c.FieldName));
            if (collisions.Length > 0)
                return Fail($"{viewName}.cs already declares: {string.Join(", ", collisions)}. " +
                            "Remove those hand-written declarations, or untick those children.");

            var targetPath = $"{folder}/{viewName}{GeneratedSuffix}";
            if (File.Exists(targetPath))
            {
                if (!TryReadAllText(targetPath, out var existing, out var existingError))
                    return Fail(existingError);

                if (!BindingManifest.HasMarker(existing))
                    return Fail($"{targetPath} exists but was not written by this generator. " +
                                "Refusing to overwrite it.");
            }

            var guid = prefab != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab))
                : string.Empty;

            var source = BindingPartialEmitter.Emit(ExtractNamespace(handText, viewName), viewName, guid, candidates);

            try
            {
                File.WriteAllText(targetPath, source);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[UIFramework] Could not write {targetPath}: {e.Message}");
                return Fail($"Could not write {targetPath}: {e.Message}");
            }

            AssetDatabase.Refresh();

            return new GenerateResult
            {
                Success = true,
                Path = targetPath,
                Message = $"Wrote {candidates.Count} binding(s) to {targetPath}"
            };
        }

        /// Reads back the previous generation so the wizard can re-tick the same children.
        public static bool TryReadExisting(string viewName, out string prefabGuid, out List<BindingCandidate> previous)
        {
            prefabGuid = null;
            previous = new List<BindingCandidate>();

            var scriptPath = FindViewScriptPath(viewName);
            if (scriptPath == null) return false;

            var folder = (Path.GetDirectoryName(scriptPath) ?? string.Empty).Replace('\\', '/');
            var targetPath = $"{folder}/{viewName}{GeneratedSuffix}";
            if (!File.Exists(targetPath)) return false;

            if (!TryReadAllText(targetPath, out var text, out var error))
            {
                Debug.LogWarning($"[UIFramework] {error}");
                return false;
            }

            return BindingManifest.TryParse(text, out prefabGuid, out previous);
        }

        /// <summary>
        /// Every read goes through here so a locked or unreadable file becomes a message in the
        /// wizard rather than an exception thrown out of OnGUI, where the user sees nothing but a
        /// console stack trace.
        /// </summary>
        private static bool TryReadAllText(string path, out string text, out string error)
        {
            try
            {
                text = File.ReadAllText(path);
                error = null;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                text = null;
                error = $"Could not read {path}: {e.Message}";
                return false;
            }
        }

        private static GenerateResult Fail(string message) => new() { Success = false, Message = message };
    }
}
