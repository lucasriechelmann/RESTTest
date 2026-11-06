using RESTTest.Models;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

public class AssertionViewModelTests
{
    private static (CollectionViewModel Collection, RequestViewModel Request) BuildCollection()
    {
        var node = NodeViewModel.CreateRequest("A");
        var collection = new CollectionViewModel { Name = "Test" };
        collection.Items.Add(node);
        return (collection, node.Request!);
    }

    private static int CountDirty(CollectionViewModel collection, Action change)
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
    public void A_new_assertion_checks_for_status_200()
    {
        var assertion = new AssertionViewModel();

        Assert.Equal(AssertionTarget.StatusCode, assertion.Target);
        Assert.Equal(AssertionOperator.EqualTo, assertion.Operator);
        Assert.Equal("200", assertion.Expected);
    }

    [Fact]
    public void Adding_removing_and_editing_assertions_marks_the_collection_dirty()
    {
        var (collection, request) = BuildCollection();

        Assert.True(CountDirty(collection, () => request.AddAssertionCommand.Execute(null)) > 0);
        Assert.True(CountDirty(collection, () => request.Assertions[0].Expected = "201") > 0);
        Assert.True(CountDirty(collection, () => request.RemoveAssertionCommand.Execute(request.Assertions[0])) > 0);
    }

    [Fact]
    public void Derived_visibility_flags_are_transient_so_one_edit_is_one_dirty_event()
    {
        var (collection, request) = BuildCollection();
        request.AddAssertionCommand.Execute(null);
        var assertion = request.Assertions[0];

        // Operator is persisted (1 event). It also raises ShowsExpected, which must not count.
        Assert.Equal(1, CountDirty(collection, () => assertion.Operator = AssertionOperator.NotEqualTo));

        // Target is persisted, and here forces a persisted operator change too (2 events). The
        // option list, placeholder and ShowsSelector it raises must not add to that.
        assertion.Target = AssertionTarget.JsonPath;
        assertion.Operator = AssertionOperator.Exists;
        Assert.Equal(2, CountDirty(collection, () => assertion.Target = AssertionTarget.StatusCode));
    }

    [Fact]
    public void Changing_the_target_notifies_the_view_about_every_derived_property()
    {
        var assertion = new AssertionViewModel();
        var raised = new List<string?>();
        assertion.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        assertion.Target = AssertionTarget.Header;

        Assert.Contains(nameof(AssertionViewModel.OperatorOptions), raised);
        Assert.Contains(nameof(AssertionViewModel.ShowsSelector), raised);
        Assert.Contains(nameof(AssertionViewModel.SelectorPlaceholder), raised);
    }

    [Fact]
    public void Changing_the_target_swaps_an_operator_that_no_longer_applies()
    {
        var assertion = new AssertionViewModel
        {
            Target = AssertionTarget.JsonPath,
            Operator = AssertionOperator.Exists,
        };

        assertion.Target = AssertionTarget.StatusCode;

        Assert.Contains(assertion.Operator, assertion.OperatorOptions);
        Assert.NotEqual(AssertionOperator.Exists, assertion.Operator);
    }

    [Fact]
    public void Changing_the_target_keeps_an_operator_that_still_applies()
    {
        var assertion = new AssertionViewModel
        {
            Target = AssertionTarget.JsonPath,
            Operator = AssertionOperator.NotEqualTo,
        };

        assertion.Target = AssertionTarget.Header;

        Assert.Equal(AssertionOperator.NotEqualTo, assertion.Operator);
    }

    [Theory]
    [InlineData(AssertionTarget.StatusCode, false)]
    [InlineData(AssertionTarget.ResponseTimeMs, false)]
    [InlineData(AssertionTarget.BodyText, false)]
    [InlineData(AssertionTarget.Header, true)]
    [InlineData(AssertionTarget.JsonPath, true)]
    public void Only_header_and_json_path_targets_ask_for_a_name(AssertionTarget target, bool shows)
    {
        Assert.Equal(shows, new AssertionViewModel { Target = target }.ShowsSelector);
    }

    [Fact]
    public void The_exists_operator_hides_the_expected_value()
    {
        var assertion = new AssertionViewModel { Target = AssertionTarget.JsonPath };

        assertion.Operator = AssertionOperator.Exists;
        Assert.False(assertion.ShowsExpected);

        assertion.Operator = AssertionOperator.EqualTo;
        Assert.True(assertion.ShowsExpected);
    }

    [Fact]
    public void Numeric_targets_only_offer_numeric_comparisons()
    {
        var options = AssertionViewModel.OperatorsFor(AssertionTarget.StatusCode);

        Assert.DoesNotContain(AssertionOperator.Contains, options);
        Assert.DoesNotContain(AssertionOperator.Exists, options);
        Assert.Contains(AssertionOperator.LessThan, options);
    }

    [Fact]
    public void Assertions_survive_a_model_round_trip()
    {
        var node = NodeViewModel.CreateRequest("A");
        node.Request!.Assertions.Add(new AssertionViewModel
        {
            Enabled = false,
            Target = AssertionTarget.JsonPath,
            Selector = "$.data[0].id",
            Operator = AssertionOperator.GreaterThan,
            Expected = "10",
        });

        var restored = NodeViewModel.FromModel(node.ToModel()).Request!.Assertions;

        var assertion = Assert.Single(restored);
        Assert.False(assertion.Enabled);
        Assert.Equal(AssertionTarget.JsonPath, assertion.Target);
        Assert.Equal("$.data[0].id", assertion.Selector);
        Assert.Equal(AssertionOperator.GreaterThan, assertion.Operator);
        Assert.Equal("10", assertion.Expected);
    }

    [Fact]
    public void Duplicating_a_request_copies_its_assertions_independently()
    {
        var node = NodeViewModel.CreateRequest("A");
        node.Request!.Assertions.Add(new AssertionViewModel { Expected = "200" });

        var copy = node.Clone("A copy");
        copy.Request!.Assertions[0].Expected = "404";

        Assert.Equal("200", node.Request.Assertions[0].Expected);
    }

    [Fact]
    public void The_enabled_count_follows_the_ticks()
    {
        var (_, request) = BuildCollection();
        request.Assertions.Add(new AssertionViewModel { Enabled = true });
        request.Assertions.Add(new AssertionViewModel { Enabled = false });

        Assert.Equal(1, request.EnabledAssertionCount);
    }
}
