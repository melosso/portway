namespace PortwayApi.Helpers;

public static class DirectoryHelper
{
    public static void EnsureDirectoryStructure()
    {
        // Ensure base endpoints directory exists
        var endpointsBaseDir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints");
        if (!Directory.Exists(endpointsBaseDir))
            Directory.CreateDirectory(endpointsBaseDir);

        // Ensure SQL endpoints directory exists
        var sqlEndpointsDir = Path.Combine(endpointsBaseDir, "SQL");
        if (!Directory.Exists(sqlEndpointsDir))
            Directory.CreateDirectory(sqlEndpointsDir);

        // Ensure Proxy endpoints directory exists
        var proxyEndpointsDir = Path.Combine(endpointsBaseDir, "Proxy");
        if (!Directory.Exists(proxyEndpointsDir))
            Directory.CreateDirectory(proxyEndpointsDir);

        // Ensure Webhook directory exists
        var webhookDir = Path.Combine(endpointsBaseDir, "Webhooks");
        if (!Directory.Exists(webhookDir))
            Directory.CreateDirectory(webhookDir);
            
        // Ensure Files directory exists
        var filesDir = Path.Combine(endpointsBaseDir, "Files");
        if (!Directory.Exists(filesDir))
            Directory.CreateDirectory(filesDir);
            
        // Ensure Static directory exists
        var staticDir = Path.Combine(endpointsBaseDir, "Static");
        if (!Directory.Exists(staticDir))
            Directory.CreateDirectory(staticDir);
    }
    
    /// <summary>
    /// Creates namespace directory structure for an endpoint type
    /// </summary>
    private static void CreateNamespaceStructure(string endpointType, string[] namespaces)
    {
        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", endpointType);
        foreach (var ns in namespaces)
        {
            var nsDir = Path.Combine(baseDir, ns);
            if (!Directory.Exists(nsDir))
                Directory.CreateDirectory(nsDir);
        }
    }
    
    /// <summary>
    /// Extracts namespace and endpoint name from a file path
    /// Returns (namespace, endpointName) where namespace can be null for non-namespaced endpoints
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
            // Handle deeper nesting: endpoints/SQL/CRM/Sales/Accounts/entity.json
            // Use all but last part as namespace, last as endpoint
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
        var reserved = new[] { "api", "docs", "swagger", "health", "admin", "system", "composite", "webhook", "files" };
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