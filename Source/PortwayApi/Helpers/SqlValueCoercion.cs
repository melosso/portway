using System.Globalization;
using PortwayApi.Services.Database;
using PortwayApi.Services.Providers;

namespace PortwayApi.Helpers;

public static class SqlValueCoercion
{
    public static bool TryCoerce(ColumnMetadata? column, object? value, SqlProviderType provider, out object? coerced)
    {
        coerced = value;
        if (column is null || value is not string text)
            return true;

        var invariant = CultureInfo.InvariantCulture;
        switch (column.ClrType)
        {
            case "System.Int16" or "System.Int32" or "System.Int64" or "System.Byte" or "System.UInt16" or "System.UInt32" or "System.UInt64":
                if (!long.TryParse(text, NumberStyles.Integer, invariant, out var integer))
                    return false;
                coerced = integer;
                return true;

            case "System.Decimal" or "System.Double" or "System.Single":
                if (!decimal.TryParse(text, NumberStyles.Float, invariant, out var number))
                    return false;
                coerced = number;
                return true;

            case "System.DateTime" when provider != SqlProviderType.Sqlite:
                if (!DateTimeOffset.TryParse(text, invariant, DateTimeStyles.AssumeUniversal, out var local))
                    return false;
                coerced = DateTime.SpecifyKind(local.UtcDateTime, DateTimeKind.Unspecified);
                return true;

            case "System.DateTimeOffset" when provider != SqlProviderType.Sqlite:
                if (!DateTimeOffset.TryParse(text, invariant, DateTimeStyles.AssumeUniversal, out var zoned))
                    return false;
                coerced = zoned.ToUniversalTime();
                return true;

            default:
                return true;
        }
    }
}
