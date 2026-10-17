namespace IntentLens;

/// <summary>Projects public branch metadata onto transitions between visible actions.</summary>
public sealed record MoveReference(MoveGraph Graph, IReadOnlyList<MoveReference.Link> Links, bool Collapsed)
{
    public sealed record Link(string Source, string Target, double? Chance, bool Conditional, bool Branched, string? ConditionLabel = null);
    public sealed record Section(string Title, MoveReference Reference, string Note = "", bool ConditionalScope = false, MoveNode? Selection = null, bool MarkCurrent = true, string? TerminalAction = null);

    public static IReadOnlyList<StepDistribution> ForecastSteps(MoveGraph graph, Forecast forecast, IReadOnlyList<Section> sections)
    {
        var terminals = sections.Where(section => section.TerminalAction is not null).Select(section => section.TerminalAction!).ToHashSet(StringComparer.Ordinal);
        if (terminals.Contains(graph.Current)) { return []; }
        var steps = new List<StepDistribution>();
        foreach (var step in forecast.Steps)
        {
            steps.Add(step);
            if (step.Unknown == 0 && step.Conditional.Count == 0 && step.Known.Count > 0 &&
                Math.Abs(step.Known.Values.Sum() - 1) < .000001 && step.Known.Where(pair => pair.Value > 0).All(pair => terminals.Contains(pair.Key))) { break; }
        }
        return steps;
    }

    public static bool CanMarkCurrent(MoveGraph graph, string sharedEntry, IReadOnlyList<string> predecessorActions)
    {
        if (graph.Current != sharedEntry) { return true; }
        var known = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        if (graph.History.Count == 0 || graph.History.Any(id => !known.Contains(id))) { return false; }
        var previous = graph.History.LastOrDefault(id => id != sharedEntry);
        return previous is not null && predecessorActions.Contains(previous, StringComparer.Ordinal);
    }

    public IReadOnlyList<Section>? SharedChoiceSections(MoveGraph original)
    {
        var branches = original.Nodes.Where(node => node.Kind == "random").ToArray();
        if (!Collapsed || branches.Length != 1 || original.Nodes.Any(node => node.Kind is not ("move" or "random"))) { return null; }
        var selection = branches[0];
        var options = selection.Edges.Select(edge => edge.Target).Distinct(StringComparer.Ordinal).ToArray();
        if (options.Length is < 2 or > 4) { return null; }
        var nodes = original.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        if (options.Any(id => !nodes.TryGetValue(id, out var node) || node.Kind != "move" || node.Edges.Count != 1 || node.Edges[0].Target != selection.Id)) { return null; }
        var entries = original.Nodes.Where(node => node.Kind == "move" && !options.Contains(node.Id, StringComparer.Ordinal)).ToArray();
        var order = new List<string>();
        if (entries.Length > 0)
        {
            var roots = entries.Where(node => !entries.Any(other => other.Edges.Any(edge => edge.Target == node.Id))).ToArray();
            if (roots.Length != 1) { return null; }
            var id = roots[0].Id;
            while (id != selection.Id)
            {
                if (!nodes.TryGetValue(id, out var node) || order.Contains(id, StringComparer.Ordinal) || node.Edges.Count != 1) { return null; }
                order.Add(id);
                id = node.Edges[0].Target;
            }
            if (order.Count(id => !options.Contains(id, StringComparer.Ordinal)) != entries.Length) { return null; }
        }
        var sharedCurrent = order.Contains(original.Current, StringComparer.Ordinal) && options.Contains(original.Current, StringComparer.Ordinal);
        var history = original.History.Where(id => nodes.TryGetValue(id, out var node) && node.Kind == "move").ToArray();
        var currentLogged = original.History.All(nodes.ContainsKey) && history.Length > 0 && history[^1] == original.Current;
        var inOpening = currentLogged && history.Length <= order.Count && history.SequenceEqual(order.Take(history.Length));
        var afterOpening = currentLogged && history.Length > order.Count && history.Take(order.Count).SequenceEqual(order);
        var sections = new List<Section>();
        if (order.Count > 0)
        {
            // The last entry action can also be a recurring option. It does not repeat inside the opener.
            var links = order.Zip(order.Skip(1)).Select(pair => new Link(pair.First, pair.Second, 1, false, false)).ToArray();
            var opening = order.Select(id => nodes[id]).Select(node => node with
            { Edges = links.Where(link => link.Source == node.Id).Select(link => new MoveEdge(link.Target)).ToArray() }).ToArray();
            var reference = new MoveReference(Graph with { Nodes = opening, History = original.History.Where(order.Contains).ToArray() }, links, Collapsed);
            sections.Add(new(TextCatalog.Get("Opening"), reference, MarkCurrent: !sharedCurrent || inOpening));
        }
        sections.Add(new(TextCatalog.Get("RepeatingChoice"), ForActions(options), TextCatalog.Get("RepeatingChoiceNote"),
            Selection: selection, MarkCurrent: !sharedCurrent || afterOpening));
        return sections;
    }

