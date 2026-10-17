using IntentLens.Player;
using Xunit;

namespace IntentLens.Tests;

public sealed class CycleLayoutTests
{
    private static MoveNode Move(string id, string target) => new(id, id, "move", true, [new(target)]);

    [Fact]
    public void Horizontal_choice_keeps_probabilities_on_separate_routes()
    {
        var graph = new MoveGraph("A", ["A"], [Move("A", "choice"), Move("B", "choice"),
            new("choice", "choice", "random", false, [new("A"), new("B")])]);
        var layout = Layout(graph);
        Assert.Single(layout.Positions.Values.Select(position => position.Y).Distinct());
        Assert.True(layout.Size.X > layout.Size.Y);
        Assert.NotEqual(layout.Connections.Single(edge => edge.Source == "choice" && edge.Target == "A").Label,
            layout.Connections.Single(edge => edge.Source == "choice" && edge.Target == "B").Label);
        AssertOrthogonal(layout);
        Assert.True(layout.Size.Y < 260);
    }

    [Fact]
    public void Two_way_cycle_uses_distinct_routes_and_cardinal_arrow_directions()
    {
        var layout = Layout(new("A", ["A"], [Move("A", "B"), Move("B", "A")]));
        AssertOrthogonal(layout);
        Assert.False(layout.Connections[0].Points.SequenceEqual(layout.Connections[1].Points.Reverse()));
    }

    [Fact]
    public void Self_loop_stays_visible_instead_of_collapsing_to_a_zero_length_arrow()
    {
        var layout = Layout(new("A", ["A"], [Move("A", "A")]));
        AssertOrthogonal(layout);
        Assert.True(layout.Connections.Single().Points.Length >= 5);
        Assert.NotEqual(layout.Connections[0].Points[0], layout.Connections[0].Points[^1]);
    }

    [Theory]
    [InlineData("Queen")]
    [InlineData("TheInsatiable")]
    [InlineData("OwlMagistrate")]
    [InlineData("GremlinMerc")]
    public void Enemy_routes_do_not_cross_unrelated_actions_or_drop_transitions(string monster)
    {
        // Topologies inspected from the installed 0.107.1 native state machines.
        MoveGraph graph = monster switch
        {
            "GremlinMerc" => new("gimme", ["gimme"], [Move("gimme", "double"), Move("double", "hehe"), Move("hehe", "gimme")]),
            "Queen" => new("strings", ["strings"], [Move("strings", "mine"), Move("mine", "choice1"),
                Move("burn", "choice2"), new("choice2", "choice2", "conditional", false, [new("burn", null), new("head", null)]),
                new("choice1", "choice1", "conditional", false, [new("burn", null), new("head", null)]),
                Move("head", "execution"), Move("execution", "enrage"), Move("enrage", "head")]),
            "TheInsatiable" => new("liquify", ["liquify"], [Move("liquify", "thrash"), Move("bite", "salivate"),
                Move("thrash", "bite"), Move("thrash2", "thrash"), Move("salivate", "thrash2")]),
            _ => new("scrutiny", ["scrutiny"], [Move("scrutiny", "peck"), Move("peck", "flight"), Move("flight", "verdict"), Move("verdict", "scrutiny")])
        };
        var layout = Layout(graph, measured: true);
        Assert.Equal(graph.Nodes.Sum(node => node.Edges.Count), layout.Connections.Count);
        AssertOrthogonal(layout);
        Assert.True(layout.Size.Y <= 540, $"{monster} exceeds the native tooltip's practical height budget");
        foreach (var current in graph.Nodes.Where(node => node.Kind == "move"))
        {
            var advanced = Layout(graph with { Current = current.Id, History = [.. graph.History, current.Id] }, measured: true);
            foreach (var position in layout.Positions) { Assert.Equal(position.Value, advanced.Positions[position.Key]); }
        }
        if (monster == "TheInsatiable")
        {
            Assert.Single(layout.Positions.Values.Select(position => position.Y).Distinct());
            var chain = new[] { "liquify", "thrash", "bite", "salivate", "thrash2" };
            foreach (var (source, target) in chain.Zip(chain.Skip(1)))
            { Assert.True(layout.Positions[source].X < layout.Positions[target].X); }
            var loop = layout.Connections.Single(edge => edge.Source == "thrash2");
            Assert.Equal("thrash", loop.Target);
            Assert.True(loop.Points.Min(point => point.Y) < layout.Positions["thrash"].Y);
        }
        foreach (var edge in layout.Connections)
        {
            foreach (var node in graph.Nodes.Where(node => node.Id != edge.Source && node.Id != edge.Target))
            {
                var position = layout.Positions[node.Id];
                var nodeIndex = CycleLayout.Order(graph).Select((candidate, index) => (candidate.Id, index)).Single(pair => pair.Id == node.Id).index;
                var width = 80 + nodeIndex % 3 * 48;
                const int height = 120;
                foreach (var (from, to) in edge.Points.Zip(edge.Points.Skip(1)))
                {
                    var crosses = from.X == to.X
                        ? from.X > position.X && from.X < position.X + width && Math.Max(from.Y, to.Y) > position.Y && Math.Min(from.Y, to.Y) < position.Y + height
                        : from.Y > position.Y && from.Y < position.Y + height && Math.Max(from.X, to.X) > position.X && Math.Min(from.X, to.X) < position.X + width;
                    Assert.False(crosses, $"{monster}: {edge.Source} -> {edge.Target} crosses {node.Id}");
                }
            }
        }
        if (monster == "Queen")
        {
            var forecast = ProbabilityEngine.Evaluate(graph);
            Assert.Equal(1d, forecast.Steps[0].Known["mine"]);
            Assert.Equal(["burn", "head"], forecast.Steps[1].Conditional);
            Assert.Empty(forecast.Steps[1].Known);
        }
    }

