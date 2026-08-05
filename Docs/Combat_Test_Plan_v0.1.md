# Combat Test Plan v0.1 — WP-06 Engine shell

> Статус: `EXECUTED / PASSED`; WP-06 — `COMPLETED`.
>
> Этот Markdown-документ является явным проектным решением, подготовленным на основе нормативных источников. Он закрывает `OPEN-05` и `OPEN-WP06-01..07` только для WP-06; он не выдаётся за отсутствовавший оригинальный DOCX.

## 1. Назначение

Документ задаёт точную pass/fail-матрицу для WP-06 Engine shell:

- contract refinements, необходимые для запуска одного боя;
- validation и атомарную initialization;
- фиксированный 12-фазный tick coordinator;
- минимальный system-action battle;
- timeout boundary, outcome lifecycle и watchdog;
- determinism, replay integrity и архитектурные ограничения.

Матрица реализована и выполнена; итог закрытия WP-06 зафиксирован в §15.

## 2. Нормативные источники

- Combat Design Specification v0.1: §§3.1–3.4, 4.1–4.3, 5.2, 6.1–6.2, 7.1, 9.4–9.5, 14.1–14.3 и §15.
- Combat Lab Technical Design v0.1: §§10–12, 14.3, 15.2, 18, 21, 23.1–23.3, §25 и приложение B.
- Combat Event & Replay Schema v0.1: §§2.3, 5–8, 9.1–9.2, 10.1–10.2, 14–15, 17 и 19.3.
- Текущий canonical balance artifact и `CompiledBattleConfig`.
- [WP-06 Brief](./WP-06_Brief.md) — scope и архитектурные границы.
- [Decisions](./Decisions.md) — принятые решения и история закрытия OPEN.

При конфликте игровое правило берётся из CDS, wire/integrity — из Replay Schema, архитектура — из Technical Design, а exact pass/fail для WP-06 — из этого Test Plan.

## 3. Закрытие OPEN

| OPEN | Решение |
|---|---|
| `OPEN-05`, `OPEN-WP06-01` | Этот документ является exact acceptance matrix для WP-06. |
| `OPEN-WP06-02` | Timeout разрешается на pre-tick boundary `tick == time_limit`; активные тики — `0..limit-1`. |
| `OPEN-WP06-03` | Fixed fallback priority: `sys_approach > sys_retreat > sys_wait` только среди уже legal system-actions; WP-06 acceptance использует единственный supported candidate `sys_wait`. |
| `OPEN-WP06-04` | `BattleRequest` получает `BattleId`/`ModeRulesSnapshot`; input digest возвращает `journal.Begin` после initialization. |
| `OPEN-WP06-05` | `journal.Complete` возвращает неигровой integrity receipt с final digest и optional published replay ID. |
| `OPEN-WP06-06` | Зафиксированы Mode Rules, стартовые значения, event cap и zero-progress watchdog settings. |
| `OPEN-WP06-07` | Raw external input валидирует non-throwing factory; строгие typed constructors сохраняют programming invariants. |

## 4. Contract refinements

### 4.1 BattleRequest и Mode Rules

`BattleRequest` получает:

- обязательный `ExternalId BattleId`;
- immutable `ModeRulesSnapshot` вместо одной версии режима.

`ModeRulesSnapshot` содержит:

- `StableId Id`;
- `ArtifactVersion Version = mode.rules/0.1`;
- `NormalizationMode`;
- явно заданные allowlists для animals, actions, passives, gear и tactics.

Каждый allowlist defensively copied, строго отсортирован ordinal по Stable ID и не содержит повторов. Sentinel `all`, пустой список как implicit-all и runtime defaults запрещены.

WP-06 поддерживает только `NormalizationMode.None`. Другой режим возвращает deterministic `BattleResult.Rejected` до journal begin. Изменение смысла Mode Rules требует нового `mode_rules_id` либо engine version. Replay wire по v0.1 хранит только `mode_rules_id`.

Для обычного запуска используется ID `combat_lab_standard_v01`; для короткого acceptance fixture — `engine_shell_wait_v01`.

### 4.2 Journal lifecycle

`input_digest` не добавляется в `BattleRequest`: digest зависит от `replay_id` и engine-derived initial frames и потому не существует до initialization.

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

`CombatJournalStart` является immutable contract и содержит:

- `BattleId`;
- engine/RNG/ordering versions;
- `ConfigReference`;
- `BattleInputSnapshot`: master seed, Mode Rules ID, arena;
- два build snapshots и соответствующие полностью рассчитанные initial frames.

Concrete journal создаётся composition root с `ReplayId` и profile. Journal добавляет `ReplayId` к input projection, вычисляет SHA-256 и возвращает `JournalBeginResult`. Core копирует `InputDigest` в `BattleStartedPayload`.

После последнего `BattleEnded` метод `Complete` возвращает `JournalCompletion`; Core передаёт его `FinalDigest` и `PublishedReplayId` в `BattleResult.Completed`.

Receipt содержит только identity/integrity metadata и не является gameplay data. `Append` по-прежнему возвращает только causal event identity. Cast Core к concrete replay journal и зависимость `Battle.Core → Battle.Replay` запрещены.

Все profiles считают одну chain потоково:

- Standard/Diagnostic сохраняют canonical bodies;
- SummaryOnly хеширует те же drafts и освобождает bodies;
- FailureCapture хранит bounded tail и не маскируется под published replay.

При одинаковых `ReplayId`, request и config Standard, Diagnostic и SummaryOnly дают одинаковые input/final digests. `BattleEndedPayload` не содержит final digest: digest последнего event вычисляется только после его сериализации.

### 4.3 Обязательный lifecycle вызовов

1. Validate raw/typed input и request/config compatibility.
2. Атомарно построить оба FighterState и initial frames.
3. Вызвать `journal.Begin` ровно один раз.
4. Выпустить `BattleStarted` с digest из begin receipt.
5. Выполнить активные ticks и append canonical events.
6. Выпустить `BattleEnded` последним event.
7. Вызвать `journal.Complete` ровно один раз.
8. Сформировать `BattleResult` из summary и completion receipt.

Rejected path не вызывает `Begin`, `Append` или `Complete`. Append до Begin, второй Begin, Append после BattleEnded и второй Complete являются programming/invariant errors.

## 5. Initialization oracle

### 5.1 Modifier order

Применяется решение CDS §6.2:

1. base animal profile;
2. mode normalization;
3. gear;
4. passive initialization;
5. permanent effects;
6. temporary effects;
7. clamp.

Внутри слоя: `Priority`, затем Stable ID. Оба FighterState полностью создаются до passive initialization; initialization не потребляет Decision/Resolution RNG.

### 5.2 Стартовые значения

- current health = итоговый derived `MaxHealth`;
- current energy = итоговый derived `MaxEnergy`;
- unique resource current = `fighters[].start_resource`;
- unique resource maximum = configured/derived `max_resource`;
- stagger, cooldowns, effects, opportunity/control/watchdog counters = `0`/empty;
- state = `DecisionReady`, action ID/action phase/state timer = `null`;
- positions = `global.arena.start_position_a/b`;
- facing: A вправо, B влево;
- tick = `0`, next sequence = `0`;
- Decision и Resolution RNG созданы по WP-03, их indices = `0`.

Новые DATA keys `start_health` и `start_energy` не создаются.

### 5.3 Обязательные technical settings

В source workbook/schema/generated config добавлены и зафиксированы:

| Key | Значение v0.1 | Validation |
|---|---:|---|
| `global.sim.max_events_per_battle` | `200000` | integer `4..200000`; учитывает `BattleStarted` и `BattleEnded` |
| `global.sim.max_zero_progress_ticks` | `100` | positive integer |

Отсутствие key, неверный тип или значение вне диапазона даёт pre-start rejection без default. Добавление settings меняет canonical config hash; hash обновляется только через штатный WP-04 export/generation pipeline.

Effect frame остаётся ограничен `128` элементами текущей Replay Schema. Trigger/effect execution caps реализуются и тестируются владельцем семантики WP-10; WP-06 не создаёт trigger/effect system досрочно.

## 6. Tick и timeout boundary

### 6.1 Активные ticks

При `battle.time_limit_ticks = N`, где `N > 0`:

- initialization и первый common decision snapshot относятся к tick `0`;
- `TickCoordinator` вызывается ровно для ticks `0..N-1`;
- каждый активный tick вызывает все 12 фаз в нормативном порядке, включая no-op phases;
- phase 12 увеличивает tick ровно один раз;
- когда tick становится `N`, до Snapshot/phase 1 выполняется timeout boundary check;
- на tick `N` новый coordinator pass, snapshot, decisions и gameplay phases не запускаются.

