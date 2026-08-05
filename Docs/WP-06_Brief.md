# WP-06 Engine shell

> Статус: `COMPLETED`.
>
> Exact pass/fail: [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md).

## Цель

Реализовать минимальное детерминированное ядро одного боя:

- публичный `CombatEngine.Simulate`;
- pre-start validation и атомарный Battle Setup;
- внутренние `BattleState`/`FighterState` и initial public frames;
- фиксированный 12-фазный `TickCoordinator`;
- system-action vertical slice;
- defeat/draw/timeout outcome shell, event cap и zero-progress watchdog;
- canonical lifecycle от journal Begin до `BattleEnded`/Complete.

Главный acceptance target — `wait_equal_l1`: короткий system-wait battle с точным 8-event trace, описанным в Test Plan.

## Нормативные источники

- Combat Design Specification v0.1: §§2, 3.1–3.4, 4.1–4.3, 5.2, 6.1–6.2, 7.1, 8.1, 9.4–9.5, 12.3–12.4, 14.1, 14.3, 15, 16.2–16.3.
- Combat Lab Technical Design v0.1: §§5–6, 10–12, 14.3, 15.2, 18, 21, 23.1–23.3, 25 и приложения B, C, E.
- Combat Event & Replay Schema v0.1: §§2.3, 5–8, 9.1–9.2, 10.1–10.2, 14–15, 17, 19.3, 21.
- `CombatLab/config/generated/combat.balance.v0.1.json` и соответствующий `CompiledBattleConfig` — runtime DATA/Stable IDs.
- [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md) — exact acceptance matrix WP-06.
- [Decisions.md](./Decisions.md) — история source conflicts и закрытия OPEN.

При конфликте игровое правило берётся из CDS, wire/integrity — из Replay Schema, архитектура — из Technical Design, pass/fail WP-06 — из Test Plan.

## Implementation readiness

`OPEN-05` и `OPEN-WP06-01..07` закрыты Test Plan и принятыми решениями:

- timeout boundary и terminal trace определены;
- system fallback priority и system-wait fixture определены;
- identity/input/final digest data flow определён без reverse dependency;
- Mode Rules, стартовые values и technical settings определены;
- raw-input/strict-constructor boundary определён.

Требования реализованы; artifact gate закрыт, acceptance matrix и Release build/test green.

## Архитектурные границы

- Production-код engine shell размещается в `Battle.Core`; contract refinements — в `Battle.Contracts`.
- `Battle.Core` зависит только от `Battle.Contracts` и BCL.
- Запрещены зависимости от `Battle.Config`, `Battle.Replay`, Runner/CLI infrastructure, Unity, filesystem, network, wall-clock time и task scheduler.
- `Simulate` синхронен; внутри одного боя нет I/O, `async` или cancellation polling.
- `BattleRequest`, `ModeRulesSnapshot`, `CompiledBattleConfig` и input snapshot неизменяемы после `BattleStarted`.
- Journal принимает simulation facts и возвращает только identity/integrity receipts; он не возвращает gameplay data и не участвует в решениях.
- Все gameplay numbers, стартовые позиции, timings и system actions читаются из compiled config; hardcode баланса запрещён.

## Public execution contract

```csharp
public sealed class CombatEngine
{
    public BattleResult Simulate(
        BattleRequest request,
        CompiledBattleConfig config,
        ICombatEventJournal journal);
}
```

`BattleResult` всегда различает:

- `Completed` — корректно завершённый бой с summary/final digest;
- `Rejected` — ожидаемая validation ошибка до journal Begin/`BattleStarted`;
- `FailedInvariant` — техническое нарушение после старта; это не draw/loss и не основание для награды.

Ожидаемый invalid input не выражается exception. Null typed API arguments и невозможный direct constructor call являются programming errors.

## Identity и journal receipts

`BattleRequest` получает обязательные `BattleId` и immutable `ModeRulesSnapshot`. `input_digest` в request не хранится, потому что зависит от `ReplayId` и engine-derived initial frames.

