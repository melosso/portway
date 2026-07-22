using System.Text.Json.Nodes;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class ResponseTransformHelperTests
{
    private static ProxyResponseTransforms Rules(
        List<string>? remove = null, Dictionary<string, string>? rename = null, List<string>? mask = null)
        => new() { Remove = remove, Rename = rename, Mask = mask };

    [Fact]
    public void Apply_RemovesTopLevelField()
    {
        var result = ResponseTransformHelper.Apply(
            """{"id":1,"internalNotes":"secret"}""", Rules(remove: ["internalNotes"]));

        var node = JsonNode.Parse(result)!.AsObject();
        Assert.False(node.ContainsKey("internalNotes"));
        Assert.Equal(1, (int)node["id"]!);
    }

    [Fact]
    public void Apply_RenamesField()
    {
        var result = ResponseTransformHelper.Apply(
            """{"cust_nm":"Acme"}""", Rules(rename: new() { ["cust_nm"] = "customerName" }));

        var node = JsonNode.Parse(result)!.AsObject();
        Assert.False(node.ContainsKey("cust_nm"));
        Assert.Equal("Acme", (string)node["customerName"]!);
    }

    [Fact]
    public void Apply_MasksField()
    {
        var result = ResponseTransformHelper.Apply(
            """{"ssn":"123-45-6789"}""", Rules(mask: ["ssn"]));

        Assert.Equal("***", (string)JsonNode.Parse(result)!.AsObject()["ssn"]!);
    }

    [Fact]
    public void Apply_TransformsArrayElements()
    {
        var result = ResponseTransformHelper.Apply(
            """[{"a":1,"x":2},{"a":3,"x":4}]""", Rules(remove: ["x"]));

        var array = JsonNode.Parse(result)!.AsArray();
        Assert.All(array, item => Assert.False(item!.AsObject().ContainsKey("x")));
    }

    [Fact]
    public void Apply_TransformsODataValueWrapper()
    {
        var result = ResponseTransformHelper.Apply(
            """{"value":[{"secret":"x","id":1}]}""", Rules(remove: ["secret"]));

        var element = JsonNode.Parse(result)!.AsObject()["value"]!.AsArray()[0]!.AsObject();
        Assert.False(element.ContainsKey("secret"));
        Assert.Equal(1, (int)element["id"]!);
    }

    [Fact]
    public void Apply_RemoveWinsOverRenameAndMask()
    {
        var result = ResponseTransformHelper.Apply(
            """{"f":"v"}""", Rules(remove: ["f"], rename: new() { ["f"] = "g" }, mask: ["f"]));

        var node = JsonNode.Parse(result)!.AsObject();
        Assert.False(node.ContainsKey("f"));
        Assert.False(node.ContainsKey("g"));
    }

    [Fact]
    public void Apply_RenameSkippedWhenTargetExists()
    {
        var result = ResponseTransformHelper.Apply(
            """{"a":1,"b":2}""", Rules(rename: new() { ["a"] = "b" }));

        var node = JsonNode.Parse(result)!.AsObject();
        Assert.Equal(1, (int)node["a"]!);
        Assert.Equal(2, (int)node["b"]!);
    }

    [Fact]
    public void Apply_InvalidJson_ReturnsInputUnchanged()
    {
        var input = "not json at all";
        Assert.Equal(input, ResponseTransformHelper.Apply(input, Rules(remove: ["x"])));
    }

    [Fact]
    public void Apply_NoRules_ReturnsInputUnchanged()
    {
        var input = """{"id":1}""";
        Assert.Equal(input, ResponseTransformHelper.Apply(input, Rules()));
    }

    [Fact]
    public void ApplyToRows_TransformsDictionaryRows()
    {
        var rows = new List<object>
        {
            new Dictionary<string, object?> { ["ssn"] = "123", ["cust_nm"] = "Acme", ["internal"] = 1 }
        };

        var result = ResponseTransformHelper.ApplyToRows(rows, new ProxyResponseTransforms
        {
            Remove = ["internal"],
            Rename = new() { ["cust_nm"] = "customerName" },
            Mask = ["ssn"]
        });

        var row = Assert.IsType<Dictionary<string, object?>>(result[0]);
        Assert.False(row.ContainsKey("internal"));
        Assert.False(row.ContainsKey("cust_nm"));
        Assert.Equal("Acme", row["customerName"]);
        Assert.Equal("***", row["ssn"]);
    }

    [Fact]
    public void ApplyToRows_NonDictionaryRows_PassThrough()
    {
        var rows = new List<object> { "plain" };
        var result = ResponseTransformHelper.ApplyToRows(rows, new ProxyResponseTransforms { Remove = ["x"] });
        Assert.Equal("plain", result[0]);
    }
}
