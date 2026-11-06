using RESTTest.Services;

namespace RESTTest.Tests;

public class DisplayFormatTests
{
    [Theory]
    [InlineData(0, "0 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1.00 s")]
    [InlineData(2500, "2.50 s")]
    public void Durations_switch_to_seconds_at_one_second(long ms, string expected)
    {
        Assert.Equal(expected, DisplayFormat.Duration(ms));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.00 MB")]
    public void Sizes_use_the_largest_unit_that_fits(long bytes, string expected)
    {
        Assert.Equal(expected, DisplayFormat.Size(bytes));
    }

    [Theory]
    [InlineData(200, "Success")]
    [InlineData(299, "Success")]
    [InlineData(302, "Redirect")]
    [InlineData(404, "ClientError")]
    [InlineData(503, "ServerError")]
    [InlineData(0, "Unknown")]
    [InlineData(100, "Unknown")]
    public void Status_codes_map_to_a_colour_category(int status, string expected)
    {
        Assert.Equal(expected, DisplayFormat.StatusCategory(status));
    }
}