Целевой port:

```csharp
public readonly record struct JournalBeginResult(
    Sha256Digest InputDigest);

public readonly record struct JournalCompletion(
    Sha256Digest FinalDigest,
    ExternalId? PublishedReplayId);

public interface ICombatEventJournal
{
    JournalBeginResult Begin(in CombatJournalStart start);
    CombatEventIdentity Append(in CombatEventDraft draft);
    JournalCompletion Complete(in BattleSummary summary);
}
```

Порядок вызовов:

1. Validate request/config.
2. Атомарно initialize оба fighters и initial frames.
3. `Begin` вычисляет input digest; concrete journal добавляет назначенный composition root `ReplayId`.
4. Core выпускает `BattleStarted` с digest из receipt.
5. После terminal outcome Core выпускает `BattleEnded` последним.
6. `Complete` возвращает final digest/optional published replay ID для `BattleResult.Completed`.

Rejected path не вызывает ни один journal method. Append до Begin, повторный Begin/Complete и Append после BattleEnded запрещены.

Standard, Diagnostic и SummaryOnly считают одну chain потоково. При одинаковом ReplayId их input/final digests совпадают; SummaryOnly может освобождать event bodies. Final digest не помещается внутрь `BattleEndedPayload`, чтобы не создавать self-referential hash.

## Pre-start validation

До journal Begin engine детерминированно проверяет:

1. engine/config/schema/RNG/ordering/mode versions и config hash;
2. два build snapshots с правильными Fighter ID/side;
3. существующий AnimalId, два разных special нужного животного, один passive, gear Offense/Defense/Utility правильной категории и tactic;
4. ownership всех Stable IDs и Mode Rules allowlists;
5. только `NormalizationMode.None` для WP-06;
6. обязательные runtime settings и безопасные integer ranges;
7. возможность построить оба состояния и public frames без partial mutation.

Structural source JSON/Workbook validation остаётся ответственностью WP-04. Raw external DTO сначала проходит non-throwing factory, накапливающий deterministic sorted rejection errors; strict contract objects создаются только при отсутствии ошибок.

При expected rejection journal остаётся untouched, replay не создаётся, а попытка не считается draw/loss/completed battle.

## Инициализация tick 0

Минимальный runtime state содержит tick/sequence/phase/outcome, fighter identity/position/facing, health/energy/resource, base/derived stats, state/action timers, cooldowns/effects/control state, watchdog counters и два gameplay RNG streams.

Порядок modifiers по CDS §6.2:

1. base animal profile;
2. mode normalization;
3. gear;
4. passive initialization;
5. permanent effects;
6. temporary effects;
7. clamp.

Внутри слоя: `Priority`, затем Stable ID. Оба FighterState полностью созданы до passive initialization; initialization не потребляет RNG.

Стартовые значения:

- current HP/energy равны итоговым derived MaxHealth/MaxEnergy;
- unique resource берётся из `start_resource`, maximum — из fighter DATA;
- stagger/cooldowns/effects/opportunity/control/watchdog counters равны `0`/empty;
- state `DecisionReady`, action/phase/timer null;
- позиции `global.arena.start_position_a/b`, facing навстречу друг другу;
- tick/next sequence `0`, Decision/Resolution RNG indices `0`.

Новые `start_health`/`start_energy` keys не нужны.

## Technical settings

Source workbook, schema и generated config содержат:

| Key | v0.1 | Validation |
|---|---:|---|
| `global.sim.max_events_per_battle` | `200000` | integer `4..200000` |
| `global.sim.max_zero_progress_ticks` | `100` | positive integer |

Missing/wrong/range value даёт pre-start rejection; default в Core запрещён. Изменение generated config/hash выполняется только штатной регенерацией.

Effect/trigger execution caps принадлежат WP-10. WP-06 сохраняет текущий Replay Schema cap `128` для public effect frames, но не реализует effect system досрочно.

## TickCoordinator