Это разрешает расхождение источников так: все последствия последнего разрешённого tick полностью применены по CDS, после чего guard `tick >= time_limit` из приложения B Technical Design срабатывает на следующей границе.

`N <= 0` отклоняется до старта. `state.Tick > N` является invariant failure.

### 6.2 Приоритет outcome

Defeat/DoubleKO, возникшие в phase 11 tick `N-1`, завершают бой и имеют приоритет над timeout. В таком случае `TimeoutReached` не выпускается.

Если outcome отсутствует, timeout использует post-state последнего активного tick:

```text
left  = hpA * maxHpB
right = hpB * maxHpA
```

Вычисление выполняется exact `Int64` с существующей overflow policy.

- `left > right`: `TimeoutReached` → `BattleEnded(FighterAWin, TimeoutHealthFraction)`;
- `right > left`: `TimeoutReached` → `BattleEnded(FighterBWin, TimeoutHealthFraction)`;
- equality: `TimeoutReached` → `DrawDeclared(TimeoutEqualHealthFraction)` → `BattleEnded(Draw, TimeoutEqualHealthFraction)`.

Все timeout events имеют `tick = end_tick = duration_ticks = N`. `BattleEnded` всегда последний; event count включает его.

## 7. System-action policy

Availability predicates применяются до fallback priority и никогда не ослабляются. Среди уже legal system-actions действует fixed order:

1. `sys_approach`;
2. `sys_retreat`;
3. `sys_wait`.

Выбор единственного legal system action:

- `DecisionSelectionMode.OnlyLegalAction`;
- `rng = null`, Decision RNG index не меняется;
- chosen weight и weight sum берутся из config;
- reason code `OnlyLegalAction`.

Если legal candidates существуют, но их weight sum равен `0`:

- выбирается первый legal system action по fixed order;
- `DecisionSelectionMode.ZeroWeightFallback`;
- chosen weight = `0`, weight sum = `0`, `rng = null`;
- reason code `ZeroWeightFallback`.

Пустой legal system list после старта является `FailedInvariant`; запрещено молча снимать distance, wall, control или другие predicates.

WP-06 не реализует availability/movement semantics `sys_approach` и `sys_retreat`: это WP-07/WP-08. Engine-shell selector предоставляет только supported candidate `sys_wait`; общий selector заменит этот узкий seam в WP-08.

## 8. Golden acceptance case `wait_equal_l1`

### 8.1 Input

- case ID: `wait_equal_l1`;
- battle ID: `battle-wp06-wait-equal-l1`;
- replay ID: `replay-wp06-wait-equal-l1`;
- master seed: `2026072901`;
- engine/RNG/ordering versions: текущие `ContractVersions`;
- Mode Rules: `engine_shell_wait_v01`, `mode.rules/0.1`, normalization `None`, явные allowlists;
- config: canonical WP-06 fixture, эквивалентный generated v0.1, но с `battle.time_limit_ticks = 1` и обязательными technical settings;
- system availability seam возвращает только `sys_wait`.

Fighter A:

- bear;
- specials `bear_earthbreaker`, `bear_rampage_charge`;
- passive `bear_thick_hide`;
- gear `gear_offense_power_wraps`, `gear_defense_reinforced_hide`, `gear_utility_sprint_soles`;
- tactic `tactic_pressure`.

Fighter B:

- kangaroo;
- specials `kangaroo_flying_kick`, `kangaroo_tail_counter`;
- passive `kangaroo_never_still`;
- gear `gear_offense_precision_lens`, `gear_defense_reinforced_hide`, `gear_utility_sprint_soles`;
- tactic `tactic_position`.

Initial oracle:

| Fighter | Position/facing | HP | Energy | Unique resource | State |
|---|---|---:|---:|---|---|
| A | `2000` / Right | `1650/1650` | `1000/1000` | `rage 0/1000` | DecisionReady |
| B | `8000` / Left | `1150/1150` | `1000/1000` | `tempo 0/1000` | DecisionReady |

Initiative order в `BattleStarted`: B, затем A; это не меняет canonical actor emission order A, затем B.

### 8.2 Exact canonical trace

