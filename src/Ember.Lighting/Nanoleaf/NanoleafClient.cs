using System.Net;
using System.Text;
using System.Text.Json;

namespace Ember.Lighting.Nanoleaf;

/// <summary>One light panel, with where it sits on the controller's own layout.</summary>
public sealed record NanoleafPanel(int Id, int X, int Y);

public sealed record NanoleafInfo(string Name, string Serial, IReadOnlyList<NanoleafPanel> Panels);

/// <summary>The controller answered, but it is not in pairing mode: hold its power button for 5 to 7 seconds first.</summary>
public sealed class NanoleafPairingException() : Exception("Hold the power button on the Nanoleaf controller for 5 to 7 seconds until the light flashes, then press Link within 30 seconds.");

/// <summary>
/// A Nanoleaf controller over its local OpenAPI (plain HTTP on port 16021, no account and no cloud). The token it hands out at
/// pairing is sent to that controller only. Each panel is one light, shown in order from left to right.
/// </summary>
public sealed class NanoleafClient(HttpClient http, string host, string? token)
{
    public const int Port = 16021;
    /// <summary>Shapes controller and similar parts have no light of their own.</summary>
    const int ControllerShape = 12;

    public string Host => host;

    static Uri Url(string host, string path) => new($"http://{host}:{Port}/api/v1/{path}");

    /// <summary>Asks the controller for a token. It only answers while it has just been put into pairing mode.</summary>
    public static async Task<string> PairAsync(HttpClient http, string host, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Url(host, "new"));
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.Forbidden) throw new NanoleafPairingException();
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParsePairing(body);
    }

    public static string ParsePairing(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("auth_token", out var t) && t.GetString() is { Length: > 0 and <= 128 } token && token.All(char.IsAsciiLetterOrDigit)) return token;
        }
        catch (Exception) { /* not the answer of a Nanoleaf controller */ }
        throw new InvalidOperationException("That did not look like a Nanoleaf controller.");
    }

    public async Task<NanoleafInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var info = await GetAsync("", ct).ConfigureAwait(false);
        var layout = await GetAsync("panelLayout/layout", ct).ConfigureAwait(false);
        return ParseInfo(info, layout);
    }

    async Task<string> GetAsync(string path, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, path);
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new InvalidOperationException("The controller no longer accepts the saved token. Link it again in Settings.");
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The controller answered {(int)res.StatusCode}.");
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The name and serial number from the controller's description, and its panels ordered left to right (then top to bottom).</summary>
    public static NanoleafInfo ParseInfo(string info, string layout)
    {
        string name = "Nanoleaf", serial = "";
        var panels = new List<NanoleafPanel>();
        try
        {
            using var d = JsonDocument.Parse(info);
            if (d.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s) name = s.Length > 60 ? s[..60] : s;
            if (d.RootElement.TryGetProperty("serialNo", out var sn) && sn.GetString() is { } serialNo) serial = new string(serialNo.Where(char.IsAsciiLetterOrDigit).Take(32).ToArray());
        }
        catch (Exception) { /* keep the defaults */ }
        try
        {
            using var d = JsonDocument.Parse(layout);
            foreach (var p in d.RootElement.GetProperty("positionData").EnumerateArray())
            {
                if (p.TryGetProperty("shapeType", out var shape) && shape.TryGetInt32(out var st) && st == ControllerShape) continue;
                if (!p.TryGetProperty("panelId", out var id) || !id.TryGetInt32(out var pid) || pid < 0) continue;
                var x = p.TryGetProperty("x", out var px) && px.TryGetInt32(out var xi) ? xi : 0;
                var y = p.TryGetProperty("y", out var py) && py.TryGetInt32(out var yi) ? yi : 0;
                panels.Add(new NanoleafPanel(pid, x, y));
                if (panels.Count >= 200) break;
            }
        }
        catch (Exception) { /* a layout that cannot be read: no panels */ }
        return new NanoleafInfo(name, serial, panels.OrderBy(p => p.X).ThenBy(p => p.Y).ToList());
    }

    /// <summary>Shows one colour per panel, straight away. <paramref name="panels"/> and <paramref name="colours"/> line up.</summary>
    public async Task ShowAsync(IReadOnlyList<NanoleafPanel> panels, IReadOnlyList<Color> colours, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Put, "effects");
        req.Content = new StringContent(FramePayload(panels, colours), Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The controller answered {(int)res.StatusCode}.");
    }

    public async Task SetOnAsync(bool on, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Put, "state");
        req.Content = new StringContent(on ? "{\"on\":{\"value\":true}}" : "{\"on\":{\"value\":false}}", Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The controller answered {(int)res.StatusCode}.");
    }

    /// <summary>
    /// The "display" write: "count, then for each panel: id, frames (1), red, green, blue, white (0), transition in tenths of a second".
    /// A transition of 1 is the shortest the panels take.
    /// </summary>
    public static string FramePayload(IReadOnlyList<NanoleafPanel> panels, IReadOnlyList<Color> colours)
    {
        var sb = new StringBuilder().Append(panels.Count);
        for (var i = 0; i < panels.Count; i++)
        {
            var (r, g, b) = (i < colours.Count ? colours[i] : Color.Black).ToBytes();
            sb.Append(' ').Append(panels[i].Id).Append(" 1 ").Append(r).Append(' ').Append(g).Append(' ').Append(b).Append(" 0 1");
        }
        return JsonSerializer.Serialize(new { write = new { command = "display", animType = "static", animData = sb.ToString(), loop = false, palette = Array.Empty<object>() } });
    }

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("The Nanoleaf controller is not linked yet.");
        return new HttpRequestMessage(method, Url(host, token + (path.Length == 0 ? "" : "/" + path)));
    }
}

