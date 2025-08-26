using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.Extensions.Logging;

namespace PortwayApi.Classes.Swagger;

public class AlphabeticalEndpointSorter : IDocumentFilter
{
    private readonly ILogger<AlphabeticalEndpointSorter> _logger;

    public AlphabeticalEndpointSorter(ILogger<AlphabeticalEndpointSorter> logger)
    {
        _logger = logger;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        _logger.LogInformation("🔥 AlphabeticalEndpointSorter is RUNNING!");
        
        try
        {
            // Sort all tags alphabetically
            if (swaggerDoc.Tags != null && swaggerDoc.Tags.Count > 0)
            {
                _logger.LogInformation("� Found {Count} tags to sort: {Tags}", 
                    swaggerDoc.Tags.Count, 
                    string.Join(", ", swaggerDoc.Tags.Select(t => t.Name)));
                
                var originalTags = swaggerDoc.Tags.ToList();
                var sortedTags = originalTags
                    .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                
                swaggerDoc.Tags.Clear();
                foreach (var tag in sortedTags)
                {
                    swaggerDoc.Tags.Add(tag);
                }
                
                _logger.LogInformation("✅ Tags sorted! New order: {Tags}", 
                    string.Join(", ", swaggerDoc.Tags.Select(t => t.Name)));
            }
            else
            {
                _logger.LogWarning("⚠️ No tags found to sort (Tags is null or empty)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in AlphabeticalEndpointSorter");
            throw; // Re-throw to see the actual issue
        }
    }
}
