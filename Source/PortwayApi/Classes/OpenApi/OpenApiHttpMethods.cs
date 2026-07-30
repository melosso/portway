namespace PortwayApi.Classes.OpenApi;

/// <summary>Verbs Portway serves that HttpMethod has no static for; cached because HttpMethod.Parse allocates for unknown verbs</summary>
public static class OpenApiHttpMethods
{
    /// <summary>OData-style partial update; OpenAPI 3.2 emits it under a path item's additionalOperations</summary>
    public static readonly HttpMethod Merge = HttpMethod.Parse("MERGE");

    /// <summary>RFC 10008 body-carried read; OpenAPI 3.2 has a native query field for it</summary>
    public static readonly HttpMethod Query = HttpMethod.Parse("QUERY");
}
