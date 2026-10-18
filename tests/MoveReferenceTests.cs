using Xunit;

namespace IntentLens.Tests;

public sealed class MoveReferenceTests
{
    private static MoveNode Move(string id, string next) => new(id, id, "move", true, [new(next)]);
    private static MoveNode Choice(params MoveEdge[] edges) => new("choice", "choice", "random", false, edges);
    private static MoveGraph Graph(params MoveNode[] nodes) => new("A", ["A"], nodes);

    [Fact]
    public void Reference_shows_actions_instead_of_conditional_machine_states()
    {
        var graph = Graph(Move("A", "choice1"), Move("B", "choice2"), Move("C", "D"), Move("D", "C"),
            new("choice1", "condition", "conditional", false, [new("B", null), new("C", null)]),
            new("choice2", "condition", "conditional", false, [new("B", null), new("C", null)]));
        var original = GraphCodec.Encode(graph);
        var reference = MoveReference.Create(graph);
        Assert.True(reference.Collapsed);
        Assert.Equal(4, reference.Graph.Nodes.Count);
        Assert.Equal(6, reference.Links.Count);
        Assert.All(reference.Graph.Nodes, node => Assert.Equal("move", node.Kind));
        Assert.Contains(reference.Links, link => link.Source == "B" && link.Target == "B" && link.Conditional && link.Chance is null);
        Assert.Equal(original, GraphCodec.Encode(graph));
    }

    [Fact]
    public void Reference_base_weights_do_not_apply_current_history_eligibility()
    {
        var graph = Graph(Move("A", "choice"), Move("B", "choice"), Choice(new("A", 1, "CannotRepeat"), new("B", 1)));
        var reference = MoveReference.Create(graph);
        Assert.Equal(.5, reference.Links.Single(link => link.Source == "A" && link.Target == "A").Chance);
        Assert.Equal(.5, reference.Links.Single(link => link.Source == "A" && link.Target == "B").Chance);
        Assert.Equal(1, ProbabilityEngine.Evaluate(graph).Steps[0].Known["B"]);
    }

    [Fact]
    public void Reference_multiplies_nested_weights_and_merges_paths_to_the_same_action()
    {
        var graph = Graph(Move("A", "choice"), Move("B", "A"), Move("C", "A"),
            Choice(new("B", 1), new("inner", 1)), new("inner", "random", "random", false, [new("B", 1), new("C", 3)]));
        var reference = MoveReference.Create(graph);
        Assert.Equal(.625, reference.Links.Single(link => link.Source == "A" && link.Target == "B").Chance);
        Assert.Equal(.375, reference.Links.Single(link => link.Source == "A" && link.Target == "C").Chance);
    }

    [Fact]
    public void Reference_does_not_invent_chances_for_dynamic_weights()
    {
        var reference = MoveReference.Create(Graph(Move("A", "choice"), Move("B", "A"), Choice(new("A", null), new("B", 1))));
        Assert.All(reference.Links.Where(link => link.Source == "A"), link => Assert.Null(link.Chance));
    }

    [Fact]
    public void Reference_retains_raw_graph_when_a_branch_has_no_resolvable_action()
    {
        var graph = Graph(Move("A", "choice"), Choice(new MoveEdge("choice", 1)));
        var reference = MoveReference.Create(graph);
        Assert.False(reference.Collapsed);
        Assert.Same(graph, reference.Graph);
    }

    [Fact]
    public void Source_verified_condition_labels_do_not_turn_into_numeric_chances()
    {
        var graph = Graph(Move("A", "condition"), Move("B", "A"), Move("C", "A"),
            new("condition", "condition", "conditional", false, [new("B", null), new("C", null)]));
        var reference = MoveReference.Create(graph, new Dictionary<(string, string), string>
        { [("condition", "B")] = "companion alive", [("condition", "C")] = "companion defeated" });
        var link = reference.Links.Single(link => link.Source == "A" && link.Target == "B");
        Assert.Equal("companion alive", link.ConditionLabel);
        Assert.True(link.Conditional);
        Assert.Null(link.Chance);
    }

