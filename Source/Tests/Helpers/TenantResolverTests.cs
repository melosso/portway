using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public sealed class TenantResolverTests
{
    private static EndpointDefinition Endpoint(params string[] headers) => new()
    {
        Type = EndpointType.SQL,
        DatabaseObjectName = "Orders",
        Tenancy = headers.ToFrozenDictionary(h => h, _ => "CompanyId", StringComparer.OrdinalIgnoreCase),
    };

    private static AuthToken Token(string tenants) => new()
    {
        Username = "t",
        TokenHash = "h",
        TokenSalt = "s",
        AllowedTenants = tenants,
    };

    private static TenantResolution Resolve(string tenants, EndpointDefinition endpoint, params (string Name, string[] Values)[] headers)
    {
        var dictionary = new HeaderDictionary();
        foreach (var (name, values) in headers)
            dictionary[name] = values;
        return TenantResolver.Resolve(Token(tenants), endpoint, dictionary);
    }

    [Fact]
    public void NoTenancyIgnoresHeader()
    {
        var result = Resolve("{}", new EndpointDefinition { Type = EndpointType.SQL }, ("X-Company-Id", ["ACME"]));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void NoTokenRefused()
    {
        var result = TenantResolver.Resolve(null, Endpoint("X-Company-Id"), new HeaderDictionary());

        Assert.Equal(403, result.Status);
    }

    [Fact]
    public void TokenWithoutHeaderRefused() =>
        Assert.Equal(403, Resolve("""{"X-Client-Id":["1"]}""", Endpoint("X-Company-Id")).Status);

    [Fact]
    public void SingleValueIsDefault() =>
        Assert.Equal("ACME", Resolve("""{"X-Company-Id":["ACME"]}""", Endpoint("X-Company-Id")).Values["x-company-id"]);

    [Fact]
    public void SeveralValuesNeedHeader() =>
        Assert.Equal(400, Resolve("""{"X-Company-Id":["ACME","GLOBEX"]}""", Endpoint("X-Company-Id")).Status);

    [Fact]
    public void WildcardNeedsHeader() =>
        Assert.Equal(400, Resolve("""{"X-Company-Id":["*"]}""", Endpoint("X-Company-Id")).Status);

    [Fact]
    public void HeaderSelectsHeldValue() =>
        Assert.Equal("GLOBEX", Resolve("""{"X-Company-Id":["ACME","GLOBEX"]}""", Endpoint("X-Company-Id"), ("x-company-id", ["GLOBEX"])).Values["X-Company-Id"]);

    [Fact]
    public void HeaderCannotGrant()
    {
        var result = Resolve("""{"X-Company-Id":["ACME"]}""", Endpoint("X-Company-Id"), ("X-Company-Id", ["GLOBEX"]));

        Assert.Equal(403, result.Status);
        Assert.DoesNotContain("GLOBEX", result.Error);
    }

    [Fact]
    public void ValueIsCaseSensitive() =>
        Assert.Equal(403, Resolve("""{"X-Company-Id":["ACME"]}""", Endpoint("X-Company-Id"), ("X-Company-Id", ["acme"])).Status);

    [Fact]
    public void WildcardAcceptsValidValue() =>
        Assert.Equal("ANY", Resolve("""{"X-Company-Id":["*"]}""", Endpoint("X-Company-Id"), ("X-Company-Id", ["ANY"])).Values["X-Company-Id"]);

    [Theory]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("A,B")]
    [InlineData("a'b")]
    [InlineData("a/b")]
    [InlineData("")]
    public void InvalidValueRefused(string value) =>
        Assert.Equal(400, Resolve("""{"X-Company-Id":["*"]}""", Endpoint("X-Company-Id"), ("X-Company-Id", [value])).Status);

    [Fact]
    public void RepeatedHeaderRefused() =>
        Assert.Equal(400, Resolve("""{"X-Company-Id":["*"]}""", Endpoint("X-Company-Id"), ("X-Company-Id", ["A", "B"])).Status);

    [Fact]
    public void AllHeadersMustResolve()
    {
        var endpoint = Endpoint("X-Company-Id", "X-Client-Id");

        Assert.Equal(403, Resolve("""{"X-Company-Id":["ACME"]}""", endpoint).Status);
        var both = Resolve("""{"X-Company-Id":["ACME"],"X-Client-Id":["7","8"]}""", endpoint, ("X-Client-Id", ["8"]));
        Assert.Equal(("ACME", "8"), (both.Values["X-Company-Id"], both.Values["X-Client-Id"]));
    }

    [Fact]
    public void UnreadableGrantsGrantNothing() =>
        Assert.Equal(403, Resolve("not json", Endpoint("X-Company-Id"), ("X-Company-Id", ["ACME"])).Status);
}

