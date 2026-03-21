using System.Text.Json;
using PortwayApi.Classes;
using PortwayApi.Classes.Helpers;
using Serilog;

namespace PortwayApi.Helpers;

public static class SqlInputValidator
{
    public static (bool IsValid, string? ErrorMessage, List<ValidationDetail>? Errors) Validate(
        JsonElement data,
        EndpointDefinition endpoint,
        string httpMethod)
    {
        var errors = new List<ValidationDetail>();

        // Check for empty request body
        if (data.ValueKind == JsonValueKind.Undefined || data.ValueKind == JsonValueKind.Null)
        {
            return (false, "Request body cannot be empty", null);
        }

        // Check required columns for POST (CREATE) operations
        if (httpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) && endpoint.RequiredColumns != null)
        {
            foreach (var requiredColumn in endpoint.RequiredColumns)
            {
                bool hasProperty = data.TryGetProperty(requiredColumn, out var propValue);
                bool isEmpty = !hasProperty ||
                            propValue.ValueKind == JsonValueKind.Null ||
                            (propValue.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(propValue.GetString()));

                if (isEmpty)
                {
                    errors.Add(new ValidationDetail(requiredColumn, $"{requiredColumn} is required"));
                }
            }
        }

        // Check allowed columns (with alias support)
        if (endpoint.AllowedColumns != null && endpoint.AllowedColumns.Count > 0)
        {
            // Parse allowed columns to handle aliases (e.g., "EmployeeId;ID")
            var allowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in endpoint.AllowedColumns)
            {
                var parts = column.Split(';');
                allowedFields.Add(parts[0]); // Add primary column name
                if (parts.Length > 1)
                {
                    allowedFields.Add(parts[1]); // Add alias if exists
                }
            }

            foreach (var property in data.EnumerateObject())
            {
                if (!allowedFields.Contains(property.Name))
                {
                    errors.Add(new ValidationDetail(property.Name, $"{property.Name} is not an allowed property"));
                }
            }
        }

        // Validate regex patterns
        if (endpoint.ColumnValidation != null)
        {
            foreach (var validation in endpoint.ColumnValidation)
            {
                string columnName = validation.Key;

                if (data.TryGetProperty(columnName, out var propValue) &&
                    propValue.ValueKind == JsonValueKind.String)
                {
                    string? value = propValue.GetString();

                    if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(validation.Value.Regex))
                    {
                        try
                        {
                            var regex = new System.Text.RegularExpressions.Regex(validation.Value.Regex);

                            if (!regex.IsMatch(value))
                            {
                                errors.Add(new ValidationDetail(columnName,
                                    string.IsNullOrEmpty(validation.Value.ValidationMessage)
                                        ? $"{columnName} does not match the required format"
                                        : validation.Value.ValidationMessage));
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Invalid regex pattern for column {Column}", columnName);
                        }
                    }
                }
            }
        }

        if (errors.Any())
        {
            return (false, "Validation failed", errors);
        }

        return (true, null, null);
    }
}