    [Fact]
    public void Merged_conditional_paths_do_not_claim_one_rule_describes_all_paths()
    {
        var graph = Graph(Move("A", "first"), Move("B", "A"),
            new("first", "first", "conditional", false, [new("B", null), new("second", null)]),
            new("second", "second", "conditional", false, [new("B", null)]));
        var reference = MoveReference.Create(graph, new Dictionary<(string, string), string>
        { [("first", "B")] = "one rule", [("first", "second")] = "another rule", [("second", "B")] = "nested rule" });
        var link = reference.Links.Single(link => link.Source == "A" && link.Target == "B");
        Assert.Null(link.ConditionLabel);
        Assert.Null(link.Chance);
    }

    [Fact]
    public void Phase_sections_keep_the_opener_repeat_and_post_death_cycle_separate()
    {
        var graph = Graph(Move("A", "B"), Move("B", "first"), Move("C", "second"),
            new("first", "condition", "conditional", false, [new("C", null), new("D", null)]),
            new("second", "condition", "conditional", false, [new("C", null), new("D", null)]),
            Move("D", "E"), Move("E", "F"), Move("F", "D"));
        var reference = MoveReference.Create(graph);
        var opener = reference.ForActions(["A", "B", "C"]);
        var later = reference.ForActions(["F", "D", "E"]);
        Assert.Equal(["A", "B", "C"], opener.Graph.Nodes.Select(node => node.Id));
        Assert.Equal(["F", "D", "E"], later.Graph.Nodes.Select(node => node.Id));
        Assert.Equal(3, opener.Links.Count);
        Assert.Equal(3, later.Links.Count);
        Assert.Contains(opener.Links, link => link.Source == "C" && link.Target == "C");
        Assert.Contains(later.Links, link => link.Source == "E" && link.Target == "F");
        Assert.Equal(2, reference.Links.Count(link => !opener.Links.Contains(link) && !later.Links.Contains(link)));
        Assert.All(opener.Links.Where(link => link.Conditional), link => Assert.Null(link.Chance));
        Assert.Equal(8, graph.Nodes.Count);
    }

    [Fact]
    public void Shared_choice_is_one_repeating_group_after_the_opening_action()
    {
        var graph = Graph(Move("A", "choice"), Move("B", "choice"), Move("C", "choice"),
            Choice(new("B", 1, "CannotRepeat"), new("C", 1, "CanRepeatXTimes", 2)));
        var original = GraphCodec.Encode(graph);
        var sections = MoveReference.Create(graph).SharedChoiceSections(graph)!;
        Assert.Equal(2, sections.Count);
        Assert.Equal(["A"], sections[0].Reference.Graph.Nodes.Select(node => node.Id));
        Assert.Equal(["B", "C"], sections[1].Reference.Graph.Nodes.Select(node => node.Id));
        Assert.Equal("random", sections[1].Selection!.Kind);
        Assert.Equal(.5, ProbabilityEngine.BaseChances(sections[1].Selection!) ["B"]);
        Assert.Equal("CannotRepeat", sections[1].Selection!.Edges[0].Repeat);
        Assert.Equal(2, sections[1].Selection!.Edges[1].MaxTimes);
        Assert.Equal(original, GraphCodec.Encode(graph));
    }

    [Fact]
    public void Shared_choice_does_not_group_options_with_different_follow_up_rules()
    {
        var graph = Graph(Move("A", "choice"), Move("B", "choice"), Move("C", "B"), Choice(new("B", 1), new("C", 1)));
        Assert.Null(MoveReference.Create(graph).SharedChoiceSections(graph));
    }

    [Fact]
    public void Shared_choice_keeps_unequal_base_weights_and_orders_the_entry_chain()
    {
        var graph = Graph(Move("X", "choice"), Move("A", "X"), Move("B", "choice"), Move("C", "choice"), Choice(new("B", 1), new("C", 3)));
        var sections = MoveReference.Create(graph).SharedChoiceSections(graph)!;
        Assert.Equal(["A", "X"], sections[0].Reference.Graph.Nodes.Select(node => node.Id));
        Assert.Equal(.25, ProbabilityEngine.BaseChances(sections[1].Selection!) ["B"]);
        Assert.Equal(.75, ProbabilityEngine.BaseChances(sections[1].Selection!) ["C"]);
    }

