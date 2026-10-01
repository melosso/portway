using PortwayApi.Auth;
using PortwayApi.Classes;

namespace PortwayApi.Helpers;

/// <summary>
/// Load-time validation of an endpoint's Tenancy map
/// </summary>
public static class TenancyRules
{
    public static IReadOnlyList<string> Validate(EndpointDefinition endpoint)
    {
        if (!endpoint.HasTenancy)
            return [];

        var errors = new List<string>();
        foreach (var header in endpoint.Tenancy!.Keys.Where(h => !TenantGrants.IsValidHeader(h)))
            errors.Add($"Tenancy header '{header}' is not a valid or allowed header name");

        switch (endpoint.Type)
        {
            case EndpointType.SQL when SqlTableValuedFunctionHelper.IsTableValuedFunction(endpoint):
                var parameters = endpoint.FunctionParameters ?? [];
                foreach (var (header, parameter) in endpoint.Tenancy)
                {
                    if (!parameters.Any(p => string.Equals(p.Name, parameter, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"Tenancy header '{header}' targets '{parameter}', which is not a function parameter");
                }
                break;

            case EndpointType.SQL:
                foreach (var (header, column) in endpoint.Tenancy)
                {
                    if (!OdataExpandRelationshipValidator.Identifier().IsMatch(column ?? string.Empty))
                        errors.Add($"Tenancy header '{header}' targets '{column}', which is not a valid column name");
                }
                break;

            case EndpointType.Standard or EndpointType.Proxy when !endpoint.IsComposite:
                foreach (var (header, upstream) in endpoint.Tenancy)
                {
                    if (!TenantGrants.IsValidHeader(upstream))
                        errors.Add($"Tenancy header '{header}' targets '{upstream}', which is not a valid or allowed upstream header");
                }
                break;

            case EndpointType.Files:
                var baseDirectory = endpoint.Properties?.GetValueOrDefault("BaseDirectory")?.ToString();
                if (string.IsNullOrWhiteSpace(baseDirectory) || Path.IsPathRooted(baseDirectory))
                {
                    errors.Add("Tenancy on a file endpoint needs a relative BaseDirectory with a placeholder per tenant header");
                    break;
                }
                var placeholders = FileFolderResolver.PlaceholderNames(baseDirectory);
                foreach (var header in endpoint.Tenancy.Keys.Where(h => !placeholders.Contains(h, StringComparer.OrdinalIgnoreCase)))
                    errors.Add($"BaseDirectory has no {{{header}}} placeholder");
                foreach (var name in placeholders.Where(p => !endpoint.Tenancy.ContainsKey(p)))
                    errors.Add($"BaseDirectory placeholder {{{name}}} is not a tenancy header");
                foreach (var name in FileFolderResolver.PlaceholdersAfterTime(baseDirectory))
                    errors.Add($"BaseDirectory placeholder {{{name}}} must come before any date placeholder");
                break;

            default:
                errors.Add($"Tenancy is not supported on {(endpoint.IsComposite ? "composite" : endpoint.Type.ToString().ToLowerInvariant())} endpoints");
                break;
        }

        return errors;
    }
}
