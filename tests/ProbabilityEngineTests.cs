using Xunit;

namespace IntentLens.Tests;

public sealed class ProbabilityEngineTests
{
    [Fact]
    public void Full_cycle_keeps_base_fifty_fifty_while_the_nearest_step_respects_no_repeat()
    {
        var choice = Choice(new("A", 1, "CannotRepeat"), new("B", 1, "CanRepeatXTimes", 2));
        var graph = Graph(Move("A", "choice"), Move("B", "choice"), choice);
        var baseChances = ProbabilityEngine.BaseChances(choice);
        var forecast = ProbabilityEngine.Evaluate(graph);
        Assert.Equal(.5, baseChances["A"]);
        Assert.Equal(.5, baseChances["B"]);
        Assert.Equal(1, forecast.Steps[0].Known["B"]);
        Assert.Equal(.5, forecast.Steps[1].Known["A"]);
        Assert.Equal(.5, forecast.Steps[1].Known["B"]);
    }

    [Fact]
    public void Base_chances_remain_unknown_for_dynamic_weights()
    {
        var chances = ProbabilityEngine.BaseChances(Choice(new("A", null), new("B", 1)));
        Assert.Null(chances["A"]);
        Assert.Null(chances["B"]);
    }
    [Fact]
    public void Constant_weight_is_read_without_running_a_delegate()
    {
        Func<float> constant = () => 3f;
        Assert.Equal(3, ConstantWeight.Read(constant));
    }

    [Fact]
    public void Dynamic_weight_is_not_executed_even_when_it_has_side_effects()
    {
        var calls = 0;
        Func<float> dynamicWeight = () => { calls++; return 2f; };
        Assert.Null(ConstantWeight.Read(dynamicWeight));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Strings_resolve_from_the_packaged_catalog()
    {
        Assert.NotEqual("Unknown", TextCatalog.Get("Unavailable"));
        Assert.Contains("{0}", TextCatalog.Get("Step"));
        var assembly = typeof(TextCatalog).Assembly;
        using var englishStream = assembly.GetManifestResourceStream("IntentLens.Localization.eng.json")!;
        var english = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(englishStream)!;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("IntentLens.Localization.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var translated = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
            Assert.Equal(english.Keys.Order(), translated.Keys.Order());
            foreach (var key in english.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(translated[key]), $"{name}: {key}");
                Assert.Equal(System.Text.CompositeFormat.Parse(english[key]).MinimumArgumentCount,
                    System.Text.CompositeFormat.Parse(translated[key]).MinimumArgumentCount);
            }
        }
    }
    private static MoveNode Move(string id, string next) => new(id, id, "move", true, [new(next)]);
    private static MoveNode Choice(params MoveEdge[] edges) => new("choice", "choice", "random", false, edges);
    private static MoveGraph Graph(params MoveNode[] nodes) => new("A", ["A"], nodes);

    [Fact]
    public void Cycle_skips_the_public_current_move_and_shows_two_following_moves()
    {
        var result = ProbabilityEngine.Evaluate(Graph(Move("A", "B"), Move("B", "A")));
        Assert.Equal(1, result.Steps[0].Known["B"]);
        Assert.Equal(1, result.Steps[1].Known["A"]);
    }

    [Fact]
    public void Weights_are_normalized_and_second_step_keeps_branch_history()
    {
        var graph = Graph(Move("A", "choice"), Move("B", "choice"),
            Choice(new("A", 3, "CannotRepeat"), new("B", 1, "CannotRepeat")));
        var result = ProbabilityEngine.Evaluate(graph);
        Assert.Equal(1, result.Steps[0].Known["B"]);
        Assert.Equal(1, result.Steps[1].Known["A"]);
        Assert.Equal(["A"], graph.History);
    }

    [Fact]
    public void Second_step_merges_path_probabilities_without_picking_a_seeded_result()
    {
        var result = ProbabilityEngine.Evaluate(Graph(Move("A", "choice"), Move("B", "C"), Move("C", "C"), Choice(new("B", 3), new("C", 1))));
        Assert.Equal(.75, result.Steps[0].Known["B"]);
        Assert.Equal(.25, result.Steps[0].Known["C"]);
        Assert.Equal(1, result.Steps[1].Known["C"]);
    }

