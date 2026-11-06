using RESTTest.Models;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>Covers duplication and the inline rename state machine.</summary>
public class NodeViewModelTests
{
    [Fact]
    public void Clone_assigns_fresh_ids_throughout_the_branch()
    {
        var folder = NodeViewModel.CreateFolder("F");
        folder.Children.Add(NodeViewModel.CreateRequest("A"));

        var copy = folder.Clone("F copy");

        var originalIds = folder.SelfAndDescendants().Select(n => n.Id).ToHashSet();
        var copyIds = copy.SelfAndDescendants().Select(n => n.Id).ToHashSet();

        Assert.Equal("F copy", copy.Name);
        Assert.Equal(2, copyIds.Count);
        // Reusing an id would make the two nodes indistinguishable to the saved-selection lookup.
        Assert.Empty(originalIds.Intersect(copyIds));
    }

    [Fact]
    public void Clone_deep_copies_the_request_so_edits_do_not_leak()
    {
        var node = NodeViewModel.CreateRequest("A");
        node.Request!.Method = "POST";
        node.Request.Path = "/things";
        node.Request.Headers.Add(new KeyValueViewModel { Key = "X-One", Value = "1" });

        var copy = node.Clone("A copy");
        copy.Request!.Path = "/changed";
        copy.Request.Headers[0].Value = "changed";

        Assert.Equal("/things", node.Request.Path);
        Assert.Equal("1", node.Request.Headers[0].Value);
        Assert.Equal("POST", copy.Request.Method);
    }

    [Fact]
    public void Begin_rename_seeds_the_editor_with_the_current_name()
    {
        var node = NodeViewModel.CreateRequest("Original");

        node.BeginRenameCommand.Execute(null);

        Assert.True(node.IsRenaming);
        Assert.Equal("Original", node.RenameText);
    }

    [Fact]
    public void Commit_rename_trims_whitespace()
    {
        var node = NodeViewModel.CreateRequest("Original");
        node.BeginRenameCommand.Execute(null);
        node.RenameText = "  Renamed  ";

        node.CommitRenameCommand.Execute(null);

        Assert.Equal("Renamed", node.Name);
        Assert.False(node.IsRenaming);
    }

    [Fact]
    public void Commit_rename_ignores_a_blank_name()
    {
        var node = NodeViewModel.CreateRequest("Original");
        node.BeginRenameCommand.Execute(null);
        node.RenameText = "   ";

        node.CommitRenameCommand.Execute(null);

        // An empty label would leave an unclickable blank row in the sidebar.
        Assert.Equal("Original", node.Name);
        Assert.False(node.IsRenaming);
    }

    [Fact]
    public void Cancel_rename_discards_the_edit()
    {
        var node = NodeViewModel.CreateRequest("Original");
        node.BeginRenameCommand.Execute(null);
        node.RenameText = "Discarded";

        node.CancelRenameCommand.Execute(null);

        Assert.Equal("Original", node.Name);
        Assert.False(node.IsRenaming);
    }

    [Fact]
    public void Commit_rename_when_not_renaming_is_a_no_op()
    {
        var node = NodeViewModel.CreateRequest("Original");
        node.RenameText = "Stale";

        node.CommitRenameCommand.Execute(null);

        Assert.Equal("Original", node.Name);
    }

    [Theory]
    [InlineData("GET", "GET")]
    [InlineData("POST", "POST")]
    [InlineData("DELETE", "DEL")]
    [InlineData("OPTIONS", "OPT")]
    [InlineData("PATCH", "PTCH")]
    public void Method_label_is_abbreviated_to_fit_the_badge(string method, string expected)
    {
        var node = NodeViewModel.CreateRequest("A");
        node.Request!.Method = method;

        Assert.Equal(expected, node.MethodLabel);
    }

    [Fact]
    public void Folders_have_no_method_label_and_no_request()
    {
        var folder = NodeViewModel.CreateFolder("F");

        Assert.Equal(string.Empty, folder.MethodLabel);
        Assert.Null(folder.Request);
        Assert.True(folder.IsFolder);
        Assert.False(folder.IsRequest);
    }

    [Fact]
    public void A_request_node_round_trips_through_its_model()
    {
        var node = NodeViewModel.CreateRequest("A");
        node.Request!.Method = "PUT";
        node.Request.Auth = AuthKind.BearerToken;
        node.Request.BearerToken = "token";
        node.Request.QueryParams.Add(new KeyValueViewModel { Key = "q", Value = "1", Enabled = false });

        var restored = NodeViewModel.FromModel(node.ToModel());

        Assert.Equal("A", restored.Name);
        Assert.Equal("PUT", restored.Request!.Method);
        Assert.Equal(AuthKind.BearerToken, restored.Request.Auth);
        Assert.Equal("token", restored.Request.BearerToken);
        Assert.False(restored.Request.QueryParams[0].Enabled);
    }
}
