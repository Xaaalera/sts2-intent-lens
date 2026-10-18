using System.Numerics;

namespace IntentLens.Player;

internal sealed record CycleLayout(IReadOnlyDictionary<string, Vector2> Positions, IReadOnlyList<CycleLayout.Connection> Connections, Vector2 Size)
{
    internal const int CellSize = 8;
    internal const int Gap = CellSize * 3;
    internal const int LaneStep = CellSize * 3;
    internal sealed record Region(Vector2 Position, Vector2 Size, Vector2 IntentCenter, float IntentWidth);
    internal sealed record Connection(string Source, string Target, Vector2[] Points, Vector2 Label);

    internal static Vector2? Place(Vector2 size, Vector2 viewport, Vector2 preferred, float safeTop,
        IReadOnlyList<(Vector2 Position, Vector2 Size)> obstacles, float? anchorX = null)
    {
        const float margin = 16;
        var candidates = new List<Vector2> { preferred };
        foreach (var obstacle in obstacles)
        {
            candidates.Add(new(obstacle.Position.X + obstacle.Size.X + margin, preferred.Y));
            candidates.Add(new(obstacle.Position.X - size.X - margin, preferred.Y));
            candidates.Add(new(preferred.X, obstacle.Position.Y - size.Y - margin));
            candidates.Add(new(preferred.X, obstacle.Position.Y + obstacle.Size.Y + margin));
        }
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Y > preferred.Y ? 1 : 0)
            .ThenBy(candidate => Vector2.DistanceSquared(candidate, preferred)))
        {
            if (anchorX is float center && (candidate.Y > preferred.Y || candidate.X > center || candidate.X + size.X < center)) { continue; }
            if (candidate.X < margin || candidate.Y < safeTop || candidate.X + size.X > viewport.X - margin || candidate.Y + size.Y > viewport.Y - margin) { continue; }
            if (obstacles.Any(obstacle => candidate.X < obstacle.Position.X + obstacle.Size.X + margin && candidate.X + size.X + margin > obstacle.Position.X &&
                candidate.Y < obstacle.Position.Y + obstacle.Size.Y + margin && candidate.Y + size.Y + margin > obstacle.Position.Y)) { continue; }
            return candidate;
        }
        return null;
    }

    internal static IReadOnlyList<MoveNode> Order(MoveGraph graph)
    {
        var ordered = new List<MoveNode>();
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in new[] { graph.History.FirstOrDefault() ?? graph.Current }.Concat(graph.Nodes.Select(node => node.Id)))
        {
            pending.Push(root);
            while (pending.TryPop(out var id))
            {
                var node = graph.Nodes.FirstOrDefault(node => node.Id == id);
                if (node is null || !visited.Add(id)) { continue; }
                ordered.Add(node);
                foreach (var edge in node.Edges.Reverse()) { pending.Push(edge.Target); }
            }
        }
        return ordered;
    }

    internal static int TopInset(MoveGraph graph, IReadOnlyList<MoveNode>? order = null)
    {
        var indices = (order ?? Order(graph)).Select((node, index) => (node.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        var routedEdges = graph.Nodes.Sum(node => node.Edges.Count(edge => indices.TryGetValue(edge.Target, out var target) && target != indices[node.Id] + 1));
        return CellSize * 3 + routedEdges * LaneStep;
    }

    internal static CycleLayout Build(MoveGraph graph, IReadOnlyDictionary<string, Region> regions)
    {
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var ordered = regions.Keys.Select(id => nodes[id]).ToArray();
        var indices = ordered.Select((node, index) => (node.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        var positions = regions.ToDictionary(pair => pair.Key, pair => pair.Value.Position, StringComparer.Ordinal);
        var connections = new List<Connection>();
        if (regions.Count == 0) { return new(positions, connections, new(298, 0)); }
        var lane = 0;
        foreach (var source in ordered)
        {
            foreach (var edge in source.Edges.Where(edge => positions.ContainsKey(edge.Target)))
            {
                var target = ordered[indices[edge.Target]];
                connections.Add(Route(source, target, regions, indices[target.Id] == indices[source.Id] + 1, lane));
                if (indices[target.Id] != indices[source.Id] + 1) { lane++; }
            }
        }
        return new(positions, connections, new(Math.Max(regions.Values.Max(region => region.Position.X + region.Size.X) + 32,
                connections.SelectMany(connection => connection.Points).Select(point => point.X).DefaultIfEmpty(0).Max() + 8),
            regions.Values.Max(region => region.Position.Y + region.Size.Y)));
    }

    internal static CycleLayout Choice(string selectionId, IReadOnlyDictionary<string, Region> regions)
    {
        var positions = regions.ToDictionary(pair => pair.Key, pair => pair.Value.Position, StringComparer.Ordinal);
        if (regions.Count == 0) { return new(positions, [], new(298, 0)); }
        var left = regions.Values.Min(region => region.Position.X);
        var right = regions.Values.Max(region => region.Position.X + region.Size.X);
        var top = regions.Values.Min(region => region.Position.Y);
        var centerY = regions.Values.Max(region => region.IntentCenter.Y);
        var centerX = (left + right) / 2;
        var loop = new Connection(selectionId, selectionId,
            [new(right + 8, centerY), new(right + 24, centerY), new(right + 24, top - LaneStep),
                new(centerX, top - LaneStep), new(centerX, top - LaneStep * 2)], new(centerX - 24, top - LaneStep - 22));
        return new(positions, [loop], new(Math.Max(212, right + 32), regions.Values.Max(region => region.Position.Y + region.Size.Y)));
    }

    private static Connection Route(MoveNode source, MoveNode target, IReadOnlyDictionary<string, Region> regions, bool adjacent, int lane)
    {
        var from = regions[source.Id];
        var to = regions[target.Id];
        var laneY = regions.Values.Min(region => region.Position.Y) - (lane + 1) * LaneStep;
        Vector2[] points;
        Vector2 label;
        if (regions.Values.Select(region => region.Position.Y).Distinct().Skip(1).Any() &&
            (!adjacent || from.Position.Y != to.Position.Y))
        {
            var start = new Vector2(from.IntentCenter.X, from.Position.Y - 2);
            var end = new Vector2(to.IntentCenter.X, to.Position.Y - 2);
            var corridorY = Math.Min(from.Position.Y, to.Position.Y) - 12;
            if (to.Position.Y > from.Position.Y)
            {
                start = new(from.IntentCenter.X, from.Position.Y + from.Size.Y + 4);
                end = new(to.IntentCenter.X, to.Position.Y - 4);
                var middleY = (start.Y + end.Y) / 2;
                points = start.X == end.X ? [start, end] : [start, new(start.X, middleY), new(end.X, middleY), end];
            }
            else if (from.Position.Y > to.Position.Y)
            {
                var leftX = regions.Values.Min(region => region.Position.X) - 12;
                if (from.Position.X == regions.Values.Where(region => region.Position.Y == from.Position.Y).Min(region => region.Position.X))
                {
                    start = from.IntentCenter - new Vector2(from.IntentWidth / 2 + 4, 0);
                    points = [start, new(leftX, start.Y), new(leftX, corridorY), new(end.X, corridorY), end];
                }
                else
                {
                    points = [start, new(start.X, from.Position.Y - 12), new(leftX, from.Position.Y - 12),
                        new(leftX, corridorY), new(end.X, corridorY), end];
                }
            }
            else if (source.Id == target.Id)
            {
                points = [start, start + new Vector2(12, 0), new(start.X + 12, corridorY), new(end.X, corridorY), end];
            }
            else { points = [start, new(start.X, corridorY), new(end.X, corridorY), end]; }
            return new(source.Id, target.Id, points, new((start.X + end.X) / 2 - 24, corridorY - 22));
        }
        if (source.Id == target.Id)
        {
            var right = from.Position.X + from.Size.X;
            points = [new(right, from.IntentCenter.Y), new(right + Gap / 2f, from.IntentCenter.Y),
                new(right + Gap / 2f, laneY), new(from.IntentCenter.X, laneY), new(from.IntentCenter.X, from.Position.Y - 2)];
            label = new(from.IntentCenter.X + 4, laneY - 22);
        }
        else if (adjacent)
        {
            var direction = Math.Sign(to.IntentCenter.X - from.IntentCenter.X);
            var start = from.IntentCenter + new Vector2(direction * (from.IntentWidth / 2 + 4), 0);
            var end = to.IntentCenter - new Vector2(direction * (to.IntentWidth / 2 + 4), 0);
            var middle = (start.X + end.X) / 2;
            points = start.Y == end.Y ? [start, end] : [start, new(middle, start.Y), new(middle, end.Y), end];
            label = new(middle - 24, Math.Max(start.Y, end.Y) + 14);
        }
        else
        {
            points = [new(from.IntentCenter.X, from.Position.Y - 2), new(from.IntentCenter.X, laneY),
                new(to.IntentCenter.X, laneY), new(to.IntentCenter.X, to.Position.Y - 2)];
            label = new((from.IntentCenter.X + to.IntentCenter.X) / 2 - 24, laneY - 22);
        }
        return new(source.Id, target.Id, points, label);
    }
}