    [Theory]
    [InlineData("UseOnlyOnce", 0, 0)]
    [InlineData("CanRepeatXTimes", 1, 0)]
    [InlineData("CanRepeatForever", 0, 1)]
    public void Repeat_and_cooldown_rules_remove_ineligible_moves(string repeat, int limit, int cooldown)
    {
        var result = ProbabilityEngine.Evaluate(Graph(Move("A", "choice"), Move("B", "choice"), Choice(new("A", 9, repeat, limit, cooldown), new("B", 1))));
        Assert.Equal(1, result.Steps[0].Known["B"]);
    }

    [Fact]
    public void Unknown_dynamic_weights_do_not_invent_equal_probabilities()
    {
        var result = ProbabilityEngine.Evaluate(Graph(Move("A", "choice"), Move("B", "choice"), Choice(new("A", null), new("B", 1))));
        Assert.Empty(result.Steps[0].Known);
        Assert.Equal(1, result.Steps[0].Unknown);
        Assert.Contains("B", result.Steps[0].Conditional);
        Assert.Equal(1, result.Steps[1].Unknown);
    }

    [Fact]
    public void Conditional_branch_stays_uncertain_instead_of_executing_the_condition()
    {
        var result = ProbabilityEngine.Evaluate(Graph(Move("A", "condition"), Move("B", "A"), new("condition", "condition", "conditional", false, [new("A"), new("B")])));
        Assert.Equal(1, result.Steps[0].Unknown);
        Assert.Empty(result.Steps[0].Known);
    }

    [Fact]
    public void Conditional_second_step_advances_possible_moves_without_inventing_probabilities()
    {
        var result = ProbabilityEngine.Evaluate(Graph(Move("A", "condition"), Move("B", "D"), Move("C", "E"), Move("D", "A"), Move("E", "A"),
            new("condition", "condition", "conditional", false, [new("B"), new("C")])));
        Assert.Equal(["B", "C"], result.Steps[0].Conditional);
        Assert.Equal(["D", "E"], result.Steps[1].Conditional);
        Assert.Empty(result.Steps[1].Known);
        Assert.Equal(1, result.Steps[1].Unknown);
    }

    [Fact]
    public void Partial_uncertainty_preserves_known_probability_mass()
    {
        var result = ProbabilityEngine.Evaluate(Graph(Move("A", "choice"), Move("B", "A"), Move("C", "A"),
            Choice(new("B", 1), new("conditional", 1)), new("conditional", "conditional", "conditional", false, [new("C")])));
        Assert.Equal(.5, result.Steps[0].Known["B"]);
        Assert.Equal(.5, result.Steps[0].Unknown);
        Assert.Equal(.5, result.Steps[1].Known["A"]);
        Assert.Equal(.5, result.Steps[1].Unknown);
    }

    [Fact]
    public void Cooldown_counts_only_move_history_and_expires_after_other_moves()
    {
        var nodes = new[] { Move("A", "choice"), Move("B", "choice"), Choice(new("A", 1, Cooldown: 1), new("B", 1)) }.ToDictionary(node => node.Id);
        Assert.False(ProbabilityEngine.Eligible(new("A", Cooldown: 1), ["A", "choice"], nodes));
        Assert.True(ProbabilityEngine.Eligible(new("A", Cooldown: 1), ["A", "B", "choice"], nodes));
    }

    [Fact]
    public void Non_move_cycles_are_bounded_and_explicitly_unknown()
    {
        var result = ProbabilityEngine.Evaluate(Graph(Move("A", "choice"), Choice(new MoveEdge("choice"))));
        Assert.Equal(1, result.Steps[0].Unknown);
    }

    [Fact]
    public void Codec_preserves_null_weights_and_probability_mass()
    {
        var graph = Graph(Move("A", "choice"), Move("B", "A"), Choice(new MoveEdge("B", 3)));
        var result = GraphCodec.DecodeForecast(GraphCodec.Encode(ProbabilityEngine.Evaluate(GraphCodec.Decode(GraphCodec.Encode(graph)))));
        Assert.Equal(1, result.Steps[0].Known.Values.Sum() + result.Steps[0].Unknown);
    }
}
