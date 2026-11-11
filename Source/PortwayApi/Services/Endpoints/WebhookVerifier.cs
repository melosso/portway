namespace PortwayApi.Services;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;
using PortwayApi.Classes;
using PortwayApi.Helpers;

public enum WebhookMode
{
    Hmac,
    SharedSecret,
    StandardWebhooks
}

/// <summary>
/// Effective verification of one webhook id. <see cref="Error"/> set means the id answers 401.
/// </summary>
public sealed record WebhookRule
{
    public required string Scheme { get; init; }
    public WebhookMode Mode { get; init; }
    public string SecretName { get; init; } = string.Empty;
    public string? PreviousSecretName { get; init; }
    public string? Header { get; init; }
    public string? Username { get; init; }
    public string Prefix { get; init; } = string.Empty;
    public bool Base64 { get; init; }
    public bool KeyBase64 { get; init; }
    public string SignedContent { get; init; } = "{body}";
    public string? TimestampHeader { get; init; }
    public string? IdHeader { get; init; }
    public bool TimestampList { get; init; }
    public int ToleranceSeconds { get; init; } = 300;
    public string? DeliveryIdHeader { get; init; }
    public string? DeliveryIdField { get; init; }
    public bool BodyHashKey { get; init; }
    public bool Deduplicate { get; init; }
    public string? Error { get; init; }
    public string? Warning { get; init; }

    /// <summary>
    /// Header that must be present before the body is read
    /// </summary>
    public string RequiredHeader => Mode switch
    {
        WebhookMode.StandardWebhooks => "webhook-signature",
        WebhookMode.SharedSecret when Username is not null => "Authorization",
        _ => Header ?? "Authorization"
    };

    /// <summary>
    /// Every header name this rule reads, for log redaction
    /// </summary>
    public IEnumerable<string> HeaderNames =>
        new[] { RequiredHeader, TimestampHeader, IdHeader, DeliveryIdHeader,
            Mode == WebhookMode.StandardWebhooks ? "webhook-id" : null, Mode == WebhookMode.StandardWebhooks ? "webhook-timestamp" : null }
        .OfType<string>();
}

