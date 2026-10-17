using System.Collections;
using System.Globalization;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes;

namespace IntentLens.Player;

internal sealed class IntentOverlay : IDisposable
{
    private readonly NCreature _creature;
    private readonly HBoxContainer _summary;
    private readonly Action _refresh;
    private bool _expandRequested;
    private string _snapshot = "";
    private string _compact = "";
    private Control? _graphTip;
    private object? _nativeTipSet;
    private Control? _nativeTipContainer;
    private string _description = "";
    private MoveGraph? _graph;
    private MoveReference? _reference;
    private string _renderedDescription = "";
    private bool _compactReference;
    private BoxContainer? _referenceBody;
    private VBoxContainer? _referenceRoot;
    private MegaLabel? _referenceFooter;
    private IReadOnlyList<MoveReference.Section> _sections = [];
    private readonly List<ReferenceView> _referenceViews = [];
    private sealed record ReferenceView(MoveReference.Section Section, Control Canvas, GridContainer Grid, Control Routes, MegaLabel Note)
    {
        internal string Signature { get; set; } = "";
    }
    internal int NodeCount { get; private set; }
    internal int EdgeCount { get; private set; }
    internal bool Expanded => GodotObject.IsInstanceValid(_graphTip) && _graphTip!.IsVisibleInTree() && _graphTip.Modulate.A > 0;
    internal bool RetainHover => false;

    internal IntentOverlay(NCreature creature, Action refresh)
    {
        _creature = creature;
        _refresh = refresh;
        _summary = new HBoxContainer { Name = "IntentLensSummary", MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 2,
            Modulate = new Color(1, 1, 1, .60f) };
        _summary.AddThemeConstantOverride("separation", 14);
        creature.Hitbox.AddChild(_summary);
        creature.Hitbox.MouseEntered += _refresh;
        creature.Hitbox.MouseExited += _refresh;
        creature.Hitbox.FocusEntered += _refresh;
        creature.Hitbox.FocusExited += _refresh;
    }

    internal void Refresh(MoveGraph graph, Forecast forecast, bool expanded)
    {
        var snapshot = GraphCodec.Encode(graph) + GraphCodec.Encode(forecast);
        if (snapshot != _snapshot)
        {
            _snapshot = snapshot;
            _description = DescribeGraph(graph);
            _graph = graph;
            _reference = MoveReference.Create(graph, StateSnapshot.DescribeConditions(_creature.Entity.Monster!, graph));
            _sections = StateSnapshot.ReferenceSections(_creature.Entity.Monster!, _reference, graph);
            RebuildSummary(MoveReference.ForecastSteps(graph, forecast, _sections));
            NodeCount = graph.Nodes.Count;
            EdgeCount = graph.Nodes.Sum(node => node.Edges.Count);
        }
        _expandRequested = expanded;
        Present();
    }

    internal void Present()
    {
        if (_graph is null || !GodotObject.IsInstanceValid(_summary)) { return; }
        _summary.ResetSize();
        var intentBounds = _creature.IntentContainer.GetGlobalRect();
        var intentAnchor = _creature.Hitbox.GetGlobalTransform().AffineInverse() * new Vector2(intentBounds.GetCenter().X, intentBounds.Position.Y);
        _summary.Position = new(intentAnchor.X - _summary.Size.X / 2, intentAnchor.Y - _summary.Size.Y - 12);
        PrepareNativeGraph();
        if (_expandRequested) { UpdateNativeTip(); }
        else
        {
            // Keep the transparent native layout warm while the ordinary compact view is shown.
            _graphTip!.Modulate = new(1, 1, 1, 0);
            UpdateReferenceLayout();
            _graphTip.ResetSize();
        }
        _summary.Visible = !Expanded;
    }

