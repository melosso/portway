using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

public class OidcFlowTests
{
    private static readonly OpenIdConnectConfiguration Document = new() { AuthorizationEndpoint = "https://idp.example.test/authorize" };
    private static readonly OidcProvider Provider = new() { Id = 1, Slug = "test", ClientId = "client" };
    private const string Redirect = "https://portway.example.test/_/api/auth/oidc/test/callback";

    [Fact]
    public void Begin_AtPruneThreshold_DropsExpiredFlowsAndKeepsLiveOnes()
    {
        var now = DateTime.UtcNow;
        var expired = Enumerable.Range(0, OidcFlow.PruneThreshold)
            .Select(_ => OidcFlow.Begin(Document, Provider, Redirect, 0, now - TimeSpan.FromHours(1)).State)
            .ToList();
        var live = OidcFlow.Begin(Document, Provider, Redirect, 0, now - TimeSpan.FromMinutes(5)).State;

        OidcFlow.Begin(Document, Provider, Redirect);

        Assert.DoesNotContain(expired, OidcFlow.IsPending);
        Assert.True(OidcFlow.IsPending(live));
    }
}
