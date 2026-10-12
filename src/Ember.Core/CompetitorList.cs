using System.Reflection;
using System.Text.Json;

namespace Ember.Core;

/// <summary>Programs that also drive fan headers or share the Super I/O chip. Data lives in competitors.json.</summary>
public sealed class CompetitorList
{
    readonly Dictionary<string, string> _byProcess = new(StringComparer.OrdinalIgnoreCase);

    public static CompetitorList Default { get; } = LoadEmbedded();

    public CompetitorList(IReadOnlyDictionary<string, string[]> displayToProcesses)
    {
        foreach (var (display, processes) in displayToProcesses)
            foreach (var p in processes) _byProcess[Strip(p)] = display;
    }

    /// <summary>Display names of the competitors found among <paramref name="processNames"/>, each once, in first-seen order.</summary>
    /// <summary>Every process name (without .exe) that belongs to the given display names, for closing them.</summary>
    public IReadOnlyList<string> ProcessNamesFor(IEnumerable<string> displayNames)
    {
        var wanted = displayNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _byProcess.Where(kv => wanted.Contains(kv.Value)).Select(kv => kv.Key).ToList();
    }

    public IReadOnlyList<string> Match(IEnumerable<string> processNames)
    {
        var found = new List<string>();
        foreach (var name in processNames)
            if (name is not null && _byProcess.TryGetValue(Strip(name), out var display) && !found.Contains(display))
                found.Add(display);
        return found;
    }

    static string Strip(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    static CompetitorList LoadEmbedded()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ember.Core.competitors.json");
            var data = s is null ? null : JsonSerializer.Deserialize<Dictionary<string, string[]>>(s);
            return new CompetitorList(data ?? new());
        }
        catch (Exception)
        {
            return new CompetitorList(new Dictionary<string, string[]>());
        }
    }
}
