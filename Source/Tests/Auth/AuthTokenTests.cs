using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

public class AuthTokenTests
{
    private static AuthToken Token(string scopes = "*", string envs = "*") =>
        new() { Username = "test", TokenHash = "h", TokenSalt = "s", AllowedScopes = scopes, AllowedEnvironments = envs };

    // HasAccessToEndpoint

    [Fact]
    public void HasAccessToEndpoint_UniversalScope_ReturnsTrue()
        => Assert.True(Token("*").HasAccessToEndpoint("Products"));

    [Fact]
    public void HasAccessToEndpoint_ExactMatch_ReturnsTrue()
        => Assert.True(Token("Products,Customers").HasAccessToEndpoint("Products"));

    [Fact]
    public void HasAccessToEndpoint_CaseInsensitiveExact_ReturnsTrue()
        => Assert.True(Token("products").HasAccessToEndpoint("Products"));

    [Fact]
    public void HasAccessToEndpoint_NoMatch_ReturnsFalse()
        => Assert.False(Token("Customers").HasAccessToEndpoint("Products"));

    [Fact]
    public void HasAccessToEndpoint_WildcardPrefix_MatchesCorrectly()
        => Assert.True(Token("Products*").HasAccessToEndpoint("ProductsDetails"));

    [Fact]
    public void HasAccessToEndpoint_WildcardPrefix_DoesNotMatchUnrelated()
        => Assert.False(Token("Products*").HasAccessToEndpoint("Customers"));

    [Fact]
    public void HasAccessToEndpoint_WildcardPrefix_MatchesPrefixItself()
        => Assert.True(Token("Products*").HasAccessToEndpoint("Products"));

    [Fact]
    public void HasAccessToEndpoint_WildcardStar_InScopeList_GrantsAccess()
        => Assert.True(Token("Customers,*").HasAccessToEndpoint("AnyEndpoint"));

    // HasAccessToEnvironment

    [Fact]
    public void HasAccessToEnvironment_UniversalEnv_ReturnsTrue()
        => Assert.True(Token(envs: "*").HasAccessToEnvironment("Production"));

    [Fact]
    public void HasAccessToEnvironment_ExactMatch_ReturnsTrue()
        => Assert.True(Token(envs: "500,700").HasAccessToEnvironment("500"));

    [Fact]
    public void HasAccessToEnvironment_CaseInsensitiveExact_ReturnsTrue()
        => Assert.True(Token(envs: "production").HasAccessToEnvironment("Production"));

    [Fact]
    public void HasAccessToEnvironment_NoMatch_ReturnsFalse()
        => Assert.False(Token(envs: "700").HasAccessToEnvironment("500"));

    [Fact]
    public void HasAccessToEnvironment_WildcardPrefix_MatchesCorrectly()
        => Assert.True(Token(envs: "5*").HasAccessToEnvironment("500"));

    [Fact]
    public void HasAccessToEnvironment_WildcardPrefix_DoesNotMatchUnrelated()
        => Assert.False(Token(envs: "5*").HasAccessToEnvironment("700"));

    [Fact]
    public void HasAccessToEnvironment_WildcardStar_InEnvList_GrantsAccess()
        => Assert.True(Token(envs: "700,*").HasAccessToEnvironment("AnyEnv"));
}
