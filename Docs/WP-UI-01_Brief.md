# WP-UI-01 Brief — Unity Replay Viewer v0.1

> Статус: `IN PROGRESS / VERTICAL SLICE IMPLEMENTED`.
>
> Дата инвентаризации: `2026-09-08`.
>
> Реализация авторизована владельцем `2026-09-08`; рекомендации `OPEN-WPUI01-01..08` приняты для v0.1. Game-first visual refinement утверждён владельцем `2026-09-08` и зафиксирован в отдельном UI Spec. Production-код `CombatLab` и существующие replay fixtures остаются неизменяемыми.

## 1. Решение в краткой форме

WP-UI-01 создаёт отдельный read-only Unity Viewer для визуального воспроизведения canonical replay `combat.replay/0.1`. Viewer не запускает `Battle.Core`, не выбирает действия, не проверяет попадание и не рассчитывает movement, damage, state или outcome. Он последовательно читает уже записанные события и копирует authoritative значения из `BattleStarted.payload.initial_frames`, event `after` frames и `BattleEnded.payload.final_frames` в presentation state.

Первый релиз обязан воспроизводить три неизменяемых Engine `0.4.0` fixture:

- `resolution-basic-l1.engine-0.4.0.json`;
- `resolution-double-ko-l1.engine-0.4.0.json`;
- `resolution-wall-grab-l1.engine-0.4.0.json`.

Полная exact pass/fail matrix находится в [Unity Replay Viewer Test Plan v0.1](./Unity_Replay_Viewer_Test_Plan_v0.1.md). Утверждённый game-first visual/interaction contract и reference mockups находятся в [Unity Replay Viewer Game-First UI Spec v0.1](./Unity_Replay_Viewer_UI_Spec_v0.1.md).

## 2. Исходная Unity-инвентаризация

На момент первоначальной подготовки этапа Unity-проект в репозитории отсутствовал. После owner approval создан проект `UnityClient/AnimalCombat` на зафиксированном Editor `6000.4.0f1`; фактический implementation checkpoint приведён в §13.

| Область | Фактическое состояние |
|---|---|
| Каталог | `UnityClient/` отсутствует и никогда не был tracked в доступной истории Git |
| Project version | `ProjectSettings/ProjectVersion.txt` отсутствует |
| Scenes | tracked `.unity` files отсутствуют |
| Packages | Unity `Packages/manifest.json` и `Packages/packages-lock.json` отсутствуют; `CombatLab/manifest.json` не является Unity package manifest |
| Assembly definitions | `.asmdef` files отсутствуют |
| Локальный Editor | Unity `6000.4.0f1`, revision `8cf496087c8f`; `Unity.exe` file version `6000.4.0.9237654` |
| Unity Hub | `3.16.4` |
| Установленные playback engines | Android, iOS, WebGL и Windows Standalone |

Следствие: последующая реализация должна сначала создать новый проект `UnityClient` на exact Editor `6000.4.0f1`; она не должна притворяться расширением уже существующей сцены или package graph.

## 3. Предлагаемая архитектура

### 3.1 Поток данных

```text
immutable replay JSON
        │
        ▼
IReplaySource ──► ReplayLoader ──► ReplayDocument (wire projection + raw payload)
                                      │
                                      ▼
                               ReplaySession
                         (ordered event cursor/clock)
                              │           │
                    exact frame copy      └──► EventCueMapper
                              │                       │
                              ▼                       ▼
                     ViewerStateStore         presentation-only cues
                              │                       │
                              └──────────┬────────────┘
                                         ▼
                              scene, HUD, event log
```

Граница ответственности:

| Компонент | Делает | Не делает |
|---|---|---|
| `IReplaySource` | Возвращает неизменённые UTF-8 bytes из bundled sample или внешнего пути | Не перезаписывает и не нормализует JSON |
| `ReplayLoader` | Ограничивает размер, разбирает envelope, обязательные поля, frames и raw payload; возвращает typed result | Не вызывает simulation и не исправляет вход |
| `ReplayDocument` | Хранит wire fields и неизвестный payload как JSON tree/token | Не зависит от gameplay assemblies |
| `ReplaySession` | Двигает cursor в исходном array order, реализует Play/Pause/Restart/speed | Не сортирует и не переигрывает события по своим правилам |
| `ViewerStateStore` | Копирует authoritative fighter frames и terminal summary | Не выводит HP, position, state или outcome из формул |
| `EventCueMapper` | Превращает event в presentation-only cue и event-log row | Не создаёт canonical event и не меняет replay |
| Presenters | Линейно отображают arena coordinate, facing, HP и cues | Не передают результат обратно в state или CombatLab |

### 3.2 Authoritative projection policy

1. Load атомарен: новый документ становится текущим только после успешного разбора обязательного envelope.
2. Array order является playback order. Viewer проверяет, что `sequence` начинается с `0`, непрерывен, совпадает с порядком массива, а `tick` не убывает. Повреждённый порядок отклоняется; Viewer его не «исправляет» сортировкой.
3. Начальное состояние берётся из `BattleStarted.payload.initial_frames`. Оно должно совпадать с `input.fighters[].initial_frame`; расхождение является load error для v0.1.
4. При активации event Viewer дословно заменяет известные fighter frames значениями из `event.after.actor` и `event.after.target`, если они присутствуют. Это snapshot application, а не вычисление transition.
5. Event без frame mutation не меняет fighter state. Его payload используется только для label, cue и event details.
6. `BattleEnded.payload.final_frames` дословно задаёт terminal projection; `outcome`, `winner_fighter_id`, `end_reason`, `end_tick` и `duration_ticks` только отображаются.
7. Keyframes не являются альтернативной simulation. В v0.1 они используются для consistency check и будущего seek, но обычный playback идёт от sequence `0`.
8. Presentation tween может интерполировать экранный transform между записанными positions, но после cue обязан закончиться в exact mapped coordinate. Tween никогда не записывается обратно в model.

### 3.3 Независимость от CombatLab

Предлагается не подключать к Unity ни `Battle.Core`, ни `Battle.Replay`, ни `Battle.Config`, ни Runner/CLI. Также не следует делать Unity dependent от compiled `Battle.Contracts.dll`: Viewer нуждается только в узкой schema-driven wire projection, а не в gameplay API.

Unity-side DTO/reader принадлежат `AnimalCombat.ReplayViewer.Contracts` и отражают внешний JSON contract. Изменение wire-схемы требует явного adapter/version decision; изменение C# contracts само по себе не меняет Viewer.

Запрещённые зависимости:

```text
UnityClient ─X─► Battle.Core
UnityClient ─X─► Battle.Config
UnityClient ─X─► Battle.Replay
UnityClient ─X─► CombatLab.Runner / CombatLab.Cli
Battle.Core ─X─► UnityClient
```

Разрешённый вход — только replay bytes, соответствующие опубликованному machine contract.

### 3.4 Предлагаемая структура проекта

```text
UnityClient/
  Assets/
    ReplayViewer/
      Scenes/ReplayViewer.unity
      Runtime/
        Contracts/
        Loading/
        Playback/
        Presentation/
        UI/
      Tests/
        EditMode/
        PlayMode/
    StreamingAssets/Replays/v0.1/
      resolution-basic-l1.engine-0.4.0.json
      resolution-double-ko-l1.engine-0.4.0.json
      resolution-wall-grab-l1.engine-0.4.0.json
  Packages/manifest.json
  Packages/packages-lock.json
  ProjectSettings/ProjectVersion.txt
```

Bundled samples создаются только как byte-for-byte copies. Canonical originals остаются в `CombatLab/fixtures/replay/v0.1/`; automated hash gate запрещает их drift и расхождение копий.

Предлагаемые assembly boundaries:

- `AnimalCombat.ReplayViewer.Contracts` — JSON envelope, raw payload, immutable view records; без `UnityEngine`;
- `AnimalCombat.ReplayViewer.Runtime` — loader, validation result, state reducer, session и clock; зависит только от Contracts;
- `AnimalCombat.ReplayViewer.Presentation` — scene/UI/presenters; зависит от Runtime;
- `AnimalCombat.ReplayViewer.Tests.EditMode` и `.Tests.PlayMode` — test-only assemblies.