| Sequence | Tick | Event | Actor → target | Source | Обязательная семантика |
|---:|---:|---|---|---|---|
| 0 | 0 | `BattleStarted` | null | null | initial frames, initiative B/A, input digest из Begin |
| 1 | 0 | `DecisionMade` | A → B | event 0 | `sys_wait`, legal `[sys_wait]`, count 1, weight `150/150`, `OnlyLegalAction`, RNG null |
| 2 | 0 | `DecisionMade` | B → A | event 0 | те же правила, один общий TickSnapshot |
| 3 | 0 | `ActionCommitted` | A → B | event 1 | costs `0/0`, startup `0`, active `3`, recovery `0`, cooldown `0`, direction None |
| 4 | 0 | `ActionCommitted` | B → A | event 2 | те же frozen commit values |
| 5 | 1 | `TimeoutReached` | null | null | health `1650/1650` и `1150/1150`; cross-products оба `1897500` |
| 6 | 1 | `DrawDeclared` | null | event 5 | reason `TimeoutEqualHealthFraction`, participants A/B, group null |
| 7 | 1 | `BattleEnded` | null | event 6 | Draw, end reason `TimeoutEqualHealthFraction`, summary count `8` |

Event IDs: `evt-0000000000` … `evt-0000000007`. Decision IDs: `dec-fighter_a-000001` и `dec-fighter_b-000001`.

Action commit target position равен позиции opponent. После commit actor frame использует нормативную для WP-06 проекцию: state `Idle`, action `sys_wait`, phase `Active`, remaining `3`. До terminal boundary phase timer повторно не декрементируется; final frames сохраняют это состояние, полные HP/energy/resources и исходные позиции.

Outcome events имеют null actor/target frames, RNG, action/effect/decision/resolution group. `TimeoutReached` использует reason `TimeLimitReached`; Draw/BattleEnded — `TimeoutEqualHealthFraction`.

Summary:

- outcome `Draw`, winner null;
- end reason `TimeoutEqualHealthFraction`;
- end tick/duration `1`;
- event count `8`;
- pivotal IDs: timeout event, затем draw event;
- final frames: A, затем B.

Новый literal digest заранее не задаётся: WP-05 уже фиксирует canonicalization SHA-256 machine vector. WP-06 обязан получить один и тот же exact input/final digest во всех повторных запусках и journal profiles; после реализации golden fixture сохраняется byte-for-byte и любое изменение требует approved version/decision change.

## 9. Watchdog и invariant lifecycle

### 9.1 Event cap

- Cap включает все canonical events.
- Перед non-terminal append Core резервирует один slot для terminal `BattleEnded`.
- Если следующий event исчерпал бы резерв, Core прекращает gameplay emit, завершает доступный journal `BattleEnded(Invalid, BattleInvalid)` в последнем slot и возвращает `FailedInvariant`.
- Число events никогда не превышает cap.

### 9.2 Zero progress

Progress stamp строится из authoritative gameplay state:

- positions;
- health/energy/unique resource/stagger;
- fighter state, action ID/phase/timer и cooldowns;
- active effects/grab/control state;
- outcome.

Tick, sequence, event/log counters, diagnostics и RNG-only mutation не считаются progress.

На каждой end-tick boundary:

- если stamp изменился, counter сбрасывается в `0`;
- если stamp не изменился, counter увеличивается на `1`;
- при `counter == global.sim.max_zero_progress_ticks` бой завершается invalid lifecycle и `FailedInvariant`;
- успешный system-action commit/action lifecycle mutation считается progress.

Watchdog failure не является draw/loss и не выдаёт reward.

## 10. Invalid-input boundary

Строгие constructors/value objects сохраняют exceptions для programming misuse. Внешний ввод проходит:

```text
raw DTO
  → TryParse + structural checks
  → deterministic sorted BattleRejectionError list
  → strict contracts only when errors = 0
  → CombatEngine semantic validation
```

Raw factory отвечает за null/format IDs, отсутствующий battle ID, side/FighterId mismatch, число/повтор specials и другие structurally impossible shapes.

Engine отвечает за version/hash mismatch, unknown/wrong-owner/wrong-slot Stable IDs, Mode Rules allowlists и runtime config prerequisites.

Внешний invalid input создаёт rejection result/artifact и не создаёт journal session. Null typed API arguments и прямой вызов strict constructor с impossible state остаются programming `ArgumentException`/`ArgumentNullException`.

## 11. Exact acceptance matrix

Все строки ниже blocking для завершения WP-06.

