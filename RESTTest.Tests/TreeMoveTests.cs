using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>
/// Covers the tree surgery behind drag-and-drop. These are the operations where a mistake
/// silently relocates or detaches a whole branch, so index maths and the guard rails get
/// explicit coverage.
/// </summary>
public class TreeMoveTests
{
    private static NodeViewModel Request(string name) => NodeViewModel.CreateRequest(name);

    private static NodeViewModel Folder(string name, params NodeViewModel[] children)
    {
        var folder = NodeViewModel.CreateFolder(name);
        foreach (var child in children)
        {
            folder.Children.Add(child);
        }

        return folder;
    }

    private static CollectionViewModel Collection(params NodeViewModel[] items)
    {
        var collection = new CollectionViewModel { Name = "Test" };
        foreach (var item in items)
        {
            collection.Items.Add(item);
        }

        return collection;
    }

    private static string[] Names(IEnumerable<NodeViewModel> nodes) => [.. nodes.Select(n => n.Name)];

    [Fact]
    public void Dropping_inside_a_folder_reparents_and_expands_it()
    {
        var request = Request("A");
        var folder = Folder("F");
        var collection = Collection(request, folder);

        collection.Move(request, folder, DropPlacement.Inside);

        Assert.Equal(["F"], Names(collection.Items));
        Assert.Equal(["A"], Names(folder.Children));
        Assert.True(folder.IsExpanded);
    }

    [Fact]
    public void Dropping_before_a_sibling_inserts_above_it()
    {
        var a = Request("A");
        var collection = Collection(Request("B"), Request("C"), a);

        collection.Move(a, collection.Items[0], DropPlacement.Before);

        Assert.Equal(["A", "B", "C"], Names(collection.Items));
    }

    [Fact]
    public void Dropping_after_a_sibling_inserts_below_it()
    {
        var a = Request("A");
        var collection = Collection(a, Request("B"), Request("C"));
        var c = collection.Items.Single(n => n.Name == "C");

        collection.Move(a, c, DropPlacement.After);

        Assert.Equal(["B", "C", "A"], Names(collection.Items));
    }

    [Fact]
    public void Reordering_within_one_list_accounts_for_the_gap_left_by_the_removal()
    {
        // The classic off-by-one: the target's index must be read after the source is pulled out.
        var a = Request("A");
        var collection = Collection(a, Request("B"), Request("C"));
        var b = collection.Items.Single(n => n.Name == "B");

        collection.Move(a, b, DropPlacement.After);

        Assert.Equal(["B", "A", "C"], Names(collection.Items));
    }

    [Fact]
    public void Moving_a_node_onto_itself_changes_nothing()
    {
        var a = Request("A");
        var collection = Collection(a, Request("B"));

        collection.Move(a, a, DropPlacement.Before);

        Assert.Equal(["A", "B"], Names(collection.Items));
    }

    [Fact]
    public void A_folder_cannot_be_dropped_into_its_own_descendant()
    {
        var inner = Folder("Inner");
        var outer = Folder("Outer", inner);
        var collection = Collection(outer);

        Assert.False(collection.CanMove(outer, inner, DropPlacement.Inside));

        collection.Move(outer, inner, DropPlacement.Inside);

        // The branch must still be attached to the root, not swallowed by itself.
        Assert.Equal(["Outer"], Names(collection.Items));
        Assert.Equal(["Inner"], Names(outer.Children));
    }

    [Fact]
    public void A_request_is_not_a_valid_inside_target()
    {
        var a = Request("A");
        var b = Request("B");
        var collection = Collection(a, b);

        Assert.False(collection.CanMove(a, b, DropPlacement.Inside));
    }

    [Fact]
    public void A_null_target_appends_to_the_root()
    {
        // This is the "dropped on empty space below the tree" path.
        var nested = Request("A");
        var folder = Folder("F", nested);
        var collection = Collection(folder);

        collection.Move(nested, target: null, DropPlacement.After);

        Assert.Equal(["F", "A"], Names(collection.Items));
        Assert.Empty(folder.Children);
    }

    [Fact]
    public void Moving_out_of_a_folder_to_a_root_sibling_works()
    {
        var nested = Request("A");
        var folder = Folder("F", nested);
        var other = Request("B");
        var collection = Collection(folder, other);

        collection.Move(nested, other, DropPlacement.Before);

        Assert.Equal(["F", "A", "B"], Names(collection.Items));
        Assert.Empty(folder.Children);
    }

    [Fact]
    public void FindParent_returns_null_for_root_nodes_and_the_folder_otherwise()
    {
        var nested = Request("A");
        var folder = Folder("F", nested);
        var collection = Collection(folder);

        Assert.Null(collection.FindParent(folder));
        Assert.Same(folder, collection.FindParent(nested));
    }

    [Fact]
    public void GetInsertTarget_puts_new_items_inside_a_selected_folder()
    {
        var folder = Folder("F");
        var collection = Collection(folder);

        Assert.Same(folder.Children, collection.GetInsertTarget(folder));
        Assert.True(folder.IsExpanded);
    }

    [Fact]
    public void GetInsertTarget_puts_new_items_beside_a_selected_request()
    {
        var nested = Request("A");
        var folder = Folder("F", nested);
        var collection = Collection(folder);

        Assert.Same(folder.Children, collection.GetInsertTarget(nested));
    }

    [Fact]
    public void GetInsertTarget_falls_back_to_the_root_with_no_selection()
    {
        var collection = Collection(Request("A"));

        Assert.Same(collection.Items, collection.GetInsertTarget(selected: null));
    }

    [Fact]
    public void Remove_detaches_a_nested_node()
    {
        var nested = Request("A");
        var folder = Folder("F", nested);
        var collection = Collection(folder);

        collection.Remove(nested);

        Assert.Empty(folder.Children);
        Assert.Single(collection.AllNodes());
    }

    [Fact]
    public void AllNodes_walks_the_whole_tree_depth_first()
    {
        var collection = Collection(
            Folder("F", Request("A"), Folder("G", Request("B"))),
            Request("C"));

        Assert.Equal(["F", "A", "G", "B", "C"], Names(collection.AllNodes()));
    }
}
