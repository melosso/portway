namespace PortwayApi.Classes.OpenApi;

public class SecurityDefinitionInfo
{
    public string Name { get; set; } = "Bearer";
    public string Description { get; set; } = "Bearer token issued by Portway. Send it as: Authorization: Bearer {token}";
    public string In { get; set; } = "Header";
    public string Type { get; set; } = "Http";
    public string Scheme { get; set; } = "Bearer";
}
