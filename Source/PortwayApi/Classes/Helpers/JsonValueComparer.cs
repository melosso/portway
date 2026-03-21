using System.Globalization;
using System.Text.Json;

namespace PortwayApi.Helpers;

public static class JsonValueComparer
{
    /// <summary>
    /// Compares a JSON field value against a target string using OData filter operators.
    /// Supported operations: eq, ne, gt, lt, ge, le.
    /// </summary>
    public static bool Compare(JsonElement fieldValue, string targetValue, string operation)
    {
        try
        {
            switch (fieldValue.ValueKind)
            {
                case JsonValueKind.String:
                    var stringValue = fieldValue.GetString() ?? "";
                    return operation switch
                    {
                        "eq" => stringValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase),
                        "ne" => !stringValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase),
                        "gt" => string.Compare(stringValue, targetValue, StringComparison.OrdinalIgnoreCase) > 0,
                        "lt" => string.Compare(stringValue, targetValue, StringComparison.OrdinalIgnoreCase) < 0,
                        "ge" => string.Compare(stringValue, targetValue, StringComparison.OrdinalIgnoreCase) >= 0,
                        "le" => string.Compare(stringValue, targetValue, StringComparison.OrdinalIgnoreCase) <= 0,
                        _ => false
                    };

                case JsonValueKind.Number:
                    if (double.TryParse(targetValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var targetNumber) && fieldValue.TryGetDouble(out var fieldNumber))
                    {
                        return operation switch
                        {
                            "eq" => Math.Abs(fieldNumber - targetNumber) < 0.0001,
                            "ne" => Math.Abs(fieldNumber - targetNumber) >= 0.0001,
                            "gt" => fieldNumber > targetNumber,
                            "lt" => fieldNumber < targetNumber,
                            "ge" => fieldNumber >= targetNumber,
                            "le" => fieldNumber <= targetNumber,
                            _ => false
                        };
                    }
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    if (bool.TryParse(targetValue, out var targetBool))
                    {
                        var fieldBool = fieldValue.GetBoolean();
                        return operation switch
                        {
                            "eq" => fieldBool == targetBool,
                            "ne" => fieldBool != targetBool,
                            _ => false
                        };
                    }
                    break;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
