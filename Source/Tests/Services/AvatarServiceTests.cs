using PortwayApi.Auth;
using PortwayApi.Services.Avatars;
using Xunit;

namespace PortwayApi.Tests.Services;

public class AvatarServiceTests
{
    private readonly AvatarService _avatars = new();

    [Fact]
    public void Styles_OfferManyStylesAndOnlyPublicDomainOnes()
    {
        Assert.True(AvatarService.Styles.Count > 20);
        Assert.Contains("shapes", AvatarService.Styles);
        Assert.Contains("lorelei", AvatarService.Styles);

        // cc by 4.0 and unlicensed styles need attribution, so they are never offered
        Assert.DoesNotContain("adventurer", AvatarService.Styles);
        Assert.DoesNotContain("personas", AvatarService.Styles);
        Assert.DoesNotContain("bottts", AvatarService.Styles);
        Assert.DoesNotContain("initials", AvatarService.Styles);
    }

    [Fact]
    public void Random_PicksAnOfferedStyleAndAValidSeed()
    {
        for (var i = 0; i < 20; i++)
        {
            var (style, seed) = AvatarService.Random();
            Assert.Contains(style, AvatarService.Styles);
            Assert.True(AvatarService.IsValidSeed(seed));
        }
    }

    [Theory]
    [InlineData("lorelei", "a1b2c3", true)]
    [InlineData("adventurer", "a1b2c3", false)]
    [InlineData("Shapes", "a1b2c3", false)]
    [InlineData("lorelei", "", false)]
    [InlineData("lorelei", "<svg onload=x>", false)]
    public void Render_OnlyForOfferedStyleAndSafeSeed(string style, string seed, bool rendered)
    {
        var uri = _avatars.Render(style, seed);
        Assert.Equal(rendered, uri is not null);
        if (rendered) Assert.StartsWith("data:image/svg+xml", uri);
    }

    [Fact]
    public void For_KeepsTheUsernameIdenticonForAccountsWithoutAChoice()
    {
        var legacy = new AdminUser { Username = "jules" };
        Assert.Equal(_avatars.Render("shapes", "jules"), _avatars.For(legacy));
    }

    [Fact]
    public void For_ReturnsNothingWhenTheAvatarWasRemoved()
    {
        Assert.Null(_avatars.For(new AdminUser { Username = "jules", AvatarStyle = "" }));
    }

    [Fact]
    public void For_RendersTheStoredChoice()
    {
        var user = new AdminUser { Username = "jules", AvatarStyle = "rings", AvatarSeed = "k3x9" };
        Assert.Equal(_avatars.Render("rings", "k3x9"), _avatars.For(user));
    }
}
