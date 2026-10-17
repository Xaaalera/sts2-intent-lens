using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using GamePlayer = MegaCrit.Sts2.Core.Entities.Players.Player;

namespace IntentLens.Player;

internal static class DevelopmentBridge
{
    private static MethodInfo? _invoke;
    internal static IDisposable? Register(Func<string, string, string> callback)
    {
        var host = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "Sts2.Devkit.GameHost")?
            .GetType("Sts2.Devkit.GameHost.GameSession");
        return host?.GetMethod("RegisterAdapter")?.Invoke(null, ["intent-lens", callback]) as IDisposable;
    }
    internal static string Mode { get; private set; } = "native";
    internal static string Engine { get; private set; } = ProbabilityEngine.Build;
    private static Task? _scenario;
    internal static string? ScenarioStatus => _scenario?.Status.ToString();

    internal static string StartTestCombat(string arguments)
    {
        if (string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("STS2_DEVKIT_SESSION")))
        { throw new ArgumentException("Owned SDK session required"); }
        if (_scenario is { IsCompleted: false }) { return "{\"accepted\":false}"; }
        using var document = JsonDocument.Parse(arguments);
        var encounterId = document.RootElement.TryGetProperty("encounter", out var encounter) ? encounter.GetString() : "BowlbugsWeak";
        var model = ModelDb.AllEncounters.FirstOrDefault(candidate => candidate.Id.Entry.Equals(encounterId, StringComparison.OrdinalIgnoreCase)
            || candidate.GetType().Name.Equals(encounterId, StringComparison.OrdinalIgnoreCase));
        if (model is null) { throw new ArgumentException("Unknown encounter"); }
        _scenario = StartCombat(model);
        TaskHelper.RunSafely(_scenario);
        return "{\"accepted\":true}";
    }

    private static async Task StartCombat(EncounterModel encounter)
    {
        var manager = RunManager.Instance;
        if (manager.IsInProgress) { throw new InvalidOperationException("Refusing to replace an existing run"); }
        var player = GamePlayer.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var run = RunState.CreateForTest([player], seed: "INTENTLENS");
        manager.SetUpNewSingleplayer(run, shouldSave: false, dailyTime: null);
        await PreloadManager.LoadRunAssets([player.Character]);
        await PreloadManager.LoadActAssets(run.Act);
        manager.Launch();
        NGame.Instance!.RootSceneContainer.SetCurrentScene(NRun.Create(run));
        var tree = (SceneTree)Godot.Engine.GetMainLoop();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await manager.GenerateMap();
        run.AddVisitedMapCoord(run.Map.StartingMapPoint.coord);
        run.ActFloor = 1;
        run.AppendToMapPointHistory(MapPointType.Monster, encounter.RoomType, encounter.Id);
        var room = AbstractRoom.FromSerializable(new SerializableRoom { RoomType = encounter.RoomType, EncounterId = encounter.Id, GoldProportion = 1 }, run);
        await manager.EnterRoom(room ?? throw new InvalidOperationException("Test encounter could not be created"));
    }

    internal static Forecast Evaluate(MoveGraph graph)
    {
        _invoke ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "Sts2.Devkit.GameHost")?
            .GetType("Sts2.Devkit.GameHost.GameSession")?.GetMethod("InvokeExtensionCommand", BindingFlags.Public | BindingFlags.Static);
        if (_invoke is not null)
        {
            var response = (string)_invoke.Invoke(null, ["intent-lens", "forecast", new Dictionary<string, string> { ["graph"] = GraphCodec.Encode(graph) }])!;
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0)
            {
                using var forecastDocument = JsonDocument.Parse(messages[0].GetString()!);
                var forecast = forecastDocument.RootElement;
                Mode = "sdk";
                Engine = forecast.GetProperty("engine").GetString()!;
                return GraphCodec.DecodeForecast(forecast.GetRawText());
            }
        }
        Mode = "native";
        Engine = ProbabilityEngine.Build;
        return ProbabilityEngine.Evaluate(graph);
    }
}