Минимальный package intent: Unity UI, Unity Test Framework и один JSON DOM reader, способный сохранять неизвестный payload. Рекомендуемый reader — Unity package `com.unity.nuget.newtonsoft-json`; exact resolved versions фиксируются созданными Editor `6000.4.0f1` manifest/lock и проходят review до начала feature-кода. Дополнительные animation, DI и native file-dialog packages для v0.1 не нужны.

## 4. Mapping canonical events в Viewer

Общее правило важнее таблицы: authoritative state меняется только копированием `after` frames. Event-specific mapping ниже управляет визуальным cue и текстом.

| Replay event | Читаемые поля | Presentation result |
|---|---|---|
| `BattleStarted` | `payload.initial_frames`, arena input | Создать двух бойцов, arena bounds, HP/facing/state; показать sequence `0` |
| `DecisionMade` | actor/target, `chosen_action_id`, `selection_mode`, weights | Decision card и строка event log; gameplay choice не повторяется |
| `ActionCommitted` | envelope `action_id`, `commit_direction`, target, timings | Commit/action label и короткий anticipation cue |
| `AttackPrepared` | `telegraph_tick`, `impact_ticks`, `direction_locked` | Telegraph cue у actor; никаких hit predictions |
| `AttackHit` | `impact_id`, `gap`, `hit_direction`, tags | Hit flash/contact cue и log row |
| `AttackMissed` | `impact_id`, `miss_reason`, range/gap | Miss cue и reason label; state не меняется при пустом `after` |
| `DamageApplied` | `hp_before`, `hp_after`, `breakdown.final`, `lethal`; target `after` frame | HP bar получает exact `after.health`; combat text показывает записанное число, не рассчитывает damage |
| `StateChanged` | `old_state`, `new_state`, duration; actor `after` frame | State badge/animation переключаются на записанное состояние |
| `FighterDefeated` | `defeated_fighter_id`, `final_health` | Defeated pose/overlay; HP берётся из frame |
| `PositionChanged` | `movement_kind`, `from_position`, `to_position`; `after` frames | Tween между recorded coordinates; `Forced`, `Dodge`, `Separation`, `Swap` различаются только cue style |
| `KnockbackApplied` | `from_position`, `to_position`, `actual_move`, `blocked_by_wall` | Forced-movement tween к exact `to_position` |
| `GrabStarted` | `grab_id`, `grabber_id`, `grabbed_id` | Grab link/pose активны до matching end |
| `GrabEnded` | `end_reason`, `throw_action_id`, final positions | Снять grab link; при `end_reason=Throw` показать throw cue |
| `WallImpact` | `wall_side`, `blocked_distance`, `wall_damage`, `wall_stagger` | Wall impact cue на указанной стороне и записанные labels |
| `ResourceChanged` | `resource_kind`, before/after/delta; actor `after` frame | Обновить дополнительный meter из frame; для wall fixture — Stagger |
| `DrawDeclared` | `draw_reason`, participants | Draw/Double-KO pre-result cue |
| `BattleEnded` | outcome/winner/reason/final frames | Остановить cursor, применить final frames, показать result panel |
| Любой другой известный v0.1 event | envelope, raw payload, `after` | Generic log row; применить exact `after`, если оно есть; без придуманного VFX |
| Неизвестный event type | parseable envelope, raw payload, optional `after` | Warning `Unsupported event type`, generic log row, продолжить playback; parseable `after` копируется дословно |

В `combat.event/0.1` нет отдельного `ThrowResolved`: throw отображается по `GrabEnded.payload.end_reason == "Throw"` и `throw_action_id`. Обязательный forced movement в `resolution-wall-grab-l1` записан как `KnockbackApplied`.

Неизвестный `event_type` формально не соответствует enum текущей schema. Поэтому forward-compatible обработка означает graceful degradation Viewer, а не объявление изменённого документа schema-valid или integrity-valid.

## 5. UX v0.1

