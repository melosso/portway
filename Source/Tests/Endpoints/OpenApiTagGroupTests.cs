using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using PortwayApi.Classes;
using PortwayApi.Classes.OpenApi;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Covers tag groups the sample endpoint configs never exercise, nested namespaces and name collisions among them
/// </summary>
public class OpenApiTagGroupTests
{
    private static OpenApiSettings Settings(string defaultGroup = "General", bool showNamespaces = true) =>
        new() { DefaultGroup = defaultGroup, ShowNamespaces = showNamespaces };

    private static async Task<OpenApiDocument> DeclareAsync(OpenApiSettings settings, params EndpointDefinition[] endpoints)
    {
        var document = new OpenApiDocument();
        foreach (var endpoint in endpoints)
        {
            OpenApiTags.Declare(document, endpoint, OpenApiTags.NameFor(endpoint), settings);
        }

        var context = new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [],
            ApplicationServices = new ServiceCollection().BuildServiceProvider()
        };
        await new GroupTitleDocumentFilter().TransformAsync(document, context, CancellationToken.None);
        return document;
    }

    private static OpenApiTag Tag(OpenApiDocument document, string name) => document.Tags!.Single(t => t.Name == name);

    [Fact]
    public async Task NamespacedEndpoint_HangsUnderItsNamespaceGroup()
    {
        var document = await DeclareAsync(Settings(), new EndpointDefinition { Namespace = "CRM", FolderName = "Accounts" });

        var endpoint = Tag(document, "CRM/Accounts");
        Assert.Equal("ns:CRM", endpoint.Parent?.Name);
        Assert.Null(endpoint.Kind);
        Assert.Equal("Accounts", endpoint.Summary);

        var group = Tag(document, "ns:CRM");
        Assert.Equal("nav", group.Kind);
        Assert.Equal("CRM", group.Summary);
        Assert.Null(group.Parent);
    }

    [Fact]
    public async Task NestedNamespace_CreatesEveryAncestorGroup()
    {
        var document = await DeclareAsync(Settings(), new EndpointDefinition { Namespace = "Sales/EMEA", FolderName = "Orders" });

        Assert.Equal("ns:Sales/EMEA", Tag(document, "Sales/EMEA/Orders").Parent?.Name);
        Assert.Equal("ns:Sales", Tag(document, "ns:Sales/EMEA").Parent?.Name);
        Assert.Null(Tag(document, "ns:Sales").Parent);
        Assert.Equal("EMEA", Tag(document, "ns:Sales/EMEA").Summary);
        Assert.Equal("Sales", Tag(document, "ns:Sales").Summary);
        Assert.All(document.Tags!.Where(t => t.Name!.StartsWith("ns:")), t => Assert.Equal("nav", t.Kind));
    }

    [Fact]
    public async Task Tag_FollowsTheRoute_LabelsOnlySetTitles()
    {
        var labelled = new EndpointDefinition { Namespace = "CRM", NamespaceDisplayName = "Customers", FolderName = "Suppliers", DisplayName = "Vendors" };

        var document = await DeclareAsync(Settings(), labelled);

        Assert.Equal("CRM/Suppliers", OpenApiTags.NameFor(labelled));
        Assert.Equal("Vendors", Tag(document, "CRM/Suppliers").Summary);
        Assert.Equal("Customers", Tag(document, "ns:CRM").Summary);
    }

    [Fact]
    public async Task FlatEndpoint_HangsUnderTheDefaultGroup_UnlessItIsEmpty()
    {
        var flat = new EndpointDefinition { FolderName = "Products", DisplayName = "Catalog" };

        var grouped = await DeclareAsync(Settings("General"), flat);
        Assert.Equal("ns:General", Tag(grouped, "Products").Parent?.Name);
        Assert.Equal("General", Tag(grouped, "ns:General").Summary);

        var ungrouped = await DeclareAsync(Settings(""), flat);
        Assert.Null(Assert.Single(ungrouped.Tags!).Parent);
    }

    [Fact]
    public async Task FlatEndpoint_AndTheSameNameInANamespaceLikeTheDefaultGroup_KeepTheirOwnTags()
    {
        var document = await DeclareAsync(Settings("General"),
            new EndpointDefinition { FolderName = "Requests" },
            new EndpointDefinition { Namespace = "General", FolderName = "Requests" });

        Assert.Equal("ns:General", Tag(document, "Requests").Parent?.Name);
        Assert.Equal("ns:General", Tag(document, "General/Requests").Parent?.Name);
        Assert.Single(document.Tags!, t => t.Kind == "nav");
    }

    [Fact]
    public async Task FlatEndpoint_NamedLikeANamespace_StaysAnEndpointTag()
    {
        var document = await DeclareAsync(Settings(""),
            new EndpointDefinition { FolderName = "WMS" },
            new EndpointDefinition { Namespace = "WMS", FolderName = "Bins" });

        var endpoint = Tag(document, "WMS");
        Assert.Null(endpoint.Kind);
        Assert.Null(endpoint.Parent);
        Assert.Equal("ns:WMS", Tag(document, "WMS/Bins").Parent?.Name);
    }

    [Fact]
    public async Task NamespaceCasing_LinksToTheDeclaredGroup()
    {
        var document = await DeclareAsync(Settings(),
            new EndpointDefinition { Namespace = "crm", FolderName = "Accounts" },
            new EndpointDefinition { Namespace = "CRM", FolderName = "Contacts" });

        Assert.Single(document.Tags!, t => string.Equals(t.Name, "ns:crm", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ns:crm", Tag(document, "CRM/Contacts").Parent?.Name);
    }

    [Fact]
    public async Task ShowNamespacesOff_DeclaresNoGroups()
    {
        var document = await DeclareAsync(Settings(showNamespaces: false),
            new EndpointDefinition { Namespace = "WMS/Inbound", FolderName = "StagingBins" },
            new EndpointDefinition { FolderName = "Products" });

        Assert.All(document.Tags!, t => Assert.Null(t.Parent));
        Assert.All(document.Tags!, t => Assert.Null(t.Kind));
        Assert.Equal("StagingBins", Tag(document, "WMS/Inbound/StagingBins").Summary);
    }

    [Fact]
    public async Task NamespaceLabel_WinsOverTheSegment_InAnyOrder()
    {
        var unlabelled = new EndpointDefinition { Namespace = "ServiceRequest", FolderName = "Cancellations" };
        var labelled = new EndpointDefinition { Namespace = "ServiceRequest", NamespaceDisplayName = "Servicing", FolderName = "Requests" };

        Assert.Equal("Servicing", Tag(await DeclareAsync(Settings(), unlabelled, labelled), "ns:ServiceRequest").Summary);
        Assert.Equal("Servicing", Tag(await DeclareAsync(Settings(), labelled, unlabelled), "ns:ServiceRequest").Summary);
    }

    [Fact]
    public async Task ConflictingNamespaceLabels_ResolveTheSameInAnyOrder()
    {
        var zeta = new EndpointDefinition { Namespace = "CRM", NamespaceDisplayName = "Zeta", FolderName = "Accounts" };
        var alpha = new EndpointDefinition { Namespace = "crm", NamespaceDisplayName = "Alpha", FolderName = "Contacts" };

        Assert.Equal("Alpha", Tag(await DeclareAsync(Settings(), zeta, alpha), "ns:CRM").Summary);
        Assert.Equal("Alpha", Tag(await DeclareAsync(Settings(), alpha, zeta), "ns:crm").Summary);
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

        var conflict = Assert.Single(OpenApiTags.NamespaceConflicts(endpoints, e => e.NamespaceDisplayName));

        Assert.Equal("CRM", conflict.Namespace, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["Alpha", "Zeta"], conflict.Values);
    }

    [Fact]
    public async Task ConflictingNamespaceDescriptions_ResolveTheSameInAnyOrder()
    {
        var zeta = new EndpointDefinition { Namespace = "CRM", NamespaceDescription = "Zeta", FolderName = "Accounts" };
        var alpha = new EndpointDefinition { Namespace = "CRM", NamespaceDescription = "Alpha", FolderName = "Contacts" };

        Assert.Equal("Alpha", Tag(await DeclareAsync(Settings(), zeta, alpha), "ns:CRM").Description);
        Assert.Equal("Alpha", Tag(await DeclareAsync(Settings(), alpha, zeta), "ns:CRM").Description);
        Assert.Equal(["Alpha", "Zeta"], Assert.Single(OpenApiTags.NamespaceConflicts([zeta, alpha], e => e.NamespaceDescription)).Values);
    }
}
