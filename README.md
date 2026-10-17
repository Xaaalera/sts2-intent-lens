# StS2 Intent Lens

Private gameplay mod for Slay the Spire 2. Show two upcoming action distributions beside each enemy; hover that enemy to expand its complete move graph.

Probabilities describe possible actions, not seeded future selections. Never inspect or advance RNG. Unsupported dynamic conditions remain explicitly uncertain.

Target baseline: Windows, StS2 0.107.1, BaseLib 3.4.7 and RitsuLib 0.6.7/API-0.107.1.

Repository content is English. See [AGENTS.md](AGENTS.md).

## Behavior

- Up to two future-intent groups appear above the native current-action indicator. The row follows its actual IntentContainer top-center with12 local units of clearance instead of a fixed hitbox offset. Native intent widgets animate and show damage, including the game's multiattack notation. Smaller chances appear below in parentheses; compact chances and the yellow step arrow follow the same native bob animation.
- The compact forecast uses 60% opacity to distinguish possible future actions from the current native intent.
- Hover the enemy to show its horizontal action diagram above that enemy in the native hover-tip scene. The redundant name/move-cycle header is hidden. Preserve valid placement rather than moving to distant free corners. The compact forecast hides during this view; ordinary game tips remain at the side.
- The expanded reference stays stationary: its intent widgets disable frame processing while native damage labels still refresh with combat state. Only the compact forecast above the enemy bobs.
- Actions read left to right; long transitions and return paths use upper lanes. Graph positions stay stable as the current move advances.
- A native `GridContainer` places action columns; native containers align measured titles, intent rows and current markers. Widths use8-unit increments and measured damage-label width. Constrained deterministic references can wrap into4 then3 columns. Rows alternate reading direction: continue downward near the row end and return on a separate outer-left corridor. Continuation/return do not share a line. Preserve logical order and wrapped columns on refresh. This is mod-local layout, not a shared SDK grid API.
- The graph uses the stock hover-tip scene directly, outside the native side-tip flow. It does not append to, resize, realign or move ordinary game tips. Respect the actual top-bar background and visible relic rectangles. Compact gaps first; a constrained phase reference keeps horizontal groups with smaller native labels/icons and condition headings instead of long notes. A tall phase stack is not used.
- Prepare the transparent native layout before hovering. Native mouse/focus entry and exit refresh immediately; pre-draw presentation runs on the game thread. The100ms model-snapshot timer does not gate showing or hiding. Cached transparent controls ignore input and are disposed with their render/input subscriptions.
- The first future step follows the public current intent. Repeat limits, once-only actions and cooldowns affect each branch's history. The full diagram shows normalized base weights with restrictions; the compact forecast shows history-adjusted chances.
- Supported internal decision states are folded into action-to-action links. Known random weights remain base percentages; conditional links have no invented numeric chance. Source-verified Queen0.107.1 captions describe companion-death rules without executing predicates or reading the private phase latch. Unknown conditions remain generic. Unsupported branch cycles retain the raw graph.
- Source-verified Queen phases use two native sections instead of one merged chain. Phase1 contains the opening actions and repeated support move while her companion lives. Phase2 contains the Enrage/attack/execution cycle after companion defeat. The ordinary layout explains immediate replacement of a queued support move on companion death; constrained horizontal rendering retains phase conditions but omits long notes/current-label rows. Current intent remains gold. No private phase latch or future RNG is read. Other enemies retain a generic section unless verified phase metadata matches.
- A supported shared random loop uses one entry section and one repeating choice group. Alternative attacks show base chances and short repeat limits under native damage widgets, separated by "or", with one return arrow to the group heading. Entry sections take their content width. No generic damage explanation or empty footer is shown. Unsupported topologies retain the generic reference. Compact history-aware forecasting stays unchanged.
- Source-verified Waterfall Giant sections separate six normal actions from defeat with Steam Eruption: stun -> explosion -> end. The rendered explosion has no repeat arrow despite the raw engine follow-up. This relies on installed death-hook/action source, not private-state inspection. Live defeat/eruption and the post-defeat compact horizon remain unverified.
- Read constant weights without invoking delegates. Unknown weights, conditions, custom states and invalid transitions stay uncertain. Never inspect/advance RNG or execute combat, condition or weight callbacks.
- Native bestiary names identify moves. Chances reflect current graph/history; gameplay may change these before a future turn.
- Damage labels use the game's current combat effects and localization. Future buffs, debuffs, targeting changes and other hypothetical effects are not simulated. No generic damage disclaimer is displayed in the graph.

