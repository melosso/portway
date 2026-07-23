namespace PortwayApi.Classes.WebUi;

using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using PortwayApi.Auth;

/// <summary>Token creation payload for the web UI, duplicate names are rejected via async validation</summary>
public sealed class TokenCreateRequest : IAsyncValidatableObject
{
    [JsonPropertyName("username")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "username is required")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("allowed_scopes")]
    public string AllowedScopes { get; set; } = "*";

    [JsonPropertyName("allowed_environments")]
    public string AllowedEnvironments { get; set; } = "*";

    [JsonPropertyName("expires_in_days")]
    public int? ExpiresInDays { get; set; }

    [JsonPropertyName("rate_limit_requests")]
    [Range(1, int.MaxValue, ErrorMessage = "rate_limit_requests must be at least 1")]
    public int? RateLimitRequests { get; set; }

    [JsonPropertyName("rate_limit_window_seconds")]
    [Range(1, 86400, ErrorMessage = "rate_limit_window_seconds must be between 1 and 86400")]
    public int? RateLimitWindowSeconds { get; set; }

    // Sync path stays empty, the async overload owns all object level rules
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];

    public async IAsyncEnumerable<ValidationResult> ValidateAsync(
        ValidationContext validationContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var username = Username.Trim();
        if (username.Length == 0)
        {
            yield return new ValidationResult("username is required", [nameof(Username)]);
            yield break;
        }

        if (validationContext.GetService(typeof(TokenService)) is not TokenService tokenService)
            yield break;

        var active = await tokenService.GetActiveTokensAsync();
        if (active.Any(t => string.Equals(t.Username.Trim(), username, StringComparison.OrdinalIgnoreCase)))
            yield return new ValidationResult($"A token named '{username}' already exists", [nameof(Username)]);
    }
}
