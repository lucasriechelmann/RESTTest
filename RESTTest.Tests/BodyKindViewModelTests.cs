using System.Text.Json;
using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>The body type on a request: which editor shows, what is saved, and what counts as an edit.</summary>
public class BodyKindViewModelTests
{
    private static (CollectionViewModel Collection, RequestViewModel Request) Build()
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
    public void A_new_request_defaults_to_a_json_body()
    {
        var request = new RequestViewModel();

        Assert.Equal(BodyKind.Json, request.BodyMode);
        Assert.True(request.IsJsonBody);
        Assert.True(request.CanFormatBody);
    }

    [Fact]
    public void A_file_saved_before_body_types_existed_loads_as_json_with_its_body_intact()
    {
        // This is exactly what an old collection file holds: no BodyKind, no form rows.
        const string legacy = "{ \"Method\": \"POST\", \"Path\": \"/x\", \"Body\": \"{\\\"a\\\":1}\" }";

        var model = JsonSerializer.Deserialize<RequestModel>(legacy, JsonFile.Options)!;
        var request = new RequestViewModel(model);

        Assert.Equal(BodyKind.Json, request.BodyMode);
        Assert.Equal("{\"a\":1}", request.Body);
        Assert.Empty(request.FormFields);
        Assert.Empty(request.MultipartParts);
    }

    [Theory]
    [InlineData(BodyKind.Json, true, false, false, false, false, true)]
    [InlineData(BodyKind.Text, false, true, false, false, false, false)]
    [InlineData(BodyKind.Xml, false, true, false, false, false, true)]
    [InlineData(BodyKind.FormUrlEncoded, false, false, true, false, false, false)]
    [InlineData(BodyKind.Multipart, false, false, false, true, false, false)]
    [InlineData(BodyKind.None, false, false, false, false, true, false)]
    public void Each_mode_shows_exactly_its_own_editor(
        BodyKind mode, bool json, bool plain, bool form, bool multipart, bool none, bool canFormat)
    {
        var request = new RequestViewModel { BodyMode = mode };

        Assert.Equal(json, request.IsJsonBody);
        Assert.Equal(plain, request.IsPlainTextBody);
        Assert.Equal(form, request.IsFormBody);
        Assert.Equal(multipart, request.IsMultipartBody);
        Assert.Equal(none, request.IsNoBody);
        Assert.Equal(canFormat, request.CanFormatBody);
    }

    [Fact]
    public void Changing_the_mode_tells_the_view_about_every_flag()
    {
        var request = new RequestViewModel();
        var raised = new List<string?>();
        request.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        request.BodyMode = BodyKind.Multipart;

        foreach (var name in new[]
                 {
                     nameof(RequestViewModel.IsJsonBody), nameof(RequestViewModel.IsPlainTextBody),
                     nameof(RequestViewModel.IsFormBody), nameof(RequestViewModel.IsMultipartBody),
                     nameof(RequestViewModel.IsNoBody), nameof(RequestViewModel.CanFormatBody),
                 })
        {
            Assert.Contains(name, raised);
        }
    }

    [Fact]
    public void Changing_the_mode_is_one_edit_and_the_derived_flags_add_no_more()
    {
        var (collection, request) = Build();

        Assert.Equal(1, CountDirty(collection, () => request.BodyMode = BodyKind.FormUrlEncoded));
    }

    [Fact]
    public void Switching_away_keeps_the_typed_body_so_switching_back_loses_nothing()
    {
        var request = new RequestViewModel { Body = "{\"keep\":true}" };

        request.BodyMode = BodyKind.None;
        request.BodyMode = BodyKind.Json;

        Assert.Equal("{\"keep\":true}", request.Body);
    }

    [Fact]
    public void Switching_mode_clears_a_stale_format_note()
    {
        var request = new RequestViewModel { Body = "{ broken" };
        request.FormatBodyCommand.Execute(null);
        Assert.NotNull(request.BodyStatus);

        request.BodyMode = BodyKind.Text;

        Assert.Null(request.BodyStatus);
        Assert.False(request.BodyStatusIsError);
    }