    public MoveReference ForActions(IReadOnlyList<string> order, string? terminal = null)
    {
        var included = order.ToHashSet(StringComparer.Ordinal);
        var links = Links.Where(link => link.Source != terminal && included.Contains(link.Source) && included.Contains(link.Target)).ToArray();
        var nodes = order.Select(id => Graph.Nodes.Single(node => node.Id == id)).Select(node => node with
        { Edges = node.Id == terminal ? [] : node.Edges.Where(edge => included.Contains(edge.Target)).ToArray() }).ToArray();
        return new(Graph with { Nodes = nodes, History = Graph.History.Where(included.Contains).ToArray() }, links, Collapsed);
    }

    public static MoveReference Create(MoveGraph graph, IReadOnlyDictionary<(string Branch, string Target), string>? descriptions = null)
    {
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var visible = graph.Nodes.Where(node => node.Kind is "move" or "unknown").ToArray();
        var paths = new List<Link>();
        var failed = visible.Length == 0;
        foreach (var source in visible)
        {
            foreach (var edge in source.Edges)
            {
                Visit(edge.Target, source.Edges.Count == 1 ? 1 : null, false, source.Edges.Count != 1, null, new(StringComparer.Ordinal));
            }

            void Visit(string id, double? chance, bool conditional, bool branched, string? label, HashSet<string> visited)
            {
                if (paths.Count >= 4096 || visited.Count >= 64 || !visited.Add(id) || !nodes.TryGetValue(id, out var node))
                { failed = true; return; }
                if (node.Kind is "move" or "unknown")
                {
                    paths.Add(new(source.Id, id, conditional ? null : chance, conditional, branched, label));
                    return;
                }
                if (node.Kind is not ("random" or "conditional") || node.Edges.Count == 0) { failed = true; return; }
                var chances = ProbabilityEngine.BaseChances(node);
                foreach (var edge in node.Edges.DistinctBy(edge => edge.Target))
                {
                    var nextChance = chance * chances.GetValueOrDefault(edge.Target);
                    var nextLabel = node.Kind == "conditional" ? conditional ? null : descriptions?.GetValueOrDefault((node.Id, edge.Target)) : label;
                    Visit(edge.Target, nextChance, conditional || node.Kind == "conditional", true, nextLabel, new(visited, StringComparer.Ordinal));
                }
            }
        }
        if (failed)
        {
            return new(graph, graph.Nodes.SelectMany(node => node.Edges.Select(edge => new Link(node.Id, edge.Target,
                node.Kind == "random" ? ProbabilityEngine.BaseChances(node).GetValueOrDefault(edge.Target) : null,
                node.Kind == "conditional", node.Kind != "move"))).ToArray(), false);
        }
        var links = paths.GroupBy(link => (link.Source, link.Target)).Select(group => new Link(group.Key.Source, group.Key.Target,
            group.All(link => link.Chance is not null) ? group.Sum(link => link.Chance!.Value) : null,
            group.Any(link => link.Conditional), group.Any(link => link.Branched),
            group.Select(link => link.ConditionLabel).Distinct().Count() == 1 ? group.First().ConditionLabel : null)).ToArray();
        var projected = visible.Select(node => node with { Edges = links.Where(link => link.Source == node.Id)
            .Select(link => new MoveEdge(link.Target, link.Chance)).ToArray() }).ToArray();
        return new(new(graph.Current, graph.History.Where(id => visible.Any(node => node.Id == id)).ToArray(), projected), links, true);
    }
}
