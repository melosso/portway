namespace PortwayApi.Helpers;

using System.Text.RegularExpressions;

/// <summary>
/// Format rules for a comma separated token scope list
/// </summary>
internal static partial class TokenScopeValidator
{
    [GeneratedRegex(@"^(\*|[A-Za-z0-9_][A-Za-z0-9_.-]*(/[A-Za-z0-9_][A-Za-z0-9_.-]*)*(@v([2-9]|[1-9][0-9]{1,2}))?(/?\*)?)$")]
    private static partial Regex Scope();

    public static string? Validate(string? scopes)
    {
        foreach (var scope in (scopes ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            if (!Scope().IsMatch(scope))
            {
                return $"Invalid scope '{scope}'. Use *, an endpoint such as CRM/Accounts, a version such as CRM/Accounts@v2, or a prefix such as CRM/*";
            }
        }

        return null;
    }
}
