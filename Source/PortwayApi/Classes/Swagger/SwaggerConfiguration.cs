using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using PortwayApi.Classes.Swagger;
using Scalar.AspNetCore;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PortwayApi.Classes;

public class SwaggerSettings
{
    public bool Enabled { get; set; } = true;
    public string? BaseProtocol { get; set; } = "https";
    public string Title { get; set; } = "API Documentation";
    public string Version { get; set; } = "v1";
    public string Description { get; set; } = "A summary of the API documentation.";
    public ContactInfo Contact { get; set; } = new ContactInfo();
    public SecurityDefinitionInfo SecurityDefinition { get; set; } = new SecurityDefinitionInfo();
    public string DocExpansion { get; set; } = "List";
    public int DefaultModelsExpandDepth { get; set; } = -1;
    public bool DisplayRequestDuration { get; set; } = true;
    public bool EnableFilter { get; set; } = true;
    public bool EnableDeepLinking { get; set; } = true;
    public bool EnableValidator { get; set; } = true;
    public bool ForceHttpsInProduction { get; set; } = true; // Always use HTTPS in production environments

    // Scalar-specific 
    public FooterInfo Footer { get; set; } = new FooterInfo();
    public bool EnableScalar { get; set; } = true;
    public string ScalarTheme { get; set; } = "purple"; // alternate, default, moon, purple, solarized, bluePlanet, saturn, kepler, mars, deepSpace
    public string ScalarLayout { get; set; } = "modern"; // modern, classic
    public bool ScalarShowSidebar { get; set; } = true;
    public bool ScalarHideDownloadButton { get; set; } = false;
    public bool ScalarHideModels { get; set; } = true; // Hide the Models/Schemas section
    public bool ScalarHideClientButton { get; set; } = true; // Hide the client generation button
    public bool ScalarHideTestRequestButton { get; set; } = false; // Hide the test request button
}

public class ContactInfo
{
    public string Name { get; set; } = "Support";
    public string Email { get; set; } = "support@yourcompany.com";
}

public class FooterInfo
{
    public string Text { get; set; } = "Powered by Scalar";
    public string Target { get; set; } = "_blank";
    public string Url { get; set; } = "#";
    public bool ShowSourceIcon { get; set; } = true;
}

public class SecurityDefinitionInfo
{
    public string Name { get; set; } = "Bearer";
    public string Description { get; set; } = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"";
    public string In { get; set; } = "Header";
    public string Type { get; set; } = "ApiKey";
    public string Scheme { get; set; } = "Bearer";
}

