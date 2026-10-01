using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;
using PortwayApi.Auth;
using PortwayApi.Classes;

namespace PortwayApi.Helpers;

/// <summary>
/// Resolved tenant values by header; Error and Status are set on refusal
/// </summary>
public readonly record struct TenantResolution(FrozenDictionary<string, string> Values, int Status, string? Error)
{
    public static readonly TenantResolution None = new(FrozenDictionary<string, string>.Empty, StatusCodes.Status200OK, null);

    public bool Succeeded => Error is null;
}

/// <summary>
/// Resolves tenant headers against the token's grants
/// </summary>
public static class TenantResolver
{
    public static TenantResolution Resolve(AuthToken? token, EndpointDefinition endpoint, IHeaderDictionary headers)
    {
        if (!endpoint.HasTenancy)
            return TenantResolution.None;

        if (token is null)
            return Refuse(StatusCodes.Status403Forbidden, "This endpoint requires a bearer token with tenant access");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in endpoint.Tenancy!.Keys)
        {
            if (!token.Tenants.TryGetValue(header, out var allowed))
                return Refuse(StatusCodes.Status403Forbidden, $"Token has no access to any {header} value");

            var sent = headers[header];
            string value;
            if (sent.Count == 0)
            {
                if (allowed.Count != 1 || allowed.Contains(TenantGrants.Wildcard))
                    return Refuse(StatusCodes.Status400BadRequest, $"Header {header} is required");
                value = allowed.Single();
            }
            else if (sent.Count > 1 || !TenantGrants.IsValidValue(sent[0]))
            {
                return Refuse(StatusCodes.Status400BadRequest, $"Header {header} must hold one valid value");
            }
            else if (allowed.Contains(sent[0]!) || allowed.Contains(TenantGrants.Wildcard))
            {
                value = sent[0]!;
            }
            else
            {
                return Refuse(StatusCodes.Status403Forbidden, $"Token has no access to this {header} value");
            }

            values[header] = value;
        }

        return new TenantResolution(values.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), StatusCodes.Status200OK, null);
    }

    private static TenantResolution Refuse(int status, string error) => new(FrozenDictionary<string, string>.Empty, status, error);
}
