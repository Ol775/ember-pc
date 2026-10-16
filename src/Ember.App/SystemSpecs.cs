using System.IO;
using System.Runtime.InteropServices;
using Ember.Core;
using System.Management;
using Microsoft.Win32;

namespace Ember.App;

/// <summary>
/// Reads the PC's specs from the registry and Windows itself (no extra service, no admin rights). Every part is read on its own, so
/// one that cannot be read leaves out only its own lines. The PC name and user name are never read.
/// </summary>
static class SystemSpecs
{
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    const string Hardware = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System";
    const string GpuClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static SpecReport Collect()
    {
        var drives = ReadDrives();
        var groups = new[]
        {
            Group("Processor", Cpu), Group("Graphics", Gpus), Group("Memory", Memory), Group("Motherboard", Board), Group("Storage", Storage),
            new SpecGroup("Drive health (S.M.A.R.T.)", drives.Select(d => new SpecItem(d.Name, d.ToLine(), !d.HealthOk)).ToList()),
            Group("Displays", Displays), Group("Windows", Windows), Group("Ember", EmberInfo),
        };
        return new SpecReport(groups.Where(g => g.Items.Count > 0).ToList(), drives);
    }

    static SpecGroup Group(string title, Func<IEnumerable<SpecItem>> read)
    {
        try { return new SpecGroup(title, read().Where(i => !string.IsNullOrWhiteSpace(i.Value)).ToList()); }
        catch (Exception ex) { DiagLog.Warn("Specs", title + " could not be read", ex); return new SpecGroup(title, []); }
    }

    static string? Text(string key, string name) => (Registry.GetValue(key, name, null) as string)?.Trim();

    static IEnumerable<SpecItem> Cpu()
    {
        var key = Hardware + @"\CentralProcessor\0";
        yield return new("Name", Regex(Text(key, "ProcessorNameString")));
        yield return new("Threads", Environment.ProcessorCount.ToString());
        if (Registry.GetValue(key, "~MHz", null) is int mhz) yield return new("Base speed", $"{mhz / 1000.0:0.0#} GHz");
    }

