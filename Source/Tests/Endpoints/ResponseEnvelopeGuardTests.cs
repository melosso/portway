using System.Text.RegularExpressions;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class ResponseEnvelopeGuardTests
{
    private static readonly Dictionary<string, Regex> Patterns = new()
    {
        ["anonymous-error"] = new(@"new\s*\{\s*(?:ok\s*=\s*false\s*,\s*)?(?:error\s*[=,]|success\s*=\s*false)"),
        ["mvc-error-result"] = new(@"new\s+(?:BadRequest|NotFound|Conflict|UnprocessableEntity)ObjectResult\("),
        ["object-result-error"] = new(@"new\s+ObjectResult\(\s*ErrorResponse"),
        ["minimal-error-result"] = new(@"(?<![\w.])(?:Typed)?Results\.(?:BadRequest|NotFound|Conflict|Problem|ValidationProblem|Unauthorized|Forbid)\("),
        ["bodyless-error"] = new(@"return\s+(?:NotFound|BadRequest|Unauthorized|Conflict|Forbid)\(\)"),
        ["plain-text-body"] = new(@"Response\.WriteAsync\(\s*"""),
    };

    private sealed record Allowance(int Count, string Reason);

    private static readonly Dictionary<string, Allowance> Allowed = new()
    {
        ["Api/EndpointController.Routing.cs|bodyless-error"] = new(2, "HEAD responses carry no body"),
        ["Api/HealthCheckEndpoints.cs|plain-text-body"] = new(1, "/health/live answers plain text by design"),
        ["Api/WebUiEndpoints.cs|minimal-error-result"] = new(2, "console HTML pages"),
        ["Api/WebUiEndpoints.EndpointsCrud.cs|anonymous-error"] = new(1, "console validation list, read by api() as errors"),
        ["Api/WebUiEndpoints.Environments.cs|anonymous-error"] = new(1, "connection test result payload with status 200"),
        ["Api/WebUiEndpoints.Oidc.cs|minimal-error-result"] = new(1, "browser OIDC start route"),
    };

    [Fact]
    public void ErrorBodiesAreBuiltThroughTheSharedHelpers()
    {
        var root = Path.Combine(RepoRoot(), "Source", "PortwayApi");
        var found = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("obj/") || relative.StartsWith("bin/") || IsEnvelopeSource(relative))
                continue;

            var text = File.ReadAllText(file);
            foreach (var (name, pattern) in Patterns)
            {
                var count = pattern.Matches(text).Count;
                if (count > 0)
                    found[$"{relative}|{name}"] = count;
            }
        }

        var problems = found
            .Where(f => !Allowed.TryGetValue(f.Key, out var a) || a.Count != f.Value)
            .Select(f => $"{f.Key}: found {f.Value}, allowed {(Allowed.TryGetValue(f.Key, out var a) ? a.Count : 0)}")
            .Concat(Allowed.Keys.Where(k => !found.ContainsKey(k)).Select(k => $"{k}: found 0, allowed {Allowed[k].Count}"))
            .ToList();

        Assert.True(problems.Count == 0,
            "Build error bodies with PortwayResults or a record in Helpers. Lower the allowlist when a site is fixed.\n" + string.Join("\n", problems));
    }

    [Theory]
    [InlineData("anonymous-error", "Results.Json(new { error = \"x\" }, statusCode: 400)")]
    [InlineData("anonymous-error", "Results.Json(new { ok = false, error = msg })")]
    [InlineData("anonymous-error", "WriteAsJsonAsync(new\n{\n    error,\n    detail,\n    success = false\n})")]
    [InlineData("anonymous-error", "new BadRequestObjectResult(new\n{\n    success = false,\n    message = \"x\"\n})")]
    [InlineData("mvc-error-result", "return new NotFoundObjectResult(body);")]
    [InlineData("object-result-error", "new ObjectResult(ErrorResponse.Of(\"x\")) { StatusCode = 403 }")]
    [InlineData("minimal-error-result", "return Results.BadRequest(ErrorResponse.Of(\"x\"));")]
    [InlineData("minimal-error-result", "return TypedResults.NotFound();")]
    [InlineData("bodyless-error", "return NotFound();")]
    [InlineData("plain-text-body", "await context.Response.WriteAsync(\"Error processing request\");")]
    public void PatternMatchesAHandBuiltError(string pattern, string code)
        => Assert.Matches(Patterns[pattern], code);

    [Theory]
    [InlineData("return PortwayResults.BadRequest(\"x\");")]
    [InlineData("return PortwayResults.NotFound(\"x\");")]
    [InlineData("return PortwayResults.MinimalError(\"x\", 400);")]
    [InlineData("Results.Json(new { ok = true, message = \"Connection successful\" })")]
    [InlineData("Results.Json(new { valid = false, errors = list })")]
    [InlineData("Results.Json(new { count = tools.Count })")]
    [InlineData("await context.Response.WriteAsync(CspNonce.Apply(html, context));")]
    [InlineData("return Ok();")]
    public void PatternIgnoresTheSharedHelpers(string code)
        => Assert.DoesNotContain(Patterns, p => p.Value.IsMatch(code));

    private static bool IsEnvelopeSource(string relative)
        => relative == "Helpers/PortwayResults.cs" || (relative.StartsWith("Helpers/") && relative.EndsWith("Response.cs"));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "portwayapi.slnx")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException("portwayapi.slnx not found above the test output directory");
    }
}
