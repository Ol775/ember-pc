namespace Ember.Core;

/// <summary>Cuts a changelog down to what a production build should show: the releases without a label, and nothing from the betas.</summary>
public static class ReleaseNotesFilter
{
    /// <summary>Keeps the text before the first release and every "## version" section whose version has no pre-release label.</summary>
    public static string ProductionOnly(string? changelog)
    {
        if (string.IsNullOrEmpty(changelog)) return "";
        var keep = new System.Text.StringBuilder();
        var include = true;
        foreach (var line in changelog.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var version = line[3..].Split(' ', 2)[0];
                include = !version.Contains('-');
            }
            if (include) keep.Append(line).Append('\n');
        }
        return keep.ToString();
    }
}