public sealed class TenantGrantsTests
{
    [Theory]
    [InlineData("""{"Authorization":["a"]}""")]
    [InlineData("""{"X-Forwarded-Host":["a"]}""")]
    [InlineData("""{"Content-Type":["a"]}""")]
    [InlineData("""{"Bad Header":["a"]}""")]
    [InlineData("""{"X-Company-Id":[]}""")]
    [InlineData("""{"X-Company-Id":[".."]}""")]
    [InlineData("""{"X-Company-Id":["a"],"x-company-id":["b"]}""")]
    [InlineData("""["X-Company-Id"]""")]
    public void Rejects(string json) => Assert.False(TenantGrants.TryParse(json, out _, out _));

    [Fact]
    public void SerializeIsCanonical()
    {
        Assert.True(TenantGrants.TryParse("""{"X-Company-Id":["GLOBEX","ACME"]}""", out var grants, out _));

        Assert.Equal("""{"X-Company-Id":["ACME","GLOBEX"]}""", TenantGrants.Serialize(grants));
        Assert.Equal(TenantGrants.Empty, TenantGrants.Serialize(TenantGrants.None));
    }
}

public sealed class TenancyRulesTests
{
    private static IReadOnlyList<string> Validate(EndpointType type, Dictionary<string, string> tenancy, Action<EndpointDefinition>? configure = null)
    {
        var endpoint = new EndpointDefinition { Type = type, Tenancy = tenancy.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase) };
        configure?.Invoke(endpoint);
        return TenancyRules.Validate(endpoint);
    }

    [Fact]
    public void SqlColumnAccepted() => Assert.Empty(Validate(EndpointType.SQL, new() { ["X-Company-Id"] = "CompanyId" }));

    [Fact]
    public void SqlColumnMustBeIdentifier() => Assert.NotEmpty(Validate(EndpointType.SQL, new() { ["X-Company-Id"] = "Company Id; drop" }));

    [Fact]
    public void ProxyRenameAccepted() => Assert.Empty(Validate(EndpointType.Standard, new() { ["X-Company-Id"] = "Administratie" }));

    [Fact]
    public void ProxyTargetCannotBeAuthorization() => Assert.NotEmpty(Validate(EndpointType.Standard, new() { ["X-Company-Id"] = "Authorization" }));

    [Fact]
    public void InboundCannotBeReserved() => Assert.NotEmpty(Validate(EndpointType.SQL, new() { ["Cookie"] = "CompanyId" }));

    [Theory]
    [InlineData(EndpointType.Static)]
    [InlineData(EndpointType.Webhook)]
    [InlineData(EndpointType.Composite)]
    public void UnsupportedTypesRefused(EndpointType type) => Assert.NotEmpty(Validate(type, new() { ["X-Company-Id"] = "x" }));

    [Fact]
    public void TvfNeedsParameter()
    {
        Assert.NotEmpty(Validate(EndpointType.SQL, new() { ["X-Company-Id"] = "Company" }, e => { e.DatabaseObjectType = "TableValuedFunction"; e.FunctionParameters = []; }));
        Assert.Empty(Validate(EndpointType.SQL, new() { ["X-Company-Id"] = "Company" }, e =>
        {
            e.DatabaseObjectType = "TableValuedFunction";
            e.FunctionParameters = [new TVFParameter { Name = "Company", Source = "Header", HeaderName = "X-Company-Id", SqlType = "NVARCHAR(20)" }];
        }));
    }

    [Fact]
    public void FilesNeedPlaceholder()
    {
        static void Base(EndpointDefinition e, string dir) => e.Properties = new() { ["BaseDirectory"] = dir };

        Assert.NotEmpty(Validate(EndpointType.Files, new() { ["X-Company-Id"] = "" }, e => Base(e, "invoices")));
        Assert.NotEmpty(Validate(EndpointType.Files, new() { ["X-Company-Id"] = "" }, e => Base(e, "/srv/{X-Company-Id}")));
        Assert.NotEmpty(Validate(EndpointType.Files, new() { ["X-Company-Id"] = "" }, e => Base(e, "{X-Company-Id}/{X-Client-Id}")));
        Assert.NotEmpty(Validate(EndpointType.Files, new() { ["X-Company-Id"] = "" }, e => Base(e, "invoices/{date}/{X-Company-Id}")));
        Assert.Empty(Validate(EndpointType.Files, new() { ["X-Company-Id"] = "" }, e => Base(e, "invoices/{X-Company-Id}/{date}")));
    }
}
