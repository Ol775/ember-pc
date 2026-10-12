using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ember.App;

/// <summary>
/// Mouse wheel scrolling for every ScrollViewer: the wheel always scrolls the page (also when the pointer is over a slider or
/// a button), moves a fixed distance per notch instead of jumping by whole lines, and eases to the new position.
/// A scroll viewer that has nothing to scroll leaves the wheel to the one around it.
/// </summary>
public static class SmoothScroll
{
    const double PixelsPerNotch = 96, Easing = 0.22;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnEnabled));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    sealed class State
    {
        public double Target;
        public bool Running, Moving;
    }

    static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    static void OnEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue)
        {
            sv.PreviewMouseWheel += OnWheel;
            sv.ScrollChanged += OnScrollChanged;
        }
        else
        {
            sv.PreviewMouseWheel -= OnWheel;
            sv.ScrollChanged -= OnScrollChanged;
        }
    }

    static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Someone else moved the page (scroll bar, keyboard, content change): the next wheel notch starts from here.
        var sv = (ScrollViewer)sender;
        var s = States.GetOrCreateValue(sv);
        if (!s.Moving && !s.Running) s.Target = sv.VerticalOffset;
    }

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (sv.ScrollableHeight <= 0 || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        var s = States.GetOrCreateValue(sv);
        if (!s.Running) s.Target = sv.VerticalOffset;
        s.Target = Math.Clamp(s.Target - e.Delta / 120.0 * PixelsPerNotch, 0, sv.ScrollableHeight);
        if (s.Running) return;
        s.Running = true;

        void Step(object? o, EventArgs a)
        {
            var diff = s.Target - sv.VerticalOffset;
            s.Moving = true;
            if (Math.Abs(diff) < 0.5 || !sv.IsLoaded)
            {
                sv.ScrollToVerticalOffset(s.Target);
                s.Running = false;
                CompositionTarget.Rendering -= Step;
            }
            else sv.ScrollToVerticalOffset(sv.VerticalOffset + diff * Easing);
            s.Moving = false;
        }
        CompositionTarget.Rendering += Step;
    }
}
