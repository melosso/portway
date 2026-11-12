using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace PortwayApi.Helpers;

public enum SecretFailure
{
    None,
    Missing,
    InvalidReference,
    InvalidCharacter
}

public readonly record struct SecretResolution(string Value, SecretFailure Failure, string? Name)
{
    public bool Succeeded => Failure == SecretFailure.None;

    public static SecretResolution Ok(string value) => new(value, SecretFailure.None, null);

    public static SecretResolution Fail(SecretFailure failure, string? name = null) => new(string.Empty, failure, name);
}

public sealed class SecretResolutionException(SecretResolution resolution)
    : InvalidOperationException("Application configuration could not be resolved for the current environment.")
{
    public SecretResolution Resolution { get; } = resolution;
}

public static partial class SecretReference
{
    [GeneratedRegex(@"\$\{secret:([^}]*)\}")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex NamePattern();

    public static bool IsValidName(string name) => NamePattern().IsMatch(name);

    public static bool ContainsReference(string? value) => value is not null && value.Contains("${secret:", StringComparison.Ordinal);

    public static IReadOnlyCollection<string> Names(string? template)
    {
        if (!ContainsReference(template))
            return [];

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ReferencePattern().Matches(template!))
        {
            if (IsValidName(match.Groups[1].Value))
                names.Add(match.Groups[1].Value);
        }
        return names;
    }

    public static bool IsQueryValueOnly(string url)
    {
        if (!ContainsReference(url))
            return true;

        var query = url.IndexOf('?');
        var fragment = url.IndexOf('#');
        if (query < 0 || (fragment >= 0 && fragment < query))
            return false;

        var matches = ReferencePattern().Matches(url);
        if (matches.Count != url.Split("${secret:").Length - 1)
            return false;

        foreach (Match match in matches)
        {
            if (match.Index < query || (fragment >= 0 && match.Index > fragment))
                return false;
            var pairStart = url.LastIndexOfAny(['?', '&'], match.Index);
            var equals = url.IndexOf('=', pairStart);
            if (equals < 0 || equals > match.Index)
                return false;
        }
        return true;
    }

    public static string? ExactName(string? value) =>
        value is not null && ReferencePattern().Match(value) is { Success: true } match
            && match.Index == 0 && match.Length == value.Length && IsValidName(match.Groups[1].Value)
            ? match.Groups[1].Value
            : null;

    public static SecretResolution ResolveExact(string template, IReadOnlyDictionary<string, string> values)
    {
        if (!ContainsReference(template))
            return SecretResolution.Ok(template);

        var match = ReferencePattern().Match(template);
        if (match.Index != 0 || match.Length != template.Length)
            return SecretResolution.Fail(SecretFailure.InvalidReference);

        return Lookup(match.Groups[1].Value, values);
    }

    public static SecretResolution ResolveHeader(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = Substitute(template, values, static v => v);
        return result.Succeeded && result.Value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0
            ? SecretResolution.Fail(SecretFailure.InvalidCharacter)
            : result;
    }

    public static SecretResolution ResolveQueryValue(string template, IReadOnlyDictionary<string, string> values) =>
        Substitute(template, values, Uri.EscapeDataString);

    public static SecretResolution ResolveConnectionString(string template, IReadOnlyDictionary<string, string> values)
    {
        if (!ContainsReference(template))
            return SecretResolution.Ok(template);

        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = template };
        }
        catch (ArgumentException)
        {
            return SecretResolution.Fail(SecretFailure.InvalidReference);
        }

        foreach (var key in builder.Keys.Cast<string>().ToList())
        {
            if (builder[key] is not string value || !ContainsReference(value))
                continue;

            var resolved = Substitute(value, values, static v => v);
            if (!resolved.Succeeded)
                return resolved;
            builder[key] = resolved.Value;
        }
        return SecretResolution.Ok(builder.ConnectionString);
    }

    private static SecretResolution Substitute(string template, IReadOnlyDictionary<string, string> values, Func<string, string> encode)
    {
        if (!ContainsReference(template))
            return SecretResolution.Ok(template);

        var output = new StringBuilder(template.Length);
        var last = 0;
        foreach (Match match in ReferencePattern().Matches(template))
        {
            var value = Lookup(match.Groups[1].Value, values);
            if (!value.Succeeded)
                return value;
            output.Append(template, last, match.Index - last).Append(encode(value.Value));
            last = match.Index + match.Length;
        }

        var rest = template.AsSpan(last);
        if (rest.Contains("${secret:", StringComparison.Ordinal))
            return SecretResolution.Fail(SecretFailure.InvalidReference);
        return SecretResolution.Ok(output.Append(rest).ToString());
    }

    private static SecretResolution Lookup(string name, IReadOnlyDictionary<string, string> values)
    {
        if (!IsValidName(name))
            return SecretResolution.Fail(SecretFailure.InvalidReference);

        return values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? SecretResolution.Ok(value)
            : SecretResolution.Fail(SecretFailure.Missing, name);
    }
}

public static class SecretResolver
{
    public static async Task<string> ResolveOrThrowAsync(
        this Interfaces.IEnvironmentSettingsProvider provider,
        string env,
        string template,
        Func<string, IReadOnlyDictionary<string, string>, SecretResolution> resolve,
        CancellationToken cancellationToken = default) =>
        ResolveOrThrow(env, template, await provider.FetchSecretsAsync(env, [template], cancellationToken), resolve);

    public static async Task<IReadOnlyDictionary<string, string>> FetchSecretsAsync(
        this Interfaces.IEnvironmentSettingsProvider provider,
        string env,
        IEnumerable<string?> templates,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in templates.SelectMany(SecretReference.Names).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await provider.GetSecretAsync(env, name, cancellationToken) is { } value)
                values[name] = value;
        }
        return values;
    }

    public static string ResolveOrThrow(
        string env,
        string template,
        IReadOnlyDictionary<string, string> values,
        Func<string, IReadOnlyDictionary<string, string>, SecretResolution> resolve)
    {
        var result = resolve(template, values);
        if (result.Succeeded)
            return result.Value;

        if (result.Failure != SecretFailure.Missing)
            Serilog.Log.Error("Secret reference in environment {Environment} failed: {Failure}", env, result.Failure);
        throw new SecretResolutionException(result);
    }
}