    static IEnumerable<SpecItem> Gpus()
    {
        using var root = Registry.LocalMachine.OpenSubKey(GpuClass);
        if (root is null) yield break;
        foreach (var name in root.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
        {
            using var k = root.OpenSubKey(name);
            var desc = k?.GetValue("DriverDesc") as string;
            if (k is null || string.IsNullOrWhiteSpace(desc) || desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue; // remote-desktop and phone-as-monitor adapters are not graphics cards
            yield return new("Card", desc.Trim());
            // 64-bit memory size where the driver gives one, else the older 32-bit value.
            var memory = k.GetValue("HardwareInformation.qwMemorySize") switch { byte[] { Length: >= 8 } b => BitConverter.ToUInt64(b, 0), long l => (ulong)l, _ => 0UL };
            if (memory == 0 && k.GetValue("HardwareInformation.MemorySize") is byte[] { Length: >= 4 } b4) memory = BitConverter.ToUInt32(b4, 0);
            if (memory > 0) yield return new("Video memory", SpecSheet.Bytes(memory));
            if (k.GetValue("DriverVersion") is string v) yield return new("Driver", v);
        }
    }

    static IEnumerable<SpecItem> Memory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref status)) yield return new("Installed", SpecSheet.Bytes(status.TotalPhys));
    }

    static IEnumerable<SpecItem> Board()
    {
        var bios = Hardware + @"\BIOS";
        yield return new("Maker", Text(bios, "BaseBoardManufacturer") ?? "");
        yield return new("Model", Text(bios, "BaseBoardProduct") ?? "");
        yield return new("BIOS", string.Join(" ", new[] { Text(bios, "BIOSVendor"), Text(bios, "BIOSVersion"), Text(bios, "BIOSReleaseDate") }.Where(s => !string.IsNullOrWhiteSpace(s))));
        var system = string.Join(" ", new[] { Text(bios, "SystemManufacturer"), Text(bios, "SystemProductName") }.Where(s => !string.IsNullOrWhiteSpace(s)));
        // Home-built PCs report a placeholder here; it adds nothing.
        if (!system.Contains("System Product Name", StringComparison.OrdinalIgnoreCase) && !system.Contains("To Be Filled", StringComparison.OrdinalIgnoreCase)) yield return new("System", system);
    }

    static IEnumerable<SpecItem> Storage()
    {
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            string text;
            bool low;
            try
            {
                text = $"{SpecSheet.Bytes((ulong)d.TotalSize)} {d.DriveFormat}, {SpecSheet.Bytes((ulong)d.AvailableFreeSpace)} free";
                low = d.AvailableFreeSpace < d.TotalSize / 20; // under 5 percent free is flagged
            }
            catch (IOException) { continue; } // a drive that went away between listing and asking
            yield return new(d.Name.TrimEnd('\\'), text + (low ? ", almost full" : ""), low);
        }
    }

    /// <summary>
    /// Health of each physical drive. Windows tells everyone the health status and (for SATA drives) whether S.M.A.R.T. predicts a
    /// failure; temperature, wear and hours on are shown where the drive reports them.
    /// </summary>
    public static IReadOnlyList<DriveSpec> ReadDrives()
    {
        try { return ReadDrivesCore(); }
        catch (Exception ex) { DiagLog.Warn("Specs", "The drives could not be read", ex); return []; }
    }

    static List<DriveSpec> ReadDrivesCore()
    {
        const string storage = @"root\Microsoft\Windows\Storage";
        var predicted = new List<(string Instance, bool Fail)>();
        try { foreach (var p in Query(@"root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus")) predicted.Add((p["InstanceName"]?.ToString() ?? "", p["PredictFailure"] is true)); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException) { /* USB and NVMe drives do not report this */ }

        // Which drive letters live on which physical disk.
        var letters = new Dictionary<string, List<char>>();
        try
        {
            foreach (var p in Query(storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
            {
                var letter = p["DriveLetter"] is null ? 0 : Convert.ToInt32(p["DriveLetter"]);
                if (letter <= 0 || p["DiskNumber"] is null) continue;
                var disk = p["DiskNumber"]!.ToString()!;
                if (!letters.TryGetValue(disk, out var list)) letters[disk] = list = [];
                list.Add((char)letter);
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException) { /* the drives just show no letters */ }

        var drives = new List<DriveSpec>();
        foreach (var d in Query(storage, "SELECT * FROM MSFT_PhysicalDisk").OrderBy(d => d["DeviceId"]?.ToString()?.PadLeft(4)))
        {
            var name = d["FriendlyName"]?.ToString()?.Trim() ?? "Drive";
            var health = d["HealthStatus"] is ushort h ? h : (ushort)5;
            var text = health switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "" };
            var ok = health is 0 or 5;
            // MSStorageDriver names the drive by the first 16 characters of its model.
            var model = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            model = model[..Math.Min(16, model.Length)];
            if (predicted.FirstOrDefault(p => p.Instance.Contains(model, StringComparison.OrdinalIgnoreCase)) is { Instance.Length: > 0 } match)
            {
                if (match.Fail) { text = "S.M.A.R.T. predicts a failure: back up now"; ok = false; }
                else if (ok) text = "S.M.A.R.T. passed";
            }
            var bus = d["BusType"] is ushort b ? b : (ushort)0;
            var media = d["MediaType"] is ushort mt ? mt : (ushort)0;
            var kind = (bus switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => "" } + media switch { 3 => " hard disk", 4 => " SSD", _ => "" }).Trim();

            // The counters hang off the disk (the same route Get-StorageReliabilityCounter takes); a drive that gives none just shows less.
            ManagementBaseObject? c = null;
            try { c = d.GetRelated("MSFT_StorageReliabilityCounter").Cast<ManagementBaseObject>().FirstOrDefault(); } catch (ManagementException) { }
            int? temp = c is not null && Num(c["Temperature"]) is > 0 and var t ? (int)t : null;
            long? hours = c is not null && Num(c["PowerOnHours"]) is > 0 and var oh ? oh : null;
            int? wear = c is not null && media == 4 && Num(c["Wear"]) is >= 0 and < 255 and var w ? (int)w : null;

            // Space is the total of its volumes; a disk with none (or one that went away) shows its size and no free space.
            ulong total = 0, free = 0;
            var mine = letters.TryGetValue(d["DeviceId"]?.ToString() ?? "", out var own) ? own.OrderBy(x => x).ToList() : [];
            foreach (var letter in mine)
                try { var di = new DriveInfo(letter.ToString()); total += (ulong)di.TotalSize; free += (ulong)di.AvailableFreeSpace; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { /* not ready */ }
            if (total == 0 && d["Size"] is ulong size) total = free = size;
            drives.Add(new DriveSpec(name, kind, bus == 7, string.Join(" ", mine.Select(x => x + ":")), total, free, text, ok, temp, hours, wear));
        }
        return drives;
    }

    static long? Num(object? o) => o is null ? null : Convert.ToInt64(o);

    static List<ManagementObject> Query(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(scope, query) { Options = { Timeout = TimeSpan.FromSeconds(10) } };
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    static IEnumerable<SpecItem> Displays()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
            yield return new($"Display {i + 1}", $"{screens[i].Bounds.Width} x {screens[i].Bounds.Height}" + (screens[i].Primary ? " (main)" : ""));
    }

    static IEnumerable<SpecItem> Windows()
    {
        const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var build = Text(key, "CurrentBuild") ?? "";
        var name = Text(key, "ProductName") ?? "Windows";
        // Windows 11 still says "Windows 10" in this value.
        if (int.TryParse(build, out var b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
        yield return new("Edition", name);
        yield return new("Version", Text(key, "DisplayVersion") ?? "");
        yield return new("Build", build + (Registry.GetValue(key, "UBR", null) is int ubr ? "." + ubr : ""));
        yield return new("Architecture", RuntimeInformation.OSArchitecture.ToString());
    }

    static IEnumerable<SpecItem> EmberInfo() => [new("Version", AppInfo.Version.ToString())];

    static string Regex(string? s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ");
}
