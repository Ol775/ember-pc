using System.Runtime.CompilerServices;

namespace Ember.Lighting.Tests;

static class TestLogFolder
{
    /// <summary>Keeps the diagnostic log of the tests out of the real log folder.</summary>
    [ModuleInitializer]
    internal static void Init() => Ember.Core.DiagLog.Use(Path.Combine(Path.GetTempPath(), "ember-lighting-tests-log"));
}
