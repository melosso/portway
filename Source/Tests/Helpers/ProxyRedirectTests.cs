using System.Net;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public sealed class ProxyRedirectTests
{
    private sealed class Upstream(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static readonly Dictionary<string, string> Secrets = new(StringComparer.OrdinalIgnoreCase) { ["ERP-KEY"] = "secret-value" };

    private static Func<string, HttpRequestMessage> Build(HttpMethod? method = null, string? body = null) => url =>
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, SecretReference.ResolveQueryValue(url, Secrets).Value);
        request.Headers.TryAddWithoutValidation("X-Api-Key", "secret-value");
        if (body is not null)
            request.Content = new StringContent(body);
        return request;
    };

    private static Task<HttpResponseMessage> Send(Upstream upstream, string url, Func<string, HttpRequestMessage>? build = null, Func<string, bool>? isUrlSafe = null) =>
        ProxyFailoverHelper.SendWithRetryAsync(new HttpClient(upstream), build ?? Build(), url, url, null, null, "test",
            isUrlSafe ?? (_ => true), TestContext.Current.CancellationToken, sameHostRedirectsOnly: true);

    [Theory]
    [InlineData("http://other.example/next")]
    [InlineData("http://api.example:8081/next")]
    public async Task SecretHeader_NotSentAfterRedirect(string location)
    {
        var upstream = new Upstream(_ => Redirect(location));

        var response = await Send(upstream, "http://api.example/start");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task SameHostRedirect_Followed()
    {
        var upstream = new Upstream(r => r.RequestUri!.AbsolutePath == "/start" ? Redirect("/next") : new HttpResponseMessage(HttpStatusCode.OK));

        var response = await Send(upstream, "http://api.example/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://api.example/next", upstream.Requests[1].RequestUri!.AbsoluteUri);
        Assert.True(upstream.Requests[1].Headers.Contains("X-Api-Key"));
    }

    [Fact]
    public async Task HttpToHttpsUpgrade_Followed()
    {
        var upstream = new Upstream(r => r.RequestUri!.Scheme == "http" ? Redirect("https://api.example/start") : new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Equal(HttpStatusCode.OK, (await Send(upstream, "http://api.example/start")).StatusCode);
    }

    [Fact]
    public async Task HttpsDowngrade_NotFollowed()
    {
        var upstream = new Upstream(_ => Redirect("http://api.example/start"));

        var response = await Send(upstream, "https://api.example/start");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task RedirectHops_Limited()
    {
        var hop = 0;
        var upstream = new Upstream(_ => Redirect($"/hop{++hop}"));

        var response = await Send(upstream, "http://api.example/start");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(ProxyFailoverHelper.MaxRedirects + 1, upstream.Requests.Count);
    }

    [Fact]
    public async Task UnsafeRedirectTarget_NotFollowed()
    {
        var upstream = new Upstream(_ => Redirect("/internal"));

        var response = await Send(upstream, "http://api.example/start", isUrlSafe: url => !url.EndsWith("/internal"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task RedirectLocation_SecretReference_IsNotResolved()
    {
        var upstream = new Upstream(r => r.RequestUri!.AbsolutePath == "/start"
            ? Redirect("/next?k=${secret:ERP-KEY}")
            : new HttpResponseMessage(HttpStatusCode.OK));

        await Send(upstream, "http://api.example/start?key=${secret:ERP-KEY}");

        Assert.Contains("secret-value", upstream.Requests[0].RequestUri!.Query);
        Assert.DoesNotContain("secret-value", upstream.Requests[1].RequestUri!.OriginalString);
    }

    [Fact]
    public async Task SeeOther_SwitchesToGetWithoutBody()
    {
        var upstream = new Upstream(r => r.Method == HttpMethod.Post ? Redirect("/done", HttpStatusCode.SeeOther) : new HttpResponseMessage(HttpStatusCode.OK));

        await Send(upstream, "http://api.example/start", Build(HttpMethod.Post, "{}"));

        Assert.Equal(HttpMethod.Get, upstream.Requests[1].Method);
        Assert.Null(upstream.Requests[1].Content);
    }

    [Fact]
    public async Task TemporaryRedirect_KeepsMethodAndBody()
    {
        var upstream = new Upstream(r => r.RequestUri!.AbsolutePath == "/start" ? Redirect("/next", HttpStatusCode.TemporaryRedirect) : new HttpResponseMessage(HttpStatusCode.OK));

        await Send(upstream, "http://api.example/start", Build(HttpMethod.Post, "{}"));

        Assert.Equal(HttpMethod.Post, upstream.Requests[1].Method);
        Assert.NotNull(upstream.Requests[1].Content);
    }

    [Theory]
    [InlineData("https://api.example/x?key=${secret:K}", true)]
    [InlineData("https://api.example/x?a=1&key=${secret:K}&b=2", true)]
    [InlineData("https://api.example/x?key=pre${secret:K}", true)]
    [InlineData("https://${secret:K}/x", false)]
    [InlineData("https://api.example/${secret:K}", false)]
    [InlineData("https://api.example/x?${secret:K}=v", false)]
    [InlineData("https://api.example/x#${secret:K}", false)]
    [InlineData("https://api.example/x?key=${secret:K", false)]
    public void Url_ReferenceOutsideQueryValue_RejectedAtLoad(string url, bool allowed)
    {
        Assert.Equal(allowed, SecretReference.IsQueryValueOnly(url));
        var errors = EndpointHandler.ValidateProxyEndpointConfiguration(new EndpointEntity { Url = "https://api.example/x", FallbackUrls = [url] });
        Assert.Equal(allowed, errors.Count == 0);
    }

    [Theory]
    [InlineData("Content-Type")]
    [InlineData("Host")]
    [InlineData("Bad Name")]
    public void EntityHeader_InvalidName_RejectedAtLoad(string name) =>
        Assert.NotEmpty(EndpointHandler.ValidateProxyEndpointConfiguration(new EndpointEntity { Url = "https://api.example/x", Headers = new() { [name] = "v" } }));

    [Fact]
    public void EntityHeader_Authorization_Allowed() =>
        Assert.Empty(EndpointHandler.ValidateProxyEndpointConfiguration(new EndpointEntity { Url = "https://api.example/x", Headers = new() { ["Authorization"] = "Bearer ${secret:K}" } }));

    [Fact]
    public void HealthProbe_DropsQueryWithReference()
    {
        Assert.Equal("https://api.example/x", PortwayApi.Services.HealthCheckService.ProbeUrl("https://api.example/x?key=${secret:K}"));
        Assert.Equal("https://api.example/x?a=1", PortwayApi.Services.HealthCheckService.ProbeUrl("https://api.example/x?a=1"));
    }

    [Fact]
    public void CompositeMask_ReplacesRawAndJsonEscapedValues()
    {
        var context = new PortwayApi.Classes.ExecutionContext();
        context.SecretValues.Add("p\"a+ss");

        var masked = context.Mask("""{"raw":"p"a+ss","escaped":"p\"a+ss","strict":"p"a+ss"}""");

        Assert.DoesNotContain("a+ss", masked);
        Assert.DoesNotContain("u002Bss", masked);
    }

    [Theory]
    [InlineData("key=evil&a=1")]
    [InlineData("%6Bey=evil&a=1")]
    [InlineData("KEY=evil&a=1")]
    [InlineData("a=1&%6b%65%79=evil")]
    public void ClientQuery_EncodedReservedName_Dropped(string query)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "key" };

        Assert.Equal("a=1", PortwayApi.Services.ProxyRequestHandler.DropReservedParams(query, reserved, new HashSet<string>()));
    }
}
