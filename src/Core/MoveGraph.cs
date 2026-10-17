namespace IntentLens;

public sealed record MoveEdge(string Target, double? Weight = 1, string Repeat = "CanRepeatForever", int MaxTimes = 0, int Cooldown = 0);

public sealed record MoveNode(string Id, string Title, string Kind, bool Logged, IReadOnlyList<MoveEdge> Edges);

public sealed record MoveGraph(string Current, IReadOnlyList<string> History, IReadOnlyList<MoveNode> Nodes);

public sealed record StepDistribution(IReadOnlyDictionary<string, double> Known, IReadOnlyList<string> Conditional, double Unknown);

public sealed record Forecast(IReadOnlyList<StepDistribution> Steps, string Note);
