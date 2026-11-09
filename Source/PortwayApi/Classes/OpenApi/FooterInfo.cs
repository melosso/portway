namespace PortwayApi.Classes.OpenApi;

public sealed class FooterInfo
{
    public const string DefaultText = "Powered by Scalar";
    public string Text { get; set; } = DefaultText;
    
    public bool CreditsScalar => string.Equals(Text?.Trim(), DefaultText, StringComparison.OrdinalIgnoreCase);
    public string Target { get; set; } = "_blank";
    public string Url { get; set; } = "#";
    public bool ShowSourceIcon { get; set; } = true;
}
