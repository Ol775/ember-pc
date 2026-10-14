namespace Ember.Core;

/// <summary>The lighting state and actions the app pipe needs. The app adapts its LightingManager to this.</summary>
public interface IAppLights
{
    bool On { get; }
    string CurrentScene { get; }
    IReadOnlyList<string> SceneNames { get; }
    /// <summary>The current scene's brightness, 0 to 1.</summary>
    double Brightness { get; }
    void SelectScene(string name);
    void SetOn(bool on);
    void SetBrightness(double value);
}

/// <summary>Turns an <see cref="AppRequest"/> from the app pipe into lighting actions and a reply.</summary>
public sealed class AppRequestHandler(IAppLights lights, Action showWindow)
{
    public AppReply Handle(AppRequest request)
    {
        switch (request)
        {
            case AppStatusRequest:
                break;
            case SelectSceneRequest x:
                if (!lights.SceneNames.Contains(x.Name)) return new AppReply(false, "That scene is not in Ember.", null);
                lights.SelectScene(x.Name);
                break;
            case SetOnRequest x:
                lights.SetOn(x.On);
                break;
            case ToggleOnRequest:
                lights.SetOn(!lights.On);
                break;
            case NudgeBrightnessRequest x:
                lights.SetBrightness(Math.Clamp(lights.Brightness + x.Delta, 0, 1));
                break;
            case ShowWindowRequest:
                showWindow();
                break;
            default:
                return new AppReply(false, "invalid", null);
        }
        return new AppReply(true, null, new AppStatus(lights.On, lights.CurrentScene, lights.SceneNames, lights.Brightness));
    }
}