    [Fact]
    public void Form_fields_and_multipart_parts_survive_a_model_round_trip()
    {
        var node = NodeViewModel.CreateRequest("A");
        var request = node.Request!;
        request.BodyMode = BodyKind.Multipart;
        request.FormFields.Add(new KeyValueViewModel { Key = "grant", Value = "x", Enabled = false });
        request.MultipartParts.Add(new MultipartPartViewModel { Key = "title", Value = "hi" });
        request.MultipartParts.Add(new MultipartPartViewModel { IsFile = true, Key = "doc", Value = @"C:\a\b.pdf" });

        var restored = NodeViewModel.FromModel(node.ToModel()).Request!;

        Assert.Equal(BodyKind.Multipart, restored.BodyMode);
        var field = Assert.Single(restored.FormFields);
        Assert.False(field.Enabled);
        Assert.Equal("grant", field.Key);
        Assert.Equal(2, restored.MultipartParts.Count);
        Assert.False(restored.MultipartParts[0].IsFile);
        Assert.True(restored.MultipartParts[1].IsFile);
        Assert.Equal(@"C:\a\b.pdf", restored.MultipartParts[1].Value);
    }

    [Fact]
    public void The_body_kind_is_written_as_a_name_in_the_json()
    {
        var model = new RequestModel { BodyKind = BodyKind.FormUrlEncoded };

        var json = JsonSerializer.Serialize(model, JsonFile.Options);

        Assert.Contains("\"BodyKind\": \"FormUrlEncoded\"", json);
    }

    [Fact]
    public void Duplicating_a_request_copies_its_form_rows_independently()
    {
        var node = NodeViewModel.CreateRequest("A");
        node.Request!.FormFields.Add(new KeyValueViewModel { Key = "k", Value = "1" });
        node.Request.MultipartParts.Add(new MultipartPartViewModel { Key = "p", Value = "1" });

        var copy = node.Clone("A copy");
        copy.Request!.FormFields[0].Value = "2";
        copy.Request.MultipartParts[0].Value = "2";

        Assert.Equal("1", node.Request.FormFields[0].Value);
        Assert.Equal("1", node.Request.MultipartParts[0].Value);
    }

    [Fact]
    public void Adding_removing_and_editing_form_rows_marks_the_collection_dirty()
    {
        var (collection, request) = Build();

        Assert.True(CountDirty(collection, () => request.AddFormFieldCommand.Execute(null)) > 0);
        Assert.True(CountDirty(collection, () => request.FormFields[0].Key = "k") > 0);
        Assert.True(CountDirty(collection, () => request.RemoveFormFieldCommand.Execute(request.FormFields[0])) > 0);
    }

    [Fact]
    public void Adding_removing_and_editing_multipart_parts_marks_the_collection_dirty()
    {
        var (collection, request) = Build();

        Assert.True(CountDirty(collection, () => request.AddMultipartPartCommand.Execute(null)) > 0);
        Assert.True(CountDirty(collection, () => request.MultipartParts[0].Value = "v") > 0);
        Assert.True(CountDirty(collection, () => request.MultipartParts[0].IsFile = true) > 0);
        Assert.True(CountDirty(collection, () => request.RemoveMultipartPartCommand.Execute(request.MultipartParts[0])) > 0);
    }

    [Fact]
    public void Turning_a_part_into_a_file_starts_its_value_afresh()
    {
        var part = new MultipartPartViewModel { Value = "some typed text" };

        part.IsFile = true;

        Assert.Equal(string.Empty, part.Value);
    }

    [Fact]
    public void Format_pretty_prints_an_xml_body_and_reports_bad_xml()
    {
        var request = new RequestViewModel { BodyMode = BodyKind.Xml, Body = "<a><b>1</b></a>" };

        request.FormatBodyCommand.Execute(null);
        Assert.Equal("Formatted.", request.BodyStatus);
        Assert.Contains("\n  <b>1</b>", request.Body.Replace("\r\n", "\n"));

        request.Body = "<a><b></a>";
        request.FormatBodyCommand.Execute(null);
        Assert.True(request.BodyStatusIsError);
        Assert.Contains("Invalid XML", request.BodyStatus);
        Assert.Equal("<a><b></a>", request.Body);
    }

    [Fact]
    public void Minify_collapses_an_xml_body()
    {
        var request = new RequestViewModel { BodyMode = BodyKind.Xml, Body = "<a>\n  <b>1</b>\n</a>" };

        request.MinifyBodyCommand.Execute(null);

        Assert.Equal("<a><b>1</b></a>", request.Body);
    }

    [Fact]
    public void Format_still_means_json_for_a_json_body()
    {
        var request = new RequestViewModel { BodyMode = BodyKind.Json, Body = "{\"a\":1}" };

        request.FormatBodyCommand.Execute(null);

        Assert.Contains("\n", request.Body);
        Assert.StartsWith("{", request.Body);
    }
}
