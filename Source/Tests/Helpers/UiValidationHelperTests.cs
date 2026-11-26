using Microsoft.Extensions.DependencyInjection;
using Moq;
using PortwayApi.Auth;
using PortwayApi.Classes.WebUi;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class UiValidationHelperTests
{
    private static IServiceProvider BuildServices(params string[] existingUsernames) =>
        BuildServicesWith(existingUsernames.Select(u => new AuthToken { Username = u, TokenHash = "hash", TokenSalt = "salt" }).ToArray());

    private static IServiceProvider BuildServicesWith(params AuthToken[] tokens)
    {
        var tokenService = new Mock<TokenService>((AuthDbContext)null!, (ITokenVerificationCache)null!);
        tokenService.Setup(s => s.GetAllTokensAsync()).ReturnsAsync(tokens.ToList());

        return new ServiceCollection()
            .AddSingleton(tokenService.Object)
            .BuildServiceProvider();
    }

    [Fact]
    public async Task MissingUsername_ReturnsRequiredError()
    {
        var request = new TokenCreateRequest { Username = "  " };

        var error = await UiValidationHelper.FirstErrorAsync(request, BuildServices(), CancellationToken.None);

        Assert.Equal("username is required", error);
    }

    [Fact]
    public async Task DuplicateUsername_ReturnsConflictError()
    {
        var request = new TokenCreateRequest { Username = "Existing" };

        var error = await UiValidationHelper.FirstErrorAsync(request, BuildServices("existing"), CancellationToken.None);

        Assert.Equal("A token named 'Existing' already exists", error);
    }

    [Fact]
    public async Task UniqueUsername_ReturnsNull()
    {
        var request = new TokenCreateRequest { Username = "fresh" };

        var error = await UiValidationHelper.FirstErrorAsync(request, BuildServices("other"), CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task DuplicateCheck_IgnoresSurroundingWhitespace()
    {
        var request = new TokenCreateRequest { Username = " existing " };

        var error = await UiValidationHelper.FirstErrorAsync(request, BuildServices("Existing"), CancellationToken.None);

        Assert.EndsWith("already exists", error);
    }

    [Fact]
    public async Task DuplicateCheck_IncludesArchivedTokens()
    {
        var archived = new AuthToken { Username = "old", TokenHash = "hash", TokenSalt = "salt", RevokedAt = DateTime.UtcNow };
        var request = new TokenCreateRequest { Username = "Old" };

        var error = await UiValidationHelper.FirstErrorAsync(request, BuildServicesWith(archived), CancellationToken.None);

        Assert.Equal("A token named 'Old' already exists", error);
    }
}
