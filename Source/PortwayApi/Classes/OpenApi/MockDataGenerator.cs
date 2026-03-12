using System.Text.Json;
using System.Text.Json.Nodes;

namespace PortwayApi.Classes.OpenApi;

/// <summary>
/// Utility to generate randomized, context-aware mock data for OpenAPI examples
/// </summary>
public static class MockDataGenerator
{
    public static JsonNode? GenerateValue(string? propertyName = null, JsonValueKind kind = JsonValueKind.String, string decimalSeparator = ".")
    {
        var name = propertyName?.ToLowerInvariant() ?? "";

        // GUID / UUID Specific patterns (High priority)
        if (name.Contains("guid") || name.Contains("uuid") || name.Contains("sysguid") || name.Equals("id") && kind == JsonValueKind.String)
        {
            return JsonValue.Create(Guid.NewGuid().ToString());
        }

        // Context-aware randomization
        if (name.StartsWith("id") || name.EndsWith("id") || name.EndsWith("pk") || name.EndsWith("key") || name.EndsWith("ref") || name.EndsWith("code") || name.EndsWith("num") || name.EndsWith("number") || name.EndsWith("count") || name.EndsWith("index") || name.EndsWith("seq") || name.EndsWith("sequence"))
        {
            return JsonValue.Create(Random.Shared.Next(1000, 9999));
        }
        
        if (name.Contains("email"))
        {
            var users = new[] { "john.doe", "jane.smith", "admin", "support", "test.user", "developer", "marketing", "sales" };
            return JsonValue.Create($"{users[Random.Shared.Next(users.Length)]}{Random.Shared.Next(10, 99)}@example.com");
        }

        if (name.Contains("date") || name.Contains("time") || name.Contains("at") || name.Contains("on") || name.Contains("timestamp"))
        {
            return JsonValue.Create(DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 1000)).ToString("yyyy-MM-ddTHH:mm:ssZ"));
        }

        if (name.Contains("amount") || name.Contains("price") || name.Contains("cost") || name.Contains("units") || name.Contains("total") || name.Contains("quantity") || name.Contains("balance"))
        {
            var val = Math.Round(Random.Shared.NextDouble() * 5000, 2);
            if (decimalSeparator == ",")
            {
                return JsonValue.Create(val.ToString().Replace(".", ","));
            }
            return JsonValue.Create(val);
        }

        if (name.Contains("name") || name.Contains("title") || name.Contains("label") || name.Contains("subject") || name.Contains("display"))
        {
            var prefixes = new[] { 
                "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Sigma", "Omega", "Prime", "Core", "Global", "Apex", "Elite", "Sky", "Ocean", "Green", "Solar", "Nova", "Stellar" 
            };
            var middle = new[] { 
                "Dynamic", "Cloud", "Secure", "Smart", "Future", "Digital", "Logic", "Vision", "Nexus", "Quantum", "Cyber", "Flow"
            };
            var suffixes = new[] { 
                "Corporation", "Inc", "GmbH", "Ltd", "Systems", "Solutions", "Group", "Logistics", "Services", "Ventures", "Networks", "Technologies", "Hub", "Lab"
            };
            
            bool useMiddle = Random.Shared.Next(0, 2) == 1;
            string result = useMiddle 
                ? $"{prefixes[Random.Shared.Next(prefixes.Length)]} {middle[Random.Shared.Next(middle.Length)]} {suffixes[Random.Shared.Next(suffixes.Length)]}"
                : $"{prefixes[Random.Shared.Next(prefixes.Length)]} {suffixes[Random.Shared.Next(suffixes.Length)]}";
                
            return JsonValue.Create(result);
        }

        if (name.Contains("status") || name.Contains("state") || name.Contains("type") || name.Contains("category") || name.Contains("code"))
        {
            var generic = new[] { "Active", "Inactive", "Pending", "Completed", "Processing", "Archived", "Draft", "Review", "Approved", "Rejected", "Open", "Closed" };
            return JsonValue.Create(generic[Random.Shared.Next(generic.Length)]);
        }

        if (name.Contains("desc") || name.Contains("comment") || name.Contains("note") || name.Contains("text") || name.Contains("message") || name.Contains("remark") || name.Contains("summary") || name.Contains("details") || name.Contains("info") || name.Contains("information") || name.Contains("log"))
        {
            var phrases = new[] {
                "Automated system update.", "Customer requested follow-up.", "Internal reference only.", "Requires manager approval.", 
                "Legacy data migrated from old system.", "Priority support requested.", "Standard operational procedure.",
                "Validated by quality assurance team.", "Batch processing completed successfully."
            };
            return JsonValue.Create(phrases[Random.Shared.Next(phrases.Length)]);
        }

        if (name.Contains("location") || name.Contains("city") || name.Contains("country") || name.Contains("region") || name.Contains("site") || name.Contains("address"))
        {
            var locations = new[] { "London", "New York", "Berlin", "Paris", "Tokyo", "Singapore", "Sydney", "Amsterdam", "Dubai", "Chicago", "Stockholm", "Madrid" };
            return JsonValue.Create(locations[Random.Shared.Next(locations.Length)]);
        }

        if (name.Contains("dept") || name.Contains("department") || name.Contains("team") || name.Contains("unit") || name.Contains("division"))
        {
            var depts = new[] { "Research", "Development", "Sales", "Marketing", "Support", "Legal", "Finance", "Operations", "HR", "Management", "Logistics" };
            return JsonValue.Create(depts[Random.Shared.Next(depts.Length)]);
        }

        if (name.Contains("color") || name.Contains("colour") || name.Contains("shade") || name.Contains("hue") || name.Contains("paint") || name.Contains("tone"))
        {
            var colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Black", "White", "Silver", "Gray" };
            return JsonValue.Create(colors[Random.Shared.Next(colors.Length)]);
        }

        if (name.Contains("currency") || name.Contains("iso") || name.Contains("symbol") || name.Contains("money") || name.Contains("cash") || name.Contains("currencycode") || name.Equals("curr") || name.Contains("ccy"))
        {
            var currencies = new[] { "USD", "EUR", "GBP", "JPY", "AUD", "CAD", "CHF", "CNY", "SEK", "NZD" };
            return JsonValue.Create(currencies[Random.Shared.Next(currencies.Length)]);
        }

        if (name.Contains("version") || name.Contains("v-") || name.Contains("rev"))
        {
            return JsonValue.Create($"{Random.Shared.Next(1, 5)}.{Random.Shared.Next(0, 9)}.{Random.Shared.Next(0, 99)}");
        }

        if (name.Contains("user") || name.Contains("owner") || name.Contains("creator") || name.Contains("modifier") || name.Contains("by"))
        {
            var users = new[] { "System", "Admin", "JS-42", "PortwayBot", "D.Smith", "A.Johnson", "GlobalProcess" };
            return JsonValue.Create(users[Random.Shared.Next(users.Length)]);
        }

        if (name.Contains("url") || name.Contains("link") || name.Contains("href") || name.Contains("uri"))
        {
            var domains = new[] { "example.com", "api.test", "portway.io", "systems.local" };
            return JsonValue.Create($"https://{domains[Random.Shared.Next(domains.Length)]}/v1/resource/{Random.Shared.Next(100, 999)}");
        }

        if (name.StartsWith("is") || name.StartsWith("has") || name.EndsWith("enabled") || name.EndsWith("active") || name.EndsWith("flag") || name.Contains("bool"))
        {
            return JsonValue.Create(Random.Shared.Next(0, 2) == 1);
        }

        // Fallback to type-based randomization
        return kind switch
        {
            JsonValueKind.String => JsonValue.Create("example_string"),
            JsonValueKind.Number => decimalSeparator == "," 
                ? JsonValue.Create(Random.Shared.Next(1, 500).ToString()) // If using comma decimals, return as string to preserve format
                : JsonValue.Create(Random.Shared.Next(1, 500)),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null => null,
            _ => JsonValue.Create("example")
        };
    }

    public static string GenerateCsvRow(List<string> columns, char delimiter)
    {
        // If delimiter is semicolon, use comma as decimal separator (common regional pattern)
        string decimalSeparator = delimiter == ';' ? "," : ".";

        var values = columns.Select(col => 
        {
            var val = GenerateValue(col, JsonValueKind.String, decimalSeparator);
            // Ensure CSV values don't contain delimiters, quotes or newlines
            string s = val?.ToString() ?? "";
            
            // Check if we need string encapsulation
            if (s.Contains(delimiter) || s.Contains("\"") || s.Contains("\n") || s.Contains(decimalSeparator) && delimiter == ',')
                s = $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        });

        return string.Join(delimiter.ToString(), values);
    }
}