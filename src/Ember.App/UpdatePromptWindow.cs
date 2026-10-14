using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ember.Core;
using static Ember.App.LightsUi;

namespace Ember.App;

public enum UpdateChoice { Later, UpdateNow, Skip }

/// <summary>
/// "Ember 1.1.0-beta.7 is ready": shown when an automatic check finds a newer version. The person can update now, be asked
/// again later (next check, or next start), or skip that one version.
/// </summary>
public sealed class UpdatePromptWindow : Window
{
    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    public static UpdateChoice Ask(Window? owner, ReleaseInfo release, string notes, bool canInstall)
    {
        var w = new UpdatePromptWindow(release, notes, canInstall)
        {
            Owner = owner is { IsVisible: true } ? owner : null,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        w.ShowDialog();
        return w.Choice;
    }

    UpdatePromptWindow(ReleaseInfo release, string notes, bool canInstall)
    {
        Title = "Ember update";
        Width = 520; SizeToContent = SizeToContent.Height; MinHeight = 240; MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var root = new StackPanel { Margin = new Thickness(24) };
        var head = Text("A new version of Ember is ready", size: 20, weight: FontWeights.SemiBold);
        AutomationProperties.SetHeadingLevel(head, AutomationHeadingLevel.Level1);
        root.Children.Add(head);
        var version = Text($"Ember {release.Version}" + (release.Prerelease ? " (pre-release)" : "") + $", you have {AppInfo.Version}", muted: true);
        version.Margin = new Thickness(0, 4, 0, 12);
        root.Children.Add(version);

        if (notes.Length > 0)
        {
            var box = new Border { Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(12), Child = Text(notes, size: 13) };
            box.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            ((TextBlock)box.Child).TextWrapping = TextWrapping.Wrap;
            root.Children.Add(box);
        }

        var how = Text(canInstall
            ? "Update now downloads the installer, checks it against its published checksum, and installs it. Windows will ask for permission, then Ember closes, updates and opens again. Fan control keeps running meanwhile."
            : "This release has no installer with a checksum. Open the release page to download it.", muted: true, size: 12);
        how.TextWrapping = TextWrapping.Wrap;
        how.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(how);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var skip = new Button { Content = "Skip this version", Style = Sty("ActionButton"), Margin = new Thickness(0, 0, 8, 0) };
        var later = new Button { Content = "Remind me later", Style = Sty("ActionButton"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var update = new Button { Content = canInstall ? "Update now" : "Open release page", Style = Sty("ActionButton"), IsDefault = true };
        skip.Click += (_, _) => { Choice = UpdateChoice.Skip; Close(); };
        later.Click += (_, _) => { Choice = UpdateChoice.Later; Close(); };
        update.Click += (_, _) => { Choice = UpdateChoice.UpdateNow; Close(); };
        buttons.Children.Add(skip);
        buttons.Children.Add(later);
        buttons.Children.Add(update);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => update.Focus();
    }
}
