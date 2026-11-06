using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>
/// Covers the change bubbling that drives auto-save. Two failure modes matter: an edit deep in
/// the tree that never reaches the root (silent data loss), and pure view state that does reach
/// it (rewriting the file every time the user clicks a row).
/// </summary>
public class DirtyTrackingTests
{
    private static (CollectionViewModel Collection, NodeViewModel Nested) BuildTree()
    {
        var request = NodeViewModel.CreateRequest("A");
        var folder = NodeViewModel.CreateFolder("F");
        folder.Children.Add(request);

        var collection = new CollectionViewModel { Name = "Test" };
        collection.Items.Add(folder);

        return (collection, request);
    }

    private static int CountDirtyEvents(CollectionViewModel collection, Action change)
    {
        var count = 0;
        void Handler(object? sender, EventArgs e) => count++;

        collection.Dirty += Handler;
        try
        {
            change();
        }
        finally
        {
            collection.Dirty -= Handler;
        }

        return count;
    }

    [Fact]
    public void Renaming_a_nested_node_marks_the_collection_dirty()
    {
        var (collection, nested) = BuildTree();

        Assert.True(CountDirtyEvents(collection, () => nested.Name = "Renamed") > 0);
    }

    [Fact]
    public void Editing_a_header_value_three_levels_down_marks_the_collection_dirty()
    {
        var (collection, nested) = BuildTree();
        nested.Request!.Headers.Add(new KeyValueViewModel { Key = "X-One", Value = "1" });

        Assert.True(CountDirtyEvents(collection, () => nested.Request!.Headers[0].Value = "2") > 0);
    }

    [Fact]
    public void Adding_and_removing_nodes_marks_the_collection_dirty()
    {
        var (collection, _) = BuildTree();
        var added = NodeViewModel.CreateRequest("B");

        Assert.True(CountDirtyEvents(collection, () => collection.Items.Add(added)) > 0);
        Assert.True(CountDirtyEvents(collection, () => collection.Items.Remove(added)) > 0);
    }

    [Fact]
    public void A_removed_node_no_longer_reports_changes()
    {
        var (collection, nested) = BuildTree();
        collection.Remove(nested);

        // Otherwise a detached node would keep triggering saves of a tree it is not part of.
        Assert.Equal(0, CountDirtyEvents(collection, () => nested.Name = "Orphan"));
    }

    [Fact]
    public void Changing_the_active_environment_marks_the_collection_dirty()
    {
        var (collection, _) = BuildTree();
        var environment = new EnvironmentViewModel { Name = "Local", BaseUrl = "http://localhost" };
        collection.Environments.Add(environment);

        Assert.True(CountDirtyEvents(collection, () => collection.ActiveEnvironment = environment) > 0);
    }

    [Fact]
    public void Editing_the_active_environment_url_updates_the_resolved_base_url()
    {
        var (collection, _) = BuildTree();
        var environment = new EnvironmentViewModel { Name = "Local", BaseUrl = "http://localhost:5000" };
        collection.Environments.Add(environment);
        collection.ActiveEnvironment = environment;

        var notified = false;
        collection.PropertyChanged += (_, e) =>
            notified |= e.PropertyName == nameof(CollectionViewModel.ActiveBaseUrl);

        environment.BaseUrl = "http://localhost:6000";

        Assert.True(notified);
        Assert.Equal("http://localhost:6000", collection.ActiveBaseUrl);
    }

    [Fact]
    public void Selection_state_does_not_mark_the_collection_dirty()
    {
        var (collection, nested) = BuildTree();

        Assert.Equal(0, CountDirtyEvents(collection, () => nested.IsSelected = true));
    }

    [Fact]
    public void Inline_rename_state_does_not_mark_the_collection_dirty()
    {
        var (collection, nested) = BuildTree();

        Assert.Equal(0, CountDirtyEvents(collection, () =>
        {
            nested.IsRenaming = true;
            nested.RenameText = "typing";
            nested.IsRenaming = false;
        }));
    }

    [Fact]
    public void The_format_status_message_does_not_mark_the_collection_dirty()
    {
        var (collection, nested) = BuildTree();
        nested.Request!.Body = "not json";

        Assert.Equal(0, CountDirtyEvents(collection, () => nested.Request!.FormatBodyCommand.Execute(null)));
    }

    [Fact]
    public void Folder_expansion_does_mark_the_collection_dirty_because_it_is_persisted()
    {
        var (collection, _) = BuildTree();
        var folder = collection.Items[0];

        Assert.True(CountDirtyEvents(collection, () => folder.IsExpanded = !folder.IsExpanded) > 0);
    }

    [Fact]
    public void Formatting_an_invalid_body_reports_an_error_and_keeps_the_text()
    {
        var (_, nested) = BuildTree();
        nested.Request!.Body = "{ oops";

        nested.Request.FormatBodyCommand.Execute(null);

        Assert.True(nested.Request.BodyStatusIsError);
        Assert.NotNull(nested.Request.BodyStatus);
        Assert.Equal("{ oops", nested.Request.Body);
    }

    [Fact]
    public void Tab_counts_ignore_disabled_and_unnamed_rows()
    {
        var (_, nested) = BuildTree();
        var request = nested.Request!;

        request.Headers.Add(new KeyValueViewModel { Enabled = true, Key = "X-One" });
        request.Headers.Add(new KeyValueViewModel { Enabled = false, Key = "X-Two" });
        request.Headers.Add(new KeyValueViewModel { Enabled = true, Key = "  " });

        Assert.Equal(1, request.EnabledHeaderCount);
    }
}
