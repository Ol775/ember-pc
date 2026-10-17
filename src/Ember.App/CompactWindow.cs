using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ember.App;

/// <summary>
/// A small window that stays on top, for a second monitor or a corner of the screen: the temperatures, the fan mode and the draw
/// in a few lines. Drag it anywhere, double-click or press Escape to close it. It only shows what the main window already knows.
/// </summary>
public sealed class CompactWindow : Window
{
    readonly TextBlock _temps = Ui.Text("–", 22, bold: true), _line = Ui.Muted("", 13), _extra = Ui.Muted("", 13);

    public CompactWindow()
    {
        Title = "Ember";
        Width = 250; Height = 118;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Topmost = true;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        FontFamily = Fonts.Ui;
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        body.Children.Add(Ui.Label("Ember"));
        body.Children.Add(_temps);
        body.Children.Add(_line);
        body.Children.Add(_extra);
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0), Child = body };
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        Content = frame;
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) Close(); else DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        System.Windows.Automation.AutomationProperties.SetName(this, "Ember compact view");
        ToolTip = "Drag to move. Double-click or press Escape to close.";
    }

    public void Show(string temps, string mode, string extra)
    {
        if (_temps.Text != temps) _temps.Text = temps;
        if (_line.Text != mode) _line.Text = mode;
        if (_extra.Text != extra) _extra.Text = extra;
    }
}