    private void RebuildSummary(IReadOnlyList<StepDistribution> steps)
    {
        foreach (var child in _summary.GetChildren()) { _summary.RemoveChild(child); child.QueueFree(); }
        var debug = new List<string>();
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (index > 0)
            {
                AddTurnArrow(_summary);
            }
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 5);
            _summary.AddChild(row);
            var outcomes = step.Known.OrderByDescending(pair => pair.Value).Select(pair => (Id: pair.Key, Chance: (double?)pair.Value))
                .Concat(step.Conditional.Select(id => (Id: id, Chance: (double?)null)));
            foreach (var outcome in outcomes)
            {
                var group = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddChild(group);
                var icons = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                group.AddChild(icons);
                AddIntentIcons(icons, outcome.Id, animated: true, futureStep: index);
                var chance = Text(string.Format(CultureInfo.InvariantCulture, TextCatalog.Get("ChanceFormat"),
                    outcome.Chance?.ToString("0.#%", CultureInfo.InvariantCulture) ?? TextCatalog.Get("UnknownSymbol")), 16);
                var animationOwner = icons.FindChildren("IntentLensPrediction", "", true, false).OfType<NIntent>().FirstOrDefault();
                if (animationOwner is not null)
                {
                    chance.Name = "IntentLensChance";
                    chance.Position = new(-4 / .85f, 66);
                    chance.Size = new(icons.GetCombinedMinimumSize().X / .85f, 22);
                    animationOwner.GetNode<Control>("%IntentHolder").AddChild(chance);
                }
                else { group.AddChild(chance); }
                debug.Add($"+{index + 1}:{outcome.Id}:{outcome.Chance?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
            }
            if (step.Unknown > .000001 && step.Conditional.Count == 0) { row.AddChild(Text(TextCatalog.Get("UnknownSymbol"), 23)); }
        }
        _compact = string.Join(";", debug);
    }

    private static void AddTurnArrow(Control parent)
    {
        var animationOwner = parent.FindChildren("IntentLensPrediction", "", true, false).OfType<NIntent>().FirstOrDefault();
        var slot = new Control { CustomMinimumSize = new(48, 56), MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(slot);
        NativeArrow.Create(slot, [new(7, 34), new(53, 34)]);
        if (animationOwner is not null)
        {
            Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(slot) || slot.IsQueuedForDeletion() || !GodotObject.IsInstanceValid(animationOwner)) { return; }
                var animatedHolder = animationOwner.GetNode<Control>("%IntentHolder");
                foreach (var child in slot.GetChildren().OfType<Node2D>().ToArray())
                {
                    child.Reparent(animatedHolder, true);
                    child.Position += animatedHolder.Position;
                }
            }).CallDeferred();
        }
    }

    private void AddIntentIcons(Control parent, string moveId, bool animated, int futureStep = 0)
    {
        var machine = _creature.Entity.Monster?.MoveStateMachine;
        var state = machine?.States.GetValueOrDefault(moveId) ?? (_creature.Entity.Monster?.NextMove is { } current && current.Id == moveId ? current : null);
        if (state is not MoveState move)
        { parent.AddChild(Text(TextCatalog.Get("UnknownSymbol"), 23)); return; }
        foreach (var intent in move.Intents)
        {
            var holder = new Control { CustomMinimumSize = animated ? new(64, 84) : new(_compactReference ? 46 : 56, _compactReference ? 48 : 58), MouseFilter = Control.MouseFilterEnum.Ignore };
            parent.AddChild(holder);
            var icon = NIntent.Create((float)_creature.GetHashCode() * .01f);
            icon.Name = "IntentLensPrediction";
            icon.MouseFilter = Control.MouseFilterEnum.Ignore;
            icon.Scale = Vector2.One * (animated ? .85f : _compactReference ? .65f : .80f);
            icon.Position = new(4, animated ? 0 : -6);
            holder.AddChild(icon);
            var displayed = animated ? StateSnapshot.ForecastIntent(_creature.Entity.Monster!, moveId, intent, futureStep) : intent;
            icon.UpdateIntent(displayed, _creature.Entity.CombatState?.PlayerCreatures ?? [], _creature.Entity);
            if (!animated)
            {
                icon.ProcessMode = Node.ProcessModeEnum.Disabled;
                icon.GetNode<Sprite2D>("%Intent").Texture = intent.GetTexture(_creature.Entity.CombatState?.PlayerCreatures ?? [], _creature.Entity);
            }
        }
    }

    private string DescribeGraph(MoveGraph graph)
    {
        var nodes = graph.Nodes.ToDictionary(node => node.Id);
        var numbers = graph.Nodes.Select((node, index) => (node.Id, Number: index + 1)).ToDictionary(pair => pair.Id, pair => pair.Number);
        var lines = new List<string>();
        foreach (var node in graph.Nodes)
        {
            var title = node.Id == graph.Current ? TextCatalog.Get("Current") + ": " + node.Title : node.Title;
            lines.Add($"[b]{numbers[node.Id]}. {title}[/b]");
            var chances = ProbabilityEngine.BaseChances(node);
            var next = node.Edges.Select(edge =>
            {
                var target = numbers.TryGetValue(edge.Target, out var number) ? number.ToString(CultureInfo.InvariantCulture) : TextCatalog.Get("UnknownSymbol");
                var chance = node.Kind == "random" && chances.GetValueOrDefault(edge.Target) is double probability
                    ? probability.ToString("0.#%", CultureInfo.InvariantCulture)
                    : node.Kind == "move" ? "" : TextCatalog.Get("UnknownSymbol");
                var rules = Restriction(edge);
                return $"{target} {chance}{(rules.Length == 0 ? "" : " (" + rules + ")")}";
            });
            lines.Add("    → " + string.Join(" / ", next));
        }
        return string.Join('\n', lines);
    }

    private void PrepareNativeGraph()
    {
        if (!GodotObject.IsInstanceValid(_graphTip))
        {
            // Reuse the game's scene without ever adding it to the native side-tip flow.
            _graphTip = PreloadManager.Cache.GetScene("res://scenes/ui/hover_tip.tscn").Instantiate<Control>();
            _graphTip.Modulate = new(1, 1, 1, 0);
            _creature.Hitbox.AddChild(_graphTip);
            _graphTip.GetNode<MegaLabel>("%Title").GetParent<Control>().Visible = false;
            _graphTip.GetNode<TextureRect>("%Icon").Texture = null;
            _graphTip.Name = "IntentLensNativeGraph";
            _graphTip.MouseFilter = Control.MouseFilterEnum.Ignore;
            foreach (var control in _graphTip.FindChildren("*", "Control", true, false).OfType<Control>())
            { control.MouseFilter = Control.MouseFilterEnum.Ignore; }
            _graphTip.ZIndex = 10;
            _graphTip.Scale = Vector2.One / _creature.Hitbox.GetGlobalTransform().Scale;
        }
        if (_renderedDescription != _description)
        {
            RenderNativeGraph(_graphTip!);
            _renderedDescription = _description;
        }
    }

    private void UpdateNativeTip()
    {
        var wasExpanded = Expanded;
        var sets = (IDictionary)typeof(NHoverTipSet).GetField("_activeHoverTips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var nativeSet = sets[_creature.Hitbox];
        if (nativeSet is null) { _graphTip!.Modulate = new(1, 1, 1, 0); return; }
        if (!ReferenceEquals(nativeSet, _nativeTipSet) || !GodotObject.IsInstanceValid(_nativeTipContainer))
        {
            _nativeTipSet = nativeSet;
            _nativeTipContainer = (Control)nativeSet.GetType().GetField("_textHoverTipContainer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(nativeSet)!;
        }
        _graphTip!.Visible = true;
        if (!UpdateReferenceLayout()) { _graphTip.Modulate = new(1, 1, 1, 0); return; }
        _graphTip.Modulate = Colors.White;
        _graphTip.ResetSize();
        var tipContainer = _nativeTipContainer!;
        var tips = tipContainer.GetChildren().OfType<Control>().Where(child => !child.IsQueuedForDeletion()).ToArray();
        var viewport = _creature.GetViewportRect().Size;
        var anchor = _creature.Hitbox.GetGlobalRect();
        var graphSize = _graphTip!.GetGlobalRect().Size;
        // The TopBar root covers the viewport; its BgImage is the actual occupied HUD strip.
        var background = NRun.Instance?.GlobalUi?.TopBar?.GetNodeOrNull<Control>("BgImage");
        var safeTop = Math.Max(112, background is { } && background.IsVisibleInTree() ? background.GetGlobalRect().End.Y + 12 : 112);
        var relics = NRun.Instance?.GlobalUi?.RelicInventory?.RelicNodes
            .Where(relic => GodotObject.IsInstanceValid(relic) && relic.IsVisibleInTree() && !relic.IsQueuedForDeletion())
            .Select(relic => relic.GetGlobalRect()).ToArray() ?? [];
        var preferred = new Vector2(Math.Clamp(anchor.GetCenter().X - graphSize.X / 2, 16, Math.Max(16, viewport.X - graphSize.X - 16)),
            Math.Max(safeTop, anchor.Position.Y - graphSize.Y - 16));
        safeTop = Math.Max(safeTop, relics.Where(bounds => bounds.Position.X < preferred.X + graphSize.X + 16 && bounds.End.X + 16 > preferred.X)
            .Select(bounds => bounds.End.Y + 16).DefaultIfEmpty(0).Max());
        preferred.Y = Math.Max(preferred.Y, safeTop);
        if (wasExpanded && _graphTip.GlobalPosition.Y >= safeTop && _graphTip.GlobalPosition.Y <= preferred.Y)
        { preferred = _graphTip.GlobalPosition; }
        var obstacles = tips.Select(tip => tip.GetGlobalRect())
            .Concat(relics)
            .Select(bounds =>
            (new System.Numerics.Vector2(bounds.Position.X, bounds.Position.Y), new System.Numerics.Vector2(bounds.Size.X, bounds.Size.Y))).ToArray();
        var placement = CycleLayout.Place(new(graphSize.X, graphSize.Y), new(viewport.X, viewport.Y), new(preferred.X, preferred.Y), safeTop, obstacles, anchor.GetCenter().X);
        if (placement is null)
        {
            if (_referenceViews.Any(view => view.Grid.GetThemeConstant("h_separation") > CycleLayout.CellSize))
            { foreach (var view in _referenceViews) { view.Grid.AddThemeConstantOverride("h_separation", CycleLayout.CellSize); } }
            else if (!_compactReference)
            {
                _compactReference = true;
                _renderedDescription = "";
            }
            else
            {
                foreach (var view in _referenceViews.Where(view => view.Section.Selection is null && view.Grid.Columns > 3 &&
                    view.Section.Reference.Graph.Nodes.All(node => node.Kind == "move" && node.Edges.Count <= 1)))
                {
                    view.Grid.Columns = Math.Max(3, Math.Min(4, view.Grid.Columns - 1));
                    view.Grid.AddThemeConstantOverride("v_separation", CycleLayout.CellSize * 4);
                }
            }
            _graphTip.Modulate = new(1, 1, 1, 0);
            return;
        }
        _graphTip.GlobalPosition = new(placement.Value.X, placement.Value.Y);
    }

    private void RenderNativeGraph(Control tip)
    {
        var description = tip.GetNode<MegaRichTextLabel>("%Description");
        description.Text = "";
        foreach (var child in description.GetChildren().Where(child => child.Name.ToString() == "IntentLensDiagram").ToArray())
        { description.RemoveChild(child); child.QueueFree(); }
        var wrappedColumns = _referenceViews.Where(view => view.Grid.Columns < view.Section.Reference.Graph.Nodes.Count)
            .Select(view => view.Grid.Columns).DefaultIfEmpty(int.MaxValue).Min();
        _referenceViews.Clear();
        _referenceRoot = new VBoxContainer { Name = "IntentLensDiagram", MouseFilter = Control.MouseFilterEnum.Ignore };
        _referenceRoot.AddThemeConstantOverride("separation", 8);
        description.AddChild(_referenceRoot);
        _referenceBody = new BoxContainer { Name = "IntentLensGroups", MouseFilter = Control.MouseFilterEnum.Ignore };
        _referenceBody.AddThemeConstantOverride("separation", _compactReference ? CycleLayout.CellSize : CycleLayout.Gap);
        _referenceRoot.AddChild(_referenceBody);
        var sharedChoice = _sections.Any(section => section.Selection is not null);
        var footerNotes = new List<string>();
        _referenceFooter = null;
        foreach (var section in _sections)
        {
            var graph = section.Reference.Graph;
            var box = new VBoxContainer { Name = "IntentLensPhase", MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 4);
            _referenceBody.AddChild(box);
            if (section.Title.Length > 0)
            {
                var heading = Text(section.Title, _compactReference ? 14 : 16);
                heading.HorizontalAlignment = HorizontalAlignment.Left;
                box.AddChild(heading);
            }
            var canvas = new Control { Name = "IntentLensPhaseCanvas", MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddChild(canvas);
            var routes = new Control { Name = "IntentLensRoutes", MouseFilter = Control.MouseFilterEnum.Ignore };
            canvas.AddChild(routes);
            var grid = new GridContainer { Name = "IntentLensActionGrid", Columns = Math.Max(1, section.Selection is null &&
                graph.Nodes.All(node => node.Kind == "move" && node.Edges.Count <= 1) ? Math.Min(wrappedColumns, graph.Nodes.Count) : graph.Nodes.Count),
                Position = new(_compactReference ? 12 : 20, section.Selection is null ? Math.Max(24, CycleLayout.TopInset(graph, graph.Nodes) - (_compactReference ? 24 : 0)) : 48), MouseFilter = Control.MouseFilterEnum.Ignore };
            grid.AddThemeConstantOverride("h_separation", CycleLayout.CellSize * 2);
            if (grid.Columns < graph.Nodes.Count) { grid.AddThemeConstantOverride("v_separation", CycleLayout.CellSize * 4); }
            canvas.AddChild(grid);
            var titleHeight = 0f;
            var iconHeight = 0f;
            foreach (var node in graph.Nodes)
            {
                var nodeContainer = new VBoxContainer { Name = "IntentLensMoveNode_" + node.Id, MouseFilter = Control.MouseFilterEnum.Ignore };
                nodeContainer.AddThemeConstantOverride("separation", 0);
                grid.AddChild(nodeContainer);
                var title = Text(node.Title, _compactReference ? 14 : 16);
                title.Name = "IntentLensMoveTitle";
                title.VerticalAlignment = VerticalAlignment.Center;
                title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                title.ClipText = false;
                title.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
                var icons = new HBoxContainer { Name = "IntentLensMoveIcons",
                    Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
                nodeContainer.AddChild(title);
                nodeContainer.AddChild(icons);
                if (node.Kind == "move")
                {
                    AddIntentIcons(icons, node.Id, animated: false);
                }
                else
                {
                    icons.AddChild(Text(TextCatalog.Get("UnknownSymbol"), 27));
                }
                if (section.Selection is not null)
                {
                    var probability = ProbabilityEngine.BaseChances(section.Selection).GetValueOrDefault(node.Id);
                    var chance = Text(string.Format(CultureInfo.InvariantCulture, TextCatalog.Get("ChanceFormat"),
                        probability?.ToString("0.#%", CultureInfo.InvariantCulture) ?? TextCatalog.Get("UnknownSymbol")), 14);
                    chance.Name = "IntentLensBaseChance";
                    chance.SetTextAutoSize(string.Join('\n', new[] { chance.Text,
                        Restriction(section.Selection.Edges.First(edge => edge.Target == node.Id)) }.Where(text => text.Length > 0)));
                    nodeContainer.AddChild(chance);
                }
                var current = Text(section.MarkCurrent && node.Id == graph.Current ? TextCatalog.Get("Current") : "", 12);
                current.Name = "IntentLensCurrentMove";
                current.Visible = !_compactReference;
                nodeContainer.AddChild(current);
                if (section.MarkCurrent && node.Id == graph.Current) { title.AddThemeColorOverride("font_color", StsColors.gold); }
                var font = title.GetThemeFont("font");
                var wordWidth = node.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(word => font.GetStringSize(word, fontSize: title.GetThemeFontSize("font_size")).X).DefaultIfEmpty(0).Max();
                var preferredTitleWidth = Math.Min(124, Math.Max(wordWidth,
                    Math.Min(sharedChoice && section.Selection is null ? 80 : 124, font.GetStringSize(node.Title, fontSize: title.GetThemeFontSize("font_size")).X)));
                var width = MathF.Ceiling(Math.Max(72, Math.Max(preferredTitleWidth, icons.GetCombinedMinimumSize().X)) / CycleLayout.CellSize) * CycleLayout.CellSize;
                nodeContainer.CustomMinimumSize = new(width, 0);
                title.Size = new(width, 0);
                titleHeight = Math.Max(titleHeight, font.GetMultilineStringSize(node.Title, width: width, fontSize: title.GetThemeFontSize("font_size"),
                    brkFlags: TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive).Y + 4);
                iconHeight = Math.Max(iconHeight, icons.GetCombinedMinimumSize().Y);
            }
            // Equal native row minimums align intent centers without absolute title/icon offsets.
            foreach (var node in grid.GetChildren().OfType<VBoxContainer>())
            {
                node.GetChild<Control>(0).CustomMinimumSize = new(0, titleHeight);
                node.GetChild<Control>(1).CustomMinimumSize = new(0, iconHeight);
                node.GetChild<Control>(2).CustomMinimumSize = new(0, _compactReference ? 0 : section.Selection is null ? 18 : 22);
                if (section.Selection is not null) { node.GetChild<Control>(3).CustomMinimumSize = new(0, 18); }
            }
            grid.ResetSize();
    
            var originals = _graph!.Nodes.ToDictionary(node => node.Id);
            var ruleSources = sharedChoice || section.ConditionalScope ? Enumerable.Empty<MoveNode>() : _graph.Nodes;
            var restrictions = ruleSources.SelectMany(node => node.Edges)
                .Where(edge => graph.Nodes.Any(node => node.Id == edge.Target) && Restriction(edge).Length > 0)
                .Select(edge => originals[edge.Target].Title + ": " + Restriction(edge)).Distinct().ToList();
            if (section.Note.Length > 0 && section.Selection is null && !_compactReference) { restrictions.Insert(0, section.Note); }
            if (!section.ConditionalScope && section.Reference.Links.Any(link => link.Conditional && link.ConditionLabel is null))
            { restrictions.Add(TextCatalog.Get("ConditionalExplanation")); }
            if (section.Selection is null && section.Reference.Links.Any(link => link.Branched && link.Chance is not null))
            { restrictions.Add(TextCatalog.Get("BaseChanceExplanation")); }
            if (sharedChoice) { footerNotes.AddRange(restrictions); restrictions.Clear(); }
            var note = Text("", 14);
            note.Size = new(grid.GetCombinedMinimumSize().X + 52, 20);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.HorizontalAlignment = HorizontalAlignment.Left;
            note.VerticalAlignment = VerticalAlignment.Top;
            note.SetTextAutoSize(string.Join('\n', restrictions));
            canvas.AddChild(note);
            _referenceViews.Add(new(section, canvas, grid, routes, note));
        }
        if (sharedChoice && footerNotes.Count > 0)
        {
            _referenceFooter = Text("", 14);
            _referenceFooter.Size = new(Math.Max(200, _referenceBody.GetCombinedMinimumSize().X), 20);
            _referenceFooter.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _referenceFooter.HorizontalAlignment = HorizontalAlignment.Left;
            _referenceFooter.VerticalAlignment = VerticalAlignment.Top;
            _referenceFooter.SetTextAutoSize(string.Join('\n', footerNotes.Distinct()));
            _referenceRoot.AddChild(_referenceFooter);
        }
    }

    private bool UpdateReferenceLayout()
    {
        var ready = true;
        foreach (var view in _referenceViews) { if (!UpdateGraphLayout(view)) { ready = false; } }
        _referenceBody!.ResetSize();
        foreach (var view in _referenceViews.Where(view => view.Note.Visible))
        {
            var height = view.Note.Position.Y + view.Note.Size.Y + 6;
            if (height > view.Canvas.CustomMinimumSize.Y) { view.Canvas.CustomMinimumSize = new(view.Canvas.CustomMinimumSize.X, height); }
        }
        if (_referenceFooter is not null)
        {
            var width = _referenceBody.GetCombinedMinimumSize().X;
            _referenceFooter.CustomMinimumSize = Vector2.Zero;
            _referenceFooter.Size = new(width, 20);
            _referenceFooter.CustomMinimumSize = new(0, _referenceFooter.GetCombinedMinimumSize().Y);
        }
        _referenceRoot!.ResetSize();
        _referenceRoot.GetParent<Control>().CustomMinimumSize = _referenceRoot.GetCombinedMinimumSize();
        return ready;
    }

    private bool UpdateGraphLayout(ReferenceView view)
    {
        var graph = view.Section.Reference.Graph;
        var blocks = view.Grid.GetChildren().OfType<VBoxContainer>().ToArray();
        if (blocks.Length > view.Grid.Columns)
        {
            var ordered = graph.Nodes.Select(node => blocks.Single(block => block.Name.ToString() == "IntentLensMoveNode_" + node.Id))
                .Chunk(view.Grid.Columns).SelectMany((row, index) => index % 2 == 0 ? row : row.Reverse()).ToArray();
            if (!blocks.SequenceEqual(ordered))
            {
                for (var index = 0; index < ordered.Length; index++) { view.Grid.MoveChild(ordered[index], index); }
                return false;
            }
        }
        var minimumChanged = false;
        foreach (var block in blocks)
        {
            var icons = block.GetChild<HBoxContainer>(1);
            foreach (var holder in icons.GetChildren().OfType<Control>())
            {
                var intent = holder.GetChildren().OfType<NIntent>().FirstOrDefault();
                if (intent is null) { continue; }
                var width = Math.Max(_compactReference ? 46 : 56, MathF.Ceiling(intent.GetNode<MegaRichTextLabel>("%Value").GetContentWidth() * intent.Scale.X + 8));
                if (holder.CustomMinimumSize.X == width) { continue; }
                holder.CustomMinimumSize = new(width, holder.CustomMinimumSize.Y);
                intent.Position = new(4 + (width - (_compactReference ? 46 : 56)) / 2, -6);
                minimumChanged = true;
            }
            var minimumWidth = MathF.Ceiling(Math.Max(block.CustomMinimumSize.X, icons.GetCombinedMinimumSize().X) / CycleLayout.CellSize) * CycleLayout.CellSize;
            block.CustomMinimumSize = new(minimumWidth, block.CustomMinimumSize.Y);
        }
        if (minimumChanged) { return false; }
        // Container sorting is deferred by Godot. Do not route from initial zero rectangles.
        if (blocks.Any(block => block.Size.X <= 0 || block.GetChild<Control>(1).Size.Y <= 0) ||
            blocks.Zip(blocks.Skip(1)).Any(pair => pair.Second.Position.Y < pair.First.Position.Y ||
                pair.Second.Position.Y == pair.First.Position.Y && pair.Second.Position.X < pair.First.Position.X + pair.First.Size.X)) { return false; }
        var regions = new Dictionary<string, CycleLayout.Region>(StringComparer.Ordinal);
        foreach (var block in graph.Nodes.Select(node => blocks.Single(block => block.Name.ToString() == "IntentLensMoveNode_" + node.Id)))
        {
            var icons = block.GetChild<HBoxContainer>(1);
            var position = view.Grid.Position + block.Position;
            var center = position + icons.Position + icons.Size / 2;
            regions[block.Name.ToString()["IntentLensMoveNode_".Length..]] = new(
                new(position.X, position.Y), new(block.Size.X, block.Size.Y), new(center.X, center.Y), icons.GetCombinedMinimumSize().X);
        }
        var signature = string.Join(';', regions.Select(pair => pair.Key + ":" + pair.Value));
        if (view.Signature == signature) { return true; }
        view.Signature = signature;
        foreach (var child in view.Routes.GetChildren()) { view.Routes.RemoveChild(child); child.QueueFree(); }
        var layout = view.Section.Selection is null ? CycleLayout.Build(graph, regions) : CycleLayout.Choice(view.Section.Selection.Id, regions);
        if (_compactReference && view.Section.Selection is null) { layout = layout with { Size = new(layout.Size.X - 12, layout.Size.Y) }; }
        if (view.Section.Selection is null && _sections.Any(section => section.Selection is not null))
        { layout = layout with { Size = new(layout.Size.X - 28, layout.Size.Y) }; }
        var nodes = graph.Nodes.ToDictionary(node => node.Id);
        foreach (var connection in layout.Connections.DistinctBy(connection => (connection.Source, connection.Target)))
        {
            NativeArrow.Create(view.Routes, connection.Points.Select(point => new Vector2(point.X, point.Y)).ToArray(), 14);
            if (view.Section.Selection is not null) { continue; }
            var link = view.Section.Reference.Links.First(link => link.Source == connection.Source && link.Target == connection.Target);
            if (!link.Branched || view.Section.ConditionalScope && link.Conditional) { continue; }
            var chance = link.Conditional ? link.ConditionLabel ?? TextCatalog.Get("ConditionalEdge") : link.Chance is double probability
                ? probability.ToString("0.#%", CultureInfo.InvariantCulture) : TextCatalog.Get("UnknownSymbol");
            var label = Text(string.Format(CultureInfo.InvariantCulture, TextCatalog.Get("ChanceFormat"), chance), 14);
            var labelWidth = Math.Max(48, label.GetCombinedMinimumSize().X);
            label.Size = new(labelWidth, 22);
            label.Position = new(connection.Label.X - (labelWidth - 48) / 2, connection.Label.Y);
            view.Routes.AddChild(label);
        }
        if (view.Section.Selection is not null)
        {
            var positions = regions.Values.OrderBy(region => region.Position.X).ToArray();
            foreach (var (left, right) in positions.Zip(positions.Skip(1)))
            {
                var label = Text(TextCatalog.Get("ChoiceOr"), 14);
                label.Size = new(24, 22);
                label.Position = new((left.Position.X + left.Size.X + right.Position.X) / 2 - 12, left.IntentCenter.Y - 11);
                view.Routes.AddChild(label);
            }
        }
        var canvas = view.Grid.GetParent<Control>();
        if (view.Note.Text.Length == 0)
        {
            view.Note.Visible = false;
            canvas.CustomMinimumSize = new(layout.Size.X, layout.Size.Y);
            canvas.Size = canvas.CustomMinimumSize;
            _graphTip!.ResetSize();
            return true;
        }
        view.Note.Position = new(0, layout.Size.Y + 4);
        view.Note.Size = new(layout.Size.X, 20);
        var noteHeight = view.Note.GetThemeFont("font").GetMultilineStringSize(view.Note.Text, width: layout.Size.X, fontSize: 14).Y + 6;
        canvas.CustomMinimumSize = new(layout.Size.X, layout.Size.Y + 4 + Math.Max(0, noteHeight));
        canvas.Size = canvas.CustomMinimumSize;
        view.Note.Size = new(canvas.Size.X, Math.Max(20, noteHeight));
        _graphTip!.ResetSize();
        return true;
    }
    private static string Restriction(MoveEdge edge)
    {
        var parts = new List<string>();
        if (edge.Repeat == "UseOnlyOnce") { parts.Add(TextCatalog.Get("Once")); }
        if (edge.Repeat == "CannotRepeat") { parts.Add(TextCatalog.Get("NoRepeat")); }
        if (edge.Repeat == "CanRepeatXTimes") { parts.Add(string.Format(CultureInfo.InvariantCulture, TextCatalog.Get("RepeatLimit"), edge.MaxTimes)); }
        if (edge.Cooldown > 0) { parts.Add(string.Format(CultureInfo.InvariantCulture, TextCatalog.Get("Cooldown"), edge.Cooldown)); }
        return string.Join(", ", parts);
    }

    private static MegaLabel Text(string text, int size)
    {
        var label = new MegaLabel { MouseFilter = Control.MouseFilterEnum.Ignore, AutoSizeEnabled = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_bold_glyph_space_two.tres"));
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", StsColors.cream);
        label.AddThemeConstantOverride("outline_size", 3);
        label.SetTextAutoSize(text);
        return label;
    }

    internal object Diagnostics() => new Dictionary<string, object>
    {
        ["orthogonalArrows"] = HasOnlyRightAngleArrows(),
        ["creature"] = _creature.Entity.Name, ["compact"] = _compact, ["expanded"] = Expanded,
        ["nodes"] = NodeCount, ["edges"] = EdgeCount, ["nativeIntents"] = true, ["nativeHoverTip"] = true,
        ["displayedActions"] = _reference?.Graph.Nodes.Count ?? 0,
        ["collapsedBranches"] = _reference?.Collapsed ?? false,
        ["referenceLinks"] = _reference?.Links.Select(link => new Dictionary<string, object?>
        {
            ["source"] = link.Source, ["target"] = link.Target, ["chance"] = link.Chance,
            ["conditional"] = link.Conditional, ["conditionLabel"] = link.ConditionLabel
        }).ToArray() ?? [],
        ["summarySize"] = _summary.Size.ToString(), ["summaryPosition"] = _summary.GlobalPosition.ToString(),
        ["nativeIntentPosition"] = _creature.IntentContainer.GlobalPosition.ToString(),
        ["nativeIntentSize"] = _creature.IntentContainer.Size.ToString(),
        ["summaryBounds"] = new[] { _summary.GetGlobalRect().Position.X, _summary.GetGlobalRect().Position.Y, _summary.GetGlobalRect().Size.X, _summary.GetGlobalRect().Size.Y },
        ["nativeIntentBounds"] = new[] { _creature.IntentContainer.GetGlobalRect().Position.X, _creature.IntentContainer.GetGlobalRect().Position.Y, _creature.IntentContainer.GetGlobalRect().Size.X, _creature.IntentContainer.GetGlobalRect().Size.Y },
        ["visibleForecastSteps"] = (_summary.GetChildCount() + 1) / 2,
        ["history"] = _graph?.History.ToArray() ?? [],
        ["viewport"] = new[] { _creature.GetViewportRect().Size.X, _creature.GetViewportRect().Size.Y },
        ["layoutCellSize"] = CycleLayout.CellSize,
        ["columnGap"] = _referenceViews.Count > 0 ? _referenceViews[0].Grid.GetThemeConstant("h_separation") : 0,
        ["nativeHudBottom"] = NRun.Instance?.GlobalUi?.TopBar?.GetNodeOrNull<Control>("BgImage")?.GetGlobalRect().End.Y ?? 0,
        ["nativeGrid"] = _referenceViews.Count > 0,
        ["layoutReady"] = _referenceViews.Count > 0 && _referenceViews.All(view => view.Signature.Length > 0),
        ["routeCount"] = _referenceViews.Sum(view => view.Routes.GetChildren().OfType<Line2D>().Count()),
        ["phases"] = _referenceViews.Select(view => new Dictionary<string, object>
        {
            ["title"] = view.Section.Title, ["actions"] = view.Section.Reference.Graph.Nodes.Select(node => node.Id).ToArray(),
            ["routes"] = view.Section.Selection is null ? view.Section.Reference.Links.Count : 1, ["note"] = view.Note.Text,
            ["kind"] = view.Section.Selection is not null ? "choice" : view.Section.ConditionalScope ? "phase" : "sequence",
            ["current"] = view.Section.MarkCurrent && view.Section.Reference.Graph.Nodes.Any(node => node.Id == view.Section.Reference.Graph.Current)
                ? view.Section.Reference.Graph.Current : "",
            ["baseChances"] = view.Section.Selection is not null ? ProbabilityEngine.BaseChances(view.Section.Selection) : new Dictionary<string, double?>(),
            ["noteHeight"] = view.Note.Size.Y,
            ["position"] = new[] { view.Canvas.GlobalPosition.X, view.Canvas.GlobalPosition.Y },
            ["size"] = new[] { view.Canvas.Size.X, view.Canvas.Size.Y }
        }).ToArray(),
        ["actionRegions"] = _referenceViews.SelectMany(view => view.Grid.GetChildren().OfType<VBoxContainer>()
            .Select(block => new Dictionary<string, object>
            {
                ["phase"] = view.Section.Title, ["name"] = block.Name.ToString(),
                ["position"] = new[] { block.GlobalPosition.X, block.GlobalPosition.Y }, ["size"] = new[] { block.Size.X, block.Size.Y },
                ["titleLines"] = block.GetChild<MegaLabel>(0).GetLineCount(), ["titleHeight"] = block.GetChild<Control>(0).Size.Y,
                ["iconY"] = block.GetChild<Control>(1).Position.Y, ["iconWidth"] = block.GetChild<Control>(1).GetCombinedMinimumSize().X
            })).ToArray(),
        ["sharedFooter"] = _referenceFooter is not null ? new Dictionary<string, object>
        {
            ["text"] = _referenceFooter.Text, ["height"] = _referenceFooter.Size.Y,
            ["position"] = new[] { _referenceFooter.GlobalPosition.X, _referenceFooter.GlobalPosition.Y }
        } : new Dictionary<string, object>(),
        ["hoverTips"] = GodotObject.IsInstanceValid(_graphTip) && GodotObject.IsInstanceValid(_nativeTipContainer)
            ? _nativeTipContainer!.GetChildren().OfType<Control>().Append(_graphTip!).Where(tip => tip.Visible && !tip.IsQueuedForDeletion())
                .Select(tip => new Dictionary<string, object> { ["name"] = tip.Name.ToString(),
                    ["position"] = new[] { tip.GetGlobalRect().Position.X, tip.GetGlobalRect().Position.Y },
                    ["size"] = new[] { tip.GetGlobalRect().Size.X, tip.GetGlobalRect().Size.Y } }).ToArray() : [],
        ["intentWidgets"] = _summary.FindChildren("IntentLensPrediction", "", true, false).OfType<NIntent>()
            .Concat(GodotObject.IsInstanceValid(_graphTip)
                ? _graphTip!.FindChildren("IntentLensPrediction", "", true, false).OfType<NIntent>() : [])
            .Select(icon => new Dictionary<string, object> { ["damage"] = icon.GetNode<MegaRichTextLabel>("%Value").Text,
                ["processing"] = icon.CanProcess(), ["bobPosition"] = icon.GetNode<Control>("%IntentHolder").Position.ToString(),
                ["chancePositions"] = icon.FindChildren("IntentLensChance", "", true, false).OfType<Control>().Select(label => label.GlobalPosition.ToString()).ToArray(),
                ["arrowPositions"] = icon.FindChildren("IntentLensNativeArrow", "", true, false).OfType<Node2D>().Select(arrow => arrow.GlobalPosition.ToString()).ToArray() }).ToArray(),
        ["description"] = _description
    };

    private bool HasOnlyRightAngleArrows()
    {
        var tails = _summary.FindChildren("*", "Line2D", true, false).OfType<Line2D>().ToList();
        if (GodotObject.IsInstanceValid(_graphTip)) { tails.AddRange(_graphTip!.FindChildren("*", "Line2D", true, false).OfType<Line2D>()); }
        return tails.All(line =>
        {
            var points = line.Points;
            return points.Zip(points.Skip(1)).All(pair => pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y);
        });
    }

    private void RemoveTip()
    {
        if (GodotObject.IsInstanceValid(_graphTip)) { _graphTip!.QueueFree(); }
        _graphTip = null;
        _nativeTipSet = null;
        _nativeTipContainer = null;
        _renderedDescription = "";
        _referenceViews.Clear();
        _referenceBody = null;
        _referenceRoot = null;
        _referenceFooter = null;
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_creature.Hitbox))
        {
            _creature.Hitbox.MouseEntered -= _refresh;
            _creature.Hitbox.MouseExited -= _refresh;
            _creature.Hitbox.FocusEntered -= _refresh;
            _creature.Hitbox.FocusExited -= _refresh;
        }
        if (GodotObject.IsInstanceValid(_summary)) { _summary.QueueFree(); }
        RemoveTip();
    }
}
