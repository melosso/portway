using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PortwayApi.Classes.OpenApi;
using PortwayApi.Helpers;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class OpenApiErrorSchemaTests : ApiTestBase
{
    public static TheoryData<string, Type> Schemas => new()
    {
        { StandardResponses.ErrorSchemaId, typeof(ErrorResponse) },
        { StandardResponses.ValidationSchemaId, typeof(ValidationErrorResponse) },
        { StandardResponses.RateLimitSchemaId, typeof(RateLimitErrorResponse) },
        { StandardResponses.CompositeSchemaId, typeof(CompositeErrorResponse) },
    };

    [Theory]
    [MemberData(nameof(Schemas))]
    public async Task ErrorSchema_HasTheRecordProperties(string schemaId, Type record)
    {
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaId);

        var documented = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order();
        var serialized = new DefaultJsonTypeInfoResolver().GetTypeInfo(record, JsonSerializerOptions.Default).Properties.Select(p => p.Name).Order();

        Assert.Equal(serialized, documented);
    }
}