public readonly record struct WebhookVerifyResult(bool Verified, string Reason)
{
    public static readonly WebhookVerifyResult Ok = new(true, "verified");

    public static WebhookVerifyResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// Builds verification rules from entity.json and verifies deliveries on raw bytes
/// </summary>
public static partial class WebhookVerifier
{
    private const int DigestLength = 32;
    private const int MinimumKeyLength = 16;

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,255}$")]
    private static partial Regex DeliveryIdPattern();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Placeholder();

    public static bool IsValidDeliveryId(string? value) => value is not null && DeliveryIdPattern().IsMatch(value);

    private static WebhookVerification? Preset(string scheme) => scheme.ToLowerInvariant() switch
    {
        "github" => new() { Header = "X-Hub-Signature-256", Prefix = "sha256=", Encoding = "Hex", SignedContent = "{body}", DeliveryIdHeader = "X-GitHub-Delivery" },
        "stripe" => new() { Header = "Stripe-Signature", Format = "TimestampList", Encoding = "Hex", SignedContent = "{timestamp}.{body}", DeliveryIdField = "id" },
        "slack" => new() { Header = "X-Slack-Signature", Prefix = "v0=", Encoding = "Hex", TimestampHeader = "X-Slack-Request-Timestamp", SignedContent = "v0:{timestamp}:{body}", DeliveryIdField = "event_id" },
        "shopify" => new() { Header = "X-Shopify-Hmac-SHA256", Encoding = "Base64", SignedContent = "{body}", DeliveryIdHeader = "X-Shopify-Webhook-Id" },
        "jira" => new() { Header = "X-Hub-Signature", Prefix = "sha256=", Encoding = "Hex", SignedContent = "{body}", DeliveryIdHeader = "X-Atlassian-Webhook-Identifier" },
        "mollie" => new() { Header = "X-Mollie-Signature", Prefix = "sha256=", Encoding = "Hex", SignedContent = "{body}", DeliveryIdField = "id" },
        "shiphero" => new() { Header = "x-shiphero-hmac-sha256", Encoding = "Base64", SignedContent = "{body}", DeliveryIdHeader = "X-Shiphero-Message-ID" },
        _ => null
    };

    /// <summary>
    /// Builds the effective rule. Invalid configuration yields a rule with <see cref="WebhookRule.Error"/>.
    /// </summary>
    public static WebhookRule Build(WebhookVerification config)
    {
        var scheme = config.Scheme ?? string.Empty;
        var errors = new List<string>();

        if (config.Unknown is { Count: > 0 } unknown)
            errors.Add($"unknown field {string.Join(", ", unknown.Keys)}");

        var secretName = SecretReference.ExactName(config.Secret);
        if (secretName is null)
            errors.Add("Secret must be a ${secret:NAME} reference");
        var previousName = config.PreviousSecret is null ? null : SecretReference.ExactName(config.PreviousSecret);
        if (config.PreviousSecret is not null && previousName is null)
            errors.Add("PreviousSecret must be a ${secret:NAME} reference");

        var fixedFormat = Preset(scheme) is not null || scheme.Equals("StandardWebhooks", StringComparison.OrdinalIgnoreCase);
        if (fixedFormat && config with { Scheme = null, Secret = null, PreviousSecret = null, Deduplicate = false, Unknown = null } != new WebhookVerification())
            errors.Add($"scheme {scheme} accepts Secret, PreviousSecret and Deduplicate only");

        var source = Preset(scheme) is { } preset ? preset with { Deduplicate = config.Deduplicate } : config;
        var rule = scheme.ToLowerInvariant() switch
        {
            "standardwebhooks" => new WebhookRule { Scheme = "StandardWebhooks", Mode = WebhookMode.StandardWebhooks, DeliveryIdHeader = "webhook-id", Deduplicate = config.Deduplicate },
            "sharedsecret" => BuildSharedSecret(source, errors),
            "hmac" => BuildHmac("Hmac", source, errors),
            _ when Preset(scheme) is not null => BuildHmac(scheme, source, errors),
            _ => Invalid(scheme, errors, $"unknown scheme '{scheme}'")
        };

        if (rule.Deduplicate && !rule.BodyHashKey && rule.DeliveryIdHeader is null && rule.DeliveryIdField is null)
            errors.Add("Deduplicate requires DeliveryIdHeader, DeliveryIdField or DedupeKey BodyHash");

        var signsReplayGuard = rule.Mode == WebhookMode.StandardWebhooks
            || rule.SignedContent.Contains("{timestamp}", StringComparison.Ordinal)
            || rule.SignedContent.Contains("{id}", StringComparison.Ordinal);

        return rule with
        {
            SecretName = secretName ?? string.Empty,
            PreviousSecretName = previousName,
            Error = errors.Count > 0 ? string.Join("; ", errors) : null,
            Warning = signsReplayGuard ? null : "signs neither a timestamp nor a delivery id; a captured delivery can be replayed"
        };
    }

    private static WebhookRule Invalid(string scheme, List<string> errors, string error)
    {
        errors.Add(error);
        return new WebhookRule { Scheme = scheme };
    }

    private static WebhookRule BuildSharedSecret(WebhookVerification config, List<string> errors)
    {
        if ((config.Header is null) == (config.Username is null))
            errors.Add("SharedSecret requires Header or Username");
        if (config.Header is not null && !HeaderPolicy.IsValidName(config.Header))
            errors.Add("Header is not a valid header name");

        return new WebhookRule
        {
            Scheme = "SharedSecret",
            Mode = WebhookMode.SharedSecret,
            Header = config.Header,
            Username = config.Username,
            SignedContent = string.Empty,
            DeliveryIdHeader = config.DeliveryIdHeader,
            DeliveryIdField = config.DeliveryIdField,
            BodyHashKey = IsBodyHash(config, errors),
            Deduplicate = config.Deduplicate
        };
    }

    private static WebhookRule BuildHmac(string scheme, WebhookVerification config, List<string> errors)
    {
        if (config.Header is null || !HeaderPolicy.IsValidName(config.Header))
            errors.Add("Header is required");

        var base64 = config.Encoding?.ToLowerInvariant() switch
        {
            "hex" => false,
            "base64" => true,
            _ => Fail(errors, "Encoding must be Hex or Base64")
        };
        var keyBase64 = (config.KeyEncoding ?? "Utf8").ToLowerInvariant() switch
        {
            "utf8" => false,
            "base64" => true,
            _ => Fail(errors, "KeyEncoding must be Utf8 or Base64")
        };
        var timestampList = (config.Format ?? "Plain").ToLowerInvariant() switch
        {
            "plain" => false,
            "timestamplist" => true,
            _ => Fail(errors, "Format must be Plain or TimestampList")
        };

        var template = config.SignedContent ?? string.Empty;
        var placeholders = Placeholder().Matches(template).Select(m => m.Value).ToList();
        if (placeholders.Count(p => p == "{body}") != 1)
            errors.Add("SignedContent must contain {body} exactly once");
        if (placeholders.Any(p => p is not ("{body}" or "{timestamp}" or "{id}")))
            errors.Add("SignedContent accepts {body}, {timestamp} and {id} only");

        var signsTimestamp = placeholders.Contains("{timestamp}");
        if (timestampList && !signsTimestamp)
            errors.Add("Format TimestampList requires {timestamp} in SignedContent");
        if (timestampList && config.TimestampHeader is not null)
            errors.Add("Format TimestampList reads the timestamp from the signature header; remove TimestampHeader");
        if (!timestampList && signsTimestamp && config.TimestampHeader is null)
            errors.Add("{timestamp} in SignedContent requires TimestampHeader");
        if (config.TimestampHeader is not null && !signsTimestamp)
            errors.Add("TimestampHeader requires {timestamp} in SignedContent");
        if (placeholders.Contains("{id}") != (config.IdHeader is not null))
            errors.Add("{id} in SignedContent and IdHeader are set together");
        if (config.ToleranceSeconds is < 60 or > 900)
            errors.Add("ToleranceSeconds must be between 60 and 900");
        if (config.DeliveryIdHeader is not null && config.DeliveryIdField is not null)
            errors.Add("set DeliveryIdHeader or DeliveryIdField, not both");

        return new WebhookRule
        {
            Scheme = scheme,
            Mode = WebhookMode.Hmac,
            Header = config.Header,
            Prefix = config.Prefix ?? string.Empty,
            Base64 = base64,
            KeyBase64 = keyBase64,
            SignedContent = template,
            TimestampHeader = config.TimestampHeader,
            IdHeader = config.IdHeader,
            TimestampList = timestampList,
            ToleranceSeconds = config.ToleranceSeconds ?? 300,
            DeliveryIdHeader = config.DeliveryIdHeader,
            DeliveryIdField = config.DeliveryIdField,
            BodyHashKey = IsBodyHash(config, errors),
            Deduplicate = config.Deduplicate
        };
    }

    private static bool IsBodyHash(WebhookVerification config, List<string> errors) => (config.DedupeKey ?? "DeliveryId").ToLowerInvariant() switch
    {
        "deliveryid" => false,
        "bodyhash" => true,
        _ => Fail(errors, "DedupeKey must be DeliveryId or BodyHash")
    };

    private static bool Fail(List<string> errors, string error)
    {
        errors.Add(error);
        return false;
    }

    /// <summary>
    /// Verifies a delivery against each secret in turn. Every comparison is constant time.
    /// </summary>
    public static WebhookVerifyResult Verify(WebhookRule rule, IHeaderDictionary headers, ReadOnlySpan<byte> body, IReadOnlyList<string> secrets, DateTimeOffset now)
    {
        if (rule.Error is not null)
            return WebhookVerifyResult.Fail("configuration error");
        if (secrets.Count == 0 || secrets.Any(string.IsNullOrWhiteSpace))
            return WebhookVerifyResult.Fail("secret missing");

        return rule.Mode switch
        {
            WebhookMode.SharedSecret => VerifySharedSecret(rule, headers, secrets),
            WebhookMode.StandardWebhooks => VerifyStandardWebhooks(rule, headers, body, secrets, now),
            _ => VerifyHmac(rule, headers, body, secrets, now)
        };
    }

    private static WebhookVerifyResult VerifySharedSecret(WebhookRule rule, IHeaderDictionary headers, IReadOnlyList<string> secrets)
    {
        if (!TrySingle(headers, rule.RequiredHeader, out var value))
            return WebhookVerifyResult.Fail("credential header missing or repeated");

        if (rule.Username is not null)
        {
            if (!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return WebhookVerifyResult.Fail("Basic credentials missing");
            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value["Basic ".Length..].Trim()));
            }
            catch (FormatException)
            {
                return WebhookVerifyResult.Fail("Basic credentials malformed");
            }
            var parts = decoded.Split(':', 2);
            if (parts.Length != 2 || !FixedTimeEqualsHashed(parts[0], rule.Username))
                return WebhookVerifyResult.Fail("credential mismatch");
            value = parts[1];
        }

        foreach (var secret in secrets)
        {
            if (Encoding.UTF8.GetByteCount(secret) < MinimumKeyLength)
                return WebhookVerifyResult.Fail("secret shorter than 16 bytes");
            if (FixedTimeEqualsHashed(value, secret))
                return WebhookVerifyResult.Ok;
        }
        return WebhookVerifyResult.Fail("credential mismatch");
    }

    private static bool FixedTimeEqualsHashed(string provided, string expected)
    {
        Span<byte> a = stackalloc byte[DigestLength];
        Span<byte> b = stackalloc byte[DigestLength];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided), a);
        SHA256.HashData(Encoding.UTF8.GetBytes(expected), b);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static WebhookVerifyResult VerifyHmac(WebhookRule rule, IHeaderDictionary headers, ReadOnlySpan<byte> body, IReadOnlyList<string> secrets, DateTimeOffset now)
    {
        if (!TrySingle(headers, rule.Header!, out var signatureHeader))
            return WebhookVerifyResult.Fail("signature header missing or repeated");

        string? timestamp = null;
        List<string> signatures;
        if (rule.TimestampList)
        {
            if (!TryParseTimestampList(signatureHeader, out timestamp, out signatures))
                return WebhookVerifyResult.Fail("signature header malformed");
        }
        else
        {
            if (!signatureHeader.StartsWith(rule.Prefix, StringComparison.Ordinal))
                return WebhookVerifyResult.Fail("signature prefix missing");
            signatures = [signatureHeader[rule.Prefix.Length..]];
            if (rule.TimestampHeader is not null && !TrySingle(headers, rule.TimestampHeader, out timestamp))
                return WebhookVerifyResult.Fail("timestamp header missing or repeated");
        }

        if (timestamp is not null && !InWindow(timestamp, rule.ToleranceSeconds, now))
            return WebhookVerifyResult.Fail("timestamp outside the tolerance window");

        string? id = null;
        if (rule.IdHeader is not null && (!TrySingle(headers, rule.IdHeader, out id) || !IsValidDeliveryId(id)))
            return WebhookVerifyResult.Fail("id header missing, repeated or invalid");

        return Match(rule.SignedContent, timestamp, id, body, secrets, rule.KeyBase64 ? DecodeBase64Key : DecodeUtf8Key, signatures, rule.Base64);
    }

    private static WebhookVerifyResult VerifyStandardWebhooks(WebhookRule rule, IHeaderDictionary headers, ReadOnlySpan<byte> body, IReadOnlyList<string> secrets, DateTimeOffset now)
    {
        if (!TrySingle(headers, "webhook-signature", out var signatureHeader)
            || !TrySingle(headers, "webhook-id", out var id)
            || !TrySingle(headers, "webhook-timestamp", out var timestamp))
            return WebhookVerifyResult.Fail("webhook headers missing or repeated");
        if (!IsValidDeliveryId(id))
            return WebhookVerifyResult.Fail("webhook-id invalid");
        if (!InWindow(timestamp, rule.ToleranceSeconds, now))
            return WebhookVerifyResult.Fail("timestamp outside the tolerance window");

        var signatures = signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.StartsWith("v1,", StringComparison.Ordinal))
            .Select(s => s[3..])
            .ToList();

        return Match("{id}.{timestamp}.{body}", timestamp, id, body, secrets,
            static secret => DecodeBase64Key(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret), signatures, base64: true);
    }

    private static WebhookVerifyResult Match(string template, string? timestamp, string? id, ReadOnlySpan<byte> body,
        IReadOnlyList<string> secrets, Func<string, byte[]?> decodeKey, List<string> signatures, bool base64)
    {
        if (signatures.Count == 0)
            return WebhookVerifyResult.Fail("no signature");

        Span<byte> expected = stackalloc byte[DigestLength];
        Span<byte> provided = stackalloc byte[DigestLength];
        foreach (var secret in secrets)
        {
            if (decodeKey(secret) is not { Length: >= MinimumKeyLength } key)
                return WebhookVerifyResult.Fail("key shorter than 16 bytes or not decodable");

            ComputeHmac(key, template, timestamp, id, body, expected);
            foreach (var signature in signatures)
            {
                if (TryDecodeDigest(signature, base64, provided) && CryptographicOperations.FixedTimeEquals(expected, provided))
                    return WebhookVerifyResult.Ok;
            }
        }
        return WebhookVerifyResult.Fail("signature mismatch");
    }

    private static void ComputeHmac(byte[] key, string template, string? timestamp, string? id, ReadOnlySpan<byte> body, Span<byte> destination)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        var last = 0;
        foreach (Match placeholder in Placeholder().Matches(template))
        {
            hmac.AppendData(Encoding.UTF8.GetBytes(template[last..placeholder.Index]));
            if (placeholder.Value == "{body}")
                hmac.AppendData(body);
            else
                hmac.AppendData(Encoding.UTF8.GetBytes(placeholder.Value == "{timestamp}" ? timestamp! : id!));
            last = placeholder.Index + placeholder.Length;
        }
        hmac.AppendData(Encoding.UTF8.GetBytes(template[last..]));
        hmac.GetHashAndReset(destination);
    }

    private static bool TryDecodeDigest(string value, bool base64, Span<byte> destination)
    {
        if (base64)
            return Convert.TryFromBase64String(value, destination, out var written) && written == DigestLength;
        return value.Length == DigestLength * 2
            && Convert.FromHexString(value, destination, out _, out var bytes) == System.Buffers.OperationStatus.Done
            && bytes == DigestLength;
    }

    private static byte[]? DecodeUtf8Key(string secret) => Encoding.UTF8.GetBytes(secret);

    private static byte[]? DecodeBase64Key(string secret)
    {
        try
        {
            return Convert.FromBase64String(secret);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool InWindow(string timestamp, int toleranceSeconds, DateTimeOffset now) =>
        timestamp.Length is > 0 and <= 12 && timestamp.All(char.IsAsciiDigit)
        && Math.Abs(now.ToUnixTimeSeconds() - long.Parse(timestamp)) <= toleranceSeconds;

    private static bool TryParseTimestampList(string header, out string? timestamp, out List<string> signatures)
    {
        timestamp = null;
        signatures = [];
        foreach (var part in header.Split(','))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length != 2)
                return false;
            if (pair[0] == "t")
            {
                if (timestamp is not null)
                    return false;
                timestamp = pair[1];
            }
            else if (pair[0] == "v1")
            {
                signatures.Add(pair[1]);
            }
        }
        return timestamp is not null;
    }

    private static bool TrySingle(IHeaderDictionary headers, string name, out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValue(name, out StringValues values) || values.Count != 1 || string.IsNullOrEmpty(values[0]))
            return false;
        value = values[0]!;
        return true;
    }
}