    [Fact]
    public void Routes_use_measured_native_regions_for_different_title_and_multiattack_widths()
    {
        var graph = new MoveGraph("A", ["A"], [Move("A", "B"), Move("B", "C"), Move("C", "A")]);
        var regions = new Dictionary<string, CycleLayout.Region>
        {
            ["A"] = new(new(20, 120), new(104, 144), new(72, 200), 56),
            ["B"] = new(new(148, 120), new(240, 144), new(268, 200), 176),
            ["C"] = new(new(412, 120), new(80, 144), new(452, 200), 28)
        };
        var layout = CycleLayout.Build(graph, regions);
        var forward = layout.Connections.Single(edge => edge.Source == "A");
        Assert.Equal(new System.Numerics.Vector2(104, 200), forward.Points[0]);
        Assert.Equal(new System.Numerics.Vector2(176, 200), forward.Points[^1]);
        Assert.Equal(524, layout.Size.X);
        Assert.Equal(264, layout.Size.Y);
        AssertOrthogonal(layout);
        Assert.All(layout.Connections.SelectMany(edge => edge.Points), point =>
        {
            Assert.InRange(point.X, 0, layout.Size.X);
            Assert.InRange(point.Y, 0, layout.Size.Y);
        });
    }

    [Fact]
    public void Different_native_icon_centers_still_have_only_right_angle_segments()
    {
        var graph = new MoveGraph("A", ["A"], [Move("A", "B"), Move("B", "A")]);
        var layout = CycleLayout.Build(graph, new Dictionary<string, CycleLayout.Region>
        {
            ["A"] = new(new(20, 64), new(96, 100), new(68, 110), 56),
            ["B"] = new(new(160, 64), new(144, 160), new(232, 154), 116)
        });
        AssertOrthogonal(layout);
        Assert.Equal(4, layout.Connections.Single(edge => edge.Source == "A").Points.Length);
    }

    [Fact]
    public void Empty_graph_has_no_routes_or_action_regions()
    {
        var layout = CycleLayout.Build(new("", [], []), new Dictionary<string, CycleLayout.Region>());
        Assert.Empty(layout.Positions);
        Assert.Empty(layout.Connections);
    }

