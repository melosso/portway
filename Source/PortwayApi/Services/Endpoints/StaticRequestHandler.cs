namespace PortwayApi.Services;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using Serilog;

/// <summary>Serves static endpoint content with optional OData-style filtering</summary>
public sealed class StaticRequestHandler
{
    private readonly Services.Caching.CacheManager _cacheManager;

    public StaticRequestHandler(Services.Caching.CacheManager cacheManager)
    {
        _cacheManager = cacheManager;
    }

    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    /// <summary>Serves a resolved static endpoint request</summary>
    public async Task<IActionResult> HandleAsync(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip)
    {
        var url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
        Log.Debug("Static Content Request: {Url}", url);

        {
            // Build path to content file - handle namespaced endpoints
            string endpointPath;
            if (endpoint.HasNamespace)
            {
                // For namespaced endpoints, use the full namespace/endpoint structure
                endpointPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static",
                    endpoint.EffectiveNamespace!, endpoint.FolderName ?? endpointName);
            }
            else
            {
                // For non-namespaced endpoints, use just the endpoint name
                endpointPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", endpointName);
            }

            var contentFile = endpoint.Properties!["ContentFile"].ToString()!;
            var contentFilePath = Path.Combine(endpointPath, contentFile);

            if (!System.IO.File.Exists(contentFilePath))
            {
                Log.Warning("Content file not found: {FilePath}", contentFilePath);
                return PortwayResults.NotFound($"Content file not found: {contentFile}");
            }

            // Get content type and filtering settings
            var contentType = endpoint.Properties["ContentType"].ToString();

            // Auto-detect content type if not specified
            if (string.IsNullOrEmpty(contentType))
            {
                contentType = GetContentTypeFromExtension(contentFile);
                Log.Debug("Auto-detected content type: {ContentType} for file: {ContentFile}", contentType, contentFile);
            }

            var enableFiltering = (bool)(endpoint.Properties.GetValueOrDefault("EnableFiltering", false));

            // Content negotiation: Check if client's Accept header matches our content type
            var acceptHeader = context.Request.Headers["Accept"].ToString();
            if (!string.IsNullOrEmpty(acceptHeader) && acceptHeader != "*/*")
            {
                var acceptedTypes = acceptHeader.Split(',').Select(t => t.Trim().Split(';')[0]).ToList();

                // Check if our content type is acceptable to the client
                if (!acceptedTypes.Contains(contentType) && !acceptedTypes.Contains("*/*"))
                {
                    Log.Debug("Content negotiation failed: Client accepts {AcceptHeader}, endpoint provides {ContentType}",
                        acceptHeader, contentType);

                    return PortwayResults.NotAcceptable($"Endpoint provides '{contentType}' but client accepts '{acceptHeader}'");
                }
            }

            // Check if OData filtering is requested and supported
            var hasODataParams = !string.IsNullOrEmpty(select) || !string.IsNullOrEmpty(filter) ||
                               !string.IsNullOrEmpty(orderby) || context.Request.Query.ContainsKey("$top") || context.Request.Query.ContainsKey("$skip");

            // Serve from server-side cache when no OData params (raw file, cacheable as-is)
            byte[] contentBytes;
            if (!hasODataParams)
            {
                var staticCacheKey = $"static:{endpointName}:{env}";
                var cached = await _cacheManager.GetAsync<byte[]>(staticCacheKey);
                if (cached != null)
                {
                    Log.Debug("Cache hit for static content: {Endpoint}", endpointName);
                    return new FileContentResult(cached, contentType!);
                }
                contentBytes = await System.IO.File.ReadAllBytesAsync(contentFilePath);
                await _cacheManager.SetAsync(staticCacheKey, contentBytes, endpointName);
            }
            else
            {
                contentBytes = await System.IO.File.ReadAllBytesAsync(contentFilePath);
            }

            if (hasODataParams && enableFiltering)
            {
                if (contentType!.Contains("json"))
                {
                    // Apply JSON filtering
                    return await ApplyJsonFiltering(context, contentBytes, contentType, select, filter, orderby, top, skip);
                }
                else if (contentType.Contains("xml"))
                {
                    // Apply XML filtering
                    return await ApplyXmlFiltering(context, contentBytes, contentType, select, filter, orderby, top, skip);
                }
            }

            Log.Debug("Serving static content: {Endpoint} ({ContentType})", endpointName, contentType);

            return new FileContentResult(contentBytes, contentType!);
        }
    }

    /// <summary>Applies JSON filtering using OData-style parameters</summary>
    private Task<IActionResult> ApplyJsonFiltering(
        HttpContext context,
        byte[] jsonBytes,
        string contentType,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip)
    {
        try
        {
            var json = Encoding.UTF8.GetString(jsonBytes);
            var jsonDoc = JsonDocument.Parse(json);
            
            // Start with the root data
            JsonElement data = jsonDoc.RootElement;
            List<JsonElement> items = new List<JsonElement>();
            
            // Handle different JSON structures
            if (data.ValueKind == JsonValueKind.Array)
            {
                // Direct array
                items = data.EnumerateArray().ToList();
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                // Look for common array properties
                var arrayProperties = new[] { "data", "items", "results", "value", "users", "records", "countries", "products", "orders", "customers" };
                foreach (var prop in arrayProperties)
                {
                    if (data.TryGetProperty(prop, out var arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
                    {
                        items = arrayElement.EnumerateArray().ToList();
                        Log.Debug("Found array property '{Property}' with {Count} items", prop, items.Count);
                        break;
                    }
                }
                
                // If no array found, treat the object as a single item
                if (items.Count == 0)
                {
                    Log.Debug("No array property found, treating root object as single item");
                    items.Add(data);
                }
            }
            
            Log.Debug("Applying OData filtering to {Count} items", items.Count);
            
            // Apply filtering
            if (!string.IsNullOrEmpty(filter))
            {
                items = ApplyFilter(items, filter);
                Log.Debug("After filter: {Count} items", items.Count);
            }
            
            // Apply ordering
            if (!string.IsNullOrEmpty(orderby))
            {
                items = ApplyOrderBy(items, orderby);
                Log.Debug("After orderby: {Count} items", items.Count);
            }
            
            // Apply pagination (skip and top)
            var totalCount = items.Count;
            items = items.Skip(skip).Take(top).ToList();
            Log.Debug("After pagination (skip:{Skip}, top:{Top}): {Count} items", skip, top, items.Count);
            
            // Apply field selection
            if (!string.IsNullOrEmpty(select))
            {
                items = ApplySelect(items, select);
                Log.Debug("After select: field selection applied", items.Count);
            }
            
            // Build result in the correct API format
            var result = CollectionResponse<object>.Of(items.Select(SerializeJsonElement).ToList()!);
            
            var resultJson = JsonSerializer.Serialize(result, IndentedJsonOptions);
            var resultBytes = Encoding.UTF8.GetBytes(resultJson);
            
            Log.Debug("JSON filtering applied successfully: {Count} items returned", items.Count);

            context.Response.Headers["X-Filtering-Status"] = "Applied";
            context.Response.Headers["X-Total-Count"] = totalCount.ToString();
            context.Response.Headers["X-Returned-Count"] = items.Count.ToString();
            
            return Task.FromResult<IActionResult>(new FileContentResult(resultBytes, contentType));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error applying JSON filtering: {Message}", ex.Message);
            context.Response.Headers["X-Filtering-Status"] = "Error";
            return Task.FromResult<IActionResult>(PortwayResults.ServerError(context, "Internal server error during filtering"));
        }
    }
    
    /// <summary>Applies OData-style filtering to JSON items</summary>
    private List<JsonElement> ApplyFilter(List<JsonElement> items, string filter)
    {
        try
        {
            Log.Debug("Parsing filter: {Filter}", filter);
            
            // OData function call syntax: contains(Field, 'value'), startswith(Field, 'value'), endswith(Field, 'value')
            var fnMatch = System.Text.RegularExpressions.Regex.Match(
                filter.Trim(),
                @"^(contains|startswith|endswith)\((\w+),\s*'([^']*)'\)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fnMatch.Success)
            {
                var operation = fnMatch.Groups[1].Value.ToLower();
                var field     = fnMatch.Groups[2].Value;
                var value     = fnMatch.Groups[3].Value;
                return items.Where(item =>
                {
                    if (!TryGetPropertyCI(item, field, out var fieldValue)) return false;
                    var s = fieldValue.GetString() ?? string.Empty;
                    return operation switch
                    {
                        "contains"    => s.Contains(value, StringComparison.OrdinalIgnoreCase),
                        "startswith"  => s.StartsWith(value, StringComparison.OrdinalIgnoreCase),
                        "endswith"    => s.EndsWith(value, StringComparison.OrdinalIgnoreCase),
                        _             => false
                    };
                }).ToList();
            }

            // Parse filter expression (simplified OData filter support). Supports: field eq 'value', field ne 'value', field gt number, field lt number, etc
            var filterParts = filter.Split(' ');
            if (filterParts.Length >= 3)
            {
                var field = filterParts[0];
                var operation = filterParts[1].ToLower();
                var value = string.Join(" ", filterParts.Skip(2));
                
                // Remove quotes from string values
                value = value.Trim('\'', '"');
                
                Log.Debug("Filter components - Field: {Field}, Operation: {Operation}, Value: {Value}", field, operation, value);
                
                var filteredItems = items.Where(item =>
                {
                    if (!TryGetPropertyCI(item, field, out var fieldValue))
                    {
                        Log.Debug("Field '{Field}' not found in item", field);
                        return false;
                    }
                    
                    Log.Debug("Comparing field '{Field}' value '{FieldValue}' with '{TargetValue}' using operation '{Operation}'", 
                        field, fieldValue, value, operation);
                        
                    var result = operation switch
                    {
                        "eq" => JsonValueComparer.Compare(fieldValue, value, "eq"),
                        "ne" => JsonValueComparer.Compare(fieldValue, value, "ne"),
                        "gt" => JsonValueComparer.Compare(fieldValue, value, "gt"),
                        "lt" => JsonValueComparer.Compare(fieldValue, value, "lt"),
                        "ge" => JsonValueComparer.Compare(fieldValue, value, "ge"),
                        "le" => JsonValueComparer.Compare(fieldValue, value, "le"),
                        "contains" => fieldValue.GetString()?.Contains(value, StringComparison.OrdinalIgnoreCase) == true,
                        "startswith" => fieldValue.GetString()?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true,
                        "endswith" => fieldValue.GetString()?.EndsWith(value, StringComparison.OrdinalIgnoreCase) == true,
                        _ => false
                    };
                    
                    Log.Debug("Filter result for item: {Result}", result);
                    return result;
                }).ToList();
                
                Log.Debug("Filter matched {Count} items out of {TotalCount}", filteredItems.Count, items.Count);
                return filteredItems;
            }
            
            Log.Warning("Filter expression could not be parsed: {Filter}", filter);
            return items; // Return original if filter couldn't be parsed
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Filter parsing failed, returning unfiltered data");
            return items;
        }
    }

    /// <summary>Applies OData-style ordering to JSON items</summary>
    private List<JsonElement> ApplyOrderBy(List<JsonElement> items, string orderby)
    {
        try
        {
            var orderParts = orderby.Split(' ');
            var field = orderParts[0];
            var direction = orderParts.Length > 1 && orderParts[1].ToLower() == "desc" ? "desc" : "asc";
            
            return direction == "desc"
                ? items.OrderByDescending(item => GetSortableValue(item, field)).ToList()
                : items.OrderBy(item => GetSortableValue(item, field)).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OrderBy parsing failed, returning original order");
            return items;
        }
    }
    
    /// <summary>Case-insensitive property lookup: tries exact name first, then falls back to a linear scan</summary>
    private static bool TryGetPropertyCI(JsonElement item, string field, out JsonElement value)
    {
        if (item.TryGetProperty(field, out value))
            return true;
        foreach (var prop in item.EnumerateObject())
        {
            if (prop.Name.Equals(field, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>Gets a sortable value from a JSON element</summary>
    private object GetSortableValue(JsonElement item, string field)
    {
        if (!TryGetPropertyCI(item, field, out var fieldValue))
            return "";
            
        return fieldValue.ValueKind switch
        {
            JsonValueKind.String => fieldValue.GetString() ?? "",
            JsonValueKind.Number => fieldValue.TryGetDouble(out var d) ? d : 0,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => ""
        };
    }
    
    /// <summary>Applies field selection to JSON items</summary>
    private List<JsonElement> ApplySelect(List<JsonElement> items, string select)
    {
        try
        {
            var fields = select.Split(',').Select(f => f.Trim()).ToArray();
            var selectedItems = new List<JsonElement>();
            
            foreach (var item in items)
            {
                var selectedObject = new Dictionary<string, object?>();
                
                foreach (var field in fields)
                {
                    if (TryGetPropertyCI(item, field, out var fieldValue))
                    {
                        selectedObject[field] = SerializeJsonElement(fieldValue);
                    }
                }
                
                var json = JsonSerializer.Serialize(selectedObject);
                var element = JsonDocument.Parse(json).RootElement;
                selectedItems.Add(element);
            }
            
            return selectedItems;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Select parsing failed, returning all fields");
            return items;
        }
    }

    /// <summary>Applies XML filtering using OData-style parameters</summary>
    private Task<IActionResult> ApplyXmlFiltering(
        HttpContext context,
        byte[] xmlBytes,
        string contentType,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip)
    {
        try
        {
            var xmlString = Encoding.UTF8.GetString(xmlBytes);
            var doc = XDocument.Parse(xmlString);
            
            // Find repeating elements (likely the data items to filter)
            var rootElement = doc.Root;
            if (rootElement == null)
            {
                return Task.FromResult<IActionResult>(new FileContentResult(xmlBytes, contentType));
            }

            // Find the main data items - prioritize direct children of root
            var directChildren = rootElement.Elements().ToList();
            List<XElement> items;
            
            if (directChildren.Count > 1)
            {
                // Multiple direct children - these are likely our main data items
                items = directChildren;
                Log.Debug("Found {Count} direct child elements under root '{RootName}'", items.Count, rootElement.Name.LocalName);
            }
            else
            {
                // Look for collections of similar elements at any level
                var elementGroups = rootElement.Descendants()
                    .GroupBy(e => e.Name.LocalName)
                    .Where(g => g.Count() > 1)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();

                if (elementGroups != null)
                {
                    // Found repeating elements - these are likely our data items
                    items = elementGroups.ToList();
                    Log.Debug("Found {Count} repeating XML elements of type '{ElementName}'", items.Count, elementGroups.Key);
                }
                else
                {
                    // Fallback: treat root as single item
                    items = new List<XElement> { rootElement };
                    Log.Debug("No repeating elements found, treating root as single item");
                }
            }

            var totalCount = items.Count;
            Log.Debug("Applying XML filtering to {Count} elements", totalCount);

            // Apply filtering
            if (!string.IsNullOrEmpty(filter))
            {
                items = ApplyXmlFilter(items, filter);
                Log.Debug("After filter: {Count} items remaining", items.Count);
            }

            // Apply ordering
            if (!string.IsNullOrEmpty(orderby))
            {
                items = ApplyXmlOrderBy(items, orderby);
                Log.Debug("After orderby: items reordered");
            }

            // Apply skip
            if (skip > 0)
            {
                items = items.Skip(skip).ToList();
                Log.Debug("After skip: {Count} items remaining", items.Count);
            }

            // Apply top
            if (top > 0 && top < items.Count)
            {
                items = items.Take(top).ToList();
                Log.Debug("After top: {Count} items selected", items.Count);
            }

            // Apply select (field selection)
            if (!string.IsNullOrEmpty(select))
            {
                items = ApplyXmlSelect(items, select);
                Log.Debug("After select: field selection applied");
            }

            // Rebuild XML with filtered items
            XDocument resultDoc;
            if (directChildren.Count > 1)
            {
                // Create new document with same structure but filtered items
                resultDoc = new XDocument(doc.Declaration);
                var newRoot = new XElement(rootElement.Name, rootElement.Attributes());
                
                // Add filtered items back to the root
                foreach (var item in items)
                {
                    newRoot.Add(new XElement(item));
                }
                
                resultDoc.Add(newRoot);
            }
            else
            {
                // Simple structure - just replace root children
                resultDoc = new XDocument(doc.Declaration);
                var newRoot = new XElement(rootElement.Name, rootElement.Attributes());
                newRoot.Add(items);
                resultDoc.Add(newRoot);
            }

            var resultXml = resultDoc.ToString();
            var resultBytes = Encoding.UTF8.GetBytes(resultXml);
            
            Log.Debug("XML filtering applied successfully: {Count} items returned out of {Total}", items.Count, totalCount);

            context.Response.Headers["X-Filtering-Status"] = "Applied";
            context.Response.Headers["X-Total-Count"] = totalCount.ToString();
            context.Response.Headers["X-Returned-Count"] = items.Count.ToString();
            
            return Task.FromResult<IActionResult>(new FileContentResult(resultBytes, contentType));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error applying XML filtering: {Message}", ex.Message);
            
            // Fallback to original content
            context.Response.Headers["X-Filtering-Status"] = "Error";
            return Task.FromResult<IActionResult>(new FileContentResult(xmlBytes, contentType));
        }
    }

    /// <summary>Serializes a JsonElement to an object for JSON output</summary>
    private object? SerializeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => SerializeJsonElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(SerializeJsonElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    /// <summary>Auto-detects content type based on file extension (uses shared ContentTypeHelper)</summary>
    internal static string GetContentTypeFromExtension(string fileName) => ContentTypeHelper.GetContentType(fileName);

    /// <summary>Common ID field names for validation</summary>



    /// <summary>Applies XML filtering using OData $filter syntax</summary>
    private List<XElement> ApplyXmlFilter(List<XElement> items, string filter)
    {
        try
        {
            Log.Debug("Parsing XML filter: {Filter}", filter);
            
            // Simple filter implementations for common patterns
            if (filter.Contains(" eq "))
            {
                // Handle equality: field eq 'value'
                var parts = filter.Split(" eq ");
                if (parts.Length == 2)
                {
                    var fieldName = parts[0].Trim();
                    var fieldValue = parts[1].Trim().Trim('\'').Trim('"');
                    
                    return items.Where(item => 
                    {
                        var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                        return element?.Value?.Equals(fieldValue, StringComparison.OrdinalIgnoreCase) == true;
                    }).ToList();
                }
            }
            else if (filter.Contains(" contains "))
            {
                // Handle contains: contains(field, 'value')
                var containsMatch = Regex.Match(filter, @"contains\((\w+),\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
                if (containsMatch.Success)
                {
                    var fieldName = containsMatch.Groups[1].Value;
                    var fieldValue = containsMatch.Groups[2].Value;
                    
                    return items.Where(item => 
                    {
                        var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                        return element?.Value?.Contains(fieldValue, StringComparison.OrdinalIgnoreCase) == true;
                    }).ToList();
                }
                
                // Handle simple contains: field contains 'value'
                var simpleParts = filter.Split(" contains ");
                if (simpleParts.Length == 2)
                {
                    var fieldName = simpleParts[0].Trim();
                    var fieldValue = simpleParts[1].Trim().Trim('\'').Trim('"');
                    
                    return items.Where(item => 
                    {
                        var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                        return element?.Value?.Contains(fieldValue, StringComparison.OrdinalIgnoreCase) == true;
                    }).ToList();
                }
            }
            
            Log.Warning("Unsupported XML filter pattern: {Filter}", filter);
            return items;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "XML filter parsing failed: {Filter}, returning unfiltered results", filter);
            return items;
        }
    }

    /// <summary>Applies XML ordering using OData $orderby syntax</summary>
    private List<XElement> ApplyXmlOrderBy(List<XElement> items, string orderby)
    {
        try
        {
            Log.Debug("Parsing XML orderby: {OrderBy}", orderby);
            
            var parts = orderby.Split(' ');
            var fieldName = parts[0].Trim();
            var direction = parts.Length > 1 && parts[1].Trim().Equals("desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
            
            if (direction == "desc")
            {
                return items.OrderByDescending(item =>
                {
                    var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                    return element?.Value ?? "";
                }).ToList();
            }
            else
            {
                return items.OrderBy(item =>
                {
                    var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                    return element?.Value ?? "";
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "XML orderby parsing failed: {OrderBy}, returning original order", orderby);
            return items;
        }
    }

    /// <summary>Applies XML field selection using OData $select syntax</summary>
    private List<XElement> ApplyXmlSelect(List<XElement> items, string select)
    {
        try
        {
            Log.Debug("Parsing XML select: {Select}", select);
            
            var fields = select.Split(',').Select(f => f.Trim()).ToList();
            var selectedItems = new List<XElement>();
            
            foreach (var item in items)
            {
                var newItem = new XElement(item.Name);
                
                // Copy attributes
                foreach (var attr in item.Attributes())
                {
                    newItem.Add(new XAttribute(attr));
                }
                
                // Add only selected fields
                foreach (var field in fields)
                {
                    var elements = item.Descendants().Where(e => e.Name.LocalName.Equals(field, StringComparison.OrdinalIgnoreCase));
                    foreach (var element in elements)
                    {
                        // Only add direct children, not nested descendants
                        if (element.Parent == item)
                        {
                            newItem.Add(new XElement(element));
                        }
                    }
                }
                
                selectedItems.Add(newItem);
            }
            
            return selectedItems;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "XML select parsing failed: {Select}, returning all fields", select);
            return items;
        }
    }

}
