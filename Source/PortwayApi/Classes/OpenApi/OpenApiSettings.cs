namespace PortwayApi.Classes.OpenApi;

public class OpenApiSettings
{
    public bool Enabled { get; set; } = true;
    public string Title { get; set; } = "API Documentation";
    public string Version { get; set; } = "v1";
    public string Description { get; set; } = "A summary of the API documentation.";
    public ContactInfo Contact { get; set; } = new ContactInfo();
    public SecurityDefinitionInfo SecurityDefinition { get; set; } = new SecurityDefinitionInfo();
    public bool ForceHttpsInProduction { get; set; } = true; // Always use HTTPS in production environments
    public bool ShowNamespaces { get; set; } = true; // groups endpoints by namespace in the sidebar
    public string DefaultGroup { get; set; } = "General"; // sidebar group for endpoints without a namespace, empty keeps them flat
    public bool ShowBadges { get; set; } = true; // labels operations with MCP and OData badges
    public bool MarkdownEnabled { get; set; } = false; // serves /docs/openapi.md for llms
    public ExternalDocsInfo ExternalDocs { get; set; } = new ExternalDocsInfo();

    // Scalar-specific
    public FooterInfo Footer { get; set; } = new FooterInfo();
    public string ScalarTheme { get; set; } = "purple"; // portway, alternate, default, moon, purple, solarized, bluePlanet, saturn, kepler, mars, deepSpace
    public string ScalarLayout { get; set; } = "modern"; // modern, classic
    public bool ScalarShowSidebar { get; set; } = true;
    public bool ScalarHideDownloadButton { get; set; } = false;
    public bool ScalarHideModels { get; set; } = true; // Hide the Models/Schemas section
    public bool ScalarHideClientButton { get; set; } = true; // Hide the client generation button
    public bool ScalarHideTestRequestButton { get; set; } = false; // Hide the test request button
}
