namespace IntentLens;

public static class ProbabilityEngine
{
    public const string Build = "0.1.1";

    public static IReadOnlyDictionary<string, double?> BaseChances(MoveNode node)
    {
        var valid = node.Kind == "random" && node.Edges.All(edge => edge.Weight is not null and >= 0 && double.IsFinite(edge.Weight.Value));
        var total = valid ? node.Edges.Sum(edge => edge.Weight!.Value) : 0;
        return node.Edges.GroupBy(edge => edge.Target).ToDictionary(group => group.Key,
            group => total > 0 ? (double?)(group.Sum(edge => edge.Weight!.Value) / total) : null);
    }
    public static Forecast Evaluate(MoveGraph graph)
    {
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var paths = new List<Path> { new(graph.Current, graph.History.ToArray(), 1) };
        var steps = new List<StepDistribution>();
        var unresolved = 0d;
        var conditional = new HashSet<string>(StringComparer.Ordinal);
        for (var step = 0; step < 2; step++)
        {
            var next = new List<Path>();
            var previousConditional = conditional.ToArray();
            conditional.Clear();
            foreach (var possible in previousConditional)
            {
                if (nodes.TryGetValue(possible, out var state) && state.Kind == "move" && state.Edges.Count == 1)
                {
                    foreach (var future in ReachableMoves(state.Edges[0].Target, nodes)) { conditional.Add(future); }
                }
            }
            foreach (var path in paths)
            {
                if (!nodes.TryGetValue(path.Current, out var current) || current.Kind != "move" || current.Edges.Count != 1)
                {
                    unresolved += path.Probability;
                    continue;
                }
                Resolve(current.Edges[0].Target, path, null, new HashSet<string>(StringComparer.Ordinal), nodes, next, conditional, ref unresolved);
            }
            var known = next.GroupBy(path => path.Current).ToDictionary(group => group.Key, group => group.Sum(path => path.Probability));
            steps.Add(new(known, conditional.Order(StringComparer.Ordinal).ToArray(), Math.Clamp(unresolved, 0, 1)));
            paths = next;
            if (paths.Count > 4096)
            {
                unresolved += paths.Sum(path => path.Probability);
                paths.Clear();
            }
        }
        return new(steps, TextCatalog.Get("Assumption"));
    }

    public static bool Eligible(MoveEdge edge, IReadOnlyList<string> history, IReadOnlyDictionary<string, MoveNode> nodes)
    {
        if (edge.Repeat == "UseOnlyOnce" && history.Contains(edge.Target)) { return false; }
        var limit = edge.Repeat switch { "CannotRepeat" => 1, "CanRepeatXTimes" => edge.MaxTimes, _ => int.MaxValue };
        if (limit <= 0) { return false; }
        if (history.Count >= limit && history.TakeLast(limit).All(id => id == edge.Target)) { return false; }
        return edge.Cooldown <= 0 || !history.Where(id => nodes.TryGetValue(id, out var node) && node.Kind == "move").TakeLast(edge.Cooldown).Contains(edge.Target);
    }

    private static void Resolve(string id, Path path, string? firstLogged, HashSet<string> visited,
        IReadOnlyDictionary<string, MoveNode> nodes, List<Path> next, HashSet<string> conditional, ref double unresolved)
    {
        if (visited.Count >= 64 || !visited.Add(id) || !nodes.TryGetValue(id, out var node))
        { unresolved += path.Probability; return; }
        firstLogged ??= node.Logged ? id : null;
        if (node.Kind == "move")
        {
            var history = firstLogged is null ? path.History : [.. path.History, firstLogged];
            next.Add(new(id, history, path.Probability));
            return;
        }
        var edges = node.Edges.Where(edge => Eligible(edge, path.History, nodes)).ToArray();
        if (node.Kind != "random" || edges.Any(edge => edge.Weight is null or < 0 || !double.IsFinite(edge.Weight.Value)))
        {
            unresolved += path.Probability;
            foreach (var possible in ReachableMoves(id, nodes)) { conditional.Add(possible); }
            return;
        }
        var total = edges.Sum(edge => edge.Weight!.Value);
        if (total <= 0) { unresolved += path.Probability; return; }
        foreach (var edge in edges.Where(edge => edge.Weight > 0))
        {
            Resolve(edge.Target, path with { Probability = path.Probability * edge.Weight!.Value / total }, firstLogged,
                new HashSet<string>(visited, StringComparer.Ordinal), nodes, next, conditional, ref unresolved);
        }
    }

    private static IEnumerable<string> ReachableMoves(string start, IReadOnlyDictionary<string, MoveNode> nodes)
    {
        var pending = new Queue<string>([start]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var id))
        {
            if (!seen.Add(id) || !nodes.TryGetValue(id, out var node)) { continue; }
            if (node.Kind == "move") { yield return id; continue; }
            foreach (var edge in node.Edges) { pending.Enqueue(edge.Target); }
        }
    }

    private sealed record Path(string Current, string[] History, double Probability);
}
