using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ember.Setup
{
    /// <summary>The installer wizard: welcome, terms, options, installing, finished. Built in code so the project has no XAML to keep in step.</summary>
    public sealed class SetupWindow : Window
    {
        static readonly Brush Back = Hex("#141110"), Panel = Hex("#1d1916"), Edge = Hex("#2e2824"), TextBrush = Hex("#f3ece6"), Muted = Hex("#a89c93"), Accent = Hex("#ff7a30");

        static Brush Hex(string h) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(h)!; b.Freeze(); return b; }

        readonly SetupOptions _o;
        readonly ContentControl _page = new ContentControl();
        readonly Button _back = new Button(), _next = new Button(), _cancel = new Button();
        readonly string? _existing = Installer.ExistingInstall();
        int _step;
        bool _busy, _failed, _done;
        CheckBox? _accept;
        TextBox? _dir, _log;
        ProgressBar? _bar;
        TextBlock? _status;
        readonly List<string> _logLines = new List<string>();

        public SetupWindow(SetupOptions options)
        {
            _o = options;
            if (_existing != null && Directory.Exists(_existing)) _o.InstallDir = _existing;
            Title = "Ember Setup";
            Width = 680; Height = 560; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Back; Foreground = TextBrush; FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 14;

            var root = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 12, 24, 20) };
            StyleButton(_back, "Back", false); StyleButton(_next, "Next", true); StyleButton(_cancel, "Cancel", false);
            _back.Click += (_, __) => Go(_step - 1);
            _next.Click += OnNext;
            _cancel.Click += (_, __) => Close();
            buttons.Children.Add(_cancel); buttons.Children.Add(_back); buttons.Children.Add(_next);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            var rule = new Border { Height = 1, Background = Edge };
            DockPanel.SetDock(rule, Dock.Bottom);
            root.Children.Add(rule);
            root.Children.Add(_page);
            Content = root;
            Closing += (_, e) =>
            {
                if (_busy) { e.Cancel = true; return; }
                if (!_done && !_failed && _step < 4 && MessageBox.Show(this, "Stop setting up Ember?", "Ember Setup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
            };
            Go(0);
        }

        // ---- navigation ----

        void Go(int step)
        {
            _step = Math.Max(0, step);
            _back.Visibility = _step is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
            _cancel.Visibility = _step < 3 ? Visibility.Visible : Visibility.Collapsed;
            _next.IsEnabled = true;
            _next.Content = _step switch { 0 => _existing != null ? "Upgrade" : "Next", 1 => "I accept", 2 => "Install", 3 => "Installing…", _ => "Finish" };
            _page.Content = _step switch { 0 => Welcome(), 1 => Terms(), 2 => Options(), 3 => Progress(), _ => Finished() };
            if (_step == 1) _next.IsEnabled = _accept?.IsChecked == true;
            if (_step == 3) { _next.IsEnabled = false; _ = RunInstall(); }
        }

        void OnNext(object sender, RoutedEventArgs e)
        {
            switch (_step)
            {
                case 2:
                    _o.InstallDir = (_dir?.Text ?? _o.InstallDir).Trim().Trim('"');
                    if (!Path.IsPathRooted(_o.InstallDir)) { MessageBox.Show(this, "Please type a full folder path, such as C:\\Program Files\\Ember.", "Ember Setup"); return; }
                    Go(3);
                    break;
                case 4:
                    if (!_failed && _o.LaunchWhenDone) Installer.OpenEmber(_o.InstallDir);
                    _done = true;
                    Close();
                    break;
                default:
                    Go(_step + 1);
                    break;
            }
        }

        async Task RunInstall()
        {
            _busy = true;
            var installer = new Installer(_o,
                (percent, text) => Dispatcher.Invoke(() => { if (_bar != null) _bar.Value = percent; if (_status != null) _status.Text = text; }),
                line => Dispatcher.Invoke(() => { _logLines.Add(line); if (_log != null) { _log.AppendText(line + Environment.NewLine); _log.ScrollToEnd(); } }));
            string? error = null;
            await Task.Run(() =>
            {
                try { installer.Run(); }
                catch (Exception ex) { error = ex.Message; }
            });
            _busy = false;
            _failed = error != null;
            _summary = error ?? (installer.Warnings ? "Ember is installed, with a warning. Check the messages in the log below." : "Ember is installed.");
            Go(4);
        }

        string _summary = "";

        // ---- pages ----

        static TextBlock T(string text, double size = 14, bool muted = false, FontWeight? weight = null, Thickness? margin = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Muted : TextBrush, Margin = margin ?? new Thickness(0, 0, 0, 8) };
            if (weight != null) t.FontWeight = weight.Value;
            return t;
        }

        static void StyleButton(Button b, string text, bool primary)
        {
            b.Content = text; b.MinWidth = 96; b.Height = 34; b.Margin = new Thickness(8, 0, 0, 0); b.Padding = new Thickness(14, 0, 14, 0);
            b.Foreground = primary ? Brushes.Black : TextBrush; b.Background = primary ? Accent : Panel; b.BorderBrush = primary ? Accent : Edge;
            b.IsDefault = primary; b.FontSize = 14; b.Cursor = System.Windows.Input.Cursors.Hand;
            b.Template = RoundedButton();
        }

        static ControlTemplate RoundedButton()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            template.Triggers.Add(disabled);
            return template;
        }

        FrameworkElement Header(string title, string? sub = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(Logo(44));
            var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(T(title, 22, weight: FontWeights.SemiBold, margin: new Thickness(0)));
            if (sub != null) text.Children.Add(T(sub, 13, true, margin: new Thickness(0, 2, 0, 0)));
            row.Children.Add(text);
            return row;
        }

        static Image Logo(int size)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = typeof(SetupWindow).Assembly.GetManifestResourceStream("logo.png");
            bmp.DecodePixelWidth = size * 2;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return new Image { Source = bmp, Width = size, Height = size };
        }

        FrameworkElement Welcome()
        {
            var p = new StackPanel { Margin = new Thickness(32, 28, 32, 8) };
            p.Children.Add(Header("Welcome to Ember", "Version " + Installer.Version));
            p.Children.Add(T("Ember monitors your PC, controls its fans, and lights your RGB devices, all on your own PC with no account and no telemetry.", 15, margin: new Thickness(0, 6, 0, 14)));
            p.Children.Add(T(_existing != null ? "Ember is already installed in " + _existing + ". Setup will upgrade it and keep your settings." : "Setup will:"));
            if (_existing == null)
            {
                foreach (var line in new[]
                {
                    "Copy Ember to your PC.",
                    "Add two Windows services: one that reads sensors and controls fans, and a watchdog that returns the fans to safe control if it stops.",
                    "Install the Microsoft .NET 8 and Visual C++ runtimes if they are missing.",
                    "Download the signed PawnIO driver, which reads motherboard sensors and fan headers.",
                    "Add one Windows Firewall rule so Govee lamps on your own network can answer (private networks only).",
                    "Add Ember to the Start menu, and to your desktop if you want.",
                })
                    p.Children.Add(T("•  " + line, 14, true, margin: new Thickness(8, 0, 0, 5)));
            }
            p.Children.Add(T("Close other fan-control programs (NZXT CAM, MSI Center, Fan Control, iCUE) before you use Ember.", 13, true, margin: new Thickness(0, 14, 0, 0)));
            return p;
        }

        FrameworkElement Terms()
        {
            var p = new DockPanel { Margin = new Thickness(32, 28, 32, 8) };
            var head = Header("Terms of Use", "Please read them. Ember controls real fans and lights on your PC.");
            DockPanel.SetDock(head, Dock.Top);
            p.Children.Add(head);
            _accept = new CheckBox { Content = "I have read and accept the Terms of Use.", Foreground = TextBrush, Margin = new Thickness(0, 12, 0, 0), IsChecked = _accept?.IsChecked ?? false };
            _accept.Click += (_, __) => _next.IsEnabled = _accept.IsChecked == true;
            DockPanel.SetDock(_accept, Dock.Bottom);
            p.Children.Add(_accept);
            var doc = new StackPanel();
            foreach (var raw in Installer.Text("TERMS.md").Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (line.StartsWith("# ")) continue;
                if (line.StartsWith("## ")) doc.Children.Add(T(line.Substring(3), 15, weight: FontWeights.SemiBold, margin: new Thickness(0, 12, 0, 4)));
                else if (line.StartsWith("- ")) doc.Children.Add(T("•  " + line.Substring(2), 13, true, margin: new Thickness(8, 0, 0, 4)));
                else doc.Children.Add(T(line, 13, true, margin: new Thickness(0, 0, 0, 6)));
            }
            var scroll = new ScrollViewer { Content = doc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) };
            p.Children.Add(new Border { Child = scroll, Background = Panel, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 6, 12) });
            return p;
        }

        FrameworkElement Options()
        {
            var p = new StackPanel { Margin = new Thickness(32, 28, 32, 8) };
            p.Children.Add(Header("Choose how to install", null));
            p.Children.Add(T("Install folder", 13, true, margin: new Thickness(0, 4, 0, 4)));
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var browse = new Button();
            StyleButton(browse, "Browse…", false);
            browse.IsDefault = false;
            browse.Click += (_, __) =>
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose where to install Ember", SelectedPath = _dir?.Text ?? _o.InstallDir })
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _dir!.Text = Path.Combine(dlg.SelectedPath, dlg.SelectedPath.EndsWith("Ember", StringComparison.OrdinalIgnoreCase) ? "" : "Ember").TrimEnd('\\');
            };
            DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(browse);
            _dir = new TextBox { Text = _o.InstallDir, Height = 34, VerticalContentAlignment = VerticalAlignment.Center, Background = Panel, Foreground = TextBrush, BorderBrush = Edge, Padding = new Thickness(8, 0, 8, 0), CaretBrush = TextBrush };
            row.Children.Add(_dir);
            p.Children.Add(row);
            p.Children.Add(T("Keep this under Program Files. Ember's services run with high privilege, so setup refuses a folder that ordinary users can change.", 12, true, margin: new Thickness(0, 0, 0, 14)));

            p.Children.Add(Check("Add a shortcut to the desktop", _o.DesktopShortcut, v => _o.DesktopShortcut = v));
            p.Children.Add(Check("Download and install the PawnIO driver (recommended)", _o.InstallPawnIO, v => _o.InstallPawnIO = v));
            p.Children.Add(T("PawnIO is what lets Ember read motherboard temperatures and control fan headers. It needs an internet connection now; setup checks its checksum and signature before running it. Without it, Ember still shows CPU and GPU sensors.", 12, true, margin: new Thickness(26, 0, 0, 12)));
            p.Children.Add(Check("Open Ember when setup finishes", _o.LaunchWhenDone, v => _o.LaunchWhenDone = v));
            return p;
        }

        static CheckBox Check(string text, bool on, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = on, Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 6) };
            c.Click += (_, __) => set(c.IsChecked == true);
            return c;
        }

        FrameworkElement Progress()
        {
            var p = new DockPanel { Margin = new Thickness(32, 28, 32, 8) };
            var head = Header("Installing Ember", "This takes a minute or two. Please do not turn the PC off.");
            DockPanel.SetDock(head, Dock.Top);
            p.Children.Add(head);
            _status = T("Starting…", 14);
            DockPanel.SetDock(_status, Dock.Top);
            p.Children.Add(_status);
            _bar = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Foreground = Accent, Background = Panel, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(_bar, Dock.Top);
            p.Children.Add(_bar);
            _log = LogBox();
            p.Children.Add(_log);
            return p;
        }

        TextBox LogBox()
        {
            var box = new TextBox { IsReadOnly = true, Background = Panel, Foreground = Muted, BorderBrush = Edge, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(8) };
            box.Text = string.Join(Environment.NewLine, _logLines) + (_logLines.Count > 0 ? Environment.NewLine : "");
            return box;
        }

        FrameworkElement Finished()
        {
            _next.Content = _failed ? "Close" : "Finish";
            _cancel.Visibility = Visibility.Collapsed;
            var p = new DockPanel { Margin = new Thickness(32, 28, 32, 8) };
            var head = Header(_failed ? "Setup did not finish" : "All done", _failed ? "Nothing was left half-installed that Ember needs to run." : null);
            DockPanel.SetDock(head, Dock.Top);
            p.Children.Add(head);
            var msg = T(_summary, 14);
            DockPanel.SetDock(msg, Dock.Top);
            p.Children.Add(msg);
            if (!_failed)
            {
                var tip = T("On the Devices page, test one fan header, then enable it. Lights start switched off.", 13, true);
                DockPanel.SetDock(tip, Dock.Top);
                p.Children.Add(tip);
                var launch = Check("Open Ember", _o.LaunchWhenDone, v => _o.LaunchWhenDone = v);
                DockPanel.SetDock(launch, Dock.Top);
                p.Children.Add(launch);
            }
            p.Children.Add(LogBox());
            return p;
        }
    }
}
