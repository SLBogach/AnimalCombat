# Unity Replay Viewer Test Plan v0.1 — WP-UI-01

> Статус: `APPROVED / BLOCKING — EXECUTION IN PROGRESS`.
>
> Owner approval получен `2026-09-08`. Выполненные checkpoint-проверки перечислены ниже; остальные cases не считаются пройденными до автоматизированного запуска соответствующего gate.

## 1. Назначение и pass/fail contract

Этот Test Plan задаёт exact acceptance для read-only Unity Replay Viewer `combat.replay/0.1`. Он уточняет [WP-UI-01 Brief](./WP-UI-01_Brief.md) и включает утверждённый [Game-First UI Spec v0.1](./Unity_Replay_Viewer_UI_Spec_v0.1.md), но не может менять replay/event schema, fixture bytes или правила `Battle.Core`.

Общий pass требует одновременно:

- всех `WPUI01-*` IDs без пропусков и дубликатов;
- green Unity EditMode и PlayMode suites;
- green Windows Standalone build/smoke;
- точных fixture projections из §4;
- неизменности исходных replay fixtures и отсутствия изменений CombatLab production-кода;
- отсутствия unhandled exception на любом negative/unknown-event case.

Blocking `OPEN-WPUI01-01..08` закрыты owner approval без изменения рекомендаций Brief. План является обязательным для оставшейся реализации и закрытия этапа.

### Текущий execution checkpoint — 2026-09-08

- Unity compilation: `0 errors / 0 warnings`;
- EditMode vertical-slice suite: `13 passed / 0 failed / 0 skipped`;
- game-first PlayMode slice: `3 passed / 0 failed / 0 skipped` — arena dominance/default visibility, telemetry isolation и recorded basic result;
- три required fixtures загрузились; canonical/bundled bytes и SHA-256 совпали;
- runtime smoke basic/double-KO/wall-grab дошёл до exact terminal sequence, HP и outcome;
- unknown event принят loader и не остановил playback;
- Play/Pause/Restart/speed session semantics прошли EditMode test;
- Game View проверен вручную для game-first initial state, recorded terminal result и wall-impact slice (`tick 1`, `sequence 11/17`, wall damage `93`).

Утверждённый game-first UXML/USS и presentation effects реализованы; event stream убран из основного игрового кадра в закрытую по умолчанию telemetry drawer. Это промежуточное evidence, а не общий pass: полный three-fixture PlayMode/negative-load набор, external-path UX, Windows Standalone и static dependency checks ещё не выполнены.

## 2. Test levels

| Level | Будущее расположение | Ответственность |
|---|---|---|
| Static | repository scripts/checks | Unity baseline, asmdef graph, forbidden dependencies, changed-path и SHA gates |
| EditMode | `UnityClient/AnimalCombat/Assets/ReplayViewer/Tests/EditMode` | loader, envelope checks, pure projection, event mapping, playback clock |
| PlayMode | `UnityClient/AnimalCombat/Assets/ReplayViewer/Tests/PlayMode` | scene binding, controls, HUD, event order, result/warnings |
| Standalone smoke | Windows x64 build | bundled fixtures, filesystem load, complete playback, no writes/crashes |
| Manual visual | reviewed checklist | readability of arena, facing, cues, HP and result |
| Regression | existing `CombatLab/CombatLab.sln` gates | confirmation that Viewer work did not alter gameplay behavior |

EditMode tests должны обращаться к production classes через public API, без reflection в приватные поля. PlayMode assertions проверяют component state/text/normalized values; pixel-perfect screenshot comparison не blocking для v0.1.

## 3. Frozen inputs

### 3.1 Required fixture pins

| Short name | Canonical path | File SHA-256 | Events / keyframes |
|---|---|---|---:|
| `basic` | `CombatLab/fixtures/replay/v0.1/resolution-basic-l1.engine-0.4.0.json` | `c56685b7b9fae47abd1b0cb503b9d2b46cfda4a890c73cc82226810626f70c99` | `11 / 2` |
| `double-ko` | `CombatLab/fixtures/replay/v0.1/resolution-double-ko-l1.engine-0.4.0.json` | `1bee7887f1603c0f95e2f48950a54549ff17dd34edb67dd85d51415f39bb188f` | `17 / 2` |
| `wall-grab` | `CombatLab/fixtures/replay/v0.1/resolution-wall-grab-l1.engine-0.4.0.json` | `25ee1cbe58c0fa1df40076ca69c79ba2dde0e00b427d4c4b7017a8a65e28843c` | `18 / 2` |