Один coordinator вызывает фазы строго в следующем порядке. Перестановка требует bump `global.sim.ordering_version`.

| № | Фаза | Компонент | Читает | Пишет |
|---:|---|---|---|---|
| 1 | Snapshot | `SnapshotBuilder` | `BattleState` | immutable `TickSnapshot` |
| 2 | Expiry | `TimerSystem` | snapshot/effects | timers, removals, derived cache |
| 3 | Resource | `ResourceSystem` | state profile | periodic resources/cooldowns |
| 4 | Phase end | `ActionPhaseSystem` | actions/timers | transitions, decision-ready |
| 5 | Decisions | `DecisionSystem` | один `TickSnapshot` | decision intents + atomic commits |
| 6 | Voluntary move | `MovementSystem` | committed movement | positions + separation |
| 7 | Collect intents | `IntentCollector` | active phases/state | defense/strike/grab/force buffers |
| 8 | Sort | `IntentOrderer` | intent buffers | canonical ordered groups |
| 9 | Resolve | `ResolutionSystem` | ordered intents/snapshot | mutation batch |
| 10 | Walls/grabs | `SpatialControlSystem` | post-impact state | wall/throw/knockdown effects |
| 11 | Outcome | `OutcomeSystem` | complete resolution group | defeat/draw |
| 12 | End tick | `EndTickSystem` | final state | expiry, events, watchdog, next tick |

No-op phase всё равно вызывается. Оба decisions читают один snapshot; commit A не меняет candidates B. Event before/after снимается из authoritative mutation; full resolution group применяется до outcome.

WP-06 создаёт orchestration shell и минимальные implementations для `sys_wait`. Movement, general availability/weights, combat resolution и effects остаются WP-07–WP-10.

## Timeout boundary

При `battle.time_limit_ticks = N > 0` coordinator выполняется для ticks `0..N-1`. Phase 12 увеличивает tick один раз. На следующей границе `tick == N` OutcomeSystem выполняет timeout до нового snapshot; coordinator pass tick N не запускается.

Defeat/DoubleKO phase 11 последнего активного tick имеет приоритет. Если outcome отсутствует, сравниваются:

```text
left  = hpA * maxHpB
right = hpB * maxHpA
```

Вычисление exact Int64 без деления:

- left > right: TimeoutReached → A win BattleEnded;
- right > left: TimeoutReached → B win BattleEnded;
- equality: TimeoutReached → DrawDeclared → BattleEnded.

Terminal events получают `tick = end_tick = duration_ticks = N`. Limit ≤ 0 отклоняется до старта; state tick > limit — invariant failure.

## System-action vertical slice

Fixed priority среди уже legal system actions:

1. `sys_approach`;
2. `sys_retreat`;
3. `sys_wait`.

Predicates не ослабляются. Only-legal и zero-weight fallback не используют RNG. Пустой legal system list после старта даёт `FailedInvariant`.

WP-06 selector поддерживает только `sys_wait`; `sys_approach`/`sys_retreat` availability и movement реализуются в WP-07, общий selector — в WP-08.

Golden case `wait_equal_l1` использует limit `1` и exact trace:

```text
BattleStarted
DecisionMade A
DecisionMade B
ActionCommitted A
ActionCommitted B
TimeoutReached
DrawDeclared
BattleEnded
```

Полные sequence/tick/payload/frame/summary oracles находятся в [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md).

## Event cap и zero-progress watchdog

- Event cap включает Started/Ended и всегда резервирует один terminal slot.
- Попытка занять резерв завершает доступный journal `BattleEnded(Invalid, BattleInvalid)` и возвращает `FailedInvariant`; cap не превышается.
- Progress stamp включает authoritative positions/resources/states/action lifecycle/cooldowns/effects/control/outcome.
- Tick/sequence/log/diagnostics/RNG-only изменения не считаются progress.
- Неизменный stamp увеличивает counter; изменение сбрасывает его.
- При counter == `global.sim.max_zero_progress_ticks` создаётся invalid lifecycle и `FailedInvariant`, не draw.
- Успешный system-action commit/lifecycle mutation считается progress, поэтому legal wait battle не получает ложный watchdog до timeout.