Первоначальная dashboard-композиция ниже описывает требуемую информацию, но не является финальным visual target. Для реализации действует утверждённый [Game-First UI Spec v0.1](./Unity_Replay_Viewer_UI_Spec_v0.1.md): arena-first экран, broadcast HUD, игровые event cues и закрытая по умолчанию `Debug / Telemetry` панель.

Одна сцена `ReplayViewer.unity` содержит:

- верхнюю строку: replay/engine/schema, load status, warning count, current `tick` и `sequence`;
- 1D arena с двумя различимыми placeholder fighters и стенами;
- HP bars, fighter IDs, facing и state;
- event card и прокручиваемый event log;
- `Play`, `Pause`, `Restart` и speed selector;
- result panel для `BattleEnded`;
- sample selector для трёх bundled fixtures и text path + `Load` для внешнего read-only JSON.

Предлагаемая playback policy:

- после Load и Restart состояние paused на `BattleStarted`, `tick=0`, `sequence=0`;
- базовый presentation slice — `400 ms` на следующий event при `1x`, потому что `combat.replay/0.1` не содержит `tick_ms` и много событий имеют один tick;
- поддерживаемые скорости: `0.25x`, `0.5x`, `1x`, `2x`, `4x`; default `1x`;
- speed меняет только presentation clock; Unity global `Time.timeScale` не используется;
- Pause замораживает cursor и активные presentation tweens;
- Restart сбрасывает transient cues/result/log selection, сохраняет выбранную скорость и не запускает playback автоматически;
- `BattleEnded` переводит session в `Completed`, повторный Play остаётся безопасным no-op до Restart.

Arena coordinate отображается линейно: `input.arena.min_position..max_position` соответствует world X `-8..+8`. Это только presentation transform. Facing `Left/Right` берётся из текущего fighter frame; `commit_direction` и `hit_direction` не переписывают facing.

## 6. In scope

- новый Unity project и одна Viewer scene после отдельного разрешения на implementation;
- чтение `combat.replay/0.1` с verified support band `battle.core/0.4.0`;
- три обязательных WP-09 fixtures;
- Play/Pause/Restart и изменение скорости;
- positions, facing, HP, current tick/sequence, event log;
- event cues, перечисленные в §4;
- graceful unknown-event handling;
- EditMode/PlayMode tests, Windows Standalone smoke и fixture immutability gates.

## 7. Out of scope

- simulation, AI/decision selection, hit/defense/damage/force formulas или outcome calculation;
- изменение любого production-файла под `CombatLab/src`, Runner/CLI, schemas или existing fixtures;
- replay editing, recording, resaving, canonicalization или migration;
- полная реализация `combat.presentation/0.1` и authoring presentation timelines;
- online loading, multiplayer/networking, batch analytics;
- production art, skeletal animation, audio/VFX polish, camera cinematics;
- random access scrub/seek, reverse playback и frame-by-frame edit;
- обещание cryptographic verification, пока не закрыт `OPEN-WPUI01-04`;
- поддержка более чем двух fighters или иных replay schema versions.

## 8. Fixture baseline

| Fixture | File SHA-256 | Events | Final digest | Expected terminal result |
|---|---|---:|---|---|
| `resolution-basic-l1.engine-0.4.0.json` | `c56685b7b9fae47abd1b0cb503b9d2b46cfda4a890c73cc82226810626f70c99` | 11 | `sha256:6c393f1bbb63d7971332df939b0a5c1ce3fe4d42ae4706819b3b643efbb63f99` | Fighter A win, Defeat, tick 0; HP A/B `100/0`, positions `4000/5200` |
| `resolution-double-ko-l1.engine-0.4.0.json` | `1bee7887f1603c0f95e2f48950a54549ff17dd34edb67dd85d51415f39bb188f` | 17 | `sha256:0af2f78597a423b324270e0bbfe00981e35621d1561312b1c2dca3cd1f3b8235` | Draw, DoubleKO, tick 0; HP A/B `0/0` |
| `resolution-wall-grab-l1.engine-0.4.0.json` | `25ee1cbe58c0fa1df40076ca69c79ba2dde0e00b427d4c4b7017a8a65e28843c` | 18 | `sha256:d2d72ec92ec70d5d3af09fe0fe434e3eadd7e30a674b885723687c5a237d407f` | Fighter A win, Defeat, tick 1; B `9500→9570`, HP `100→0`, stagger `0→75→150`, throw/right wall impact |