Во всех трёх: `schema_version=combat.replay/0.1`, `engine_version=battle.core/0.4.0`, `ordering_version=tick-pipeline/1`, `rng_version=pcg32/1`, `profile=standard`.

### 3.2 Exact event type traces

`basic`:

```text
BattleStarted, DecisionMade, DecisionMade, ActionCommitted, ActionCommitted,
AttackPrepared, AttackHit, DamageApplied, StateChanged, FighterDefeated, BattleEnded
```

`double-ko`:

```text
BattleStarted, DecisionMade, DecisionMade, ActionCommitted, ActionCommitted,
AttackPrepared, AttackPrepared, AttackHit, DamageApplied, AttackHit, DamageApplied,
StateChanged, FighterDefeated, StateChanged, FighterDefeated, DrawDeclared, BattleEnded
```

`wall-grab`:

```text
BattleStarted, DecisionMade, DecisionMade, ActionCommitted, ActionCommitted,
AttackPrepared, GrabStarted, AttackHit, DamageApplied, ResourceChanged,
KnockbackApplied, WallImpact, DamageApplied, ResourceChanged, GrabEnded,
StateChanged, FighterDefeated, BattleEnded
```

### 3.3 Synthetic inputs

Synthetic JSON/events разрешены только под Unity test directories или создаются in-memory. Они маркируются `noncanonical_test_input`, не заменяют existing fixtures и не должны записываться в `CombatLab/fixtures`.

- `attack-missed`: parseable event envelope, `event_type=AttackMissed`, `sequence=1`, `tick=0`, actor A, target B, payload `impact_id=impact:test:00`, `hit_group_id=hit-group:test:00`, `miss_reason=OutOfRange`, `gap=1500`, range `0..1000`, empty state mutation.
- `future-event`: minimal `BattleStarted` at sequence `0`, then a parseable envelope with `event_type=FutureCombatEvent`, `sequence=1`, `tick=0`, raw payload `{ "future_value": 7 }`, followed by a known terminal `BattleEnded` at sequence `2`.
- negative inputs: empty, malformed UTF-8/JSON, oversized, wrong replay schema, missing fields, duplicate/gapped sequence, decreasing tick and inconsistent initial frames.

Because `FutureCombatEvent` is outside the current schema enum, that synthetic document is deliberately not schema-valid and its integrity is deliberately not trusted. The case tests Viewer resilience only.

## 4. Exact fixture oracles

### 4.1 `basic`

- Load state: A `(position=4000, facing=Right, HP=100/100)`, B `(5200, Left, 100/100)`.
- Sequence `1`: A chooses `bear_earthbreaker`, `WeightedRng`.
- Sequence `3`: A commits `bear_earthbreaker`, direction `Right`.
- Sequence `5`: A prepares impacts `[0]`.
- Sequence `6`: hit A→B, gap `250`, direction `Right`.
- Sequence `7`: recorded HP B `100→0`, `breakdown.final=600`, `lethal=true`.
- Sequence `8`: B state `Retreat→Defeated`.
- Sequence `9`: B defeated, `final_health=0`.
- Sequence `10`: `FighterAWin / Defeat / end_tick=0`; final A/B HP `100/0`, positions `4000/5200`.

### 4.2 `double-ko`

- Sequences `7..10` apply both hits and both recorded lethal damages before defeat events.
- Sequence `8`: A HP `100→0`; sequence `10`: B HP `100→0`.
- Sequences `12` and `14`: A and B defeated in simultaneous group `resolution:0000000000:0000`.
- Sequence `15`: `DrawDeclared` with `DoubleKO` semantics.
- Sequence `16`: `Draw / DoubleKO / winner=null / end_tick=0`; both HP `0` and both state `Defeated`.

### 4.3 `wall-grab`

