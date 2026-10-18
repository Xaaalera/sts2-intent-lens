using System.Text.Json;

namespace IntentLens;

public static class GraphCodec
{
    public static string Encode(MoveGraph graph) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["current"] = graph.Current,
        ["history"] = graph.History.ToArray(),
        ["nodes"] = graph.Nodes.Select(node => new Dictionary<string, object>
        {
            ["id"] = node.Id, ["title"] = node.Title, ["kind"] = node.Kind, ["logged"] = node.Logged,
            ["edges"] = node.Edges.Select(edge => new Dictionary<string, object?>
            {
                ["target"] = edge.Target, ["weight"] = edge.Weight, ["repeat"] = edge.Repeat,
                ["maxTimes"] = edge.MaxTimes, ["cooldown"] = edge.Cooldown
            }).ToArray()
        }).ToArray()
    });

    public static MoveGraph Decode(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(root.GetProperty("current").GetString()!,
            root.GetProperty("history").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            root.GetProperty("nodes").EnumerateArray().Select(node => new MoveNode(
                node.GetProperty("id").GetString()!, node.GetProperty("title").GetString()!, node.GetProperty("kind").GetString()!, node.GetProperty("logged").GetBoolean(),
                node.GetProperty("edges").EnumerateArray().Select(edge => new MoveEdge(
                    edge.GetProperty("target").GetString()!, edge.GetProperty("weight").ValueKind == JsonValueKind.Null ? null : edge.GetProperty("weight").GetDouble(),
                    edge.GetProperty("repeat").GetString()!, edge.GetProperty("maxTimes").GetInt32(), edge.GetProperty("cooldown").GetInt32())).ToArray())).ToArray());
    }

    public static string Encode(Forecast forecast) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["engine"] = ProbabilityEngine.Build,
        ["note"] = forecast.Note,
        ["steps"] = forecast.Steps.Select(step => new Dictionary<string, object>
        {
            ["known"] = step.Known.ToDictionary(), ["conditional"] = step.Conditional.ToArray(), ["unknown"] = step.Unknown
        }).ToArray()
    });

    public static Forecast DecodeForecast(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(document.RootElement.GetProperty("steps").EnumerateArray().Select(step => new StepDistribution(
            step.GetProperty("known").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetDouble()),
            step.GetProperty("conditional").EnumerateArray().Select(value => value.GetString()!).ToArray(), step.GetProperty("unknown").GetDouble())).ToArray(),
            document.RootElement.GetProperty("note").GetString()!);
    }
}
