namespace PortwayApi.Classes.OpenApi;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>The single error-code matrix; document builders ask here instead of listing codes inline</summary>
public static class StandardErrorCodes
{
    // Every operation can fail these ways regardless of endpoint type; 503 covers a disabled endpoint
    private static readonly int[] Baseline = [400, 401, 403, 404, 500, 503];

    // Codes an operation kind adds on top of the baseline
    private static readonly FrozenDictionary<ApiOperationKind, int[]> Additional =
        new Dictionary<ApiOperationKind, int[]>
        {
            [ApiOperationKind.SqlRead] = [],
            [ApiOperationKind.SqlWrite] = [422],
            [ApiOperationKind.SqlQuery] = [415],
            [ApiOperationKind.SqlDelete] = [],
            [ApiOperationKind.Proxy] = [],
            [ApiOperationKind.Composite] = [422],
            [ApiOperationKind.Static] = [406],
            [ApiOperationKind.Webhook] = [],
            [ApiOperationKind.FileUpload] = [409, 413, 415],
            [ApiOperationKind.FileDownload] = [416],
            [ApiOperationKind.FileDelete] = [],
            [ApiOperationKind.FileList] = []
        }.ToFrozenDictionary();

    /// <summary>Documented status codes, ascending</summary>
    public static int[] For(ApiOperationKind kind)
    {
        var extra = Additional.TryGetValue(kind, out var additional) ? additional : [];
        if (extra.Length == 0)
        {
            return Baseline;
        }

        var codes = new int[Baseline.Length + extra.Length];
        Baseline.CopyTo(codes, 0);
        extra.CopyTo(codes, Baseline.Length);
        Array.Sort(codes);
        return codes;
    }

    /// <summary>Maps a SQL endpoint's HTTP method onto the kind that describes its error surface</summary>
    public static ApiOperationKind SqlKindFor(string method) => method.ToUpperInvariant() switch
    {
        "POST" or "PUT" or "PATCH" or "MERGE" => ApiOperationKind.SqlWrite,
        "QUERY" => ApiOperationKind.SqlQuery,
        "DELETE" => ApiOperationKind.SqlDelete,
        _ => ApiOperationKind.SqlRead
    };
}