- Load state: A `(8000, Right, HP=100)`, B `(9500, Left, HP=100, stagger=0)`.
- Sequence `6`: grab `grab:dec-fighter_a-000001:00`, A grabs B, `hold_max_ticks=12`.
- Sequence `7`: tick changes to `1`; hit tags are `grab,wall_impact`.
- Sequence `8`: B HP `100→0`, recorded `breakdown.final=600`.
- Sequence `9`: B stagger `0→75`.
- Sequence `10`: forced movement B `9500→9570`, `actual_move=70`, `blocked_by_wall=930`.
- Sequence `11`: right `WallImpact`, blocked `930`, wall damage `93`, wall stagger `75`.
- Sequence `12`: recorded wall damage event leaves B HP `0`, `breakdown.final=93`.
- Sequence `13`: B stagger `75→150`.
- Sequence `14`: grab ends with `Throw`, `throw_action_id=bear_earthbreaker`; A/B positions `8000/9570`.
- Sequence `17`: `FighterAWin / Defeat / end_tick=1`; final A/B HP `100/0`, positions `8000/9570`, B stagger `150`.

## 5. Acceptance matrix

### 5.1 Baseline and repository structure — 7 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-BASE-001` | Static | `UnityClient/ProjectSettings/ProjectVersion.txt` exists and pins `6000.4.0f1`; batch Editor reports the same version. |
| `WPUI01-BASE-002` | Static | `Packages/manifest.json` and `packages-lock.json` are tracked, parseable, and contain only approved direct dependencies; no unresolved Git/local absolute-path dependency exists. |
| `WPUI01-BASE-003` | Static | Exactly one required Viewer scene is tracked at `Assets/ReplayViewer/Scenes/ReplayViewer.unity` and is enabled in build settings. |
| `WPUI01-BASE-004` | Static | Contracts, Runtime, Presentation, EditMode tests and PlayMode tests each have an `.asmdef`; test assemblies are excluded from player builds. |
| `WPUI01-BASE-005` | Static | Three bundled sample files exist and each is byte-identical to its canonical source from §3.1. |
| `WPUI01-BASE-006` | Static | No `.meta` is missing for a tracked Unity asset and no `Library`, `Temp`, `Logs`, `Obj`, `Build` or `UserSettings` content is tracked. |
| `WPUI01-BASE-007` | Standalone | Windows x64 Development build opens the Viewer scene without script error or missing-reference log. |

### 5.2 Architecture boundary — 8 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-ARCH-001` | Static | No Unity asmdef/plugin/project reference targets `Battle.Core`, and no `Battle.Core.dll` or source copy exists under `UnityClient`. |
| `WPUI01-ARCH-002` | Static | No Unity asmdef/plugin/project reference targets `Battle.Config`, `Battle.Replay`, Runner or CLI. |
| `WPUI01-ARCH-003` | Static | Existing `Battle.Core.csproj` still references only `Battle.Contracts` plus BCL/package infrastructure and contains no Unity dependency. |
| `WPUI01-ARCH-004` | EditMode | Given an event with deliberately non-formula-consistent payload but authoritative `after` frame, projection equals the `after` frame byte-for-field; no derived correction occurs. |
| `WPUI01-ARCH-005` | EditMode | `DamageApplied` assigns recorded `after.target.health` and displays recorded `breakdown.final`; changing power/armor-looking values elsewhere in JSON cannot change either result. |
| `WPUI01-ARCH-006` | EditMode | `KnockbackApplied` ends at recorded `to_position`; changing `requested_move` or mass-looking input cannot change projected position. |
| `WPUI01-ARCH-007` | EditMode | `BattleEnded` copies recorded final frames/outcome; Viewer has no API that produces a winner from HP values. |
| `WPUI01-ARCH-008` | Static | Runtime assemblies expose no replay save/edit/export API and no production path opens a replay source for write access. |