Hashes фиксируют inspected baseline, но не заменяют canonical internal integrity fields.

## 9. Открытые решения до реализации

| ID | Вопрос | Рекомендация | Blocking |
|---|---|---|---|
| `OPEN-WPUI01-01` | Какой Unity baseline создать при отсутствии проекта? | Новый `UnityClient` на установленном `6000.4.0f1`; version и packages lock tracked | Да |
| `OPEN-WPUI01-02` | Как воспроизводить несколько событий одного tick без `tick_ms`? | Sequence-paced `400 ms/event @ 1x`; tick остаётся только authoritative label | Да |
| `OPEN-WPUI01-03` | Как доставлять обязательные fixtures в Standalone? | Byte-identical StreamingAssets copies + source/copy SHA parity gate; внешний path loader без native dialog | Да |
| `OPEN-WPUI01-04` | Нужна ли cryptographic replay verification в v0.1? | Нет: показывать `Integrity: not verified`; structural checks обязательны, полноценный verifier — отдельный WP | Да |
| `OPEN-WPUI01-05` | Как совместить schema enum и неизвестный event? | Lenient event dispatch после strict envelope parsing; warning, generic row, exact optional frame copy; документ не маркируется schema/integrity verified | Да |
| `OPEN-WPUI01-06` | Какой visual fidelity нужен? | Game-first arcade-fighting/sports-broadcast layout; original placeholder silhouettes, explicit arena/walls, distinct combat cues и opt-in telemetry; production assets позже | Да |
| `OPEN-WPUI01-07` | Какой release target блокирует этап? | Editor tests + Windows Standalone smoke; WebGL/Android/iOS не blocking для v0.1 | Да |
| `OPEN-WPUI01-08` | Допустим ли отдельный JSON package? | Зафиксировать совместимую с Editor версию `com.unity.nuget.newtonsoft-json`; без сторонних Git/registry dependencies | Да |

Production implementation начинается только после owner approval или корректировки blocking рекомендаций и перевода Test Plan в `APPROVED / BLOCKING`.

Owner approval получен `2026-09-08`; решения закрыты. В тот же день visual recommendation `OPEN-WPUI01-06` уточнена и повторно утверждена через Game-First UI Spec. Их статус для v0.1 — `CLOSED / IMPLEMENTATION AUTHORIZED`.

## 10. План последующей реализации

1. Закрыть `OPEN-WPUI01-01..08`, зафиксировать approved versions/policies и не менять acceptance IDs.
2. Создать пустой `UnityClient` на `6000.4.0f1`, track ProjectSettings, package manifest/lock, `.gitignore`, scene и assembly definitions.
3. Добавить byte-identical bundled fixture copies и automated source/copy/hash guard; canonical originals не трогать.
4. Реализовать schema-oriented raw loader и typed result/error model с limits, atomic load и unknown-event preservation.
5. Реализовать pure C# state projection и session clock; сначала EditMode tests, включая запрет любых gameplay calculations.
6. Реализовать event cue mapping и generic unknown cue.
7. Собрать сцену, arena mapping, fighter presenters, HP/state HUD, event log и controls.
8. Добавить PlayMode acceptance для controls, three-fixture playback и failure paths.
9. Выполнить Unity EditMode/PlayMode tests, Windows Standalone build/smoke, fixture SHA gates, CombatLab regression и `git diff --check`.
10. Провести ручной visual review, записать фактические результаты в Test Plan и только после green gates обновлять общий status/index отдельным reviewed change.

## 11. Definition of Done

WP-UI-01 может получить `COMPLETED` только если:

- все blocking decisions закрыты;
- все acceptance IDs из Test Plan реализованы и green;
- три fixtures доходят до exact terminal projections;
- неизвестный event не приводит к exception или остановке последующих событий;
- статические dependency gates подтверждают отсутствие gameplay/runtime coupling;
- исходные replay fixture bytes и весь существующий CombatLab production-код неизменны;
- tracked Unity project version/packages/scenes/asmdefs соответствуют утверждённому baseline;
- Windows Standalone smoke и Unity EditMode/PlayMode suites green;
- Unity build/test automation не создаёт git commit или push; version-control actions остаются ручным решением владельца.

## 12. Нормативные источники

Порядок применения источников сохраняется из [Index](./Index.md):

1. [Docs/Index.md](./Index.md);
2. [Docs/Implementation_Status.md](./Implementation_Status.md);
3. [Docs/Decisions.md](./Decisions.md);
4. [Docs/WP-09_Brief.md](./WP-09_Brief.md);
5. [Docs/Combat_Test_Plan_WP-09_v0.1.md](./Combat_Test_Plan_WP-09_v0.1.md);
6. `CombatLab/schemas/replay/v0.1/`;
7. `CombatLab/fixtures/replay/v0.1/`.

При конфликте machine schema/fixture имеет приоритет для wire representation. Viewer не переопределяет CDS gameplay semantics и не делает уже написанный код нормативным источником.

## 13. Implementation checkpoint — 2026-09-08

Первый vertical slice реализован в `UnityClient/AnimalCombat`:

- отдельная сцена `Assets/ReplayViewer/Scenes/ReplayViewer.unity` на UI Toolkit;
- assemblies `Contracts`, `Runtime`, `Presentation`, `Tests.EditMode` и `Tests.PlayMode`;
- structural loader `combat.replay/0.1`, contiguous sequence/nondecreasing tick checks и exact initial-frame parity;
- state projection только из `initial_frames`, event `after` и `final_frames`;
- Play, Pause, Restart, speed `0.25×..4×`, sequence-paced clock `400 ms/event @ 1×`;
- game-first UXML/USS: arena-first composition, broadcast HUD, игровые fighter silhouettes, transport bar, result overlay и закрытая по умолчанию telemetry drawer;
- exact replay position/facing, HP/state, current tick/sequence, event stream и terminal result;
- presentation-only mapping обязательных событий, distinct prepared/hit/miss/damage/movement/grab/throw/wall/result cues и generic unknown-event fallback;
- byte-identical StreamingAssets copies трёх обязательных fixtures.

После visual review владелец отклонил dashboard-доминантный вид как финальный UI и `2026-09-08` утвердил [Game-First UI Spec v0.1](./Unity_Replay_Viewer_UI_Spec_v0.1.md) с desktop/mobile-landscape reference mockups. Утверждённая game-first композиция перенесена в UXML/USS; технический event stream теперь доступен только через opt-in telemetry drawer. Unity по-прежнему копирует recorded state/value и не рассчитывает бой, урон, movement или outcome.

Проверка checkpoint:

- Unity compilation: `0 errors / 0 warnings`;
- EditMode: `13 passed / 0 failed / 0 skipped`;
- минимальный game-first PlayMode gate: `3 passed / 0 failed / 0 skipped` — arena dominance/default visibility, telemetry isolation и recorded result flow;
- basic runtime smoke: sequence `10/10`, `BattleEnded`, HP `100/0`, `FighterAWin`;
- double-KO runtime smoke: sequence `16/16`, HP `0/0`, `Draw`, `DrawDeclared` отображён;
- wall-grab runtime smoke: sequence `17/17`, HP B `0`, `GrabStarted`, `KnockbackApplied`, `WallImpact` и `GrabEnded` отображены;
- bundled/canonical SHA-256 and byte parity: green для всех трёх fixtures;
- manual Game View capture: game-first initial state, terminal result и точный wall-impact slice (`tick 1`, `sequence 11/17`, recorded wall damage `93`) проверены на wide `16:9` viewport.

Checkpoint не закрывает WP-UI-01. Остаются полная автоматизация `WPUI01-UX-001..014` и three-fixture PlayMode matrix, negative-load matrix, external-path UX, Windows Standalone build/smoke и полный static dependency gate.
