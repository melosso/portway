using System.Text.RegularExpressions;
using Serilog;

namespace PortwayApi.Helpers;

/// <summary>
/// Matches client input against an operator-supplied validation pattern with a timeout
/// </summary>
public static class ConfigPattern
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Returns false for a non-match, an invalid pattern or a timed-out match
    /// </summary>
    public static bool IsMatch(string value, string pattern, string subject)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.None, MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            Log.Warning("Validation pattern for {Subject} timed out", subject);
            return false;
        }
        catch (ArgumentException ex)
        {
            Log.Warning("Invalid validation pattern for {Subject}: {Error}", subject, ex.Message);
            return false;
        }
    }
}