### 5.3 Loading and structural safety — 12 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-LOAD-001` | EditMode | Each §3.1 fixture loads successfully; schema/engine/ordering/RNG/profile, event count and keyframe count equal §3.1. |
| `WPUI01-LOAD-002` | PlayMode | Selecting each bundled sample loads the matching replay ID and leaves session `Paused` at tick `0`, sequence `0`. |
| `WPUI01-LOAD-003` | Standalone | Loading an external valid fixture by text path reads it successfully and does not change file length, last-write timestamp or SHA-256. |
| `WPUI01-LOAD-004` | EditMode | Empty input returns typed `LoadFailed` and no exception escapes. |
| `WPUI01-LOAD-005` | EditMode | Malformed UTF-8 or JSON returns typed `LoadFailed` with a non-empty path/message and no partial session. |
| `WPUI01-LOAD-006` | EditMode | Input larger than `128 MiB` is rejected before JSON materialization; process remains responsive. |
| `WPUI01-LOAD-007` | EditMode | `schema_version != combat.replay/0.1` is rejected as unsupported without attempting event playback. |
| `WPUI01-LOAD-008` | EditMode | A replay with missing `events`, `input`, `BattleStarted`, or `BattleEnded` is rejected with a stable typed error and no exception. |
| `WPUI01-LOAD-009` | EditMode | Duplicate/gapped/nonzero-first sequence is rejected; Viewer never sorts or renumbers it. |
| `WPUI01-LOAD-010` | EditMode | Decreasing tick is rejected; equal adjacent ticks are accepted. |
| `WPUI01-LOAD-011` | EditMode | Initial frames inconsistent between input and `BattleStarted` are rejected; exact matching frames are accepted. |
| `WPUI01-LOAD-012` | PlayMode | Failed load is atomic: current valid replay becomes paused but its document/state remains intact, and error banner identifies the failed candidate. |

### 5.4 Playback controls and clock — 10 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-CTRL-001` | PlayMode | Load starts `Paused` at sequence `0`; Play changes state to `Playing` and advances to sequence `1` only after one logical slice. |
| `WPUI01-CTRL-002` | PlayMode | Pause during a slice freezes cursor and tween elapsed time for at least two rendered frames; resume continues the same slice without skip/duplicate. |
| `WPUI01-CTRL-003` | PlayMode | Restart from middle or Completed returns synchronously to exact §4 load projection, sequence `0`, tick `0`, `Paused`. |
| `WPUI01-CTRL-004` | PlayMode | Restart clears result overlay, active grab link, transient combat text and current cue while retaining loaded replay and selected speed. |
| `WPUI01-CTRL-005` | EditMode | At `1x`, one event slice consumes `400±20 ms` of supplied test clock; clock injection makes the test independent of wall-clock jitter. |
| `WPUI01-CTRL-006` | EditMode | For `0.25x/0.5x/1x/2x/4x`, a slice consumes respectively `1600/800/400/200/100 ms` within ±5% logical-clock tolerance. |
| `WPUI01-CTRL-007` | PlayMode | Changing speed during playback affects remaining presentation time without changing current/next sequence, state fields or event order. |
| `WPUI01-CTRL-008` | EditMode | Session clock uses its own multiplier and never writes Unity global `Time.timeScale`. |
| `WPUI01-CTRL-009` | PlayMode | On `BattleEnded`, state becomes `Completed`, cursor stays at final sequence, and Play is a no-op until Restart. |
| `WPUI01-CTRL-010` | PlayMode | 20 alternating Play/Pause/Restart/speed actions in one second produce no exception, duplicate event application or cursor outside `0..event_count-1`. |

### 5.5 Fighter projection and HUD — 9 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-HUD-001` | EditMode | Arena mapping sends source `0→-8`, `5000→0`, `10000→+8` and clamps no valid in-range value; tolerance `0.001`. |
| `WPUI01-HUD-002` | PlayMode | Basic load places A/B at mapped `4000/5200` and shows facing `Right/Left`. |
| `WPUI01-HUD-003` | PlayMode | Wall-grab sequence `10` completes with B at mapped `9570`; model position is exact integer `9570`. |
| `WPUI01-HUD-004` | PlayMode | Fighter visual orientation follows frame facing only; attack/commit direction cues do not mutate facing. |
| `WPUI01-HUD-005` | PlayMode | HP normalized fill is `health/max_health`: basic starts `1/1` and ends A/B `1/0`; labels show `100/100` and `0/100` exactly. |
| `WPUI01-HUD-006` | PlayMode | Current tick and sequence update to the activated event; wall-grab shows `(0,6)` then `(1,7)` without synthetic intermediate tick. |
| `WPUI01-HUD-007` | PlayMode | State label follows recorded frames; basic B ends `Defeated`, double-ko both end `Defeated`. |
| `WPUI01-HUD-008` | PlayMode | Event log has one row per applied sequence in original order and selecting a row never changes model state. |
| `WPUI01-HUD-009` | PlayMode | UI remains operable at 1280×720: controls, both HP bars, tick/sequence, current event and result are visible without overlap. |