## Build and install without the SDK

Requires .NET SDK 9. The parent workspace is optional.

```powershell
$env:STS2_GAME_DIR = 'D:/Games/Slay the Spire 2'
dotnet build src/Player/IntentLens.csproj -c Release
dotnet test tests/IntentLens.Tests.csproj
```

Local Release0.1.1 ZIP is available under `.local/packages/IntentLens-0.1.1-sts2-0.107.1.zip`, with installation instructions and checksums. Its extracted DLL/manifest passed native loader/main-menu startup in an isolated Steam-disabled copy with only the two declared libraries.161 Release tests pass. Physical standalone-combat, Steam-enabled startup and Workshop delivery remain separate checks.

With the target game closed, copy only `src/Player/bin/Release/net9.0/IntentLens.dll` and `IntentLens.json` into `mods/IntentLens/`. Install complete compatible BaseLib/RitsuLib runtimes separately. The player has no SDK assembly reference and runs its local engine when the SDK host/extension is absent.

The arrow image is loaded from the game at runtime without recoloring or packaging it. The small connector gap uses engine Line2D primitives with orthogonal routes and StsColors.gold; the stock upgrade arrow is a static image. Interactive targeting-arrow controllers and their cursor effects are not used. Other API lanes need verification.

## Develop through the SDK

Install the native player adapter in the owned test game first. Native adapter/layout changes need a justified restart or documented resident preview; probability changes use collectible generations.

```powershell
dotnet pack D:/Code/sts2-mod-devkit/src/Contracts/Sts2.Devkit.Contracts.csproj --output D:/Code/sts2-mod-devkit/.local/package-feed
dotnet restore src/Extension/intent-lens.csproj --source D:/Code/sts2-mod-devkit/.local/package-feed
dotnet build src/Extension/intent-lens.csproj --no-restore
D:/Code/sts2-mod-devkit/scripts/sdk.ps1 -Action reload -PluginId intent-lens -ArtifactPath ./src/Extension/bin/Debug/net9.0/intent-lens.dll
D:/Code/sts2-mod-devkit/scripts/sdk.ps1 -Action adapter -PluginId intent-lens -Command inspect
```

The resident adapter sends graph snapshots through the SDK bridge. Native calculations supply previews when the extension is unavailable/busy. Unloading a calculation generation does not remove the independently owned native UI.

`inspect` reports engine/mode/errors/outcomes/graph counts. `preview-graph` is an owned-SDK aid, not physical-input evidence. `test-combat` starts a non-saving encounter only when no run is active; it refuses to replace a run. Development commands have no player UI.

## Verification status

Mod UI translations live separately in [English](localization/ui/eng.json) and [Russian](localization/ui/rus.json). Add another game-language JSON beside them, preserving keys and composite-format arguments. Player UI uses RitsuLib I18N to follow the game's language, with English fallback. Native attack, effect and stun names remain game-localized. The same JSON files are embedded during builds; rebuild/reload after edits. Core and SDK Extension use the English catalog without a game dependency. Player owns and disposes the locale-change subscription; cached overlays rebuild when the locale changes.