## Автоматические тесты

Полный blocking перечень с IDs `WP06-*` находится в [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md). Обязательные suites покрывают:

- journal Begin/Append/Complete и existing digest vectors;
- validation/rejection/factory boundary;
- exact initialization order/state/RNG indices;
- 12-phase trace и shared snapshot;
- system priority/only-legal/zero-weight paths;
- timeout A/B/equality/Int32 boundary/final-tick precedence;
- exact `wait_equal_l1` replay;
- event cap, watchdog и post-start invariant lifecycle;
- repeated/profile/target determinism;
- architecture/Unity boundaries.

Coverage gate: 100% branch coverage timeout comparison и transition/terminal guards; snapshot tests не заменяют semantic assertions.

## Ready-to-implement criteria

- [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md) существует и содержит exact blocking matrix.
- `OPEN-05` и `OPEN-WP06-01..07` отмечены `CLOSED` в [Decisions.md](./Decisions.md).
- Contract data flow, timeout boundary, system fixture, technical settings и invalid-input boundary полностью определены.
- Архитектурные границы и out-of-scope WP-07+ сохранены.

Эти критерии готовности выполнены; реализация начата и завершена на уровне кода.

## Результат реализации

Реализованы contract refinements, raw boundary, полная pre-start validation, атомарный setup, runtime state, 12-фазный coordinator, system-action policy, timeout/outcome shell, event cap, watchdog и invalid failure lifecycle. Journals используют `Begin → Append → Complete`; Standard journal собирается в полный canonical replay и проходит текущие schema/semantic/integrity validators.

Golden `wait_equal_l1` закреплён точным 8-event oracle и digest-векторами:

- input: `sha256:0edc1dbc1d8d2a09c38debed5626fba5637f7304a38df258342d1d959edc8ba2`;
- final: `sha256:d06e3c2153a4fbfc495279cd6fcf7379d6f8d42c059e8756a5003d01acfa9ea6`.

CI дополнен исполняемым сравнением полного replay для `netstandard2.1`/`net10.0` и 100% branch gate для timeout comparison и transition/terminal guards.

`BLOCK-WP06-01 — CLOSED`: source workbook и generated config/schema/map/validation/manifest штатно регенерированы и содержат оба обязательных technical settings. Validation: `0 errors / 0 warnings`. Canonical config hash: `sha256:0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f`; source workbook SHA-256: `sha256:bfd8a1d70ac82d5f830a981be078ebe60772a765553d842f73f1fb6b85d54fe2`. Golden `wait_equal_l1` digests не изменились.

## Definition of Done WP-06

- [x] Contract refinements, `CombatEngine`, initialization, runtime state, 12-phase coordinator, outcome shell и watchdog реализованы.
- [x] Golden `wait_equal_l1` и code-level blocking acceptance matrix green.
- [x] Pre-start rejection не создаёт journal session; post-start invariant не маскируется под игровой результат.
- [x] `Battle.Core` сохраняет разрешённые зависимости; `UnityClient` не изменён.
- [x] Generated config/schema/manifest регенерированы штатно, новый config hash воспроизводим.
- [x] `dotnet restore --locked-mode`, Release build и Release test проходят.
- [x] `Implementation_Status.md` переведён в `COMPLETED` после закрытия artifact gate.

## Не входит в WP-06

- Полная 1D geometry и movement rules — WP-07.
- Weighted decision selector, opportunity debt и AI knowledge — WP-08.
- Defense/damage/stagger/force/grab resolution — WP-09.
- Effect/trigger/stacking/anti-loop execution — WP-10.
- Полные fighter kits — WP-11.
- Batch/CLI/storage/deployment и production rollout — WP-12+.
- Изменения `UnityClient` или перенос gameplay rules в Unity.