### 5.6 Required event mapping — 18 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-EVT-001` | EditMode | `BattleStarted` creates exactly A/B from `initial_frames`; no default fighter stats are injected. |
| `WPUI01-EVT-002` | PlayMode | Basic sequence `1` displays actor A, target B, `bear_earthbreaker`, `WeightedRng`; sequence `3` displays committed action and direction `Right`. |
| `WPUI01-EVT-003` | PlayMode | Basic sequence `5` displays AttackPrepared with telegraph tick `0`, impacts `[0]`, and actor-specific telegraph cue. |
| `WPUI01-EVT-004` | PlayMode | Basic sequence `6` displays AttackHit A→B, `gap=250`, direction `Right`; one hit cue is emitted. |
| `WPUI01-EVT-005` | EditMode | Synthetic `attack-missed` emits exactly one miss cue/row with `OutOfRange`; empty `after` leaves both fighter frames unchanged. |
| `WPUI01-EVT-006` | PlayMode | Basic sequence `7` changes B HP from `100` to `0` using recorded frame and displays recorded damage `600`; no extra HP mutation occurs. |
| `WPUI01-EVT-007` | PlayMode | Basic sequences `8` and `9` display `Retreat→Defeated` then defeated marker for B with final health `0`. |
| `WPUI01-EVT-008` | PlayMode | Wall-grab sequence `9` displays Stagger `0→75`; sequence `13` displays `75→150`, following `ResourceChanged` frames. |
| `WPUI01-EVT-009` | PlayMode | Wall-grab sequence `10` emits one forced-movement cue and ends B at `9570`, showing `actual=70`, `blocked=930`. |
| `WPUI01-EVT-010` | EditMode | Synthetic `PositionChanged(movement_kind=Forced)` tweens recorded from/to coordinates and copies its `after`; no speed/force formula is called. |
| `WPUI01-EVT-011` | PlayMode | Wall-grab sequence `6` activates grab ID and visible A→B link; unrelated events do not silently end it. |
| `WPUI01-EVT-012` | PlayMode | Wall-grab sequence `14` ends the same grab, removes link and emits Throw cue with `bear_earthbreaker`. |
| `WPUI01-EVT-013` | PlayMode | Wall-grab sequence `11` emits right-wall cue and shows recorded blocked distance `930`, wall damage `93`, wall stagger `75`. |
| `WPUI01-EVT-014` | PlayMode | Basic `BattleEnded` shows `FighterAWin`, winner A, reason `Defeat`, tick `0`, then stops playback. |
| `WPUI01-EVT-015` | PlayMode | Double-ko sequence `15` displays draw cue; `BattleEnded` shows `Draw`, `DoubleKO`, and no winner. |
| `WPUI01-EVT-016` | EditMode | A known but unmapped v0.1 event creates one generic row, copies parseable `after`, and produces no invented specialized cue. |
| `WPUI01-EVT-017` | EditMode | Synthetic `FutureCombatEvent` yields one `Unsupported event type` warning and generic row; its raw payload remains inspectable as `future_value=7`. |
| `WPUI01-EVT-018` | EditMode | After `FutureCombatEvent`, known sequence `2` is applied exactly once and session can reach Completed; no exception or forced abort occurs. |

### 5.7 End-to-end fixture acceptance — 3 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-FIX-001` | PlayMode | Basic playback consumes exactly sequences `0..10` once and terminal UI/model equals §4.1 and internal final digest text from §3.1. |
| `WPUI01-FIX-002` | PlayMode | Double-ko playback consumes exactly `0..16` once, preserves both damages before both defeats, and terminal UI/model equals §4.2. |
| `WPUI01-FIX-003` | PlayMode | Wall-grab playback consumes exactly `0..17` once, observes tick boundary and grab/forced-move/wall/throw chain, and terminal UI/model equals §4.3. |

