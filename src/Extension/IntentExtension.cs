using HotMod.Contracts;

namespace IntentLens;

public sealed class IntentExtension : IHotLogic
{
    public HotLogicManifest Manifest => new("intent-lens", "Intent Lens", ProbabilityEngine.Build, HotContract.ContractVersion,
        new Dictionary<string, string> { ["command:forecast"] = "probability-distributions" });

    public void Initialize(IHotHost host, HotReloadContext context) { }

    public HotLogicResult OnHook(HotHookContext context) => context.HookId == "command:forecast"
        ? new(true, [GraphCodec.Encode(ProbabilityEngine.Evaluate(GraphCodec.Decode(context.Values["graph"])))])
        : new(false, []);

    public void Dispose() { }
}
