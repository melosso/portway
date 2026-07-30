using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using PortwayApi.Classes.OpenApi;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Covers the nested-namespace tag tree, which the sample endpoint configs never exercise because their namespaces are flat</summary>
public class HierarchicalTagTests
{
    private static async Task<OpenApiDocument> TransformAsync(params string[] tagNames)
    {
        var document = new OpenApiDocument
        {
            Tags = new HashSet<OpenApiTag>(tagNames.Select(n => new OpenApiTag { Name = n }))
        };

        var context = new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [],
            ApplicationServices = new ServiceCollection().BuildServiceProvider()
        };

        await new HierarchicalTagDocumentFilter().TransformAsync(document, context, CancellationToken.None);
        return document;
    }

    [Fact]
    public async Task NestedNamespace_LinksTagToItsParent()
    {
        var document = await TransformAsync("CRM", "CRM/Accounts");

        var child = document.Tags!.Single(t => t.Name == "CRM/Accounts");
        Assert.Equal("CRM", child.Parent?.Name);
        Assert.Equal("nav", child.Kind);
    }

    [Fact]
    public async Task MissingAncestor_IsCreated()
    {
        var document = await TransformAsync("Sales/EMEA/Orders");

        Assert.Contains(document.Tags!, t => t.Name == "Sales");
        Assert.Contains(document.Tags!, t => t.Name == "Sales/EMEA");
        Assert.Equal("Sales/EMEA", document.Tags!.Single(t => t.Name == "Sales/EMEA/Orders").Parent?.Name);
        Assert.Equal("Sales", document.Tags!.Single(t => t.Name == "Sales/EMEA").Parent?.Name);
    }

    [Fact]
    public async Task FlatTag_IsLeftAlone()
    {
        var document = await TransformAsync("Inventory");

        var tag = document.Tags!.Single();
        Assert.Null(tag.Parent);
        Assert.Null(tag.Kind);
    }
}
