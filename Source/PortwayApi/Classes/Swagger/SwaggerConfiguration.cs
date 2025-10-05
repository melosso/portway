using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public string RoutePrefix { get; set; } = "docs";
    public string DocExpansion { get; set; } = "List";
    public int DefaultModelsExpandDepth { get; set; } = -1;
    public bool DisplayRequestDuration { get; set; } = true;
    public bool EnableFilter { get; set; } = true;
    public bool EnableDeepLinking { get; set; } = true;
    public bool EnableValidator { get; set; } = true;
    public bool ForceHttpsInProduction { get; set; } = true; // Always use HTTPS in production environments
    
    // Scalar-specific settings
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
                Log.Debug("✅ Swagger configuration loaded from appsettings.json");
            }
            else
            {
                Log.Warning("⚠️ No 'Swagger' section found in configuration. Using default settings.");
            }
        }
        catch (Exception ex)
        {
            // Log error but continue with defaults
            Log.Error(ex, "❌ Error loading Swagger configuration. Using default settings.");
        }
        
        // Ensure object references aren't null (defensive programming)
        swaggerSettings.Contact ??= new ContactInfo();
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
            
        if (string.IsNullOrWhiteSpace(swaggerSettings.RoutePrefix))
            swaggerSettings.RoutePrefix = "docs";
            
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
                
                // Important fix: Handle complex parameters in the EndpointController
                c.ParameterFilter<ComplexParameterFilter>();
                
                // Important: Ignore controller actions to use document filters instead
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
                            return false; // Exclude routes containing these patterns
                        }
                    }
                    
                    return true; // Include all other controllers
                });
                
                // Add filters in the correct order
                c.DocumentFilter<DynamicEndpointDocumentFilter>();
                c.DocumentFilter<CompositeEndpointDocumentFilter>();
                c.DocumentFilter<FileEndpointDocumentFilter>();
                c.DocumentFilter<TagSorterDocumentFilter>();
                c.OperationFilter<DynamicEndpointOperationFilter>();
                
                // Add this line to resolve conflicting actions
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            });

            // Register the parameter filter for complex parameters
            builder.Services.AddSingleton<ComplexParameterFilter>();

            // Register the document filters
            builder.Services.AddSingleton<DynamicEndpointDocumentFilter>();
            builder.Services.AddSingleton<CompositeEndpointDocumentFilter>();
            builder.Services.AddSingleton<DynamicEndpointOperationFilter>();
            builder.Services.AddSingleton<FileEndpointDocumentFilter>();
            
            Log.Debug("✅ Swagger services registered successfully");
        }
        else
        {
            Log.Information("ℹ️ Swagger is disabled in configuration");
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
            options.PreSerializeFilters.Add((swagger, httpReq) => {
                // Use the actual request scheme instead of forcing a specific one
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
                    Log.Debug("🔒 Forcing HTTPS in API documentation: Environment={Env}, Host={Host}", 
                        app.Environment.EnvironmentName, host);
                }
                
                // Also check for standard HTTPS headers
                if (httpReq.Headers.ContainsKey("X-Forwarded-Proto") && 
                    httpReq.Headers["X-Forwarded-Proto"] == "https") {
                    scheme = "https";
                }
                
                swagger.Servers = new List<OpenApiServer> { 
                    new OpenApiServer { Url = $"{scheme}://{host}{httpReq.PathBase}" } 
                };
            });
        });

        // Configure unified documentation interface at /docs using Scalar
        if (swaggerSettings.EnableScalar)
        {
            app.MapGet("/docs", () => 
            {
                var sidebarConfig = swaggerSettings.ScalarShowSidebar ? "true" : "false";
                
                // Debug logging
                Log.Debug("🎨 Scalar configuration: Theme={Theme}, Layout={Layout}", 
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
                
            var html = $@"
<!doctype html>
<html>
<head>
    <title>{swaggerSettings.Title}</title>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <style>
        body {{ margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }}
    </style>
</head>
<body>
    <script
        id=""api-reference""
        data-url=""/docs/openapi/{swaggerSettings.Version}/openapi.json""
        data-configuration='{configJson}'>
    </script>
    <script
        id=""foss-source""
        >
        (function(){{console.log(atob('JWNAbWVsb3Nzby9wb3J0d2F5JWMuIExpY2Vuc2VkIHVuZGVyICVjQUdQTCAzLjAlYy4='), atob('Y29sb3I6ICM2ZjQyYzE7IGZvbnQtd2VpZ2h0OiBib2xkOyBmb250LXNpemU6IDEycHg7'), atob('Y29sb3I6ICMzMzM7IGZvbnQtc2l6ZTogMTJweDs='), atob('Y29sb3I6ICMyOGE3NDU7IGZvbnQtd2VpZ2h0OiBib2xkOyBmb250LXNpemU6IDEycHg7'), atob('Y29sb3I6ICMzMzM7IGZvbnQtc2l6ZTogMTJ4Ow==') );}})();
    </script>
    <script src=""https://cdn.jsdelivr.net/npm/@scalar/api-reference""></script>
    <script>
        window.addEventListener('load', function() {{
            // Create icons container
            const iconsContainer = document.createElement('div');
            iconsContainer.style.cssText = `
                position: fixed;
                bottom: 20px;
                right: 20px;
                z-index: 9999;
                display: flex;
                align-items: center;
                gap: 12px;
            `;

            // Function to create tooltip
            function createTooltip(text) {{
                const tooltip = document.createElement('div');
                tooltip.textContent = text;
                tooltip.style.cssText = `
                    position: absolute;
                    bottom: 30px;
                    right: 0;
                    background: rgba(0, 0, 0, 0.8);
                    color: white;
                    padding: 6px 10px;
                    border-radius: 6px;
                    font-size: 12px;
                    font-weight: 500;
                    white-space: nowrap;
                    opacity: 0;
                    pointer-events: none;
                    transform: translateY(5px);
                    transition: all 0.2s ease;
                    backdrop-filter: blur(8px);
                `;
                return tooltip;
            }}

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

            iconsContainer.appendChild(githubContainer);
            iconsContainer.appendChild(licenseContainer);
            document.body.appendChild(iconsContainer);
        }});
    </script>
</body>
</html>";
                return Results.Content(html, "text/html");
            });
            
            Log.Information("📜 Documentation configured successfully; available at /docs/");
        }
        else
        {
            // Fallback to basic Swagger UI if Scalar is disabled
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint($"/docs/openapi/{swaggerSettings.Version}/openapi.json", $"{swaggerSettings.Title} {swaggerSettings.Version}");
                c.RoutePrefix = swaggerSettings.RoutePrefix ?? "docs";
                
                // Apply basic settings
                c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
                c.DefaultModelsExpandDepth(swaggerSettings.DefaultModelsExpandDepth);
                
                // Enable alphabetical sorting of tags
                c.ConfigObject.AdditionalItems["tagsSorter"] = "alpha";
                
                if (swaggerSettings.DisplayRequestDuration)
                    c.DisplayRequestDuration();
            });
            
            Log.Information("✅ API documentation configured successfully at /docs using Swagger UI");
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