public static class SwaggerConfiguration
{
    public static SwaggerSettings ConfigureSwagger(WebApplicationBuilder builder)
    {
        // Create default settings
        var swaggerSettings = new SwaggerSettings();
        
        try
        {
            // Attempt to bind from configuration
            var section = builder.Configuration.GetSection("Swagger");
            if (section.Exists())
            {
                section.Bind(swaggerSettings);
                Log.Debug("Swagger configuration loaded from appsettings.json");
            }
            else
            {
                Log.Warning("No 'Swagger' section found in configuration. Using default settings.");
            }
        }
        catch (Exception ex)
        {
            // Log error but continue with defaults
            Log.Error(ex, "Error loading Swagger configuration. Using default settings.");
        }

        // Ensure object references aren't null (defensive programming)
        swaggerSettings.Contact ??= new ContactInfo();
        swaggerSettings.Footer ??= new FooterInfo();
        swaggerSettings.SecurityDefinition ??= new SecurityDefinitionInfo();
        
        // Validate and fix critical values
        if (string.IsNullOrWhiteSpace(swaggerSettings.Title))
            swaggerSettings.Title = "PortwayAPI";
            
        if (string.IsNullOrWhiteSpace(swaggerSettings.Version))
            swaggerSettings.Version = "v1";
            
        if (string.IsNullOrWhiteSpace(swaggerSettings.SecurityDefinition.Name))
            swaggerSettings.SecurityDefinition.Name = "Bearer";
            
        if (string.IsNullOrWhiteSpace(swaggerSettings.SecurityDefinition.Scheme))
            swaggerSettings.SecurityDefinition.Scheme = "Bearer";
            
        if (string.IsNullOrWhiteSpace(swaggerSettings.ScalarTheme))
            swaggerSettings.ScalarTheme = "purple";
            
        if (string.IsNullOrWhiteSpace(swaggerSettings.ScalarLayout))
            swaggerSettings.ScalarLayout = "modern";
            
        // Register Swagger services if enabled
        if (swaggerSettings.Enabled)
        {
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc(swaggerSettings.Version, new OpenApiInfo
                {
                    Title = swaggerSettings.Title,
                    Version = swaggerSettings.Version,
                    Description = swaggerSettings.Description ?? "API Documentation",
                    Contact = new OpenApiContact
                    {
                        Name = swaggerSettings.Contact.Name,
                        Email = swaggerSettings.Contact.Email
                    }
                });
                
                // Add security definition for Bearer token
                c.AddSecurityDefinition(swaggerSettings.SecurityDefinition.Name, new OpenApiSecurityScheme
                {
                    Description = swaggerSettings.SecurityDefinition.Description,
                    Name = "Authorization",
                    In = ParseEnum<ParameterLocation>(swaggerSettings.SecurityDefinition.In, ParameterLocation.Header),
                    Type = ParseEnum<SecuritySchemeType>(swaggerSettings.SecurityDefinition.Type, SecuritySchemeType.ApiKey),
                    Scheme = swaggerSettings.SecurityDefinition.Scheme
                });
                
                // Add security requirement
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = swaggerSettings.SecurityDefinition.Name
                            }
                        },
                        new string[] { }
                    }
                });

                // Add custom schema filter for recursive types
                c.SchemaFilter<SwaggerSchemaFilter>();
                
                // Handle complex parameters in the EndpointController
                c.ParameterFilter<ComplexParameterFilter>();
                
                // Ignore controller actions to use document filters instead
                c.DocInclusionPredicate((docName, apiDesc) =>
                {
                    var controller = apiDesc.ActionDescriptor.RouteValues.TryGetValue("controller", out var ctrl) ? ctrl : "Unknown";
                    var routeTemplate = apiDesc.RelativePath?.ToLowerInvariant() ?? "Unknown";
                    
                    if (!string.IsNullOrEmpty(controller))
                    {
                        // Exclude specific controllers from appearing in documentation
                        var excludedControllers = new[] { "Endpoint", "PortwayApi", "Models", "SwaggerDocs" };
                        if (excludedControllers.Contains(controller, StringComparer.OrdinalIgnoreCase))
                        {
                            return false; // Exclude these controllers
                        }
                    }

                    // Also exclude by route patterns that shouldn't be documented
                    if (!string.IsNullOrEmpty(routeTemplate))
                    {
                        var excludedRoutePatterns = new[] { "portwayapi", "models", "swagger-docs", "/models" };
                        if (excludedRoutePatterns.Any(pattern => routeTemplate.Contains(pattern)))
                        {
                            // Exclude routes containing these patterns
                            return false;
                        }
                    }
                    
                    // Include all other controllers
                    return true; 
                });

                // Add filters (order is critical here)
                c.DocumentFilter<DynamicEndpointDocumentFilter>();
                c.DocumentFilter<CompositeEndpointDocumentFilter>();
                c.DocumentFilter<FileEndpointDocumentFilter>();
                c.DocumentFilter<SqlMetadataDocumentFilter>();
                c.DocumentFilter<TagSorterDocumentFilter>();
                c.OperationFilter<DynamicEndpointOperationFilter>();

                // Resolve conflicting actions by taking the first one
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            });

            // Register the parameter filter for complex parameters
            builder.Services.AddSingleton<ComplexParameterFilter>();

            // Register the document filters
            builder.Services.AddSingleton<DynamicEndpointDocumentFilter>();
            builder.Services.AddSingleton<CompositeEndpointDocumentFilter>();
            builder.Services.AddSingleton<DynamicEndpointOperationFilter>();
            builder.Services.AddSingleton<FileEndpointDocumentFilter>();
            builder.Services.AddSingleton<SqlMetadataDocumentFilter>();
            
            Log.Debug("Swagger services registered successfully");
        }
        else
        {
            Log.Information("Swagger is disabled in configuration");
        }
        
        return swaggerSettings;
    }

    public static void ConfigureDocs(WebApplication app, SwaggerSettings swaggerSettings)
    {
        if (!swaggerSettings.Enabled)
            return;
                
        // Configure Swagger JSON endpoint
        app.UseSwagger(options => {
            options.RouteTemplate = "docs/openapi/{documentName}/openapi.json";
            options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;

            options.PreSerializeFilters.Add((swagger, httpReq) => {
                    // Build the correct base URL including PathBase
                    string scheme = httpReq.Scheme;
                    
                    // Only force HTTPS if explicitly configured AND in production
                    bool isProduction = !app.Environment.IsDevelopment();
                    bool forceHttps = swaggerSettings.ForceHttpsInProduction && isProduction;
                    
                    // Check if running on localhost or a development machine
                    string host = httpReq.Host.HasValue ? httpReq.Host.Value : "localhost";
                    bool isLocalhost = host.Contains("localhost") || host.Contains("127.0.0.1");
                    
                    // Only force HTTPS for production domains, not localhost
                    if (forceHttps && !isLocalhost) {
                        scheme = "https";
                    }
                    
                    // Also check for standard HTTPS headers
                    if (httpReq.Headers.ContainsKey("X-Forwarded-Proto") && 
                        httpReq.Headers["X-Forwarded-Proto"] == "https") {
                        scheme = "https";
                    }
                    
                    // Build server URL with PathBase support
                    var pathBase = httpReq.PathBase.HasValue ? httpReq.PathBase.Value : "";
                    var serverUrl = $"{scheme}://{host}{pathBase}";
                    
                    swagger.Servers = new List<OpenApiServer> { 
                        new OpenApiServer { 
                            Url = serverUrl,
                            Description = "Current server"
                        } 
                    };
                    
                    Log.Debug("OpenAPI Server URL: {ServerUrl}", serverUrl);
                });
        });

        // Configure unified documentation interface at /docs using Scalar
        if (swaggerSettings.EnableScalar)
        {
            app.MapGet("/docs", (HttpContext context) => 
            {              
                // Get the base path for URL construction
                var pathBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "";
                var sidebarConfig = swaggerSettings.ScalarShowSidebar ? "true" : "false";
                
                // Debug logging
                Log.Debug("Scalar configuration: Theme={Theme}, Layout={Layout}", 
                    swaggerSettings.ScalarTheme, swaggerSettings.ScalarLayout);

                var configJson = $@"{{
                    ""theme"": ""{GetScalarThemeName(swaggerSettings.ScalarTheme)}"",
                    ""layout"": ""{GetScalarLayoutName(swaggerSettings.ScalarLayout)}"",
                    ""sidebar"": {sidebarConfig},
                    ""documentDownloadType"": ""{(swaggerSettings.ScalarHideDownloadButton ? "none" : "both")}"",
                    ""hideModels"": {(swaggerSettings.ScalarHideModels ? "true" : "false")},
                    ""hideClientButton"": {(swaggerSettings.ScalarHideClientButton ? "true" : "false")},
                    ""hideTestRequestButton"": {(swaggerSettings.ScalarHideTestRequestButton ? "true" : "false")}
                }}";
                
                string Base64Url(byte[] input)
                {
                    var s = Convert.ToBase64String(input);
                    return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
                }

                string RandomBase64UrlId(int minBytes = 8, int maxBytes = 20)
                {
                    if (minBytes < 1) minBytes = 1;
                    if (maxBytes < minBytes) maxBytes = minBytes;
                    int bytes = RandomNumberGenerator.GetInt32(minBytes, maxBytes + 1);
                    var b = new byte[bytes];
                    RandomNumberGenerator.Fill(b);
                    return Base64Url(b);
                }

                string RandomDataAttrName(int rndChars = 8)
                {
                    var b = new byte[rndChars];
                    RandomNumberGenerator.Fill(b);
                    var sb = new StringBuilder(rndChars * 2);
                    foreach (var vb in b) sb.Append(vb.ToString("x2"));
                    return string.Concat("data-", sb.ToString().AsSpan(0, Math.Min(rndChars, sb.Length)));
                }

                (string id, string attr) GenerateRandomElementIds() => 
                    (RandomBase64UrlId(minBytes: 6, maxBytes: 18), RandomDataAttrName(6));

                var (sourceId, sourceAttr) = GenerateRandomElementIds();
                var frontAttr = RandomDataAttrName(6);
                var (asyncId, asyncAttr) = GenerateRandomElementIds();
                var (openapiId, openapiAttr) = GenerateRandomElementIds();
                var (overlayId, overlayAttr) = GenerateRandomElementIds();
                
var html = $@"
<!doctype html>
<html>
<head>
    <title>{swaggerSettings.Title}</title>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <meta name=""referrer"" content=""no-referrer"">
    <link rel=""icon"" href=""favicon.ico"" type=""image/x-icon"">
    <style>
        body {{ margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }}
    </style>
</head>
<body>
    <script
        id=""api-reference""
        {frontAttr}=""""
        data-url=""{pathBase}/docs/openapi/{swaggerSettings.Version}/openapi.json""
        data-configuration='{configJson}'
        >
    </script>
    <script 
        id=""{sourceId}"" 
        {sourceAttr}=""""
        >
        (function(){{console.log(atob('JWNAbWVsb3Nzby9wb3J0d2F5JWMuIExpY2Vuc2VkIHVuZGVyICVjQUdQTCAzLjAlYy4='), atob('Y29sb3I6ICM2ZjQyYzE7IGZvbnQtd2VpZ2h0OiBib2xkOyBmb250LXNpemU6IDEycHg7'), atob('Y29sb3I6ICMzMzM7IGZvbnQtc2l6ZTogMTJweDs='), atob('Y29sb3I6ICMyOGE3NDU7IGZvbnQtd2VpZ2h0OiBib2xkOyBmb250LXNpemU6IDEycHg7'), atob('Y29sb3I6ICMzMzM7IGZvbnQtc2l6ZTogMTJ4Ow==') );}})();
    </script>
    <script 
        id=""{openapiId}"" 
        {openapiAttr}=""""
        >
        console.log(
            '%c@melosso/portway%c. OpenAPI URL: %c{pathBase}/docs/openapi/{swaggerSettings.Version}/openapi.json',
            'color: #6f42c1; font-weight: bold; font-size: 12px;',
            'color: #333; font-size: 12px;',
            'color: #dcaf34ff; font-weight: bold; font-size: 12px;'
        );
    </script>
    <script src=""https://cdn.jsdelivr.net/npm/@scalar/api-reference""></script>
    <script 
        id=""{overlayId}"" 
        {overlayAttr}=""""
        >
        window.addEventListener('load', function() {{
            // Tooltip
            function createTooltip(text) {{
                const tooltip = document.createElement('div');
                tooltip.textContent = text;
                tooltip.style.cssText = `
                    position: absolute;
                    top: 50%;
                    right: 100%;
                    transform: translateY(-50%) translateX(-5px);
                    background: rgba(0, 0, 0, 0.9);
                    color: white;
                    padding: 4px 8px;
                    border-radius: 4px;
                    font-size: 12px;
                    white-space: nowrap;
                    pointer-events: none;
                    opacity: 0;
                    transition: opacity 0.2s ease, transform 0.2s ease;
                    margin-bottom: 8px;
                    z-index: 10000;
                `;
                return tooltip;
            }}

            // Container for icons
            const iconsContainer = document.createElement('div');
            iconsContainer.style.cssText = `
                position: fixed;
                bottom: 20px;
                right: 20px;
                display: flex;
                gap: 12px;
                z-index: 9999;
            `;

            // GitHub
            const githubContainer = document.createElement('div');
            githubContainer.style.position = 'relative';
            
            const githubLink = document.createElement('a');
            githubLink.href = 'https://github.com/melosso/portway';
            githubLink.target = '_blank';
            githubLink.innerHTML = `
                <svg viewBox=""0 0 24 24"" width=""20"" height=""20"" style=""fill: #666; opacity: 0.7; transition: opacity 0.2s ease;"">
                    <path d=""M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.30.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z""/>
                </svg>
            `;
            githubLink.style.cssText = `
                background: transparent;
                border: none;
                padding: 0;
                cursor: pointer;
                text-decoration: none;
                display: block;
            `;

            const githubTooltip = createTooltip('View on GitHub');
            githubContainer.appendChild(githubLink);
            githubContainer.appendChild(githubTooltip);

            // License
            const licenseContainer = document.createElement('div');
            licenseContainer.style.position = 'relative';
            
            const licenseLink = document.createElement('a');
            licenseLink.href = 'https://github.com/melosso/portway/blob/main/LICENSE';
            licenseLink.target = '_blank';
            licenseLink.innerHTML = `
                <svg viewBox=""0 0 24 24"" width=""20"" height=""20"" style=""fill: #666; opacity: 0.7; transition: opacity 0.2s ease;"">
                    <path d=""M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1M10,17L6,13L7.41,11.59L10,14.17L16.59,7.58L18,9L10,17Z""/>
                </svg>
            `;
            licenseLink.style.cssText = `
                background: transparent;
                border: none;
                padding: 0;
                cursor: pointer;
                text-decoration: none;
                display: block;
            `;

            const licenseTooltip = createTooltip('AGPL 3.0 License');
            licenseContainer.appendChild(licenseLink);
            licenseContainer.appendChild(licenseTooltip);

            // Hover events
            githubContainer.addEventListener('mouseenter', function() {{
                githubLink.querySelector('svg').style.opacity = '1';
                githubTooltip.style.opacity = '1';
                githubTooltip.style.transform = 'translateY(0)';
            }});
            githubContainer.addEventListener('mouseleave', function() {{
                githubLink.querySelector('svg').style.opacity = '0.7';
                githubTooltip.style.opacity = '0';
                githubTooltip.style.transform = 'translateY(5px)';
            }});
            licenseContainer.addEventListener('mouseenter', function() {{
                licenseLink.querySelector('svg').style.opacity = '1';
                licenseTooltip.style.opacity = '1';
                licenseTooltip.style.transform = 'translateY(0)';
            }});
            licenseContainer.addEventListener('mouseleave', function() {{
                licenseLink.querySelector('svg').style.opacity = '0.7';
                licenseTooltip.style.opacity = '0';
                licenseTooltip.style.transform = 'translateY(5px)';
            }});

            {(swaggerSettings.Footer.ShowSourceIcon ? "// Conditionally add GitHub icon" : "")}
            {(swaggerSettings.Footer.ShowSourceIcon ? "iconsContainer.appendChild(githubContainer);" : "")}
            
            iconsContainer.appendChild(licenseContainer);
            document.body.appendChild(iconsContainer);
        }});
    </script>
    <script 
        id=""{asyncId}"" 
        {asyncAttr}=""""
        >
        const observer = new MutationObserver(() => {{
            const link = document.querySelector('a[href=""https://www.scalar.com""]');
            if (link) {{
                link.textContent = '{swaggerSettings.Footer.Text}';
                link.href = '{swaggerSettings.Footer.Url}';
                link.target = '{swaggerSettings.Footer.Target}';
                observer.disconnect();
            }}
        }});
        observer.observe(document.body, {{ childList: true, subtree: true }});
    </script>
</body>
</html>";
                return Results.Content(html, "text/html");
            });
            
            Log.Information("API Documentation is ready! Please visit /docs.");
        }
        else
        {
            // Fallback to basic Swagger UI if Scalar is disabled
            app.UseSwaggerUI(c =>
            {
                // Get basePath from appsettings.json
                var pathBase = app.Configuration["PathBase"] ?? "";
                
                c.SwaggerEndpoint($"{pathBase}/docs/openapi/{swaggerSettings.Version}/openapi.json", $"{swaggerSettings.Title} {swaggerSettings.Version}");
                c.RoutePrefix = "docs";
                
                // Apply basic settings
                c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
                c.DefaultModelsExpandDepth(swaggerSettings.DefaultModelsExpandDepth);
                
                // Enable alphabetical sorting of tags
                c.ConfigObject.AdditionalItems["tagsSorter"] = "alpha";
                
                if (swaggerSettings.DisplayRequestDuration)
                    c.DisplayRequestDuration();
            });
            
            Log.Information("API Documentation is ready! Please visit /docs (legacy)");
        }
    }

    private static string GetScalarThemeName(string theme)
    {
        return theme?.ToLowerInvariant() switch
        {
            "alternate" => "alternate",
            "default" => "default",
            "moon" => "moon",
            "purple" => "purple",
            "solarized" => "solarized",
            "blueplanet" => "bluePlanet",
            "saturn" => "saturn",
            "kepler" => "kepler",
            "mars" => "mars",
            "deepspace" => "deepSpace",
            "elysiajs" => "elysiajs",
            "fastify" => "fastify",
            "laserwave" => "laserwave",
            "none" => "none",
            _ => "purple"
        };
    }

    private static string GetScalarLayoutName(string layout)
    {
        return layout?.ToLowerInvariant() switch
        {
            "modern" => "modern",
            "classic" => "classic",
            _ => "modern"
        };
    }

    // Helper method for safely parsing enums with fallback
    public static T ParseEnum<T>(string value, T defaultValue) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<T>(value, true, out var result))
        {
            return defaultValue;
        }
        return result;
    }
}

// New filter to handle complex parameters in the EndpointController
public class ComplexParameterFilter : IParameterFilter
{
    public void Apply(OpenApiParameter parameter, ParameterFilterContext context)
    {
        if (parameter.Name == "catchall")
        {
            parameter.Description = "API endpoint path (e.g., 'endpoint/resource')";
            parameter.Required = true;
        }
        else if (parameter.Name == "env")
        {
            parameter.Description = "Target environment";
            parameter.Required = true;
        }
    }
}