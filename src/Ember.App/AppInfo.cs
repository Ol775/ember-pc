using System.Reflection;
using Ember.Core;

namespace Ember.App;

public static class AppInfo
{
    /// <summary>The version this build was made as, read from the assembly (set in Directory.Build.props).</summary>
    public static AppVersion Version { get; } = Read();

    static AppVersion Read()
    {
        var text = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return AppVersion.TryParse(text, out var v) ? v : new AppVersion(0, 1, 0, "beta.1");
    }
}