    [Fact]
    public void Compressed_boss_reference_fits_beside_native_tips_below_the_HUD()
    {
        var obstacles = new (System.Numerics.Vector2 Position, System.Numerics.Vector2 Size)[]
        { (new(869.4f, 295.2f), new(360, 238)) };
        var placement = CycleLayout.Place(new(647, 237), new(1920, 1080), new(1116.5f, 112), 112, obstacles);
        Assert.NotNull(placement);
        Assert.Equal(112, placement.Value.Y);
        Assert.True(placement.Value.X >= obstacles[0].Position.X + obstacles[0].Size.X + 16);
        Assert.True(placement.Value.X + 647 <= 1904);
        Assert.Equal(new System.Numerics.Vector2(869.4f, 295.2f), obstacles[0].Position);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1600, 900)]
    [InlineData(1920, 1080)]
    public void Placement_respects_HUD_and_viewport_bounds_at_desktop_targets(int width, int height)
    {
        var placement = CycleLayout.Place(new(647, 237), new(width, height), new((width - 647) / 2f, 112), 112,
            [(new(width * .1f, 295), new(360, 238))]);
        Assert.NotNull(placement);
        Assert.InRange(placement.Value.Y, 112, height - 237 - 16);
        Assert.InRange(placement.Value.X, 16, width - 647 - 16);
    }

    [Fact]
    public void No_safe_space_does_not_allow_a_reference_under_the_HUD()
    {
        var placement = CycleLayout.Place(new(700, 500), new(800, 600), new(40, 42), 112, []);
        Assert.Null(placement);
    }

    [Fact]
    public void Wide_reference_does_not_escape_to_an_unrelated_corner_when_anchored()
    {
        var placement = CycleLayout.Place(new(767, 322), new(1920, 1080), new(1137, 112), 112,
            [(new(1040, 292), new(359, 114))], anchorX: 1520);
        Assert.Null(placement);
    }

    [Theory]
    [InlineData(719, false)]
    [InlineData(679, true)]
    public void Boss_reference_compacts_to_fit_above_its_own_anchor(int width, bool fits)
    {
        var placement = CycleLayout.Place(new(width, 234), new(1920, 1080), new(1084, 162), 162,
            [(new(812.8f, 266), new(359, 114))], anchorX: 1443.8f);
        Assert.Equal(fits, placement is not null);
        if (placement is not { } position) { return; }
        Assert.Equal(162, position.Y);
        Assert.InRange(1443.8f, position.X, position.X + width);
        Assert.True(position.X >= 1187.8f);
    }

    [Theory]
    [InlineData(10, 112)]
    [InlineData(50, 238)]
    public void Anchored_reference_respects_occupied_relic_rectangles(int relicCount, int safeTop)
    {
        var relics = Enumerable.Range(0, relicCount).Select(index =>
            (new System.Numerics.Vector2(12 + index % 25 * 72, 82 + index / 25 * 72), new System.Numerics.Vector2(68, 68))).ToArray();
        var placement = CycleLayout.Place(new(600, 240), new(1920, 1080), new(1140, safeTop), safeTop,
            relics, anchorX: 1440);
        Assert.NotNull(placement);
        Assert.Equal(safeTop, placement.Value.Y);
        Assert.InRange(1440, placement.Value.X, placement.Value.X + 600);
    }

    [Fact]
    public void Phase_cycle_keeps_declared_action_order_when_history_begins_at_another_action()
    {
        var graph = new MoveGraph("attack", ["attack"], [Move("enrage", "attack"), Move("attack", "execution"), Move("execution", "enrage")]);
        var top = CycleLayout.TopInset(graph, graph.Nodes);
        var regions = new Dictionary<string, CycleLayout.Region>
        {
            ["enrage"] = new(new(20, top), new(80, 100), new(60, top + 50), 56),
            ["attack"] = new(new(116, top), new(128, 100), new(180, top + 50), 56),
            ["execution"] = new(new(260, top), new(96, 100), new(308, top + 50), 56)
        };
        var layout = CycleLayout.Build(graph, regions);
        Assert.Equal(2, layout.Connections.Single(edge => edge.Source == "enrage").Points.Length);
        Assert.True(layout.Connections.Single(edge => edge.Source == "execution").Points.Length > 2);
        Assert.Equal(top, CycleLayout.TopInset(graph with { Current = "execution", History = ["attack", "execution"] }, graph.Nodes));
        AssertOrthogonal(layout);
    }

    [Fact]
    public void Shared_choice_has_one_return_to_the_group_heading_instead_of_pairwise_action_arrows()
    {
        var regions = new Dictionary<string, CycleLayout.Region>
        {
            ["A"] = new(new(20, 48), new(96, 122), new(68, 101), 56),
            ["B"] = new(new(132, 48), new(144, 122), new(204, 101), 56)
        };
        var layout = CycleLayout.Choice("choice", regions);
        var loop = Assert.Single(layout.Connections);
        Assert.Equal("choice", loop.Source);
        Assert.Equal("choice", loop.Target);
        Assert.True(loop.Points[^1].Y < regions.Values.Min(region => region.Position.Y));
        AssertOrthogonal(layout);
        Assert.All(loop.Points, point => { Assert.InRange(point.X, 0, layout.Size.X); Assert.InRange(point.Y, 0, layout.Size.Y); });
    }

    [Fact]
    public void One_action_section_does_not_reserve_the_width_of_a_whole_reference()
    {
        var graph = new MoveGraph("A", ["A"], [new("A", "A", "move", true, [])]);
        var layout = CycleLayout.Build(graph, new Dictionary<string, CycleLayout.Region>
        { ["A"] = new(new(20, 24), new(80, 120), new(60, 100), 56) });
        Assert.InRange(layout.Size.X, 100, 140);
    }

    [Theory]
    [InlineData(6, 4)]
    [InlineData(8, 4)]
    [InlineData(10, 4)]
    [InlineData(6, 3)]
    [InlineData(8, 3)]
    [InlineData(10, 3)]
    public void Wrapped_cycles_keep_every_transition_outside_other_action_cells(int count, int columns)
    {
        var graph = new MoveGraph("0", ["0"], Enumerable.Range(0, count)
            .Select(index => Move(index.ToString(), ((index + 1) % count).ToString())).ToArray());
        var regions = Enumerable.Range(0, count).ToDictionary(index => index.ToString(), index =>
        {
            var row = index / columns;
            var column = row % 2 == 0 ? index % columns : Math.Min(columns, count - row * columns) - 1 - index % columns;
            return new CycleLayout.Region(new(20 + column * 128, 72 + row * 132), new(120, 100), new(80 + column * 128, 122 + row * 132), 56);
        });
        var layout = CycleLayout.Build(graph, regions);
        Assert.Equal(count, layout.Connections.Count);
        AssertOrthogonal(layout);
        foreach (var connection in layout.Connections)
        {
            Assert.All(connection.Points, point => { Assert.InRange(point.X, 0, layout.Size.X); Assert.InRange(point.Y, 0, layout.Size.Y); });
            foreach (var (start, end) in connection.Points.Zip(connection.Points.Skip(1)))
            {
                foreach (var region in regions.Where(pair => pair.Key != connection.Source && pair.Key != connection.Target).Select(pair => pair.Value))
                {
                    Assert.False(start.X == end.X
                        ? start.X > region.Position.X && start.X < region.Position.X + region.Size.X &&
                          Math.Max(start.Y, end.Y) > region.Position.Y && Math.Min(start.Y, end.Y) < region.Position.Y + region.Size.Y
                        : start.Y > region.Position.Y && start.Y < region.Position.Y + region.Size.Y &&
                          Math.Max(start.X, end.X) > region.Position.X && Math.Min(start.X, end.X) < region.Position.X + region.Size.X);
                }
            }
        }
    }

    [Fact]
    public void Wrapped_disconnected_terminal_actions_keep_self_loop_and_entry_routes()
    {
        var graph = new MoveGraph("0", ["0"], [Move("0", "1"), Move("1", "2"), Move("2", "3"), Move("3", "4"),
            Move("4", "5"), Move("5", "1"), Move("6", "6"), Move("7", "6")]);
        var regions = Enumerable.Range(0, 8).ToDictionary(index => index.ToString(), index =>
        {
            var column = index < 4 ? index : 7 - index;
            return new CycleLayout.Region(new(20 + column * 128, 72 + index / 4 * 132), new(120, 100),
                new(80 + column * 128, 122 + index / 4 * 132), 56);
        });
        var layout = CycleLayout.Build(graph, regions);
        Assert.Equal(8, layout.Connections.Count);
        AssertOrthogonal(layout);
        Assert.All(layout.Connections.SelectMany(connection => connection.Points), point =>
        { Assert.InRange(point.X, 0, layout.Size.X); Assert.InRange(point.Y, 0, layout.Size.Y); });
        Assert.True(layout.Connections.Single(connection => connection.Source == "6").Points.Length >= 5);
    }

    [Fact]
    public void Serpentine_continuation_and_return_have_separate_corridors_and_correct_arrow_directions()
    {
        var graph = new MoveGraph("0", ["0"], [Move("0", "1"), Move("1", "2"), Move("2", "3"),
            Move("3", "4"), Move("4", "5"), Move("5", "1")]);
        var regions = Enumerable.Range(0, 6).ToDictionary(index => index.ToString(), index =>
        {
            var column = index < 3 ? index : 5 - index;
            return new CycleLayout.Region(new(20 + column * 128, 72 + index / 3 * 132), new(120, 100),
                new(80 + column * 128, 122 + index / 3 * 132), 56);
        });
        var layout = CycleLayout.Build(graph, regions);
        var continuation = layout.Connections.Single(connection => connection.Source == "2");
        var repeat = layout.Connections.Single(connection => connection.Source == "5");
        Assert.Equal(2, continuation.Points.Length);
        Assert.True(continuation.Points[0].Y < continuation.Points[^1].Y);
        var lowerRow = layout.Connections.Single(connection => connection.Source == "3");
        Assert.True(lowerRow.Points[0].X > lowerRow.Points[^1].X);
        Assert.Contains(repeat.Points, point => point.X < regions.Values.Min(region => region.Position.X));
        foreach (var (firstStart, firstEnd) in continuation.Points.Zip(continuation.Points.Skip(1)))
        {
            foreach (var (secondStart, secondEnd) in repeat.Points.Zip(repeat.Points.Skip(1)))
            {
                Assert.False(firstStart.X == firstEnd.X && secondStart.X == secondEnd.X && firstStart.X == secondStart.X &&
                    Math.Min(Math.Max(firstStart.Y, firstEnd.Y), Math.Max(secondStart.Y, secondEnd.Y)) >
                    Math.Max(Math.Min(firstStart.Y, firstEnd.Y), Math.Min(secondStart.Y, secondEnd.Y)));
                Assert.False(firstStart.Y == firstEnd.Y && secondStart.Y == secondEnd.Y && firstStart.Y == secondStart.Y &&
                    Math.Min(Math.Max(firstStart.X, firstEnd.X), Math.Max(secondStart.X, secondEnd.X)) >
                    Math.Max(Math.Min(firstStart.X, firstEnd.X), Math.Min(secondStart.X, secondEnd.X)));
            }
        }
    }

    private static CycleLayout Layout(MoveGraph graph, bool measured = false)
    {
        var regions = new Dictionary<string, CycleLayout.Region>();
        var position = new System.Numerics.Vector2(20, CycleLayout.TopInset(graph));
        foreach (var (node, index) in CycleLayout.Order(graph).Select((node, index) => (node, index)))
        {
            var width = measured ? 80 + index % 3 * 48 : 124;
            regions[node.Id] = new(position, new(width, measured ? 120 : 96), position + new System.Numerics.Vector2(width / 2f, 50), 56);
            position.X += width + CycleLayout.Gap;
        }
        return CycleLayout.Build(graph, regions);
    }

    private static void AssertOrthogonal(CycleLayout layout)
    {
        foreach (var edge in layout.Connections)
        {
            for (var index = 1; index < edge.Points.Length; index++)
            {
                var previous = edge.Points[index - 1];
                var current = edge.Points[index];
                Assert.True(previous.X == current.X || previous.Y == current.Y, $"Diagonal segment in {edge.Source} -> {edge.Target}");
                Assert.NotEqual(previous, current);
            }
        }
    }
}
