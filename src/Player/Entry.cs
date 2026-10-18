using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2RitsuLib;
using STS2RitsuLib.Utils;
using Environment = System.Environment;

namespace IntentLens.Player;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    private static readonly Dictionary<NCreature, IntentOverlay> Overlays = new();
    private static CanvasLayer? _layer;
    private static Godot.Timer? _timer;
    private static IDisposable? _combatEnded;
    private static IDisposable? _adapter;
    private static I18N? _localization;
    private static string? _lastFailure;
    private static int _presentationThread;
    private static int _forced = -1;
    private static NCreature? _expandedOwner;

    public static void Initialize() => Callable.From(Start).CallDeferred();

    private static void Start()
    {
        _localization = new I18N("IntentLens", resourceFolders: ["IntentLens.Localization"], resourceAssembly: typeof(Entry).Assembly);
        TextCatalog.Localize = _localization.Get;
        _localization.Changed += RefreshLocalization;
        var tree = (SceneTree)Engine.GetMainLoop();
        _layer = new CanvasLayer { Layer = 40, Name = "IntentLensOverlay" };
        tree.Root.AddChild(_layer);
        _timer = new Godot.Timer { WaitTime = .1, Autostart = true, ProcessMode = Node.ProcessModeEnum.Always };
        _timer.Timeout += Tick;
        _layer.AddChild(_timer);
        _combatEnded = RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(_ => Clear(), false);
        _layer.TreeExiting += Stop;
        _adapter = DevelopmentBridge.Register(HandleDevelopmentCommand);
        RenderingServer.FramePreDraw += Present;
        Tick();
        GD.Print("intent-lens.started");
    }

    private static void Tick()
    {
        try
        {
            var room = NCombatRoom.Instance;
            var creatures = room?.CreatureNodes.Where(creature => creature.Entity.IsEnemy && creature.Entity.Monster is not null).ToArray() ?? [];
            var focused = creatures.FirstOrDefault(creature => creature.IsFocused);
            if (focused is not null) { _expandedOwner = focused; }
            else if (_expandedOwner is not null && (!Overlays.TryGetValue(_expandedOwner, out var previous) || !previous.RetainHover)) { _expandedOwner = null; }
            foreach (var removed in Overlays.Keys.Where(creature => !GodotObject.IsInstanceValid(creature) || !creatures.Contains(creature)).ToArray())
            { Overlays[removed].Dispose(); Overlays.Remove(removed); }
            for (var index = 0; index < creatures.Length; index++)
            {
                var creature = creatures[index];
                if (!Overlays.TryGetValue(creature, out var overlay)) { Overlays[creature] = overlay = new(creature, Tick); }
                var graph = StateSnapshot.Read(creature.Entity.Monster!, creature);
                overlay.Refresh(graph, DevelopmentBridge.Evaluate(graph), _forced >= 0 ? index == _forced : ReferenceEquals(creature, _expandedOwner));
            }
            _lastFailure = null;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private static void Present()
    {
        _presentationThread = Environment.CurrentManagedThreadId;
        try { foreach (var overlay in Overlays.Values) { overlay.Present(); } }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private static void ReportFailure(Exception exception)
    {
        var diagnostic = exception.ToString();
        if (_lastFailure != diagnostic)
        {
            GD.PrintErr(JsonSerializer.Serialize(new { error = new { code = 500, status = "INTERNAL", message = TextCatalog.Get("Unavailable"),
                details = new[] { new { reason = "INTENT_INSPECTION_FAILED", domain = "sts2.intent-lens" } } }, diagnostic }));
            _lastFailure = diagnostic;
        }
    }

    public static string Inspect() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["presentation"] = "orthogonal-v1",
        ["forcedPreview"] = _forced,
        ["presentationThread"] = _presentationThread,
        ["mode"] = DevelopmentBridge.Mode, ["engine"] = DevelopmentBridge.Engine, ["error"] = _lastFailure,
        ["scenarioStatus"] = DevelopmentBridge.ScenarioStatus,
        ["panels"] = Overlays.Values.Select(overlay => overlay.Diagnostics()).ToArray()
    });

    public static void PreviewGraphForVerification(int index)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STS2_DEVKIT_SESSION"))) { throw new InvalidOperationException("Owned SDK session required"); }
        _forced = index >= 0 && index < Overlays.Count && NCombatRoom.Instance is { } room
            ? Array.IndexOf(room.CreatureNodes.Where(creature => creature.Entity.IsEnemy && creature.Entity.Monster is not null).ToArray(), Overlays.Keys.ElementAt(index))
            : -1;
        Tick();
    }

    private static string HandleDevelopmentCommand(string command, string arguments)
    {
        if (command == "inspect") { return Inspect(); }
        if (command == "preview-graph")
        {
            using var document = JsonDocument.Parse(arguments);
            PreviewGraphForVerification(document.RootElement.GetProperty("index").GetInt32());
            return Inspect();
        }
        if (command == "test-combat") { return DevelopmentBridge.StartTestCombat(arguments); }
        throw new ArgumentException("Unsupported development command", nameof(command));
    }

    private static void RefreshLocalization() => Callable.From(Clear).CallDeferred();

    private static void Clear()
    {
        foreach (var overlay in Overlays.Values) { overlay.Dispose(); }
        Overlays.Clear();
        _forced = -1;
        _expandedOwner = null;
    }

    private static void Stop()
    {
        TextCatalog.Localize = null;
        if (_localization is not null) { _localization.Changed -= RefreshLocalization; _localization.Dispose(); }
        _localization = null;
        RenderingServer.FramePreDraw -= Present;
        if (GodotObject.IsInstanceValid(_timer)) { _timer!.Timeout -= Tick; }
        _timer = null;
        _combatEnded?.Dispose();
        _combatEnded = null;
        _adapter?.Dispose();
        _adapter = null;
        Clear();
    }
}
