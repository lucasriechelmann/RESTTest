using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RESTTest.ViewModels;

namespace RESTTest.Behaviors;

/// <summary>
/// Attached behavior that turns the sidebar tree into a drag-and-drop surface. It owns only the
/// visual concerns — hit-testing, the insertion indicator, hover-to-expand — and delegates the
/// actual move to an <see cref="INodeDropHandler"/>, which keeps the tree surgery in the ViewModel.
/// </summary>
public static class TreeViewDragDrop
{
    /// <summary>Private clipboard format; the payload never leaves the process.</summary>
    private const string NodeFormat = "RESTTest.NodeViewModel";

    /// <summary>Name the row container must carry in the item template for hit-testing to work.</summary>
    private const string RowElementName = "NodeRow";

    public static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler",
        typeof(INodeDropHandler),
        typeof(TreeViewDragDrop),
        new PropertyMetadata(null, OnHandlerChanged));

    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller",
        typeof(Controller),
        typeof(TreeViewDragDrop),
        new PropertyMetadata(null));

    public static void SetHandler(DependencyObject element, INodeDropHandler? value) =>
        element.SetValue(HandlerProperty, value);

    public static INodeDropHandler? GetHandler(DependencyObject element) =>
        (INodeDropHandler?)element.GetValue(HandlerProperty);

    private static void OnHandlerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView tree)
        {
            return;
        }

        if (tree.GetValue(ControllerProperty) is Controller existing)
        {
            existing.Detach();
            tree.SetValue(ControllerProperty, null);
        }

        if (e.NewValue is INodeDropHandler handler)
        {
            tree.SetValue(ControllerProperty, new Controller(tree, handler));
        }
    }

    /// <summary>Per-TreeView state: drag origin, the live adorner, and the hover-expand timer.</summary>
    private sealed class Controller
    {
        private readonly TreeView _tree;
        private readonly INodeDropHandler _handler;
        private readonly DispatcherTimer _expandTimer;

        private Point _origin;
        private NodeViewModel? _pendingDrag;
        private DropIndicatorAdorner? _adorner;
        private FrameworkElement? _adornedRow;
        private NodeViewModel? _hoverFolder;

        public Controller(TreeView tree, INodeDropHandler handler)
        {
            _tree = tree;
            _handler = handler;

            _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _expandTimer.Tick += OnExpandTimerTick;

            _tree.AllowDrop = true;
            _tree.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            _tree.PreviewMouseMove += OnPreviewMouseMove;
            _tree.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            _tree.DragOver += OnDragOver;
            _tree.DragLeave += OnDragLeave;
            _tree.Drop += OnDrop;
        }

        public void Detach()
        {
            _expandTimer.Stop();
            _expandTimer.Tick -= OnExpandTimerTick;

            _tree.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            _tree.PreviewMouseMove -= OnPreviewMouseMove;
            _tree.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            _tree.DragOver -= OnDragOver;
            _tree.DragLeave -= OnDragLeave;
            _tree.Drop -= OnDrop;

            RemoveAdorner();
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Clicking the expander chevron or typing in the inline rename box must not start a drag.
            if (FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject) is not null
                || FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
            {
                _pendingDrag = null;
                return;
            }

            _origin = e.GetPosition(_tree);
            _pendingDrag = ResolveRow(e.OriginalSource as DependencyObject)?.DataContext as NodeViewModel;
        }

        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pendingDrag = null;

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pendingDrag is null || e.LeftButton != MouseButtonState.Pressed || _pendingDrag.IsRenaming)
            {
                return;
            }

            var current = e.GetPosition(_tree);

            // Respect the system drag threshold so a sloppy click is not read as a drag.
            if (Math.Abs(current.X - _origin.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(current.Y - _origin.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var source = _pendingDrag;
            _pendingDrag = null;

            var data = new DataObject(NodeFormat, source);
            DragDrop.DoDragDrop(_tree, data, DragDropEffects.Move);

            RemoveAdorner();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (e.Data.GetData(NodeFormat) is not NodeViewModel source)
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            var row = FindRowUnderPointer(e);
            var target = row?.DataContext as NodeViewModel;
            var placement = ResolvePlacement(row, target, e);

            if (!_handler.CanMoveNode(source, target, placement))
            {
                e.Effects = DragDropEffects.None;
                RemoveAdorner();
                ResetHover();
                return;
            }

            e.Effects = DragDropEffects.Move;
            ShowAdorner(row, placement);
            UpdateHover(target, placement);
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            RemoveAdorner();
            ResetHover();
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            RemoveAdorner();
            ResetHover();

            if (e.Data.GetData(NodeFormat) is not NodeViewModel source)
            {
                return;
            }

            var row = FindRowUnderPointer(e);
            var target = row?.DataContext as NodeViewModel;
            var placement = ResolvePlacement(row, target, e);

            if (_handler.CanMoveNode(source, target, placement))
            {
                _handler.MoveNode(source, target, placement);
            }
        }

        /// <summary>
        /// Top and bottom thirds of a row mean "reorder here"; the middle means "put it in this
        /// folder". Requests have no inside, so their middle band falls back to a reorder.
        /// </summary>
        private static DropPlacement ResolvePlacement(FrameworkElement? row, NodeViewModel? target, DragEventArgs e)
        {
            if (row is null || target is null || row.ActualHeight <= 0)
            {
                // Empty space below the tree: append at the root.
                return DropPlacement.After;
            }

            // Clamped because the pointer may sit in the container's padding, just outside the
            // row element whose bounds define the before/inside/after bands.
            var ratio = Math.Clamp(e.GetPosition(row).Y / row.ActualHeight, 0, 1);

            if (target.IsFolder)
            {
                return ratio switch
                {
                    < 0.3 => DropPlacement.Before,
                    > 0.7 => DropPlacement.After,
                    _ => DropPlacement.Inside,
                };
            }

            return ratio < 0.5 ? DropPlacement.Before : DropPlacement.After;
        }

        private FrameworkElement? FindRowUnderPointer(DragEventArgs e)
        {
            var point = e.GetPosition(_tree);
            var hit = _tree.InputHitTest(point) as DependencyObject;
            return ResolveRow(hit);
        }

        private void ShowAdorner(FrameworkElement? row, DropPlacement placement)
        {
            if (row is null)
            {
                RemoveAdorner();
                return;
            }

            if (!ReferenceEquals(row, _adornedRow))
            {
                RemoveAdorner();

                var layer = AdornerLayer.GetAdornerLayer(row);
                if (layer is null)
                {
                    return;
                }

                _adorner = new DropIndicatorAdorner(row);
                layer.Add(_adorner);
                _adornedRow = row;
            }

            if (_adorner is not null)
            {
                _adorner.Placement = placement;
            }
        }

        private void RemoveAdorner()
        {
            if (_adorner is not null && _adornedRow is not null)
            {
                AdornerLayer.GetAdornerLayer(_adornedRow)?.Remove(_adorner);
            }

            _adorner = null;
            _adornedRow = null;
        }

        /// <summary>Hovering over a collapsed folder opens it, so nested drops do not need two passes.</summary>
        private void UpdateHover(NodeViewModel? target, DropPlacement placement)
        {
            var candidate = placement == DropPlacement.Inside && target is { IsFolder: true, IsExpanded: false }
                ? target
                : null;

            if (ReferenceEquals(candidate, _hoverFolder))
            {
                return;
            }

            _hoverFolder = candidate;
            _expandTimer.Stop();

            if (_hoverFolder is not null)
            {
                _expandTimer.Start();
            }
        }

        private void ResetHover()
        {
            _expandTimer.Stop();
            _hoverFolder = null;
        }

        private void OnExpandTimerTick(object? sender, EventArgs e)
        {
            _expandTimer.Stop();

            if (_hoverFolder is not null)
            {
                _hoverFolder.IsExpanded = true;
            }
        }
    }

    /// <summary>
    /// Resolves the row element for a hit-tested object.
    /// <para>
    /// The row is a named element from the item template rather than the TreeViewItem itself,
    /// because an expanded TreeViewItem's bounds include all of its children, which would make
    /// the before/inside/after maths meaningless.
    /// </para>
    /// <para>
    /// The pointer does not always land on that element though: the container contributes its own
    /// padding and indent, and a hit there would otherwise look like "no row at all" and silently
    /// become a drop at the root. So when the upward walk finds a TreeViewItem first, this
    /// descends back into that item to locate the row it owns.
    /// </para>
    /// </summary>
    private static FrameworkElement? ResolveRow(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Name: RowElementName } element
                && element.DataContext is NodeViewModel)
            {
                return element;
            }

            if (source is TreeViewItem item)
            {
                return FindOwnRow(item);
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    /// <summary>
    /// Finds the row belonging to <paramref name="root"/> itself, stopping at nested
    /// TreeViewItems so a parent never picks up one of its children's rows.
    /// </summary>
    private static FrameworkElement? FindOwnRow(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is TreeViewItem)
            {
                continue;
            }

            if (child is FrameworkElement { Name: RowElementName } element
                && element.DataContext is NodeViewModel)
            {
                return element;
            }

            if (FindOwnRow(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }
}
