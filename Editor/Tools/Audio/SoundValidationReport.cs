using System.Collections.Generic;
using System.Text;

namespace Sinkii09.UIFramework.Editor
{
    public enum SoundIssueSeverity { Warning, Error }

    public readonly struct SoundIssue
    {
        public readonly SoundIssueSeverity Severity;
        public readonly string AssetPath;
        public readonly string Message;

        public SoundIssue(SoundIssueSeverity severity, string assetPath, string message)
        {
            Severity = severity;
            AssetPath = assetPath;
            Message = message;
        }

        public override string ToString() => $"[{Severity}] {AssetPath}: {Message}";
    }

    /// <summary>The validator's findings. A game's EditMode test asserts <see cref="Errors"/> is empty.</summary>
    public sealed class SoundValidationReport
    {
        private readonly List<SoundIssue> _issues = new();

        public IReadOnlyList<SoundIssue> Issues => _issues;
        public List<SoundIssue> Errors => Filter(SoundIssueSeverity.Error);
        public List<SoundIssue> Warnings => Filter(SoundIssueSeverity.Warning);

        internal void Error(string assetPath, string message) =>
            _issues.Add(new SoundIssue(SoundIssueSeverity.Error, assetPath, message));

        internal void Warning(string assetPath, string message) =>
            _issues.Add(new SoundIssue(SoundIssueSeverity.Warning, assetPath, message));

        public override string ToString()
        {
            var text = new StringBuilder($"{Errors.Count} error(s), {Warnings.Count} warning(s)");
            foreach (var issue in _issues) text.Append('\n').Append(issue);
            return text.ToString();
        }

        private List<SoundIssue> Filter(SoundIssueSeverity severity)
        {
            var result = new List<SoundIssue>();
            foreach (var issue in _issues)
                if (issue.Severity == severity) result.Add(issue);
            return result;
        }
    }
}
