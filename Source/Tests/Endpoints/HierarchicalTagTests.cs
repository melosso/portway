using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using PortwayApi.Classes;
using PortwayApi.Classes.OpenApi;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Covers the nested-namespace tag tree, which the sample endpoint configs never exercise because their namespaces are flat
/// </summary>
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

        // Name holds the full path for the hierarchy; the display leaf goes in Summary instead
        Assert.Equal("Accounts", child.Summary);
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

    [Fact]
    public void Tag_FollowsTheRoute_NotTheLabels()
    {
        var labelled = new EndpointDefinition { Namespace = "CRM", NamespaceDisplayName = "Customers", FolderName = "Suppliers", DisplayName = "Vendors" };

        Assert.Equal("CRM/Suppliers", labelled.DocumentationTag);
    }

    [Fact]
    public void FlatEndpoint_KeepsItsOwnTag()
    {
        Assert.Equal("Products", new EndpointDefinition { FolderName = "Products", DisplayName = "Catalog" }.DocumentationTag);
    }

    [Fact]
    public async Task NamespaceCasing_LinksToTheDeclaredParent()
    {
        var document = await TransformAsync("crm/Accounts", "CRM/Contacts");

        Assert.Single(document.Tags!, t => string.Equals(t.Name, "crm", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("crm", document.Tags!.Single(t => t.Name == "CRM/Contacts").Parent?.Name);
    }

    [Fact]
    public async Task RootGroup_IsNav()
    {
        var document = await TransformAsync("CRM/Accounts");

        Assert.Equal("nav", document.Tags!.Single(t => t.Name == "CRM").Kind);
    }

    [Fact]
    public void ConflictingNamespaceLabels_ResolveTheSameInAnyOrder()
    {
        var zeta = new EndpointDefinition { Namespace = "CRM", NamespaceDisplayName = "Zeta", FolderName = "Accounts" };
        var alpha = new EndpointDefinition { Namespace = "crm", NamespaceDisplayName = "Alpha", FolderName = "Contacts" };

        var forward = new OpenApiDocument();
        OpenApiTags.Declare(forward, zeta);
        OpenApiTags.Declare(forward, alpha);

        var reverse = new OpenApiDocument();
        OpenApiTags.Declare(reverse, alpha);
        OpenApiTags.Declare(reverse, zeta);

        Assert.Equal("Alpha", forward.Tags!.Single(t => t.Name == "CRM").Summary);
        Assert.Equal("Alpha", reverse.Tags!.Single(t => t.Name == "crm").Summary);
    }

    [Fact]
    public void NamespaceLabelConflicts_ListsOnlyDisagreeingDocumentedNamespaces()
    {
        EndpointDefinition[] endpoints =
        [
            new() { Namespace = "CRM", NamespaceDisplayName = "Zeta", FolderName = "Accounts" },
            new() { Namespace = "crm", NamespaceDisplayName = "Alpha", FolderName = "Contacts" },
            new() { Namespace = "CRM", FolderName = "Leads" },
            new() { Namespace = "WMS", NamespaceDisplayName = "Warehouse", FolderName = "Bins" },
            new() { Namespace = "WMS", NamespaceDisplayName = "Warehouse", FolderName = "Zones" },
            new() { Namespace = "WMS", NamespaceDisplayName = "Hidden", FolderName = "Legacy", Hidden = true }
        ];

        var conflict = Assert.Single(OpenApiTags.NamespaceLabelConflicts(endpoints));

        Assert.Equal("CRM", conflict.Namespace, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["Alpha", "Zeta"], conflict.Labels);
    }
}
