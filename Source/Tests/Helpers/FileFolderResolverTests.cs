using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public sealed class FileFolderResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Dictionary<string, string> NoTenants = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void EmptyIsRoot() =>
        Assert.Equal(new FileFolders("", "", false), FileFolderResolver.Resolve(null, "prod", NoTenants, Now));

    [Fact]
    public void ScopeStopsAtDate() =>
        Assert.Equal(new FileFolders("uploads/prod/2026-10-01/in", "uploads/prod", false),
            FileFolderResolver.Resolve("uploads/{env}/{date}/in", "prod", NoTenants, Now));

    [Fact]
    public void TenantPlaceholderIsScoped()
    {
        var tenants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Company-Id"] = "ACME" };

        Assert.Equal(new FileFolders("invoices/ACME/2026", "invoices/ACME", false),
            FileFolderResolver.Resolve("invoices/{x-company-id}/{year}", "prod", tenants, Now));
    }

    [Fact]
    public void UnknownPlaceholderStaysLiteral() =>
        Assert.Equal("invoices/{customer}", FileFolderResolver.Resolve("invoices/{customer}", "prod", NoTenants, Now).Upload);

    [Fact]
    public void TraversalThrows() =>
        Assert.Throws<ArgumentException>(() => FileFolderResolver.Resolve("invoices/../../etc", "prod", NoTenants, Now));

    [Fact]
    public void AbsoluteKeepsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "{env}", "{year}");
        var folders = FileFolderResolver.Resolve(root, "prod", NoTenants, Now);

        Assert.True(folders.IsAbsolute);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "prod", "2026"), folders.Upload);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "prod")), folders.Scope);
    }

    [Fact]
    public void PlaceholderNamesSkipBuiltIns() =>
        Assert.Equal(["X-Company-Id"], FileFolderResolver.PlaceholderNames("a/{env}/{X-Company-Id}/{date}"));
}
