namespace PortwayApi.Helpers;

/// <summary>
/// Whether the console asks for a sign-in. True once at least one account exists.
/// Set at startup and again whenever the last account is removed or the first is added.
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
