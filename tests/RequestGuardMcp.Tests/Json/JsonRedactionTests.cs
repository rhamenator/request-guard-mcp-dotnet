using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Json;

namespace RequestGuardMcp.Tests.Json;

public class JsonRedactionTests
{
    [Fact]
    public void RedactsTopLevelMatchingField()
    {
        var node = JsonNode.Parse("""{"password":"hunter2","name":"alice"}""")!;

        JsonRedaction.RedactFields(node, ["password"]);

        Assert.Equal("[REDACTED]", node["password"]!.GetValue<string>());
        Assert.Equal("alice", node["name"]!.GetValue<string>());
    }

    [Fact]
    public void RedactsNestedFieldsRecursively()
    {
        var node = JsonNode.Parse("""{"auth":{"token":"secret"}}""")!;

        JsonRedaction.RedactFields(node, ["token"]);

        Assert.Equal("[REDACTED]", node["auth"]!["token"]!.GetValue<string>());
    }

    [Fact]
    public void RedactsFieldsInsideArrays()
    {
        var node = JsonNode.Parse("""[{"secret":"a"},{"secret":"b"}]""")!;

        JsonRedaction.RedactFields(node, ["secret"]);

        Assert.Equal("[REDACTED]", node[0]!["secret"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", node[1]!["secret"]!.GetValue<string>());
    }

    [Fact]
    public void FieldMatchIsCaseInsensitive()
    {
        var node = JsonNode.Parse("""{"Authorization":"Bearer x"}""")!;

        JsonRedaction.RedactFields(node, ["authorization"]);

        Assert.Equal("[REDACTED]", node["Authorization"]!.GetValue<string>());
    }

    [Fact]
    public void PresentFieldDetectionUsesKeysNotValuesOrSubstrings()
    {
        var node = JsonNode.Parse("""{"note":"mentions token but has no sensitive key","Authorization":"x"}""")!;

        var present = JsonRedaction.FindPresentFields(node, ["token", "authorization"]);

        Assert.Equal(["authorization"], present);
    }

    [Fact]
    public void RedactAndCollectPresentFieldsDeduplicatesAndRedactsInOnePass()
    {
        var node = JsonNode.Parse("""{"Token":"abc","nested":{"api_key":"xyz"}}""")!;

        var present = JsonRedaction.RedactAndCollectPresentFields(node, ["token", "TOKEN", "api_key"]);

        Assert.Equal(["token", "api_key"], present);
        Assert.Equal("[REDACTED]", node["Token"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", node["nested"]!["api_key"]!.GetValue<string>());
    }

    [Fact]
    public void RedactFieldsEnforcesFieldAndDepthBoundsAndCancellation()
    {
        var node = JsonNode.Parse("""{"token":"abc"}""")!;
        var tooManyFields = Enumerable.Range(0, JsonRedaction.MaxFields + 1).Select(i => $"f{i}").ToList();
        Assert.Throws<ArgumentException>(() => JsonRedaction.RedactFields(node, tooManyFields));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => JsonRedaction.RedactFields(node, ["token"], cts.Token));

        var nested = JsonNode.Parse("""{"authorization":{"token":"secret"}}""")!;
        var nestedPresent = JsonRedaction.RedactAndCollectPresentFields(nested, ["authorization", "token"]);
        Assert.Equal(["authorization", "token"], nestedPresent);
        Assert.Equal("[REDACTED]", nested["authorization"]!.GetValue<string>());

        var manyNulls = new JsonArray(Enumerable.Repeat<JsonNode?>(null, JsonRedaction.MaxNodes).ToArray());
        Assert.Throws<ArgumentException>(() => JsonRedaction.RedactFields(manyNulls, ["token"]));
    }
}