| ID | Уровень | Проверка | Pass condition |
|---|---|---|---|
| `WP06-CON-001` | Unit | Journal lifecycle | Begin/Append/Complete разрешены только в установленном порядке; каждый Begin/Complete ровно один раз. |
| `WP06-CON-002` | Conformance | Existing replay vector через новый Begin/Complete | Воспроизводятся существующие fixture input/final digests byte-for-byte. |
| `WP06-CON-003` | Unit | Receipt flow | `BattleStarted.InputDigest == Begin.InputDigest`; `BattleResult.FinalDigest == Complete.FinalDigest`. |
| `WP06-CON-004` | Unit | Profile equality | При одном ReplayId Standard/Diagnostic/SummaryOnly имеют одинаковые digests; replay ID публикуют только publishing profiles. |
| `WP06-VAL-001` | Unit | Engine/config/mode versions и hash | Любое несовпадение → sorted `Rejected`; journal untouched. |
| `WP06-VAL-002` | Unit | Build/catalog ownership/slots/allowlist | Unknown, wrong animal/slot или forbidden ID → deterministic `Rejected`; journal untouched. |
| `WP06-VAL-003` | Unit | Technical settings | Missing/wrong type/range → `Rejected`; default не подставляется. |
| `WP06-VAL-004` | Unit | Unsupported normalization | Любое значение кроме None → `Rejected` до Begin. |
| `WP06-RAW-001` | Unit | Raw request factory | Structural errors аккумулируются и сортируются; constructor exception наружу не протекает. |
| `WP06-RAW-002` | Unit | Typed misuse | Null API argument/impossible direct constructor даёт programming exception, не игровой result. |
| `WP06-INIT-001` | Unit | Modifier order | Exact CDS order и Priority/Stable ID внутри слоя. |
| `WP06-INIT-002` | Unit | Atomic initialization | Оба states готовы до passives; ошибка оставляет journal untouched. |
| `WP06-INIT-003` | Unit | Initial oracle | Exact HP/energy/resource/positions/facing/state/counters из §5.2. |
| `WP06-INIT-004` | Unit | RNG initialization | Decision/Resolution streams соответствуют WP-03; initialization draws = 0. |
| `WP06-PIPE-001` | Unit | Phase trace | Каждый active tick вызывает фазы 1..12 ровно один раз и в точном порядке, включая no-op. |
| `WP06-PIPE-002` | Unit | Tick boundary | limit 1: только tick 0 phases; terminal events tick 1. limit 2: phases ticks 0/1; terminal events tick 2. |
| `WP06-PIPE-003` | Unit | Shared snapshot | Оба decisions читают один snapshot identity/version; commit A не меняет candidates B. |
| `WP06-PIPE-004` | Unit | Emission order | Decision drafts A/B, затем commit drafts A/B; Initiative не используется как скрытый emission order. |
| `WP06-SYS-001` | Unit | Fixed priority truth table | `{approach,retreat,wait}→approach`, `{retreat,wait}→retreat`, `{approach,wait}→approach`, `{wait}→wait`. |
| `WP06-SYS-002` | Unit | Zero-weight fallback | Fixed choice, `ZeroWeightFallback`, weights 0/0, RNG null/index unchanged. |
| `WP06-SYS-003` | Unit | Only sys_wait | `OnlyLegalAction`, weight 150/150, no RNG/movement/combat intent. |
| `WP06-SYS-004` | Unit | No legal system action | `FailedInvariant`; predicates не ослабляются. |
| `WP06-TIME-001` | Unit | A timeout win | `75/100` против `50/100`: Timeout → A BattleEnded. |
| `WP06-TIME-002` | Unit | B timeout win | Обратные fractions: Timeout → B BattleEnded. |
| `WP06-TIME-003` | Unit | Equal reduced fractions | `1/3` против `2/6`: Timeout → Draw → BattleEnded. |
| `WP06-TIME-004` | Unit | Int32 boundary | Cross-products при `Int32.MaxValue` exact в Int64, без silent overflow. |
| `WP06-TIME-005` | Unit | Last-tick outcome precedence | Defeat/DoubleKO tick `N-1` завершается без TimeoutReached. |
| `WP06-TIME-006` | Unit | Invalid tick limits | limit ≤ 0 → pre-start rejection; state tick > limit → invariant. |
| `WP06-TRACE-001` | Integration | `wait_equal_l1` | Exact 8-event trace, payload oracle, summary и final frames из §8. |
| `WP06-TRACE-002` | Conformance | Replay validity | Resulting Standard JSON проходит current schema, semantic и integrity verifier. |
| `WP06-SAFE-001` | Unit | Event cap | Cap не превышен, terminal slot сохранён, failure не masquerade как result. |
| `WP06-SAFE-002` | Unit | Zero-progress threshold | Failure происходит ровно при counter == cap; изменение stamp сбрасывает counter. |
| `WP06-SAFE-003` | Unit | System progress | Legal system commit/lifecycle не даёт ложного watchdog до timeout. |
| `WP06-TERM-001` | Unit | Terminal guard | После BattleEnded запрещены mutations, decisions и canonical events. |
| `WP06-FAIL-001` | Integration | Post-start invariant | Invalid BattleEnded/failure capture, `FailedInvariant`, winner/reward отсутствуют. |
| `WP06-DET-001` | Integration | Same-process determinism | 100 повторов case дают exact drafts, RNG indices, summary, input/final digests. |
| `WP06-DET-002` | Integration/CI | Target/profile determinism | netstandard2.1/net10.0 и Standard/Diagnostic/SummaryOnly совпадают по canonical result. |
| `WP06-ARCH-001` | Architecture | Dependencies | `Battle.Core` ссылается только на Contracts/BCL; concrete replay types не используются. |
| `WP06-ARCH-002` | Repository | Unity boundary | `UnityClient` не изменён. |

