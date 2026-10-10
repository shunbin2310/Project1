using System.Text;
using System.Text.RegularExpressions;

namespace Project1.Migrations.Tests;

// A deliberately limited reader for EF-generated SQL Server scripts, not a general sqlcmd client.
internal static class MigrationSqlScript
{
    internal static IReadOnlyList<string> ReadFromCiWorkspace()
    {
        _ = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (string.IsNullOrWhiteSpace(workspace) || !Path.IsPathFullyQualified(workspace))
        {
            throw new InvalidOperationException("The GitHub workspace is required to read the generated SQL.");
        }

        // No arbitrary SQL path, application configuration, or production connection string.
        var path = Path.Combine(workspace, "artifacts", "migrations", "migrations.sql");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException("The generated migration SQL is missing, empty, or too large.");
        }

        return SplitBatches(File.ReadAllText(path, Encoding.UTF8));
    }

    internal static IReadOnlyList<string> SplitBatches(string sql)
    {
        var batches = new List<string>();
        var batch = new StringBuilder();
        var state = new LexicalState();
        // Preserve original newlines: changing CRLF inside a SQL literal would change its value.
        foreach (Match lineMatch in Regex.Matches(sql.TrimStart('\uFEFF'), @"[^\r\n]*(?:\r\n|\r|\n|$)"))
        {
            var lineWithEnding = lineMatch.Value;
            var line = lineWithEnding.TrimEnd('\r', '\n');
            if (state.IsNormal)
            {
                if (Regex.IsMatch(line, @"^\s*GO\s*(?:--.*)?$", RegexOptions.IgnoreCase))
                {
                    AddBatch();
                    continue;
                }

                var trimmed = line.TrimStart();
                if (Regex.IsMatch(line, @"^\s*GO(?:\s|;|$)", RegexOptions.IgnoreCase) ||
                    trimmed.StartsWith(':') || trimmed.StartsWith("!!", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Only plain GO separators are supported; sqlcmd directives and GO counts are refused.");
                }
            }

            batch.Append(lineWithEnding);
            state.ScanLine(line);
        }

        if (!state.IsNormal)
        {
            throw new InvalidOperationException("Migration SQL has an unterminated literal, identifier, or block comment.");
        }

        AddBatch();
        if (batches.Count == 0)
        {
            throw new InvalidOperationException("Migration SQL contains no batches.");
        }

        return batches;

        void AddBatch()
        {
            var text = batch.ToString().Trim();
            if (text.Length > 0)
            {
                batches.Add(text);
            }

            batch.Clear();
        }
    }

    private sealed class LexicalState
    {
        private char closingQuote;
        private int blockDepth;
        internal bool IsNormal => closingQuote == '\0' && blockDepth == 0;

        internal void ScanLine(string line)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var current = line[i];
                var next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (blockDepth > 0)
                {
                    if (current == '/' && next == '*') { blockDepth++; i++; }
                    else if (current == '*' && next == '/') { blockDepth--; i++; }
                    continue;
                }

                if (closingQuote != '\0')
                {
                    if (current == closingQuote)
                    {
                        if (next == closingQuote) { i++; }
                        else { closingQuote = '\0'; }
                    }

                    continue;
                }

                if (current == '-' && next == '-') { break; }
                if (current == '/' && next == '*') { blockDepth++; i++; }
                else if (current is '\'' or '"') { closingQuote = current; }
                else if (current == '[') { closingQuote = ']'; }
            }
        }
    }
}