### 5.8 Error reporting, integrity label and limits — 7 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-SAFE-001` | PlayMode | Every rejected input shows a bounded user-facing error, keeps controls responsive, and logs no unhandled exception. |
| `WPUI01-SAFE-002` | EditMode | No input string, payload value or reason text can create more than one bounded UI row/message per event; displayed raw detail is capped. |
| `WPUI01-SAFE-003` | PlayMode | Current build labels every loaded replay `Integrity: not verified`; it never labels a document valid merely because digest-shaped strings exist. |
| `WPUI01-SAFE-004` | EditMode | Unsupported `battle.core/*` outside verified `0.4.0` band may structurally load only with persistent `Untested engine version` warning; event order remains source order. |
| `WPUI01-SAFE-005` | EditMode | More than `200000` events is rejected before session creation; exactly `200000` parseable events passes the count limit. |
| `WPUI01-SAFE-006` | PlayMode | Missing/unknown actor for a cue produces generic worldless cue + warning and does not dereference a null presenter. |
| `WPUI01-SAFE-007` | Standalone | Complete playback of all three bundled samples writes no replay/config/schema file and creates no output beside normal Unity player logs. |

### 5.9 Non-regression and delivery — 8 cases

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-REG-001` | Static | SHA-256 of every pre-existing file under `CombatLab/fixtures/replay/v0.1` equals the pre-implementation baseline; at minimum §3.1 pins match exactly. |
| `WPUI01-REG-002` | Static | `git diff -- CombatLab/src CombatLab/schemas CombatLab/fixtures` is empty for the WP-UI-01 implementation change. |
| `WPUI01-REG-003` | Regression | Existing locked restore, Release build and full `CombatLab.sln` test suite pass without updating package locks. |
| `WPUI01-REG-004` | Static | `git diff --check` is green and no unrelated pre-existing user change is overwritten. |
| `WPUI01-REG-005` | Static | Every acceptance ID in this document is unique and exactly one automated or manual result references it. |
| `WPUI01-REG-006` | Manual | Reviewer can identify both fighters, facing, HP, current tick/sequence and current event throughout each fixture at `1x`. |
| `WPUI01-REG-007` | Manual | Reviewer observes a distinct hit, miss, forced movement, grab, throw, wall impact, defeat and final-result presentation using required fixtures plus synthetic miss. |
| `WPUI01-REG-008` | Static | Unity build/test automation contains no `git commit` or `git push`; version-control publication remains a separate owner action. |

### 5.10 Game-first visual experience — 14 cases

Visual hierarchy and interaction contract are defined by [Game-First UI Spec v0.1](./Unity_Replay_Viewer_UI_Spec_v0.1.md) and its approved reference mockups.

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-UX-001` | Manual | At first load on `1920×1080`, the arena plus fighters occupies at least `60%` of safe viewport height and is the visually dominant region. |
| `WPUI01-UX-002` | Manual | Fighter A/B are identifiable without opening telemetry by name, stable cyan/orange treatment, silhouette/emblem, position, and visible facing. Lettered circles alone fail. |
| `WPUI01-UX-003` | PlayMode | Both exact numeric HP values and both HP fills remain visible for every applied sequence of all three required fixtures. |
| `WPUI01-UX-004` | PlayMode | The default loaded screen has no permanent event-stream column or raw payload panel; `Debug / Telemetry` is closed. |
| `WPUI01-UX-005` | PlayMode | Opening, scrolling, and closing telemetry does not change playback state, cursor, fighter projection, selected speed, or replay bytes. |
| `WPUI01-UX-006` | Manual | `AttackPrepared`, `AttackHit`, and `AttackMissed` have three visually distinguishable arena cues; distinction does not depend on reading a schema event type. |
| `WPUI01-UX-007` | Manual | `DamageApplied` shows a bounded recorded damage number and HP settles at the exact recorded target frame without a second/invented decrement. |
| `WPUI01-UX-008` | Manual | Forced movement, grab, throw, and wall impact each have a distinct visible arena treatment; active grab persists until its matching recorded end. |
| `WPUI01-UX-009` | Manual | `StateChanged` and `FighterDefeated` are readable from the fighter/HUD treatment; defeat produces a distinct `KO` cue. |
| `WPUI01-UX-010` | PlayMode | `BattleEnded` opens a game-style result overlay using recorded outcome/winner/reason; Restart closes it and returns to exact initial projection. |
| `WPUI01-UX-011` | PlayMode | An unknown event produces one bounded generic arena cue and warning, keeps raw detail accessible in telemetry, and does not prevent the next known event. |
| `WPUI01-UX-012` | Manual | At `1280×720`, both fighters, both HP bars, tick/sequence, current primary cue, Play/Pause/Restart/speed, and result remain readable without overlap. |
| `WPUI01-UX-013` | Manual | Default game view contains no JSON field dump, long engine/schema line, or always-visible administrative card grid; those details are available through telemetry. |
| `WPUI01-UX-014` | Static | Presentation code for flashes, tweens, callouts, and overlays has no API/reference that can calculate damage, hit, movement, state, or winner. |