## 12. Test placement и coverage

- `Battle.Core.UnitTests`: validation, initialization, phase trace, system fallback, timeout, watchdog, terminal guards.
- `Battle.ConformanceTests`: Begin/Complete machine vectors, replay schema/semantic/integrity, profile equality.
- `CombatLab.IntegrationTests`: config → request → engine → journal → result, golden trace, failure lifecycle, repeated determinism.
- `ArchitectureTests`: dependency direction и отсутствие Unity/concrete replay references.

Обязателен 100% branch coverage для timeout comparison и state transition/terminal guards. Snapshot tests не заменяют semantic assertions.

## 13. Acceptance commands

Из каталога `CombatLab`:

```powershell
dotnet restore --locked-mode
dotnet build CombatLab.sln --configuration Release --no-restore
dotnet test CombatLab.sln --configuration Release --no-build
```

WP-06 считается завершённым только когда:

- все blocking cases из §11 green без skip/flaky retry;
- Release build/test green;
- generated config/schema/manifest актуальны и config hash воспроизводим;
- existing WP-02–WP-05 fixtures/digests не имеют необъяснённого drift;
- `UnityClient` не изменён;
- `Implementation_Status.md` переведён в `COMPLETED` после этих проверок.

## 14. Не входит в эту acceptance matrix

- Полная movement geometry/availability `sys_approach`/`sys_retreat` — WP-07.
- Weighted AI selector/opportunity/knowledge — WP-08.
- Damage/defense/stagger/force/grab combat resolution — WP-09.
- Trigger/effect execution, stacking и global trigger cycle caps — WP-10.
- Полные fighter kits, batch/CLI/deployment и Unity integration — WP-11+.

## 15. Результат реализации 2026-08-05

Code-level matrix §11 реализована автоматическими unit/conformance/integration/architecture tests. Дополнительно CI выполняет:

- полный schema/semantic/integrity verify Standard replay `wait_equal_l1`;
- ordinal byte comparison replay, реально собранных против `netstandard2.1` и `net10.0`;
- 100% branch gate для timeout comparison и transition/terminal guards.

Golden digests: input `sha256:0edc1dbc1d8d2a09c38debed5626fba5637f7304a38df258342d1d959edc8ba2`, final `sha256:d06e3c2153a4fbfc495279cd6fcf7379d6f8d42c059e8756a5003d01acfa9ea6`.

Stage completion gate §13 green. `BLOCK-WP06-01 — CLOSED`: workbook и generated balance artifacts штатно регенерированы, оба technical settings присутствуют, validation завершилась с `0 errors / 0 warnings`. Canonical config hash: `sha256:0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f`; source workbook SHA-256: `sha256:bfd8a1d70ac82d5f830a981be078ebe60772a765553d842f73f1fb6b85d54fe2`. Golden digests из §15 не изменились. WP-06 — `COMPLETED`.
