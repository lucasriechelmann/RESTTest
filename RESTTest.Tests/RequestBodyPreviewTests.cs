using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

public class RequestBodyPreviewTests
{
    [Theory]
    [InlineData(BodyKind.Json)]
    [InlineData(BodyKind.Text)]
    [InlineData(BodyKind.Xml)]
    public void Text_kinds_show_the_body_as_typed(BodyKind kind)
    {
        var request = new RequestModel { BodyKind = kind, Body = "payload" };

        Assert.Equal("payload", RequestBodyPreview.Describe(request));
    }

    [Fact]
    public void A_form_shows_one_key_value_line_per_enabled_row()
    {
        var request = new RequestModel
        {
            BodyKind = BodyKind.FormUrlEncoded,
            FormFields =
            [
                new KeyValueModel { Key = "a", Value = "1" },
                new KeyValueModel { Key = "off", Value = "x", Enabled = false },
                new KeyValueModel { Key = "b", Value = "2" },
            ],
        };

        Assert.Equal("a=1\r\nb=2".Replace("\r\n", Environment.NewLine), RequestBodyPreview.Describe(request));
    }

    [Fact]
    public void A_multipart_marks_files_with_an_at_sign()
    {
        var request = new RequestModel
        {
            BodyKind = BodyKind.Multipart,
            MultipartParts =
            [
                new MultipartPartModel { Key = "title", Value = "hi" },
                new MultipartPartModel { Key = "doc", Value = @"C:\x\a.pdf", IsFile = true },
            ],
        };

        Assert.Equal($"title=hi{Environment.NewLine}doc=@C:\\x\\a.pdf", RequestBodyPreview.Describe(request));
    }

    [Fact]
    public void None_and_a_body_kept_while_none_show_nothing()
    {
        Assert.Equal(string.Empty, RequestBodyPreview.Describe(new RequestModel { BodyKind = BodyKind.None, Body = "kept" }));
    }

    [Fact]
    public void Labels_name_the_kind_and_count_what_was_sent()
    {
        Assert.Equal("JSON body", RequestBodyPreview.Label(new RequestModel { Body = "{}" }));
        Assert.Equal("Text body", RequestBodyPreview.Label(new RequestModel { BodyKind = BodyKind.Text, Body = "x" }));
        Assert.Equal("XML body", RequestBodyPreview.Label(new RequestModel { BodyKind = BodyKind.Xml, Body = "<a/>" }));

        Assert.Equal(
            "Form body (2 fields)",
            RequestBodyPreview.Label(new RequestModel
            {
                BodyKind = BodyKind.FormUrlEncoded,
                FormFields = [new KeyValueModel { Key = "a" }, new KeyValueModel { Key = "b" }],
            }));

        Assert.Equal(
            "Multipart form (1 part)",
            RequestBodyPreview.Label(new RequestModel
            {
                BodyKind = BodyKind.Multipart,
                MultipartParts = [new MultipartPartModel { Key = "a" }],
            }));
    }

    [Fact]
    public void There_is_no_label_when_nothing_was_sent()
    {
        Assert.Equal(string.Empty, RequestBodyPreview.Label(new RequestModel()));
        Assert.Equal(string.Empty, RequestBodyPreview.Label(new RequestModel { BodyKind = BodyKind.None, Body = "kept" }));
        Assert.Equal(string.Empty, RequestBodyPreview.Label(new RequestModel { BodyKind = BodyKind.Multipart }));
    }

    [Fact]
    public void A_history_entry_describes_a_multipart_request_in_its_request_tab()
    {
        var history = new HistoryViewModel(new FakeDialogService(), []);
        var request = new RequestModel
        {
            Method = "POST",
            BodyKind = BodyKind.Multipart,
            MultipartParts = [new MultipartPartModel { Key = "doc", Value = @"C:\x\a.pdf", IsFile = true }],
        };
        var result = new HttpExchangeResult { Url = "http://t/x", Method = "POST", Completed = true, StatusCode = 200 };

        history.Record("Sample", request, result, []);

        var entry = history.Entries[0];
        Assert.True(entry.HasRequestBody);
        Assert.Equal(@"doc=@C:\x\a.pdf", entry.RequestBody);
        Assert.StartsWith("Multipart form (1 part)", entry.RequestSummary);
    }
}
