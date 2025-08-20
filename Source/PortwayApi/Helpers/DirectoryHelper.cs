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
    }
}