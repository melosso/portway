namespace PortwayApi.Helpers;

/// <summary>
/// Whether the console requires sign-in; true once an account exists
/// </summary>
public static class WebUiAuthState
{
    private static volatile bool _enabled;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
