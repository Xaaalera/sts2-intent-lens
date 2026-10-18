using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace IntentLens.Player;

public static class StateSnapshot
{
    public static AbstractIntent ForecastIntent(MonsterModel monster, string moveId, AbstractIntent intent, int futureStep)
    {
        if (monster.GetType().FullName == "MegaCrit.Sts2.Core.Models.Monsters.TestSubject" && moveId == "MULTI_CLAW_MOVE" &&
            intent is MultiAttackIntent multi && multi.DamageCalc is { } damage)
        {
            // Native current hit count plus the source-verified increment after each prior claw use.
            var previousUses = futureStep + (monster.NextMove?.Id == moveId ? 1 : 0);
            return new MultiAttackIntent((int)damage(), multi.Repeats + previousUses);
        }
        return intent;
    }

    public static IReadOnlyList<MoveReference.Section> ReferenceSections(MonsterModel monster, MoveReference reference, MoveGraph original)
    {
        if (monster.GetType().FullName == "MegaCrit.Sts2.Core.Models.Monsters.GasBomb" && original.Nodes.Count == 1 &&
            original.Nodes[0] is { Id: "EXPLODE_MOVE", Kind: "move", Edges.Count: 0 })
        { return [new("", reference, TerminalAction: "EXPLODE_MOVE")]; }
        if (monster is MegaCrit.Sts2.Core.Models.Monsters.DecimillipedeSegment)
        {
            string[] cycle = ["WRITHE_MOVE", "CONSTRICT_MOVE", "BULK_MOVE"];
            var expected = new HashSet<(string, string)>
            {
                (cycle[0], cycle[1]), (cycle[1], cycle[2]), (cycle[2], cycle[0]),
                ("DEAD_MOVE", "REATTACH_MOVE"), ("REATTACH_MOVE", cycle[0]), ("REATTACH_MOVE", cycle[1]), ("REATTACH_MOVE", cycle[2])
            };
            if (reference.Collapsed && reference.Graph.Nodes.All(node => node.Kind == "move") &&
                reference.Graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal).SetEquals(cycle.Concat(["DEAD_MOVE", "REATTACH_MOVE"])) &&
                reference.Links.Count == expected.Count && expected.SetEquals(reference.Links.Select(link => (link.Source, link.Target))) &&
                reference.Links.All(link => !link.Conditional))
            {
                return [new(TextCatalog.Get("SegmentCycle"), reference.ForActions(cycle), ConditionalScope: true),
                    new(TextCatalog.Get("SegmentRecovery"), reference.ForActions(["REATTACH_MOVE"]), TextCatalog.Get("SegmentRecoveryNote"), true)];
            }
        }
        if (monster.GetType().FullName == "MegaCrit.Sts2.Core.Models.Monsters.TestSubject")
        {
            string[] firstLife = ["BITE_MOVE", "SKULL_BASH_MOVE"];
            string[] secondLife = ["RESPAWN_MOVE", "MULTI_CLAW_MOVE"];
            string[] thirdLife = ["RESPAWN_MOVE", "PHASE3_LACERATE_MOVE", "BIG_POUNCE", "BURNING_GROWL_MOVE"];
            var expected = new HashSet<(string, string)>
            {
                (firstLife[0], firstLife[1]), (firstLife[1], firstLife[0]),
                (secondLife[0], secondLife[1]), (secondLife[1], secondLife[1]),
                (thirdLife[0], thirdLife[1]), (thirdLife[1], thirdLife[2]), (thirdLife[2], thirdLife[3]), (thirdLife[3], thirdLife[1])
            };
            if (reference.Collapsed && reference.Graph.Nodes.All(node => node.Kind == "move") &&
                reference.Graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal).SetEquals(firstLife.Concat(secondLife).Concat(thirdLife)) &&
                reference.Links.Count == expected.Count && expected.SetEquals(reference.Links.Select(link => (link.Source, link.Target))) &&
                reference.Links.Count(link => link.Conditional && link.Source == "RESPAWN_MOVE") == 2)
            {
                return [new(TextCatalog.Get("TestSubjectFirstLife"), reference.ForActions(firstLife), ConditionalScope: true),
                    new(TextCatalog.Get("TestSubjectSecondLife"), reference.ForActions(secondLife), TextCatalog.Get("TestSubjectClawGrowth"), ConditionalScope: true,
                        MarkCurrent: MoveReference.CanMarkCurrent(original, "RESPAWN_MOVE", firstLife)),
                    new(TextCatalog.Get("TestSubjectThirdLife"), reference.ForActions(thirdLife), ConditionalScope: true,
                        MarkCurrent: MoveReference.CanMarkCurrent(original, "RESPAWN_MOVE", ["MULTI_CLAW_MOVE"]))];
            }
        }
        string[] first = ["PUPPET_STRINGS_MOVE", "YOU_ARE_MINE_MOVE", "BURN_BRIGHT_FOR_ME_MOVE"];
        string[] second = ["ENRAGE_MOVE", "OFF_WITH_YOUR_HEAD_MOVE", "EXECUTION_MOVE"];
        var expectedLinks = new HashSet<(string, string)>
        {
            (first[0], first[1]), (first[1], first[2]), (first[1], second[1]), (first[2], first[2]),
            (first[2], second[1]), (second[0], second[1]), (second[1], second[2]), (second[2], second[0])
        };
        if (monster.GetType().FullName == "MegaCrit.Sts2.Core.Models.Monsters.Queen" && reference.Collapsed &&
            reference.Graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal).SetEquals(first.Concat(second)) &&
            expectedLinks.SetEquals(reference.Links.Select(link => (link.Source, link.Target))) &&
            reference.Links.Count(link => link.Conditional && link.ConditionLabel is not null) == 4)
        {
            return [new(TextCatalog.Get("QueenPhaseOne"), reference.ForActions(first), TextCatalog.Get("QueenPhaseOneNote"), true),
                new(TextCatalog.Get("QueenPhaseTwo"), reference.ForActions(second), TextCatalog.Get("QueenPhaseTwoNote"), true)];
        }
        string[] normal = ["PRESSURIZE_MOVE", "STOMP_MOVE", "RAM_MOVE", "SIPHON_MOVE", "PRESSURE_GUN_MOVE", "PRESSURE_UP_MOVE"];
        string[] eruption = ["ABOUT_TO_BLOW_MOVE", "EXPLODE_MOVE"];
        var eruptionLinks = normal.Zip(normal.Skip(1)).Select(pair => (pair.First, pair.Second))
            .Append((normal[^1], normal[1])).Append((eruption[0], eruption[1])).Append((eruption[1], eruption[1])).ToHashSet();
        string[] charge = ["STAMP_MOVE", "PLOW_MOVE"];
        string[] awakened = ["STUN_MOVE", "BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE"];
        var beastLinks = charge.Zip(charge.Skip(1)).Concat(awakened.Zip(awakened.Skip(1)))
            .Select(pair => (pair.First, pair.Second)).Append((charge[^1], charge[^1])).Append((awakened[^1], awakened[1])).ToHashSet();
        var profiles = new Dictionary<string, (string[] Actions, HashSet<(string, string)> Links, Func<IReadOnlyList<MoveReference.Section>> Sections)>
        {
            ["MegaCrit.Sts2.Core.Models.Monsters.WaterfallGiant"] = (normal.Concat(eruption).ToArray(), eruptionLinks,
                () => [new(TextCatalog.Get("WaterfallAlive"), reference.ForActions(normal), ConditionalScope: true),
                    new(TextCatalog.Get("WaterfallDefeated"), reference.ForActions(eruption, terminal: eruption[1]), ConditionalScope: true, TerminalAction: eruption[1])]),
            ["MegaCrit.Sts2.Core.Models.Monsters.CeremonialBeast"] = (charge.Concat(awakened).ToArray(), beastLinks,
                () => [new(TextCatalog.Get("BeastCharging"), reference.ForActions(charge), ConditionalScope: true),
                    new(string.Format(System.Globalization.CultureInfo.InvariantCulture, TextCatalog.Get("BeastAwakened"),
                        ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.PlowPower>().Title.GetFormattedText()),
                        reference.ForActions(original.Current == "STUNNED" ? ["STUNNED", .. awakened.Skip(1)] : awakened), ConditionalScope: true)])
        };
        if (profiles.TryGetValue(monster.GetType().FullName!, out var profile))
        {
            var temporary = reference.Graph.Nodes.Where(node => node.Id == "STUNNED").ToArray();
            var expectedTemporary = monster is MegaCrit.Sts2.Core.Models.Monsters.CeremonialBeast && original.Current == "STUNNED" &&
                temporary is [{ Kind: "move", Edges: [{ Target: "BEAST_CRY_MOVE" }] }];
            if (reference.Graph.Nodes.All(node => node.Kind == "move") &&
                reference.Graph.Nodes.Where(node => !expectedTemporary || node.Id != "STUNNED").Select(node => node.Id).ToHashSet(StringComparer.Ordinal).SetEquals(profile.Actions) &&
                reference.Links.All(link => !link.Conditional) &&
                reference.Links.Count == profile.Links.Count + (expectedTemporary ? 1 : 0) &&
                profile.Links.SetEquals(reference.Links.Where(link => !expectedTemporary || link.Source != "STUNNED").Select(link => (link.Source, link.Target))))
            { return profile.Sections(); }
        }
        return reference.SharedChoiceSections(original) ?? [new("", reference with { Graph = reference.Graph with { Nodes = CycleLayout.Order(reference.Graph) } })];
    }

    // Source-verified 0.107.1 rule descriptions only; never evaluate either predicate or its private latch.
    public static IReadOnlyDictionary<(string Branch, string Target), string> DescribeConditions(MonsterModel monster, MoveGraph graph)
    {
        var descriptions = new Dictionary<(string Branch, string Target), string>();
        if (monster.GetType().FullName != "MegaCrit.Sts2.Core.Models.Monsters.Queen") { return descriptions; }
        foreach (var branch in graph.Nodes.Where(node => node.Kind == "conditional" && (node.Id is "YOURE_MINE_NOW_BRANCH" or "BURN_BRIGHT_FOR_ME_BRANCH")))
        {
            if (branch.Edges.Count != 2 || !branch.Edges.Select(edge => edge.Target).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["BURN_BRIGHT_FOR_ME_MOVE", "OFF_WITH_YOUR_HEAD_MOVE"])) { continue; }
            descriptions[(branch.Id, "BURN_BRIGHT_FOR_ME_MOVE")] = TextCatalog.Get("CompanionAlive");
            descriptions[(branch.Id, "OFF_WITH_YOUR_HEAD_MOVE")] = TextCatalog.Get("CompanionDefeated");
        }
        return descriptions;
    }

    public static MoveGraph Read(MonsterModel monster, NCreature creature)
    {
        var machine = monster.MoveStateMachine;
        if (machine is null) { return new("", [], []); }
        var current = (MonsterState)typeof(MonsterMoveStateMachine).GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(machine)!;
        var states = new Dictionary<string, MonsterState>(machine.States) { [current.Id] = current };
        var currentId = current.Id;
        var titles = monster.GenerateBestiaryMoveList(creature.Visuals).Where(move => !string.IsNullOrWhiteSpace(move.stateId) && !string.IsNullOrWhiteSpace(move.displayName))
            .GroupBy(move => move.stateId!).ToDictionary(group => group.Key, group => group.First().displayName);
        foreach (var id in machine.States.Where(pair => pair.Value is MoveState).Select(pair => pair.Key))
        {
            if (titles.TryGetValue(id, out var title) && title != id) { continue; }
            // Visual subclasses can share their title/localization prefix with the logical base monster.
            var moveName = new LocString(monster.Title.LocTable,
                Regex.Replace(monster.Title.LocEntryKey, @"\.name$", "") + ".moves." + Regex.Replace(id, @"_MOVE$", "") + ".title");
            if (moveName.Exists()) { titles[id] = moveName.GetFormattedText(); }
            else { titles.Remove(id); }
        }
        if (HoverTipFactory.Static(StaticHoverTip.Stun) is HoverTip { Title: { } stunTitle })
        {
            foreach (var stunned in states.Values.OfType<MoveState>().Where(move => move.Intents.Any(intent => intent is StunIntent)))
            { titles[stunned.Id] = stunTitle; }
        }
        var nodes = states.Select(pair => ReadNode(pair.Key, pair.Value, states, titles)).ToArray();
        var history = machine.StateLog.Select(state => state.Id).ToArray();
        return new(currentId, history, nodes);
    }

    private static MoveNode ReadNode(string id, MonsterState state, IReadOnlyDictionary<string, MonsterState> states, IReadOnlyDictionary<string, string> titles)
    {
        if (state.GetType() == typeof(MoveState))
        {
            var move = (MoveState)state;
            var follow = move.FollowUpState is null ? move.FollowUpStateId : states.FirstOrDefault(pair => ReferenceEquals(pair.Value, move.FollowUpState)).Key;
            return new(id, titles.GetValueOrDefault(id,
                titles.GetValueOrDefault(Regex.Replace(id, @"_?\d+$", ""), Humanize(id))), "move", true, follow is null ? [] : [new(follow)]);
        }
        if (state.GetType() == typeof(RandomBranchState))
        {
            var random = (RandomBranchState)state;
            return new(id, TextCatalog.Get("Random"), "random", false, random.States.Select(weight => new MoveEdge(
                weight.stateId, weight.weightLambda is null ? null : ConstantWeight.Read(weight.weightLambda), weight.repeatType.ToString(), weight.maxTimes, weight.cooldown)).ToArray());
        }
        if (state.GetType() == typeof(ConditionalBranchState))
        {
            var branches = (IEnumerable)typeof(ConditionalBranchState).GetField("<States>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(state)!;
            var edges = branches.Cast<object>().Select(branch => new MoveEdge((string)branch.GetType().GetField("id")!.GetValue(branch)!, null)).ToArray();
            return new(id, TextCatalog.Get("Conditional"), "conditional", false, edges);
        }
        return new(id, Humanize(id), "unknown", false, []);
    }

    private static string Humanize(string value) => Regex.Replace(Regex.Replace(value, @"_MOVE$", "").Replace('_', ' ').ToLowerInvariant(), @"\b[a-z]", match => match.Value.ToUpperInvariant());
}
