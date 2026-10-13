using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ember.Core;
using Microsoft.Win32;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>Where Ember looks for the agreement given at install time (written by the installer for every user of the PC).</summary>
public static class TermsAcceptance
{
    public const string KeyPath = @"SOFTWARE\Ember";

    public static int ByInstaller()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            return key?.GetValue("AcceptedTerms") is int v ? v : 0;
        }
        catch (Exception) { return 0; }
    }
}

/// <summary>
/// The window shown the first time Ember opens (when the installer did not already ask), and again if the terms change.
/// Declining closes Ember; nothing is turned on until the terms are accepted.
/// </summary>
public sealed class TermsWindow : Window
{
    public static bool Ask(Window? owner)
    {
        var w = new TermsWindow { Owner = owner, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner };
        return w.ShowDialog() == true;
    }

    TermsWindow()
    {
        Title = "Ember: Terms of Use";
        Width = 720; Height = 640; MinWidth = 520; MinHeight = 420;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var root = new DockPanel { Margin = new Thickness(24) };
        var head = Text("Before you start", size: 22, weight: FontWeights.SemiBold);
        AutomationProperties.SetHeadingLevel(head, AutomationHeadingLevel.Level1);
        head.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var intro = Text("Please read the Terms of Use. Ember controls fans and lights on your PC, so they matter. The Privacy notice is under Settings, Legal.", muted: true);
        intro.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var accept = new Button { Content = "Accept and continue", Style = Sty("ActionButton"), IsEnabled = false, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        var decline = new Button { Content = "Decline and close Ember", Style = Sty("ActionButton"), IsCancel = true };
        accept.Click += (_, _) => DialogResult = true;
        decline.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(decline);
        buttons.Children.Add(accept);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var agree = new CheckBox { Content = "I have read and accept the Terms of Use and the Privacy notice.", Style = Sty("SwitchBox"), Margin = new Thickness(0, 12, 0, 0) };
        agree.Click += (_, _) => accept.IsEnabled = agree.IsChecked == true;
        AutomationProperties.SetName(agree, "I accept the Terms of Use");
        DockPanel.SetDock(agree, Dock.Bottom);
        root.Children.Add(agree);

        var host = new StackPanel();
        DocView.Fill(host, DocView.ReadEmbedded("TERMS.md"));
        var scroll = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };
        var frame = new Border { Child = scroll, Padding = new Thickness(16), CornerRadius = new CornerRadius(12) };
        frame.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        root.Children.Add(frame);
        Content = root;
    }
}
