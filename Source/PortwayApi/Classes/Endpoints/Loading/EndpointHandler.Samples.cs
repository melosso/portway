namespace PortwayApi.Classes;

using System.Text.Json;
using Serilog;
using PortwayApi.Helpers;

public static partial class EndpointHandler
{
    /// <summary>Creates sample endpoint definitions if none exist</summary>
    public static void CreateSampleEndpoints(string baseDirectory)
    {
        try
        {
            // Create SQL endpoint directory
            var sqlEndpointsDir = Path.Combine(baseDirectory, "SQL");
            if (!Directory.Exists(sqlEndpointsDir))
            {
                Directory.CreateDirectory(sqlEndpointsDir);
            }

            // Create SQL sample endpoint
            CreateSampleSqlEndpoint(sqlEndpointsDir);

            // Create Proxy endpoint directory
            var proxyEndpointsDir = Path.Combine(baseDirectory, "Proxy");
            if (!Directory.Exists(proxyEndpointsDir))
            {
                Directory.CreateDirectory(proxyEndpointsDir);
            }

            // Create Proxy sample endpoint
            CreateSampleProxyEndpoint(proxyEndpointsDir);

            // Create Composite sample endpoint
            CreateSampleCompositeEndpoint(proxyEndpointsDir);

            // Create Webhook directory
            var webhookDir = Path.Combine(baseDirectory, "Webhooks");
            if (!Directory.Exists(webhookDir))
            {
                Directory.CreateDirectory(webhookDir);
            }

            // Create Webhook sample endpoint
            CreateSampleWebhookEndpoint(webhookDir);

            // Create Files endpoint directory
            var filesEndpointsDir = Path.Combine(baseDirectory, "Files");
            if (!Directory.Exists(filesEndpointsDir))
            {
                Directory.CreateDirectory(filesEndpointsDir);
            }
            // Create Files sample endpoint
            CreateSampleFileEndpoint(filesEndpointsDir);

            // Clear the cached endpoints to force a reload
            lock (_loadLock)
            {
                _loadedProxyEndpoints = null;
                _loadedSqlEndpoints = null;
            }

            Log.Information("Created sample endpoints in each endpoint directory");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating sample endpoint definitions");
        }
    }

    private static void CreateSampleSqlEndpoint(string sqlEndpointsDir)
    {
        var sampleDir = Path.Combine(sqlEndpointsDir, "Items");
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        var samplePath = Path.Combine(sampleDir, "entity.json");
        if (!File.Exists(samplePath))
        {
            var sample = new EndpointEntity
            {
                DatabaseObjectName = "Items",
                DatabaseSchema = "dbo",
                AllowedColumns = new List<string> { "ItemCode", "Description", "Price" },
                AllowedMethods = new List<string> { "GET" },
                PrimaryKey = "ItemCode"
            };

            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(samplePath, json);
            Log.Information($"Created sample SQL endpoint definition: {samplePath}");
        }
    }

    private static void CreateSampleProxyEndpoint(string proxyEndpointsDir)
    {
        var sampleDir = Path.Combine(proxyEndpointsDir, "Sample");
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        var samplePath = Path.Combine(sampleDir, "entity.json");
        if (!File.Exists(samplePath))
        {
            var sample = new ExtendedEndpointEntity
            {
                Url = "https://jsonplaceholder.typicode.com/posts",
                Methods = new List<string> { "GET", "POST" },
                Type = "Standard",
                IsPrivate = false
            };

            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(samplePath, json);
            Log.Information($"Created sample proxy endpoint definition: {samplePath}");
        }
    }

    private static void CreateSampleCompositeEndpoint(string proxyEndpointsDir)
    {
        var compositeSampleDir = Path.Combine(proxyEndpointsDir, "SampleComposite");
        if (!Directory.Exists(compositeSampleDir))
        {
            Directory.CreateDirectory(compositeSampleDir);
        }

        var compositeSamplePath = Path.Combine(compositeSampleDir, "entity.json");
        if (!File.Exists(compositeSamplePath))
        {
            var compositeSample = new ExtendedEndpointEntity
            {
                Url = "http://localhost:8020/services/Exact.Entity.REST.EG",
                Methods = new List<string> { "POST" },
                Type = "Composite",
                CompositeConfig = new CompositeDefinition
                {
                    Name = "SampleComposite",
                    Description = "Sample composite endpoint",
                    Steps = new List<CompositeStep>
                    {
                        new CompositeStep
                        {
                            Name = "Step1",
                            Endpoint = "SampleEndpoint1",
                            Method = "POST",
                            TemplateTransformations = new Dictionary<string, string>
                            {
                                { "TransactionKey", "$guid" }
                            }
                        },
                        new CompositeStep
                        {
                            Name = "Step2",
                            Endpoint = "SampleEndpoint2",
                            Method = "POST",
                            DependsOn = "Step1",
                            TemplateTransformations = new Dictionary<string, string>
                            {
                                { "TransactionKey", "$prev.Step1.TransactionKey" }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(compositeSample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(compositeSamplePath, json);
            Log.Information($"Created sample composite endpoint definition: {compositeSamplePath}");
        }
    }

    private static void CreateSampleWebhookEndpoint(string webhookDir)
    {
        var sampleDir = Path.Combine(webhookDir, "Sample");
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        var samplePath = Path.Combine(sampleDir, "entity.json");
        if (!File.Exists(samplePath))
        {
            var sample = new EndpointEntity
            {
                DatabaseObjectName = "WebhookData",
                DatabaseSchema = "dbo",
                AllowedColumns = new List<string> { "webhook1", "webhook2" }
            };

            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(samplePath, json);
            Log.Information($"Created sample webhook endpoint definition: {samplePath}");
        }
    }
    
    private static void CreateSampleFileEndpoint(string filesEndpointsDir)
    {
        var sampleDir = Path.Combine(filesEndpointsDir, "SampleFiles");
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        var samplePath = Path.Combine(sampleDir, "entity.json");
        if (!File.Exists(samplePath))
        {
            var sample = new FileEndpointEntity
            {
                StorageType = "Local",
                BaseDirectory = "sample",
                AllowedExtensions = new List<string> { ".jpg", ".png", ".pdf", ".docx", ".xlsx" },
                IsPrivate = false,
                AllowedEnvironments = new List<string> { "prod", "dev" }
            };

            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(samplePath, json);
            Log.Information($"Created sample Files endpoint definition: {samplePath}");
        }
    }
}