    [Fact]
    public void Shared_choice_without_an_opener_does_not_add_a_fake_opening_action()
    {
        var graph = Graph(Move("A", "choice"), Move("B", "choice"), Choice(new("A", 1), new("B", null)));
        var section = Assert.Single(MoveReference.Create(graph).SharedChoiceSections(graph)!);
        Assert.NotNull(section.Selection);
        Assert.All(ProbabilityEngine.BaseChances(section.Selection!).Values, chance => Assert.Null(chance));
    }

    public static IEnumerable<object[]> SharedChoiceCases()
    {
        for (var entryCount = 0; entryCount <= 3; entryCount++)
        {
            for (var optionCount = 2; optionCount <= 4; optionCount++)
            {
                foreach (var forcedFirst in entryCount == 0 ? new[] { false } : new[] { false, true })
                {
                    foreach (var reverseNodes in new[] { false, true })
                    {
                        foreach (var knownWeights in new[] { false, true })
                        { yield return [entryCount, optionCount, forcedFirst, reverseNodes, knownWeights]; }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SharedChoiceCases))]
    public void Common_choice_patterns_do_not_depend_on_monster_names_or_node_storage_order(
        int entryCount, int optionCount, bool forcedFirst, bool reverseNodes, bool knownWeights)
    {
        var entries = Enumerable.Range(0, entryCount).Select(index => "entry" + index).ToArray();
        var options = Enumerable.Range(0, optionCount).Select(index => "option" + index).ToArray();
        var nodes = entries.Select((id, index) => Move(id, index + 1 < entries.Length ? entries[index + 1] : forcedFirst ? options[0] : "choice"))
            .Concat(options.Select(id => Move(id, "choice")))
            .Append(Choice(options.Select((id, index) => new MoveEdge(id, knownWeights ? index + 1 : null)).ToArray())).ToArray();
        if (reverseNodes) { Array.Reverse(nodes); }
        var current = entries.FirstOrDefault() ?? options[0];
        var graph = new MoveGraph(current, [current], nodes);
        var original = GraphCodec.Encode(graph);
        var sections = MoveReference.Create(graph).SharedChoiceSections(graph)!;
        Assert.Equal(entryCount == 0 ? 1 : 2, sections.Count);
        var chooser = sections[^1];
        Assert.Equal(options, chooser.Reference.Graph.Nodes.Select(node => node.Id));
        Assert.NotNull(chooser.Selection);
        if (entryCount > 0)
        {
            var expected = entries.Concat(forcedFirst ? [options[0]] : Array.Empty<string>()).ToArray();
            Assert.Equal(expected, sections[0].Reference.Graph.Nodes.Select(node => node.Id));
            Assert.Equal(expected.Length - 1, sections[0].Reference.Links.Count);
            Assert.All(sections[0].Reference.Links, link => { Assert.False(link.Branched); Assert.NotEqual(link.Source, link.Target); });
            Assert.Empty(sections[0].Reference.Graph.Nodes[^1].Edges);
        }
        var chances = ProbabilityEngine.BaseChances(chooser.Selection!);
        for (var index = 0; index < optionCount; index++)
        {
            if (knownWeights) { Assert.Equal((double)(index + 1) / Enumerable.Range(1, optionCount).Sum(), chances[options[index]]); }
            else { Assert.Null(chances[options[index]]); }
        }
        Assert.Equal(original, GraphCodec.Encode(graph));
    }

    [Theory]
    [InlineData("bite", true, false)]
    [InlineData("bite,revive,claw", false, true)]
    [InlineData("bite,revive", true, false)]
    [InlineData("claw", false, true)]
    [InlineData("bite,foreign", false, false)]
    [InlineData("", false, false)]
    public void Forced_unlogged_shared_entry_uses_the_last_known_public_phase_action(string history, bool firstRevival, bool secondRevival)
    {
        var graph = new MoveGraph("revive", history.Length == 0 ? [] : history.Split(','),
            [Move("bite", "bite"), Move("claw", "claw"), Move("revive", "claw")]);
        Assert.Equal(firstRevival, MoveReference.CanMarkCurrent(graph, "revive", ["bite"]));
        Assert.Equal(secondRevival, MoveReference.CanMarkCurrent(graph, "revive", ["claw"]));
        Assert.False(firstRevival && secondRevival);
    }

    [Fact]
    public void Current_verified_terminal_action_has_no_future_icons()
    {
        var graph = new MoveGraph("explode", ["explode"], [Move("explode", "explode")]);
        var forecast = ProbabilityEngine.Evaluate(graph);
        var section = new MoveReference.Section("", MoveReference.Create(graph), TerminalAction: "explode");
        Assert.Empty(MoveReference.ForecastSteps(graph, forecast, [section]));
        Assert.Equal(2, forecast.Steps.Count);
    }

    [Fact]
    public void Certain_terminal_next_action_stops_the_visible_horizon_without_mutating_raw_forecast()
    {
        var graph = new MoveGraph("stun", ["stun"], [Move("stun", "explode"), Move("explode", "explode")]);
        var forecast = ProbabilityEngine.Evaluate(graph);
        var before = GraphCodec.Encode(forecast);
        var section = new MoveReference.Section("", MoveReference.Create(graph), TerminalAction: "explode");
        var step = Assert.Single(MoveReference.ForecastSteps(graph, forecast, [section]));
        Assert.Equal(1, step.Known["explode"]);
        Assert.Equal(before, GraphCodec.Encode(forecast));
        Assert.Equal(2, MoveReference.ForecastSteps(graph, forecast, [section with { TerminalAction = null }]).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Uncertain_or_mixed_terminal_outcomes_do_not_discard_later_possibilities(int uncertainty)
    {
        var graph = new MoveGraph("start", ["start"], [Move("start", "explode"), Move("explode", "explode")]);
        var step = uncertainty switch
        {
            0 => new StepDistribution(new Dictionary<string, double> { ["explode"] = .5, ["continue"] = .5 }, [], 0),
            1 => new StepDistribution(new Dictionary<string, double> { ["explode"] = .5 }, [], .5),
            _ => new StepDistribution(new Dictionary<string, double> { ["explode"] = .5 }, ["continue"], 0)
        };
        var forecast = new Forecast([step, step], "");
        var section = new MoveReference.Section("", MoveReference.Create(graph), TerminalAction: "explode");
        Assert.Equal(2, MoveReference.ForecastSteps(graph, forecast, [section]).Count);
    }

    [Fact]
    public void Verified_terminal_action_ends_the_reference_without_changing_the_raw_graph()
    {
        var graph = new MoveGraph("stun", ["stun"], [Move("normal", "normal"), Move("stun", "explode"), Move("explode", "explode")]);
        var encoded = GraphCodec.Encode(graph);
        var reference = MoveReference.Create(graph).ForActions(["stun", "explode"], terminal: "explode");
        Assert.Equal(new[] { "stun", "explode" }, reference.Graph.Nodes.Select(node => node.Id));
        Assert.Single(reference.Links);
        Assert.Equal("stun", reference.Links[0].Source);
        Assert.Equal("explode", reference.Links[0].Target);
        Assert.Empty(reference.Graph.Nodes.Single(node => node.Id == "explode").Edges);
        Assert.Equal(encoded, GraphCodec.Encode(graph));
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    public void An_action_reused_after_the_opener_has_at_most_one_proven_current_marker(int stage, bool openingCurrent, bool choiceCurrent)
    {
        string[] history = stage switch
        {
            0 => ["A", "B"],
            1 => ["A", "B", "B"],
            2 => ["A", "foreign", "B"],
            _ => ["A"]
        };
        var graph = new MoveGraph("B", history, [Move("A", "B"), Move("B", "choice"), Move("C", "choice"), Choice(new("B", 1), new("C", 1))]);
        var sections = MoveReference.Create(graph).SharedChoiceSections(graph)!;
        Assert.Equal(openingCurrent, sections[0].MarkCurrent);
        Assert.Equal(choiceCurrent, sections[1].MarkCurrent);
        Assert.False(sections[0].MarkCurrent && sections[1].MarkCurrent);
    }
}