/// <summary>
/// The panels laid out as a grid of cells, as they sit on the wall, so an effect on the Lights canvas sweeps across them the way
/// it would across the real shape. Built from the controller's own positions: panels that line up share a column or a row.
/// Null when the panels are in one line already, when two would land in one cell, or when the grid would be huge.
/// </summary>
public sealed record NanoleafGrid(int Columns, int Rows, IReadOnlyList<int> CellOfPanel)
{
    public int Cells => Columns * Rows;

    public static NanoleafGrid? TryBuild(IReadOnlyList<NanoleafPanel> panels)
    {
        if (panels.Count < 2) return null;
        var column = Cluster(panels.Select(p => p.X).ToList());
        var row = Cluster(panels.Select(p => -p.Y).ToList());   // the controller counts y upwards; the canvas counts rows downwards
        if (column is null || row is null) return null;
        var (columns, rows) = (column.Max() + 1, row.Max() + 1);
        if (columns < 2 || rows < 2 || columns * rows > 400) return null;
        var cells = panels.Select((_, i) => row[i] * columns + column[i]).ToList();
        return cells.Distinct().Count() == cells.Count ? new NanoleafGrid(columns, rows, cells) : null;
    }

    /// <summary>Numbers each value by its column: values closer than half the smallest gap between values share one.</summary>
    static int[]? Cluster(IReadOnlyList<int> values)
    {
        var sorted = values.Distinct().Order().ToList();
        if (sorted.Count < 2) return Enumerable.Repeat(0, values.Count).ToArray();
        var gap = sorted.Zip(sorted.Skip(1), (a, b) => b - a).Min();
        var tolerance = Math.Max(1, gap / 2.0);
        var groupOf = new Dictionary<int, int>();
        var group = 0;
        var anchor = sorted[0];
        foreach (var v in sorted)
        {
            if (v - anchor > tolerance) { group++; anchor = v; }
            groupOf[v] = group;
        }
        return values.Select(v => groupOf[v]).ToArray();
    }
}
