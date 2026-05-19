using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class ContentTypeHelperTests
{
    [Theory]
    [InlineData("image.jpg", "image/jpeg")]
    [InlineData("image.JPG", "image/jpeg")]
    [InlineData("archive.ZIP", "application/zip")]
    [InlineData("doc.PDF", "application/pdf")]
    [InlineData("page.html", "text/html")]
    [InlineData("script.JS", "text/javascript")]
    [InlineData("data.JSON", "application/json")]
    [InlineData("unknown.xyz", "application/octet-stream")]
    public void GetContentType_KnownExtensions_ReturnCorrectMimeType(string filename, string expected)
    {
        Assert.Equal(expected, ContentTypeHelper.GetContentType(filename));
    }

    [Fact]
    public void StaticFileExtensions_ContainsCommonExtensions()
    {
        var map = ContentTypeHelper.StaticFileExtensions;
        Assert.True(map.ContainsKey(".jpg"));
        Assert.True(map.ContainsKey(".png"));
        Assert.True(map.ContainsKey(".pdf"));
        Assert.True(map.ContainsKey(".json"));
        Assert.True(map.ContainsKey(".html"));
        Assert.True(map.ContainsKey(".woff2"));
    }

    [Fact]
    public void StaticFileExtensions_CaseInsensitiveLookup()
    {
        var map = ContentTypeHelper.StaticFileExtensions;
        Assert.True(map.TryGetValue(".JPG", out var val));
        Assert.Equal("image/jpeg", val);
    }

    [Fact]
    public void GetCacheDuration_Html_FiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ContentTypeHelper.GetCacheDuration(".html"));
    }

    [Fact]
    public void GetCacheDuration_Woff2_SevenDays()
    {
        Assert.Equal(TimeSpan.FromDays(7), ContentTypeHelper.GetCacheDuration(".woff2"));
    }

    [Fact]
    public void GetCacheDuration_Unknown_ThirtyMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), ContentTypeHelper.GetCacheDuration(".unknown"));
    }
}
