using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace RESTTest.Behaviors;

/// <summary>
/// Helpers for the inline rename editor: give it focus the moment it appears, and commit the
/// edit when it loses focus (clicking elsewhere should keep the typed name, not discard it).
/// </summary>
public static class FocusBehavior
{
    public static readonly DependencyProperty FocusOnVisibleProperty = DependencyProperty.RegisterAttached(
        "FocusOnVisible",
        typeof(bool),
        typeof(FocusBehavior),
        new PropertyMetadata(false, OnFocusOnVisibleChanged));

    public static readonly DependencyProperty LostFocusCommandProperty = DependencyProperty.RegisterAttached(
        "LostFocusCommand",
        typeof(ICommand),
        typeof(FocusBehavior),
        new PropertyMetadata(null, OnLostFocusCommandChanged));

    public static void SetFocusOnVisible(DependencyObject element, bool value) =>
        element.SetValue(FocusOnVisibleProperty, value);

    public static bool GetFocusOnVisible(DependencyObject element) =>
        (bool)element.GetValue(FocusOnVisibleProperty);

    public static void SetLostFocusCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(LostFocusCommandProperty, value);

    public static ICommand? GetLostFocusCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(LostFocusCommandProperty);

    private static void OnFocusOnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnIsVisibleChanged;
        element.Loaded -= OnLoaded;

        if (e.NewValue is true)
        {
            element.IsVisibleChanged += OnIsVisibleChanged;

            // IsVisibleChanged only fires on a transition. A node created already in rename mode
            // has its container realized with this element *born* visible, so that event never
            // comes; Loaded is the hook that catches it.
            element.Loaded += OnLoaded;

            if (element.IsVisible)
            {
                FocusNow(element);
            }
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is FrameworkElement element)
        {
            FocusNow(element);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Every row's editor raises Loaded when its container is realized, so only the one
        // actually being renamed (and therefore visible) should take focus.
        if (sender is FrameworkElement { IsVisible: true } element)
        {
            FocusNow(element);
        }
    }

    private static void FocusNow(FrameworkElement element)
    {
        // Focus has to wait for layout: the element is visible but not yet focusable on this pass.
        element.Dispatcher.BeginInvoke(
            () =>
            {
                if (!element.IsVisible)
                {
                    return;
                }

                element.Focus();
                Keyboard.Focus(element);

                if (element is TextBox textBox)
                {
                    textBox.SelectAll();
                }
            },
            DispatcherPriority.Input);
    }

    private static void OnLostFocusCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.LostFocus -= OnLostFocus;

        if (e.NewValue is ICommand)
        {
            element.LostFocus += OnLostFocus;
        }
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && GetLostFocusCommand(element) is { } command
            && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