Итого: `96` unique proposed acceptance cases: исходные `82` и `14` утверждённых game-first UX cases.

## 6. Planned execution commands

После реализации и утверждения exact paths:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath '<repo>\UnityClient' `
  -runTests -testPlatform EditMode `
  -testResults '<repo>\UnityClient\TestResults\EditMode.xml'

& 'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath '<repo>\UnityClient' `
  -runTests -testPlatform PlayMode `
  -testResults '<repo>\UnityClient\TestResults\PlayMode.xml'

dotnet restore CombatLab\CombatLab.sln --locked-mode
dotnet build CombatLab\CombatLab.sln -c Release --no-restore
dotnet test CombatLab\CombatLab.sln -c Release --no-build
git diff --check
```

Windows build должен выполняться через tracked Editor build method, чтобы output path и scene list не зависели от локального UI. Build output и TestResults не tracking artifacts.

## 7. Traceability к обязательным требованиям

| Требование | Acceptance IDs |
|---|---|
| Три named fixtures | `LOAD-001..003`, `FIX-001..003` |
| Play/Pause/Restart/speed | `CTRL-001..010` |
| Positions/facing | `HUD-001..004`, `EVT-009..010` |
| HP bars | `HUD-005`, `EVT-006`, `FIX-001..003` |
| Current tick/sequence | `HUD-006`, `FIX-001..003` |
| DecisionMade/ActionCommitted | `EVT-002` |
| AttackPrepared/Hit/Missed | `EVT-003..005` |
| DamageApplied | `EVT-006`, `ARCH-005` |
| StateChanged/FighterDefeated | `EVT-007` |
| Forced movement/grab/throw/wall | `EVT-009..013`, `FIX-003` |
| BattleEnded | `EVT-014..015`, `FIX-001..003` |
| Unknown event without crash | `EVT-017..018`, `SAFE-001` |
| Game-first hierarchy and arena dominance | `UX-001..004`, `UX-012..013` |
| Distinct game presentation cues | `UX-006..011` |
| Opt-in read-only telemetry | `UX-004..005`, `UX-011`, `UX-013` |
| Unity does not calculate combat | `ARCH-001..008` |
| Historical fixtures immutable | `BASE-005`, `REG-001..002` |
| CombatLab production untouched | `ARCH-001..003`, `REG-002..003` |

## 8. Completion record

Этот раздел обновляется по мере implementation execution; `PARTIAL` не считается закрытием gate:

| Gate | Result | Evidence |
|---|---|---|
| Blocking decisions | `PASS` | owner approval `2026-09-08`; UI Spec status `APPROVED / BLOCKING` |
| ID inventory `96/96` | `PASS` | `96` rows, `96` unique IDs, `0` duplicates |
| Unity EditMode | `PASS — 13/13` | `UnityClient/AnimalCombat/TestResults/EditModeGameFirst.txt` |
| Unity PlayMode | `PARTIAL — 3/3 slice` | `UnityClient/AnimalCombat/TestResults/PlayModeGameFirst.txt`; full matrix remains open |
| Windows Standalone build/smoke | `NOT RUN` | — |
| Fixture SHA/source-copy parity | `PASS` | all three required canonical/bundled SHA-256 values match §3.1 |
| CombatLab regression | `NOT RUN` | — |
| Manual visual review | `PARTIAL` | initial, result and wall-impact Game View captures reviewed; remaining fixture/cue matrix open |
| `git diff --check` | `PASS` | local checkpoint `2026-09-08` |

WP-UI-01 остаётся `IN PROGRESS`, пока каждая строка не green и фактические evidence paths не reviewed.
