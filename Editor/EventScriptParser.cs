using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The manual-typing syntax for event commands: CommandName(key="value", key="value");
/// one command per line. This is what "Compile"/"Validate" checks when a
/// person types commands by hand instead of using the click-and-play popup -
/// per the design, click-and-play always produces valid syntax by
/// construction, but hand-typed text needs real validation (missing
/// semicolons, unknown commands, unquoted values, etc).
/// </summary>
internal static class EventScriptParser
{
    /// <summary>Parses raw script text into commands. Returns (commands, null) on success, or (null, message) with a human-readable error pointing at the offending line.</summary>
    public static (List<EventCommand>? Commands, string? Error) Parse(string rawText)
    {
        var commands = new List<EventCommand>();
        var lines = rawText.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;

            if (!line.EndsWith(";"))
                return (null, $"Line {i + 1}: missing semicolon ';' at the end of the line.\n\n{line}");

            string body = line[..^1].Trim();

            int parenOpen = body.IndexOf('(');
            int parenClose = body.LastIndexOf(')');
            if (parenOpen < 0 || parenClose < 0 || parenClose < parenOpen)
                return (null, $"Line {i + 1}: expected the form CommandName(parameters);\n\n{line}");

            string commandName = body[..parenOpen].Trim();
            string paramsBody = body[(parenOpen + 1)..parenClose];

            if (!Enum.TryParse<EventCommandType>(commandName, ignoreCase: true, out var commandType))
                return (null, $"Line {i + 1}: unknown command \"{commandName}\".\n\n{line}");

            var parameters = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(paramsBody))
            {
                foreach (var rawPair in SplitTopLevelCommas(paramsBody))
                {
                    string pair = rawPair.Trim();
                    if (pair.Length == 0) continue;

                    int eq = pair.IndexOf('=');
                    if (eq < 0)
                        return (null, $"Line {i + 1}: expected key=\"value\" - got \"{pair}\".\n\n{line}");

                    string key = pair[..eq].Trim();
                    string valueRaw = pair[(eq + 1)..].Trim();
                    if (valueRaw.Length < 2 || valueRaw[0] != '"' || valueRaw[^1] != '"')
                        return (null, $"Line {i + 1}: value for \"{key}\" must be in quotes, e.g. {key}=\"...\".\n\n{line}");

                    string innerValue = valueRaw[1..^1];
                    if (HasUnescapedQuote(innerValue))
                        return (null, $"Line {i + 1}: unexpected quote inside the value for \"{key}\" - this usually means a comma is missing between two parameters.\n\n{line}");

                    parameters[key] = innerValue.Replace("\\\"", "\"");
                }
            }

            commands.Add(new EventCommand { Type = commandType, Parameters = parameters });
        }

        return (commands, null);
    }

    /// <summary>Converts a command list back into script text - used both to show the manual-typing view and to fill it in automatically after a click-and-play edit.</summary>
    public static string ToScriptText(List<EventCommand> commands)
    {
        var lines = new List<string>();
        foreach (var cmd in commands)
        {
            var paramStrs = cmd.Parameters.Select(kv => $"{kv.Key}=\"{kv.Value.Replace("\"", "\\\"")}\"");
            lines.Add($"{cmd.Type}({string.Join(", ", paramStrs)});");
        }
        return string.Join("\n", lines);
    }

    /// <summary>True if s contains a " that isn't escaped as \" - a sign the value string ended early and something else (another parameter, missing its comma) got merged in.</summary>
    private static bool HasUnescapedQuote(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"' && (i == 0 || s[i - 1] != '\\'))
                return true;
        }
        return false;
    }

    /// <summary>Splits on commas that aren't inside a quoted string, so a comma inside a quoted value doesn't break parsing.</summary>
    private static List<string> SplitTopLevelCommas(string s)
    {
        var parts = new List<string>();
        bool inQuotes = false;
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"' && (i == 0 || s[i - 1] != '\\'))
                inQuotes = !inQuotes;
            else if (s[i] == ',' && !inQuotes)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }
}
