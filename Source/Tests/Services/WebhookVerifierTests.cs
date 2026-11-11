using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using PortwayApi.Classes;
using PortwayApi.Services;
using Xunit;

namespace PortwayApi.Tests.Services;

public sealed class WebhookVerifierTests
{
    private const string Ref = "${secret:HOOK}";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static WebhookRule Rule(WebhookVerification config)
    {
        var rule = WebhookVerifier.Build(config with { Secret = config.Secret ?? Ref });
        Assert.Null(rule.Error);
        return rule;
    }

    private static HeaderDictionary Headers(params (string Name, string Value)[] headers)
    {
        var dictionary = new HeaderDictionary();
        foreach (var (name, value) in headers)
            dictionary[name] = StringValues.Concat(dictionary[name], value);
        return dictionary;
    }

    private static bool Verify(WebhookRule rule, HeaderDictionary headers, string body, string secret, DateTimeOffset? now = null) =>
        WebhookVerifier.Verify(rule, headers, Encoding.UTF8.GetBytes(body), [secret], now ?? Now).Verified;

    private static byte[] Hmac(string key, string content) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(content));

    private static string Hex(string key, string content) => Convert.ToHexStringLower(Hmac(key, content));

    [Fact]
    public void GitHub_PublishedVector()
    {
        var rule = Rule(new WebhookVerification { Scheme = "GitHub" });

        Assert.True(Verify(rule, Headers(("X-Hub-Signature-256", "sha256=757107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e17")), "Hello, World!", "It's a Secret to Everybody"));
    }

    [Fact]
    public void Slack_PublishedVector()
    {
        const string body = "token=xyzz0WbapA4vBCDEFasx0q6G&team_id=T1DC2JH3J&team_domain=testteamnow&channel_id=G8PSS9T3V&channel_name=foobar&user_id=U2CERLKJA&user_name=roadrunner&command=%2Fwebhook-collect&text=&response_url=https%3A%2F%2Fhooks.slack.com%2Fcommands%2FT1DC2JH3J%2F397700885554%2F96rGlfmibIGlgcZRskXaIFfN&trigger_id=398738663015.47445629121.803a0bc887a14d10d2c447fce8b6703c";
        var rule = Rule(new WebhookVerification { Scheme = "Slack" });
        var headers = Headers(("X-Slack-Signature", "v0=a2114d57b48eac39b9ad189dd8316235a7b4a8d21a10bd27519666489c69b503"), ("X-Slack-Request-Timestamp", "1531420618"));

        Assert.True(Verify(rule, headers, body, "8f742231b10e8888abcd99yyyzzz85a5", DateTimeOffset.FromUnixTimeSeconds(1531420618)));
    }

    private static HeaderDictionary StandardHeaders(string signature) =>
        Headers(("webhook-id", "msg_p5jXN8AQM9LWM0D4loKWxJek"), ("webhook-timestamp", "1614265330"), ("webhook-signature", signature));

    [Fact]
    public void StandardWebhooks_PublishedVector()
    {
        var rule = Rule(new WebhookVerification { Scheme = "StandardWebhooks" });

        Assert.True(WebhookVerifier.Verify(rule, StandardHeaders("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE="), Encoding.UTF8.GetBytes("""{"test": 2432232314}"""),
            ["whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw"], DateTimeOffset.FromUnixTimeSeconds(1614265330)).Verified);
    }

    [Fact]
    public void StandardWebhooks_BinaryVector()
    {
        var rule = Rule(new WebhookVerification { Scheme = "StandardWebhooks" });
        var body = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        Assert.True(WebhookVerifier.Verify(rule, StandardHeaders("v1,sEcZ042G8Crqgxbybb3y0eDxQR3TcfYsk11I1XULhSI="), body,
            ["whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw"], DateTimeOffset.FromUnixTimeSeconds(1614265330)).Verified);
    }

    [Fact]
    public void StandardWebhooks_MultipleSignatures_AnyVerifies()
    {
        var rule = Rule(new WebhookVerification { Scheme = "StandardWebhooks" });

        Assert.True(WebhookVerifier.Verify(rule, StandardHeaders("v1,AAAA v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE="), Encoding.UTF8.GetBytes("""{"test": 2432232314}"""),
            ["whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw"], DateTimeOffset.FromUnixTimeSeconds(1614265330)).Verified);
    }

    private const string Secret = "whsec_stripe-test-secret-0001";
    private const string Body = """{"id":"evt_1","type":"payment"}""";

    private static HeaderDictionary Stripe(long t, params string[] v1) =>
        Headers(("Stripe-Signature", $"t={t}," + string.Join(",", v1.Select(v => $"v1={v}"))));

    [Fact]
    public void Stripe_ComputedSignature()
    {
        var rule = Rule(new WebhookVerification { Scheme = "Stripe" });
        var t = Now.ToUnixTimeSeconds();

        Assert.True(Verify(rule, Stripe(t, Hex(Secret, $"{t}.{Body}")), Body, Secret));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void ExpiredAndFutureTimestamps_Refused(int offset)
    {
        var rule = Rule(new WebhookVerification { Scheme = "Stripe" });
        var t = Now.ToUnixTimeSeconds() + offset;

        Assert.False(Verify(rule, Stripe(t, Hex(Secret, $"{t}.{Body}")), Body, Secret));
    }

    [Fact]
    public void RepeatedStripeTimestamp_Refused()
    {
        var rule = Rule(new WebhookVerification { Scheme = "Stripe" });
        var t = Now.ToUnixTimeSeconds();

        Assert.False(Verify(rule, Headers(("Stripe-Signature", $"t={t},t={t},v1={Hex(Secret, $"{t}.{Body}")}")), Body, Secret));
    }

    [Fact]
    public void TimestampList_RotatedSecrets_AnyV1Verifies()
    {
        var rule = Rule(new WebhookVerification { Scheme = "Stripe" });
        var t = Now.ToUnixTimeSeconds();

        Assert.True(Verify(rule, Stripe(t, Hex("old-secret-0000000000", $"{t}.{Body}"), Hex(Secret, $"{t}.{Body}")), Body, Secret));
    }

    [Fact]
    public void PreviousSecret_Verifies()
    {
        var rule = Rule(new WebhookVerification { Scheme = "GitHub", PreviousSecret = "${secret:OLD}" });

        Assert.True(WebhookVerifier.Verify(rule, Headers(("X-Hub-Signature-256", "sha256=" + Hex("previous-secret-000", Body))), Encoding.UTF8.GetBytes(Body),
            [Secret, "previous-secret-000"], Now).Verified);
    }

    [Fact]
    public void TimestampList_CommaTemplate_Verifies()
    {
        var rule = Rule(new WebhookVerification { Scheme = "Hmac", Header = "X-Sig", Format = "TimestampList", Encoding = "Base64", SignedContent = "{timestamp},{body}" });
        var t = Now.ToUnixTimeSeconds();

        Assert.True(Verify(rule, Headers(("X-Sig", $"t={t},v1={Convert.ToBase64String(Hmac(Secret, $"{t},{Body}"))}")), Body, Secret));
    }

    [Theory]
    [InlineData("Shopify", "X-Shopify-Hmac-SHA256", "", true)]
    [InlineData("ShipHero", "x-shiphero-hmac-sha256", "", true)]
    [InlineData("Jira", "X-Hub-Signature", "sha256=", false)]
    [InlineData("Mollie", "X-Mollie-Signature", "sha256=", false)]
    public void Presets_ComputedSignature(string scheme, string header, string prefix, bool base64)
    {
        var rule = Rule(new WebhookVerification { Scheme = scheme });
        var digest = Hmac(Secret, Body);

        Assert.True(Verify(rule, Headers((header, prefix + (base64 ? Convert.ToBase64String(digest) : Convert.ToHexStringLower(digest)))), Body, Secret));
    }

    [Fact]
    public void OneByteBodyChange_Refused()
    {
        var rule = Rule(new WebhookVerification { Scheme = "GitHub" });

        Assert.False(Verify(rule, Headers(("X-Hub-Signature-256", "sha256=" + Hex(Secret, Body))), Body.Replace("1", "2"), Secret));
    }

    [Theory]
    [InlineData("sha256=zz07ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e17")]
    [InlineData("sha256=757107ea")]
    [InlineData("757107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e17")]
    [InlineData("sha256=757107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e1700")]
    public void NonHexWrongLengthOrMissingPrefix_Refused(string signature)
    {
        var rule = Rule(new WebhookVerification { Scheme = "GitHub" });

        Assert.False(Verify(rule, Headers(("X-Hub-Signature-256", signature)), "Hello, World!", "It's a Secret to Everybody"));
    }

    [Fact]
    public void DuplicateSignatureHeader_Refused()
    {
        var rule = Rule(new WebhookVerification { Scheme = "GitHub" });
        var signature = "sha256=" + Hex(Secret, Body);

        Assert.False(Verify(rule, Headers(("X-Hub-Signature-256", signature), ("X-Hub-Signature-256", signature)), Body, Secret));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Verification_EmptySecret_FailsClosed(string secret)
    {
        var rule = Rule(new WebhookVerification { Scheme = "GitHub" });

        Assert.False(Verify(rule, Headers(("X-Hub-Signature-256", "sha256=" + Hex(secret, Body))), Body, secret));
    }

    [Fact]
    public void Verification_ShortKey_FailsClosed()
    {
        var rule = Rule(new WebhookVerification { Scheme = "GitHub" });

        var result = WebhookVerifier.Verify(rule, Headers(("X-Hub-Signature-256", "sha256=" + Hex("short-key", Body))), Encoding.UTF8.GetBytes(Body), ["short-key"], Now);

        Assert.False(result.Verified);
        Assert.Contains("16 bytes", result.Reason);
    }

    [Theory]
    [InlineData("msg id")]
    [InlineData("msg/1")]
    [InlineData("")]
    public void InvalidIdCharacters_Refused(string id)
    {
        var rule = Rule(new WebhookVerification { Scheme = "Hmac", Header = "X-Sig", Encoding = "Hex", SignedContent = "{id}.{body}", IdHeader = "X-Id" });

        Assert.False(Verify(rule, Headers(("X-Sig", Hex(Secret, $"{id}.{Body}")), ("X-Id", id)), Body, Secret));
    }

    [Fact]
    public void SharedSecret_HeaderVerifies()
    {
        var rule = Rule(new WebhookVerification { Scheme = "SharedSecret", Header = "X-Hook-Key" });

        Assert.True(Verify(rule, Headers(("X-Hook-Key", Secret)), Body, Secret));
        Assert.False(Verify(rule, Headers(("X-Hook-Key", Secret + "x")), Body, Secret));
    }

    [Fact]
    public void SharedSecret_DifferentLength_ConstantTime()
    {
        var rule = Rule(new WebhookVerification { Scheme = "SharedSecret", Header = "X-Hook-Key" });

        Assert.False(Verify(rule, Headers(("X-Hook-Key", "s")), Body, Secret));
        Assert.False(Verify(rule, Headers(("X-Hook-Key", new string('s', 4096))), Body, Secret));
    }

    [Fact]
    public void SharedSecret_BasicVerifies()
    {
        var rule = Rule(new WebhookVerification { Scheme = "SharedSecret", Username = "erp" });
        string Basic(string user, string password) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

        Assert.True(Verify(rule, Headers(("Authorization", Basic("erp", Secret))), Body, Secret));
        Assert.False(Verify(rule, Headers(("Authorization", Basic("other", Secret))), Body, Secret));
    }

    [Fact]
    public void Hmac_LargeBody_Verifies()
    {
        var rule = Rule(new WebhookVerification { Scheme = "GitHub" });
        var body = new string('a', 10 * 1024 * 1024 - 16);

        Assert.True(Verify(rule, Headers(("X-Hub-Signature-256", "sha256=" + Hex(Secret, body))), body, Secret));
    }

    [Fact]
    public void TimestampHeaderWithoutTemplate_LoadError() =>
        Assert.NotNull(WebhookVerifier.Build(new WebhookVerification { Scheme = "Hmac", Secret = Ref, Header = "X-Sig", Encoding = "Hex", SignedContent = "{body}", TimestampHeader = "X-Ts" }).Error);

    [Fact]
    public void Hmac_RefusesUnsignedTimestamp() =>
        Assert.NotNull(WebhookVerifier.Build(new WebhookVerification { Scheme = "Hmac", Secret = Ref, Header = "X-Sig", Encoding = "Hex", SignedContent = "{timestamp}.{body}" }).Error);

    [Fact]
    public void TimestampList_WithTimestampHeader_LoadError() =>
        Assert.NotNull(WebhookVerifier.Build(new WebhookVerification { Scheme = "Hmac", Secret = Ref, Header = "X-Sig", Encoding = "Hex", Format = "TimestampList", SignedContent = "{timestamp}.{body}", TimestampHeader = "X-Ts" }).Error);

    [Theory]
    [InlineData("literal-secret")]
    [InlineData("prefix-${secret:HOOK}")]
    public void LiteralSecret_LoadError(string secret) =>
        Assert.NotNull(WebhookVerifier.Build(new WebhookVerification { Scheme = "GitHub", Secret = secret }).Error);

    [Fact]
    public void UnknownScheme_LoadError() =>
        Assert.NotNull(WebhookVerifier.Build(new WebhookVerification { Scheme = "Sha1Hmac", Secret = Ref }).Error);

    [Fact]
    public void Preset_RefusesOtherFields() =>
        Assert.NotNull(WebhookVerifier.Build(new WebhookVerification { Scheme = "GitHub", Secret = Ref, Header = "X-Other" }).Error);

    [Theory]
    [InlineData("{body}{body}")]
    [InlineData("{timestamp}")]
    [InlineData("{url}.{body}")]
    public void SignedContent_Invalid_LoadError(string template) =>
        Assert.NotNull(WebhookVerifier.Build(new WebhookVerification { Scheme = "Hmac", Secret = Ref, Header = "X-Sig", Encoding = "Hex", SignedContent = template }).Error);

    [Fact]
    public void UnsignedReplayGuard_Warns()
    {
        Assert.NotNull(Rule(new WebhookVerification { Scheme = "GitHub" }).Warning);
        Assert.Null(Rule(new WebhookVerification { Scheme = "Stripe" }).Warning);
    }

    [Fact]
    public void SharedSecretReference_AcrossIds_Warns()
    {
        var rules = PortwayApi.Classes.EndpointHandler.BuildVerification(new()
        {
            ["a"] = new WebhookVerification { Scheme = "Stripe", Secret = Ref },
            ["b"] = new WebhookVerification { Scheme = "Stripe", Secret = Ref }
        }, ["a", "b"])!;

        Assert.All(rules.Values, r => Assert.Contains("shared", r.Warning));
    }

    [Fact]
    public void VerificationIdOutsideAllowedColumns_IsError()
    {
        var rules = PortwayApi.Classes.EndpointHandler.BuildVerification(new() { ["ghost"] = new WebhookVerification { Scheme = "Stripe", Secret = Ref } }, ["a"])!;

        Assert.NotNull(rules["ghost"].Error);
    }
}
