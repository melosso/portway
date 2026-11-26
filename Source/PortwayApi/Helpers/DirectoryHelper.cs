namespace PortwayApi.Helpers;

public static class DirectoryHelper
{
    public static void EnsureDirectoryStructure()
    {
        foreach (var (type, _, _) in PortwayApi.Classes.EndpointHandler.RoutingOrder)
            Directory.CreateDirectory(PortwayApi.Classes.EndpointHandler.DirectoryOf(type));
    }

    /// <summary>
    /// Extracts namespace and endpoint name from a file path Returns (namespace, endpointName) where namespace can be null for non-namespaced endpoints
    /// </summary>
    public static (string? Namespace, string EndpointName) ExtractNamespaceAndEndpoint(string filePath, string baseDirectory)
    {
        var relativePath = Path.GetRelativePath(baseDirectory, Path.GetDirectoryName(filePath)!);
        var parts = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1)
        {
            // No namespace: endpoints/SQL/Accounts/entity.json
            return (null, parts[0]);
        }
        else if (parts.Length == 2)
        {
            // With namespace: endpoints/SQL/CRM/Accounts/entity.json
            return (parts[0], parts[1]);
        }
        else if (parts.Length > 2)
        {
            // Deeper nesting (e.g. SQL/CRM/Sales/Accounts/entity.json): all but the last segment is the namespace, the last is the endpoint
            var ns = string.Join("/", parts.Take(parts.Length - 1));
            return (ns, parts.Last());
        }

        // Fallback for edge cases
        return (null, "Unknown");
    }

    /// <summary>
    /// Validates namespace naming conventions
    /// </summary>
    public static List<string> ValidateNamespaceName(string namespaceName)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            errors.Add("Namespace name cannot be empty");
            return errors;
        }

        // Check namespace naming rules
        if (!System.Text.RegularExpressions.Regex.IsMatch(namespaceName, @"^[A-Za-z][A-Za-z0-9_]*$"))
        {
            errors.Add("Namespace must start with a letter and contain only letters, numbers, and underscores");
        }

        if (namespaceName.Length > 50)
        {
            errors.Add("Namespace cannot exceed 50 characters");
        }

        // Reserved namespace names
        var reserved = new[] { "api", "docs", "openapi", "health", "admin", "system", "composite", "webhook", "files" };
        if (reserved.Contains(namespaceName.ToLowerInvariant()))
        {
            errors.Add($"'{namespaceName}' is a reserved namespace name");
        }

        return errors;
    }

    /// <summary>
    /// Creates a new namespace directory structure for an endpoint type
    /// </summary>
    public static bool CreateNamespaceDirectory(string endpointType, string namespaceName)
    {
        var validationErrors = ValidateNamespaceName(namespaceName);
        if (validationErrors.Any())
        {
            return false;
        }

        var namespaceDir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", endpointType, namespaceName);
        if (!Directory.Exists(namespaceDir))
        {
            Directory.CreateDirectory(namespaceDir);
            return true;
        }

        return false; // Already exists
    }
}