161 isolated cases pass, including84 generated shared-choice topology combinations and four current-marker history cases. They cover histories/uncertainty, non-execution of callbacks, action/phase projections, forced openings that reuse a recurring option, topology guards, unequal weights, stable orthogonal routing, viewport exclusion and content sizing. These are abstract cases, not161 real enemies.

Actual owned-game evidence:

- KnightsElite, previews56/57: Spectral Knight's forced opening and recurring choice are separate; no enemy-name exception. Original raw references and compact forecasts match for all three knights. Short repeat limits now sit below the corresponding chance instead of repeating attack names in a verbose footer. Actual1920x1080 image checked; final owner acceptance pending.
- HunterKillerNormal, preview54: one entry action, one repeating two-option choice and one return arrow. Base chances remain50/50; compact history-aware forecasts are unchanged. Entry/choice widths104/212; full panel399x359 with a complete full-width footer at1920x1080. Actual image and geometry checked; owner feedback positive.
- QueenBoss, preview52: two3-action phase sections, local cycles and the source-verified companion-death replacement explanation. Actual image/geometry checked; owner feedback positive. The live companion-death/phase2 transition was not executed.
- Earlier actual Gremlin Merc/Insatiable and isolated model checks retain their original scope in research. OwlMagistrateNormal is an ordinary Monster encounter, not a boss or elite.

Current owned test runs the packaged native Release0.1.1 without SDK. Earlier resident UI IntentLens.LivePreview84 had with separate English/Russian catalogs and current-game-language lookup. Russian Beast rendering is stable within1920x1080 (locale83-checks.json); other translated layouts and interactive language switching remain unverified. Temporary native current actions outside the registered machine now retain their identity, follow-up and native intent widget/title. Beast references separate charging from the post-Plow phase; the native effect title is used and its ordinary tooltip retains the numeric condition. A native threshold-damage/stun/next-turn transition was executed. Seven encounter types/eight actors/ten state checks across78-81 have stable expanded bounds inside1920x1080, below HUD and without native text-tip overlap (sweep81-summary.json). These are mixed-version checks, not all encounters on81. Native-anchor placement was visually checked on Test Subject and all three Decimillipede segments; two diagnostic samples per actor preserve alignment and forecast strings at logical1920x1080. Previews71-76 additionally checked four encounter types/seven actor cases: Test Subject lives and public-history revival markers, Decimillipede normal/recovery sections and native shared-prefix titles, Soul Nexus choices, and Living Fog spawning/removing a terminal GasBomb. Two Test Subject revivals and a growing claw attack were executed through native debug gameplay; recovery remains source-only. Certain terminal actions clip the visible forecast while unknown/mixed paths retain it. Native claw intents project the verified extra hit per use; other future effects are not simulated.161 cases passed in anomalous-encounters.trx. Earlier evidence follows with its original scope. Giant70 uses owner-approved serpentine arrows, with separate continuation/return corridors; its691x250 rectangle remains stable. Beyond the prior seven actor cases/four encounters on65, twelve additional actor cases/six encounter types were visually checked across65/67/69. Two samples per case remain visible with identical rectangles, inside the logical1920x1080 viewport and without native-tip overlap. Giant69 is691x250 with a separate terminal sequence; Queen69 regression is731x149, horizontal. This is bounded diagnostic-hover evidence, not all-enemy/physical-input acceptance. Native previews stay resident until restart; they are not collectible UI HMR or the installed DLL. Inspect actual state after old-host publication failures before replaying mutations.

Desktop targets are1280x720,1600x900 and1920x1080, including intermediate widths. Latest live images establish1920x1080 only; smaller targets have bounded geometry tests, not full rendering acceptance. Broad enemy/viewport coverage, physical turn/input checks, Steam-enabled startup, physical native-only combat, multiplayer, Workshop and alternate API lanes remain pending.

## Review

Run `npm ci` and `npm run review:gate`. Attest only completed configured independent results using `npm run review:attest -- <results-file>`; never bypass the hook. Hosted CI scans secrets, not game behavior.
