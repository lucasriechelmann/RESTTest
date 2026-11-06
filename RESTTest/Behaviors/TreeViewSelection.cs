using System.Windows;
using System.Windows.Controls;

namespace RESTTest.Behaviors;

/// <summary>
/// Pushes <see cref="TreeView.SelectedItem"/> — which is read-only and therefore not bindable —
/// into a ViewModel property. Only the view-to-ViewModel direction is needed here, because the
/// item container style binds <c>TreeViewItem.IsSelected</c> to the node, so a selection made in
/// the ViewModel already reaches the view.
/// </summary>
public static class TreeViewSelection
{
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.RegisterAttached(
        "SelectedItem",
        typeof(object),
        typeof(TreeViewSelection),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedItemChanged));

    private static readonly DependencyProperty IsHookedProperty = DependencyProperty.RegisterAttached(
        "IsHooked",
        typeof(bool),
        typeof(TreeViewSelection),
        new PropertyMetadata(false));

    /// <summary>
    /// WPF does not select a TreeView row on right-click, which makes a context menu act on
    /// whatever was selected before. This opts the tree into selecting the row first.
    /// </summary>
    public static readonly DependencyProperty SelectOnRightClickProperty = DependencyProperty.RegisterAttached(
        "SelectOnRightClick",
        typeof(bool),
        typeof(TreeViewSelection),
        new PropertyMetadata(false, OnSelectOnRightClickChanged));

    public static void SetSelectOnRightClick(DependencyObject element, bool value) =>
        element.SetValue(SelectOnRightClickProperty, value);

    public static bool GetSelectOnRightClick(DependencyObject element) =>
        (bool)element.GetValue(SelectOnRightClickProperty);

    private static void OnSelectOnRightClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView tree)
        {
            return;
        }

        tree.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;

        if (e.NewValue is true)
        {
            tree.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
        }
    }

    private static void OnPreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;

        while (source is not null and not TreeViewItem)
        {
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        if (source is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    public static void SetSelectedItem(DependencyObject element, object? value) =>
        element.SetValue(SelectedItemProperty, value);

    public static object? GetSelectedItem(DependencyObject element) =>
        element.GetValue(SelectedItemProperty);

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView tree || (bool)tree.GetValue(IsHookedProperty))
        {
            return;
        }

        tree.SetValue(IsHookedProperty, true);
        tree.SelectedItemChanged += (sender, args) =>
        {
            if (sender is TreeView source)
            {
                SetSelectedItem(source, args.NewValue);
            }
        };
    }
}
