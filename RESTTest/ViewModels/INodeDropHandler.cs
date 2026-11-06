namespace RESTTest.ViewModels;

/// <summary>
/// Implemented by <see cref="MainViewModel"/> and consumed by the drag-and-drop behavior, so the
/// behavior never needs to know the concrete root ViewModel.
/// </summary>
public interface INodeDropHandler
{
    bool CanMoveNode(NodeViewModel source, NodeViewModel? target, DropPlacement placement);

    void MoveNode(NodeViewModel source, NodeViewModel? target, DropPlacement placement);
}
