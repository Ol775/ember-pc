using Ember.Core;
using Ember.Lighting;

namespace Ember.App;

/// <summary>
/// Lets the Stream Deck plugin reach the running app: starts the app pipe (ember.app.v1) and maps its requests onto the
/// lighting manager. If the pipe cannot start (another process owns the name) the app carries on without it.
/// </summary>
sealed class AppControl : IAsyncDisposable
{
    sealed class Lights(LightingManager m) : IAppLights
    {
        public bool On => m.State.On;
        public string CurrentScene => m.State.Current.Name;
        public IReadOnlyList<string> SceneNames => m.State.Scenes.Select(s => s.Name).ToList();
        public double Brightness => m.State.Current.Brightness;
        public void SelectScene(string name) => m.SelectScene(name);
        public void SetOn(bool on) => m.SetOn(on);
        public void SetBrightness(double value) => m.SetBrightness(value);
    }

    readonly AppPipeServer _server;

    public AppControl(LightingManager lights, Action showWindow)
    {
        var handler = new AppRequestHandler(new Lights(lights), showWindow);
        _server = new AppPipeServer(handler.Handle, EmberNames.AppPipeName);
    }

    public void Start()
    {
        try { _server.Start(); }
        catch (Exception ex) { DiagLog.Warn("AppPipe", "Could not start the app pipe; the Stream Deck plugin cannot reach lights", ex); }
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
