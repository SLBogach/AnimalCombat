# Текущий статус реализации

## Завершено

- WP-00 Bootstrap
- WP-01 Contracts
- WP-02 Fixed-point math
- WP-03 Deterministic RNG
- WP-04 Configuration pipeline
- WP-05 Replay
- WP-06 Engine shell
- WP-07 Movement
- WP-08 Decisions
- WP-09 Resolution

WP-05 завершил typed event journal, canonical JSON, SHA-256 event chain и replay verifier. Его требования сохранены в [WP-05_Brief.md](./WP-05_Brief.md).

## Завершённый WP-06 Engine shell

**WP-06 Engine shell — `COMPLETED`.**

Production-код Engine Shell и автоматические тесты blocking matrix реализованы:

1. `BattleRequest`, `ModeRulesSnapshot`, journal receipts и typed `CombatJournalStart`;
2. non-throwing raw request factory и детерминированная pre-start validation всех версий, allowlists, owner/slot и technical settings;
3. атомарная инициализация, семь слоёв modifier ordering и два WP-03 RNG stream без стартовых draws;
4. синхронный `CombatEngine.Simulate` и детерминированный 12-фазный `TickCoordinator`;
5. policy `sys_approach > sys_retreat > sys_wait`; WP-06 runtime vertical slice исполняет только `sys_wait`, movement остаётся WP-07;
6. точная timeout boundary, defeat/DoubleKO precedence, event cap и zero-progress watchdog;
7. journal lifecycle `Begin → Append → Complete`, bounded failure capture и полный Standard replay artifact;
8. exact `wait_equal_l1`, existing digest vector, 100 повторов, profile parity, реальное сравнение `netstandard2.1`/`net10.0` и coverage gate.

Scope и pass/fail находятся в [WP-06_Brief.md](./WP-06_Brief.md) и [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md).

## Закрытие artifact gate

`BLOCK-WP06-01 — CLOSED`: source workbook и generated config/schema/map/validation/manifest штатно регенерированы и содержат:

- `global.sim.max_events_per_battle = 200000`;
- `global.sim.max_zero_progress_ticks = 100`.

Оба ключа обязательны в schema и `CompiledBattleConfig`; отсутствие любого из них даёт pre-start `Rejected`, defaults в Engine запрещены. Экспортёр поддерживает shared formulas, создаваемые Excel при сохранении книги. Validation завершилась с `0 errors / 0 warnings`; WP-04 reproducibility и WP-06 target-determinism gates прошли.

## Canonical balance artifact

- config hash: `sha256:0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f`;
- source workbook SHA-256: `sha256:bfd8a1d70ac82d5f830a981be078ebe60772a765553d842f73f1fb6b85d54fe2`;
- validation: `0 errors / 0 warnings`.

## Replay fixture v0.1

- input digest: `sha256:26bd0244bada8360818da1de29c926c09ae1f2e31915654c5e86fd954b2cca5b`
- final digest: `sha256:bdf470b43b23569fbfbe053772fdc4684b531e48b4a88d6b3b577ad122a1e69e`
- canonical events: `13`

## Golden `wait_equal_l1`

Historical Engine `battle.core/0.1.0`:

- file SHA-256: `4d35559d0cd879c627328b490cb7bd99e946ef45ceb537bac1c753c8e517f292`;
- input: `sha256:0edc1dbc1d8d2a09c38debed5626fba5637f7304a38df258342d1d959edc8ba2`;
- final: `sha256:d06e3c2153a4fbfc495279cd6fcf7379d6f8d42c059e8756a5003d01acfa9ea6`;
- canonical events: `8`.

Historical Engine `battle.core/0.2.0`:

- file SHA-256: `ee56e6186506b3b962c52d6f0ca3f6a22597b94b362226e7252a9f53938f2409`;
- input: `sha256:89f3cf32381147cc18bd5f842060fb73d0730607068dcc72d7fccae8f183f8e2`;
- final: `sha256:95670ca45d0f1d9be0b72781871f23a1a44e6a7ed218306b42266c8ca3c6373b`;
- canonical events: `8`.

Оба artifacts дают `Draw / TimeoutEqualHealthFraction` на tick `1`; historical bytes сохранены до Engine bump и не перезаписывались.

## Последний завершённый этап

**WP-07 Movement — `COMPLETED`.**

Реализованы:

1. checked body-aware 1D geometry, wall bounds, surface gap, preserved order и facing;
2. derived `MoveSpeed`/`CollisionRadius`, строгая pre-start validation и утверждённый defer stat clamp без DATA default;
3. inclusive neutral band `1500..1600`, `sys_approach`/`sys_retreat`/`sys_wait` availability без RNG;
4. `battle.core/0.2.0`, frozen commit descriptor, phase-4 lifecycle и deterministic phase-6 atomic pair movement;
5. proportional largest-remainder allocation, wall redistribution и separation;
6. exact movement event projection, strict replay semantic validation и tamper rejection;
7. historical/current wait fixtures и pinned `approach_band_l3`;
8. repeat/profile/culture/mirror/target determinism, event-cap/watchdog, architecture и coverage gates.

Pinned `approach_band_l3`:

- fixture config: `sha256:6abd6c81701abacdb394fe637e450ae357719e5caf49ef17ccb269573e2ee7b4`;
- input: `sha256:dae170bccf84b44e6c0c173692e6198c45ec0e0ae1484bf9c7dd989cad4a0b20`;
- final: `sha256:956b15fd915222f8b404823dfab070c6bc2f6e1852309d1ef12dc988954cfe93`;
- file SHA-256: `7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873`;
- canonical events: `18`.

Локальный Windows execution от `2026-08-05`: locked restore green; Release build `0` warnings/errors; `528` tests passed; WP-04 reproducibility, WP-06/WP-07 target parity green; WP-02/WP-03/WP-06/WP-07 critical coverage `100%`, Battle.Core line gate `>=85%`. `UnityClient` и DATA artifacts не изменены.

GitHub Actions execution от `2026-08-11` для code head `2248ac9`: `ubuntu-latest` и `windows-latest`, Debug и Release — все четыре jobs green. Release jobs подтвердили WP-04 reproducibility, WP-06/WP-07 target determinism, полный test suite и coverage gates на обеих OS.

`OPEN-WP07-01..13` закрыты и реализованы. `OPEN-WP07-13` сохраняет обязательство WP-10: общий stat clamp/DATA migration должен быть добавлен до movement effects/modifiers.

Фактическая GitHub Actions Windows/Linux matrix green; все blocking acceptance criteria выполнены, поэтому WP-07 переведён в `COMPLETED`.

## Завершённый WP-08 Decisions

**WP-08 Decisions — `COMPLETED`.**

Реализованы все `107` уникальных blocking ID из [Combat Test Plan WP-08 v0.1](./Combat_Test_Plan_WP-08_v0.1.md):

1. typed decision profiles, catalog и фиксированный availability pipeline с первой стабильной причиной отказа;
2. последовательный checked fixed-point pipeline `Tactic → Situation → Synergy → Counter → Variety → Opportunity`, selection precedence и единственный unbiased Decision RNG draw там, где он требуется;
3. repeat/opportunity state, immutable общий phase-5 snapshot и атомарный A/B commit с costs, cooldown, frozen timings/target/direction и generic lifecycle;
4. `DecisionMade`, `ActionCommitted`, `AttackPrepared`, diagnostic `DecisionTrace` и commitment `decision.batch-snapshot/0.1` без изменения canonical event chain;
5. усиленная replay-проверка mode/weights/RNG, timings, target/direction, costs, telegraph и lifecycle, включая typed non-throwing tamper rejection;
6. Engine bump до `battle.core/0.3.0`; event/replay/balance/RNG/ordering versions не изменены;
7. unit, conformance, integration, determinism, historical replay, safety, architecture и coverage checks для WP-08.

Строгая таблица WP-07 system availability сохранена как часть decision catalog:

- `gap < inner` и `outward_headroom > 0` — только `sys_retreat`;
- `gap < inner` и `outward_headroom = 0` — только `sys_wait`;
- `inner <= gap <= outer` — только `sys_wait`;
- `gap > outer` — только `sys_approach`.

Mode exclusion не заменяет требуемое system action другим: отсутствие обязательного кандидата приводит к typed invariant/rejection, а не к скрытому fallback.

Добавлены typed guards:

- `InvalidSystemAction` с path `$.actions[<action_id>]` для неизвестного дополнительного `sys_*` action;
- `DecisionTimingOverflowRisk` с path `$.actions[<action_id>].hit_schedule` для reachable overflow impact timing;
- diagnostic checked catalog допускает не более `256` кандидатов, а legal decision set — не более `128`; превышение и reachable weight-sum risk отклоняются до начала боя;
- runtime counters, timing и decision arithmetic используют checked operations и typed failure вместо wraparound.

### WP-08 replay fixtures

Historical fixtures `battle.core/0.1.0`/`0.2.0` и movement golden `approach_band_l3` не перезаписывались; их pins остаются без изменений.

Current `wait_equal_l1` для `battle.core/0.3.0` создан отдельным versioned artifact:

- fixture config: `sha256:f7524a127ca0ec085562d1ca43fc91d384b7f713f1ddb323be53bc701f6d0dc3`;
- input: `sha256:4155833aa33fd60fee5f034dc8f4050afb957682af5141701d6dca463bbc7a08`;
- final: `sha256:bcc34972a33aadd5da02f3c5d3996ecd76c0037fbfe5e94e25cdf883ca9177f9`;
- file SHA-256: `8793101a52a2d261ba29e03453bff97298c8cefb16f81e76a76fb357ad684bdd`;
- canonical events: `8`.

Historical files при создании current fixture не менялись.

Weighted golden `decision_weighted_l1` (`battle.core/0.3.0`):

- fixture config: `sha256:26c53cf464539e2ebf1eb37f90d73715adb0842e29e6b7a9eeaede8336d49227`;
- input: `sha256:eaee293a90e5fc432ab1822965b3f632abc803bd79b23ae401a8fc9fd8a2b021`;
- final: `sha256:6ed4f34aa845096ee63d125d306fbef64ff469773e14389bfe1152146a007f3f`;
- file SHA-256: `1e2ea3f87bab119b1db687556d7835b2791089b095d202285c7e7f037e331eb0`;
- canonical events: `9`.

### Локальная проверка WP-08

Consolidated Windows run от `2026-08-19` по §15 Combat Test Plan завершён:

- `dotnet restore --locked-mode` — green;
- Release build — `0` warnings / `0` errors;
- полный solution — `875` passed / `0` failed / `0` skipped (`522` Core, `317` Conformance, `36` Integration; Performance project пока не содержит тестов);
- filtered `WorkPackage=WP08` — `347` passed / `0` failed / `0` skipped (`153` Core, `184` Conformance, `10` Integration);
- WP-04 generated reproducibility, WP-06/WP-07/WP-08 target determinism и historical replay SHA gates — green;
- WP-02/WP-03/WP-06/WP-07/WP-08 coverage gates — green; required critical branches `100%`, Battle.Core line coverage `>=85%`;
- `git diff --check` — green; historical fixtures имеют прежние SHA.

### GitHub Actions WP-08

GitHub Actions execution от `2026-08-19` для code head `26e151f`: `ubuntu-latest` и `windows-latest`, Debug и Release — все четыре jobs green. CI подтвердил полную test suite и обязательные generated, target-determinism, historical replay и coverage gates на обеих ОС.

`UnityClient` и generated balance artifacts не изменены.

## Завершённый WP-09 Resolution

**WP-09 Resolution — `COMPLETED`.**

Реализованы:

1. Engine повышен до `battle.core/0.4.0`; event/replay/balance/RNG/ordering versions сохранены.
2. Resolution DATA материализуется до `journal.Begin` в immutable typed profiles; runtime parsing/defaults отсутствуют.
3. Фазы 7–11 исполняют canonical intent collection/order, geometry, Counter/Dodge/Block, damage/chip, stagger/control, MoveSelf, Push/Pull/Swap, wall, grab/throw и group-aware defeat/Double KO.
4. `ResolutionPlan` использует preview/preflight/commit для атомарности state, RNG и event cap; watchdog учитывает только authoritative progress.
5. `ResolutionReplaySemanticValidator` проверяет Engine `0.4.x`, не переинтерпретируя historical `0.1.x`/`0.2.x`/`0.3.x` replay.
6. Созданы отдельные current fixtures `wait`, `decision-weighted`, `resolution-basic`, `resolution-double-ko`, `resolution-wall-grab` для Engine `0.4.0`; historical fixture bytes сохранены.
7. Все `128` unique blocking acceptance IDs имеют автоматически обнаруживаемые тесты; inventory запрещает пропуски и дубли.

Локальная проверка `2026-09-07`:

- locked restore — green;
- Release и Debug build — `0` warnings / `0` errors;
- полный solution — `907` passed / `0` failed / `0` skipped (`537` Core, `329` Conformance, `41` Integration; Performance project пока не содержит тестов);
- filtered `WorkPackage=WP09` — `32` passed / `0` failed (`15` Core, `12` Conformance, `5` Integration), inventory — ровно `128` acceptance IDs;
- WP-04 generated reproducibility и WP-06/WP-07/WP-08/WP-09 target determinism — green;
- пять Engine `0.4.0` fixtures совпадают byte-for-byte между `netstandard2.1` и `net10.0` в Debug/Release;
- historical SHA pins `0.1.0`/`0.2.0`/`0.3.0` — green;
- selected critical arithmetic/order/transition/safety branches — `100%`; combined Battle.Core line coverage — `88.02%` при gate `>=85%`.

Canonical balance JSON, workbook и generated artifacts не изменены. `UnityClient` не изменён.

GitHub Actions execution от `2026-09-08` для code head `9317c82`: `ubuntu-latest` и `windows-latest`, Debug и Release — все четыре jobs green. CI подтвердил полный test suite и обязательные generated, target-determinism, historical replay и coverage gates на обеих ОС. Все completion conditions WP-09 выполнены.

## Реализация WP-10 Effects

**WP-10 — `IN PROGRESS / LOCAL ACCEPTANCE PASSED / CI PENDING`; checkpoint `2026-10-04`, `132/132` blocking IDs исполнены локально.**

Подготовка выполнена в `feature/wp-10-effects` от локального `master@81b1488`: completed WP-09 и сохранённый Unity Viewer checkpoint. Read-only проверка GitHub в этот момент показала remote `master@e610c76`; локальный master опережал remote на один commit. Merge/commit Viewer не закрывает его отдельную acceptance matrix.

Созданы:

- [WP-10 Brief](./WP-10_Brief.md) — source sections, implementation boundaries, exact runtime semantics, DATA proposals и план;
- [Combat Test Plan WP-10 v0.1](./Combat_Test_Plan_WP-10_v0.1.md) — `132` unique proposed blocking acceptance IDs;
- `OPEN-WP10-01..28` и matrix приняты владельцем `2026-10-03`; все проектные решения `CLOSED`.

Предлагается Engine `battle.core/0.5.0`, новый баланс `combat.balance/0.2`/`v0.2`, отдельные versioned workbook/schema/generated artifacts. v0.1 и replay Engine0.1–0.4 сохраняются. Обязательство `OPEN-WP07-13` переносится в физический gate WP-10: 15 stat min/max pairs должны быть в validated DATA до runtime movement modifiers. Дополнительно требуются queue caps, explicit effect rules/metadata и knockdown/interrupt DATA.

`BLOCK-WP10-APPROVAL-01 — CLOSED`. Реализован независимый history/DATA/contracts slice:

- automated SHA + semantic verification всех десяти historical replay Engine `0.1–0.4` до version bump;
- pins source workbook, config/schema/map/validation/manifest v0.1 и проверка source machine replay package;
- source-compatible immutable `CompiledBattleConfig.EffectRules` с lookup и защитой от duplicate RuleId;
- typed effect rule/target/refresh/role/control/interrupt vocabulary, `EffectRuleProfile` и explicit `StatBounds`;
- version-selecting strict reader/compiler и отдельная schema definition v0.2; default schema writer v0.1 остаётся byte-identical;
- обязательность всех 36 новых settings, validation stat domains/roles/durations/lookups/groups/references/targets/interrupt tokens;
- version-aware manifest с `effect_rules` count для v0.2, включая loader count verification;
- separate physical workbook/schema/generated v0.2: 36 settings, 120 action metadata fields, 9 gear priorities, 30 effect metadata fields, 65 rule fields;
- repo-native .NET/OpenXML мигратор и `migrate-config`: pinned v0.1 input, CreateNew-only output, atomic publish после exporter/compiler validation, сохранение старых ячеек/formulas/styles и untouched OPC parts;
- version-aware exporter/CLI: separate names/schema/manifest/counts для v0.2, existing v0.1 export byte-identical; `--schema-output` позволяет read-only verification рабочего дерева;
- `verify-wp10-generated.ps1`: две fresh-process migration/export/loader проверки без записи в source/schema/generated; добавлен в четыре CI configurations (remote execution ещё не подтверждён);
- immutable `EffectProfile`/`EffectModifier`, pure `EffectStatMath` с checked floor/Add aggregation/product per fold/last Override/clamp;
- per-fighter cloneable `EffectStore`: пять policies, exact end-exclusive/remaining/due boundaries, real-mutation budgets, priority expiry/ordinal public order и fatigue lookup once;
- cloneable bounded `EffectTriggerQueue`: causal ordering, root-cycle/once guards, independent fighter rule budgets/cooldowns, depth/tick caps, quiet EndOfTick occurrence identity; пока без Engine/journal integration;
- strict Core `EffectSetupMaterializer`: explicit v0.2/reference settings, 15 bounds, metadata/roles/groups/lookup/duration checks, sorted typed issues и достижимость Global/selected Action/transitive Effect rules;
- immutable `EffectRuntimeDefinition`/per-fighter definitions: base и initial stats, canonical static gear Add/product/Override/clamp; no mutable stores/queues/caches в shared compiled config;
- conservative `EffectArithmeticProof` для intermediate Add/product/derived folds, reachable lifetime/cooldown timers и control stage boundaries; mutually exclusive profiles используют один group envelope. Полный consumer-specific damage/decision/event-reserve proof ещё требуется;
- typed `ActionInterruptProfile`: explicit strengths/phases, Armored protection boundary, Unstoppable Stun/Knockdown filters без bypass Grab/Defeat;
- pure `EffectControlMath`/`KnockdownTimeline`: resistance→fatigue→stun clamp и exact half-open Fall/Grounded/GetUp, checked total;
- 156 WP10 tests: 48 Conformance + 99 Core + 9 Integration, включая 65 новых тестов этого продолжения. Synthetic/compiled corruption tests не заменяют physical workbook.

Локальная проверка Windows `2026-10-03`:

- locked restore — green;
- Release/Debug build — `0 warnings / 0 errors`; после обнаружения restricted reused MSBuild workers использованы `-m:1 -nr:false`;
- Release/Debug полный solution — `1063 passed / 0 failed / 0 skipped`: Core636, Conformance377, Integration50; Performance project по-прежнему пуст;
- WP10 filtered run: `156` проходят; acceptance metadata присутствует у 17 полностью исполненных IDs. Materializer/store/queue foundation tests пока не закрывают pre-Begin/closure/Engine integration cases;
- `verify-wp04-generated.ps1 -Configuration Release` — green, config hash прежний;
- `verify-wp10-generated.ps1 -Configuration Release/Debug` — green: native XLSX и export artifacts одинаковы, manifest отличается только generated_utc;
- `verify-wp09-target-determinism.ps1 -Configuration Release` — green: пять current Engine0.4 fixtures byte-identical между actual netstandard2.1/net10.0 assemblies;
- historical SHA/semantic verification — green; `git diff --check -- CombatLab Docs` — green.
- свежие WP02/WP03/WP06/WP07/WP08/WP09 critical coverage gates — 100%, combined Core line `88.74%`; pure `EffectStatMath`, `EffectControlMath`, `KnockdownTimeline` line/branch — 100%. Reader branch `94.83%`, arithmetic proof branch `92.55%`, store/queue/control integration ещё не закрывают full WP10 coverage; gates не ослаблены.

На checkpoint2026-10-03 были исполнены17/132: `WP10-BASE-002`, `WP10-DATA-001/003/009`, `WP10-MOD-001..007`, `WP10-CTRL-003`, `WP10-KDN-002`, `WP10-INT-001`, `WP10-REG-001/002/007`. Последующие atomic47/132 и текущий control60/132 checkpoints — ниже. Physical `BLOCK-WP10-DATA-01..04`, `BLOCK-WP10-ARTIFACT-01`, `BLOCK-WP10-HISTORY-01` CLOSED после экспорта/loader/pin checks. Core materializer до Begin и full consumer-specific overflow proof остаются обязательны до публичного включения Effects.

`BLOCK-WP10-TOOLING-01 — CLOSED`: владелец явно разрешил `.NET/OpenXML мигратор` 2026-10-03 вместо недоступного runtime навыка Spreadsheets. Сохранение оформления/formulas проверено по OPC/cell XML; визуальный render в Excel/artifact-tool не выполнялся. Workflow и проверенные hashes — [WP-10 Migration](./WP-10_Migration.md).

### Atomic runtime checkpoint — 2026-10-04

- `EffectRuntime`: private stores/queue/cache/lineage, typed owner/recipient/condition resolution, bounded cycle diagnostics; no auto-passive execution, no additional RNG.
- `AtomicBattleBatch`: deep preview бойцов, cooldowns/debts/consumed hits/grabs/outcome, обеих RNG streams, store/queue budgets/visited и drafts; exact preflight всего batch плюс final active-effect cleanup/BattleEnded reserve до публикации.
- EffectAdded/Removed отражают отдельные authoritative public deltas; Replace даёт empty intermediate membership; application event IDs, expiry sources и genuine action/group lineage сохраняются.
- Conditional TickCoordinator wiring: Before expiry/closure/recompute до decisions; lifecycle hooks; group closure после всех frozen impacts и до defeat; EndOfTick затем repeated After due drain; normal EOT не выполняется при immediate terminal.
- Runtime stats recompute из immutable base/gear; damage channels до armor/chip, chance offsets до clamp, fatigue duration channel в control calculation. Weight/GrabPriority consumers и protections ещё не завершены.
- Terminal cleanup trigger-free, reserved Invalid completion; даже failure первой closure сохраняет BattleStarted seq0 и единственный BattleEnded/Complete. Journal Append-port по прежнему trusted; rollback гарантия относится к engine prepare/validation/caps, не внешнему journal transport failure.
- Непубличный controlled constructor позволяет unit harness вызвать полный Simulate; публичные constructors не принимают partial DATA. Producer остаётся0.4/balance0.1, v0.2 pre-Begin factory пока не подключена.

Добавлены39 unit tests; totals:195 WP10 tests (138U/48C/9I), full Release/Debug1102 (675 Core/377 Conformance/50 Integration), no failures/skips; restore locked/build0 warnings/errors. Исполнены47/132 IDs,30 новых; точный список — [Test Plan checkpoint](./Combat_Test_Plan_WP-10_v0.1.md). Metadata47 unique/no duplicates; полный132 inventory ещё не готов. Старые coverage gates/thresholds сохранены; новые WP10 critical branches ещё не100%. Generated v0.1/v0.2, historical SHA/semantic и actual netstandard2.1/net10.0 Engine0.4 determinism проходят; это не WP10 effect target/process conformance.

На atomic checkpoint оставались85 cases; последующий control checkpoint — ниже. Similar unit assertions не закрывают ещё не исполненные I/C requirements. WP-10 не COMPLETED. File list и результаты — [WP-10 Migration continuation](./WP-10_Migration.md).

### Control runtime checkpoint — 2026-10-04

- `EffectControlSystem`: separate frozen hit/control filters, allowed/prevented transitions, explicit ActionCancelled и earlier-real-cause guards. EffectControlSystem line/branch100% в fresh Core coverage.
- FighterRuntimeState: clone-safe bounded latest-commit ledger (actor/category), absolute stun expiry, immutable knockdown timeline/internal stage; no same-commit refresh, dead-target wakeup, fictitious GetUp action or duplicated cancellation. Phase2 owns control timers; phase4 no longer decrements them as actions.
- Resolution: immunity permits damage/stagger/reset but does not cancel actions for prevented control; Armored strength threshold and Unstoppable protected categories/phases; Counter/Grab/Defeat cannot be ignored. Knockdown follows complete group damage/force/throw and precedes defeat. GroundHit-only geometry on all three stages, no defense draw for ordinary miss.
- Fatigue lookup once, shared stack lifetime/crossing-only immunity; Wakeup/ControlEnded hooks before decision snapshot. GrabLockout uses the active role effect's authoritative interval instead of an independent legacy timer in this branch; legacy Engine0.4 retains its prior lockout.
- Cancellation retains committed energy/resource/cooldown, removes future descriptors/hits. Only an already-created current-group UninterruptibleImpact intent survives; stale same-ActionId/different-DecisionId cannot survive.
- Knockdown action-owned hooks derive the originating attacker from the canonical causal chain, not StateChanged.actor (victim). Bounded source walk, no RNG or inferred synthetic cause.

Добавлены48 unit tests; totals:243 WP10 tests (186U/48C/9I), full Release/Debug1150 (723 Core/377 Conformance/50 Integration), no failures/skips; locked restore/build0 warnings/errors. Исполнены60/132 IDs: предыдущие47 плюс `CTRL-001/005/006/007/009/011`, `KDN-001/004/007`, `INT-002/003/004/008`. Source audit60 entries/60 unique/0 duplicate/0 unknown, matrix132 unchanged. Exact list — Test Plan checkpoint. I/C control acceptance ещё не закрыт controlled unit assertions.

WP04 generated Release, WP10 generated Release/Debug, WP09 actual netstandard2.1/net10.0 determinism/historical pins — green. Legacy WP02/03/06/07/08/09 critical gates100%; combined Core line89.74%. Core coverage: `WP10ControlFinalCore`; Integration: `WP10ControlFinalIntegration`; sequential Replay collectors: `WP10ControlWP08ReplayFinal` / `WP10ControlWP09ReplayFinal`. Full WP10 critical/process/TFM/remote CI остаются открытыми.

Остались72 cases: pre-Begin v0.2/full consumer proof, decision weight/grab consumers, production-level control integration/conformance, version0.5/replay verifier/new goldens, full matrix inventory/critical coverage/determinism/CI. Следующий срез — remaining consumers и consumer-specific validation. Public producer остаётся0.4, CLI не выпускает effect replay. UnityClient, generated/historical bytes и посторонние changes сохранены; commit/push не выполнялись.

v0.2 workbook SHA `3628c7fecafc91622d19086675bfd935529e53ef3622286ce309bbe47d0fc8bf`; config SHA `5361ec68359de06a1f4ff458893ad4d537825c873ffb0252e272a5b429b4e0c4`; export `0 errors / 0 warnings`, five effect rules. Reproducibility v0.2 Release/Debug gates green; migrated XLSX bytes equal in en-US/ru-RU/tr-TR; Unix ZIP metadata normalization отдельно проверена synthetic test, но фактический Linux CI ещё нужен.

Workbook/generated v0.1, fixtures и UnityClient не изменялись этим slice; посторонние Unity-изменения сохранены. Git commit/push не выполнялись.

### Consumer checkpoint — 2026-10-04

- `EffectDecisionView`: immutable copied weight sources/protection in shared phase5 snapshot. BlockWeight/PunishWeight/WallActionWeight только в Situation по exact tags; matching channels объединяются в canonical order до floor, без повторения в других стадиях.
- Effective signed GrabPriority frozen при collection; conflict winner/payload используют captured value. Grab availability использует frozen opponent eligibility; Mass/ControlResistance читаются при resolution-group freeze.
- `EffectArithmeticProof.ReachableRange/WeightUpper`: bounded reachable envelopes, including absent effects, gear/Override, group exclusion and combined weight channels. Schema maximum не подменяет реально достижимую неизменённую характеристику.
- `EffectConsumerArithmeticProof`: damage/armor, control/fatigue/timeline, force/wall, timing, six-stage decision/weight sum, grab Add intermediate proof. Controlled Simulate возвращает sorted Rejected до Begin при риске; external production materializer/full DATA-007 gate ещё не подключён.

Добавлены32 unit tests; всего275 WP10 tests (218U/48C/9I). Исполнены61/132, новый MOD-011; remaining71, no matrix changes. Locked restore/Release+Debug build0 warnings/errors; full suite1182 (755 Core/377 Conformance/50 Integration),0 failures/skips. Performance project по-прежнему пуст. Source trait audit61 unique/no duplicates/no unknown; full132 discovery inventory ещё required.

WP04 generated Release, WP10 generated Release/Debug, WP09 actual-target/historical gate прошли. Fresh Core coverage `WP10ConsumersCore`, Integration `WP10ConsumersIntegration`, sequential Replay `WP10ConsumersWP08Replay`/`WP10ConsumersWP09Replay`; legacy selected critical gates100%, fresh combined Core line90.60%; EffectDecisionView line/branch100%. New full WP10 critical coverage не закрыт. Native artifacts/historical hashes не менялись, UnityClient/postоронние changes сохранены; commit/push не выполнялись.

На consumer checkpoint public Engine/CLI оставались0.4/balance0.1. External pre-Begin v0.2 и movement/event-reserve proof завершены следующим срезом ниже; I/C control/freeze acceptance, replay0.5/goldens, full coverage/inventory/process/TFM/remote CI ещё обязательны.

### Продолжение — versioned v0.2 setup, 2026-10-04

- `BattleSetupFactory` подключает strict materializer и consumer proof для explicit `battle.core/0.5.0` + balance v0.2. Весь reachable graph, оба билда, initial geometry и арифметика проверяются до journal.Begin. Default public producer остаётся0.4; новый versioned путь пока internal до replay/golden gates, без definition injection.
- V0.2 initialization использует агрегированные Add/product/ordered Override и clamp всех15 initial stats. Static MaxHealth1770/currentHP1770 валиден, runtime structural/maxima modifiers запрещены. Legacy0.4 initialization остаётся последовательным — historical semantics/hashes сохранены.
- Consumer proof дополнен Int32 arena width/radius/speed/combat-move pair bounds и checked closure/terminal-cleanup reserve arithmetic. System startup/recovery берутся из fixed DATA, без ActionSpeed scaling. Малый max_events не отвергает валидный potential graph: actual batch preflight даёт FailedInvariant/EventCapExceeded, без partial commit, с одним terminal BattleInvalid/Complete.
- Flag Override принимает только integer0/1; bool/string/другие числа дают typed domain rejection, без coercion. Проверены stable sorted code/path для неизвестных vocabulary/reference/phase tokens, incompatible groups/roles/lookups/durations, reachable sum/product/timer/consumer overflow, dormant WP-11 channels и unselected Action rules.
- Full Simulate через real compiled v0.2 проверяет initial empty frames, Before-boundary D1 expiry, terminal cleanup и A/B/A reuse без effect/counter/RNG leaks. No-Begin rejection не подменён controlled definition harness; I/C release cases этим не закрываются.

Добавлены21 unit tests:17 `EffectVersionedSetupTests` и4 consumer-proof tests. Всего296 WP10 tests (239U/48C/9I); full Release/Debug1203 (776 Core/377 Conformance/50 Integration),0 failures/skips. Locked restore green; Release/Debug build0 warnings/errors. Performance project по-прежнему пуст. Новые исполненные blocking IDs: `DATA-002/004/005/006/007/010/011`, `MOD-014`. Итого69/132, осталось63. Source trait audit69 entries/69 unique/0 duplicate/0 unknown; full132 discovery inventory ещё required.

WP04 generated Release, WP10 fresh-process migration/export/load Release/Debug и WP09 five actual netstandard2.1/net10.0 Engine0.4 fixtures/historical pins — green. Artifact/workbook/schema/fixture bytes не менялись. Fresh coverage: `WP10VersionedSetupFinalCore`, `WP10VersionedSetupIntegration`, sequential `WP10VersionedSetupWP08Replay`/`WP10VersionedSetupWP09Replay`. Legacy selected WP02/03/06/07/08/09 critical gates100%; combined Core line92.26%. Consumer proof line100%, branch97.36%; Reader branch95.19%, new full WP10 critical coverage ещё не100%. Thresholds/matrix не ослаблены; Linux/remote CI не запускались.

Остались I/C effect/control/freeze acceptance, versioned replay0.5 verifier/current+effect goldens, remaining U/C/I cases, full coverage/inventory/process/TFM/profile/culture/OS/remote CI gates. Public Engine/CLI всё ещё0.4/balance0.1, эффектный replay пока не является доступным CLI release. UnityClient и посторонние изменения сохранены; commit/push не выполнялись. File list и тестовая команда — [WP-10 Migration](./WP-10_Migration.md).

### Продолжение — full-loop effects/control integration, 2026-10-04

Production-compiled integration harness проводит synthetic JSON через настоящий v0.2 compiler, strict pre-Begin setup, весь Engine0.5 loop и canonical journal. Definitions/state не инжектируются; observer только читает phase snapshots. Для тестов добавлены friend access IntegrationTests и optional observer internal versioned constructor; public Engine/CLI остаются0.4.

- Проверены BattleStart после двух initial frames, D1/D2 Before/After границы, expiry-trigger closure с latest successful application lineage, удаление дочернего D1 After на той же границе, silent EndOfTick occurrence и suppression на terminal tick.
- Проверены frozen startup/recovery и speed уже начатого system movement segment: следующий commit/segment использует обновлённые stats. Equal trade читает pre-group Armor для обоих impacts; следующая группа видит эффект. Изменение Initiative не пересортировывает frozen intents, но влияет на collection следующего tick; обе decisions видят общий post-expiry snapshot.
- Full-loop control cases подтверждают один fatigue на living stun/grab exit, crossing2→3 immunityD25 без renewal на cap, knockdown Fall/Grounded/GetUp2/6/3, Ready phase2tick21 до decisions, wakeup expiry aftertick45, legal GroundHit/lethal precedence и throw/force/wall before knockdown без duplicate cancellation/lockout/fatigue.
- Interrupt integration подтверждает отсутствие ordinary-hit cancel в combat Active/Recovery, explicit movement Startup/Active cancellation, сохранение paid Energy12/cooldown100 без refund, удаление future hits и survival только уже созданного current-group UninterruptibleImpact intent.
- Timeout/lethal cleanup trigger-free, final effects empty, BattleEnded last. HP0 subject не принимает late DamageTaken effect до defeat. Event-cap failure с уже активными effects сохраняет резерв для всех removals+invalid terminal и откатывает failed closure budgets.
- Canonical journal распознаёт Engine0.5 как наследующий WP08 event roles. Это только producer journal compatibility: standalone replay verifier0.5 и effect semantic validators ещё не реализованы. Historical policies/bytes не менялись.

Добавлены27 integration tests в четырёх классах + shared fixture. Новые22 blocking IDs: `MOD-008/012`, `EXP-003/006/008/009`, `TRG-002/011/012/014`, `CTRL-002/004`, `KDN-003/005/006/008`, `INT-005/006/007`, `SAFE-006/007/009`. Итого91/132, осталось41. Source audit91 traits/91 unique/0 duplicate/0 unknown; full132 discovery gate остаётся обязательным.

Locked restore green; Release/Debug build0 warnings/errors. Полный suite1230 (776 Core/377 Conformance/77 Integration),0 failures/skips; WP10 filtered323 (239U/48C/36I). Performance project по-прежнему пуст. WP04 generated Release, WP10 fresh-process native reproduction Release/Debug и WP09 five actual-target/historical checks Release/Debug green. Workbook/schema/generated/manifest/fixtures/hashes не заменялись. Fresh coverage: `WP10IntegrationCore`, `WP10IntegrationFinalIntegration`, sequential `WP10IntegrationWP08Replay`/`WP10IntegrationWP09Replay`; legacy critical WP02/03/06/07/08/09 gates100%, combined Core line92.36%. Полный WP10 critical coverage не100%; matrix/thresholds не ослаблены. Linux/remote CI не запускались. UnityClient/postоронние изменения сохранены; commit/push не выполнялись.

### Продолжение — remaining control/stack/queue + replay0.5, 2026-10-04

Исполнены ещё17 blocking IDs: `STACK-007/011/012`, `TRG-003/007/013`, `CTRL-008/010/012`, `EVT-001..008`. Теперь108/132, осталось24. Stack/trigger/control разделы полностью представлены acceptance tests; source audit108 traits/108 unique/0 duplicate/0 unknown. Это не закрытие полного132 discovery gate.

- Unit cases проверяют StrongestWins CompareKey/tie ordering и missing compare rejection, Replace/Strongest group capacity, guards, отдельные rule/effect budgets и independent A/B ControlEnded budgets.
- Full-loop integration проверяет group replacement при заполненных32/128 slots без transient overflow, rollback при следующем new-group превышении, общий grab-lockout разных actions и точный expiry, failed block → post-damage guard-break → следующие stat/weight consumers, max-hold/release/throw/lethal control endings без double fatigue/lockout.
- Добавлен version-specific public effect/control replay verifier0.5, составленный с существующими decision/resolution policies. Проверяются explicit balance0.2 metadata, affected frames/causes/lineage, no RNG, stack deltas, lifetime/expiry/latest application, Replace Removed→Added, prevented control, actionless knockdown stages/ready boundary, sparse keyframes и terminal empty membership. Verifier не выводит private DATA/queue budgets/cooldowns из публичного replay; config-aware producer conformance `EVT-009` ещё обязателен.
- 60 conformance tests проверяют позитивные witnesses и integrity-rehashed semantic tampering;9 integration replay scenarios проходят schema/verifier без warnings. Historical0.1–0.4 hashes/policies не заменены.
- Verifier выявил producer-frame ошибки: на timeout cleanup effects remaining были от предыдущего tick, а control before-frame countdown отставал до expiry projection. Исправлена текущая абсолютная проекция timers только в effects path; переходы состояний/legacy0.4 семантика сохранены. Defensive zero-startup Active frames Block/Dodge/CounterWindow и DodgeRecovery принимаются только policy0.5.

Этот срез добавил85 tests:7U/60C/18I. Locked restore green; Release/Debug build0 warnings/errors; полный suite1315 (783 Core/437 Conformance/95 Integration),0 failures/skips; WP10 filtered408 (246U/108C/54I). Пустой Performance project даёт прежнее сообщение no tests, не skipped acceptance.

WP04 generated Release, WP10 native fresh-process generated Release/Debug и WP09 actual netstandard2.1/net10.0 five historical/current0.4 scenarios Release/Debug green. Эти target checks не закрывают новый WP10 target/determinism gate. Workbook/schema/generated/manifest/fixtures/hashes не менялись. Fresh reports: `WP10ReplayCore`, `WP10ReplayIntegration`, `WP10ReplayFinalConformance`; legacy WP02/03/06/07/08/09 critical gates100%, combined Core line92.45%. Новый effect replay witness line98%/branch91.44%; полный WP10 critical coverage остаётся открытым, thresholds/matrix не ослаблены.

Public Engine/CLI остаются0.4/balance0.1, internal producer0.5 + standalone verifier0.5 доступны тестам. Goldens/public release, config-aware conformance, remaining24 IDs/full inventory/critical coverage/determinism/OS/remote CI не завершены. UnityClient и посторонние изменения сохранены; commit/push не выполнялись. Точный остаток и file list — Test Plan и WP-10 Migration.

### Предыдущий checkpoint — public release и local gates, 2026-10-04

Public Engine0.5/balance0.2 и CLI `run-demo` доступны. Добавлены девять verified golden replay/config pairs и отдельный manifest; все historical0.1–0.4 pins сохранены. Закрыты оставшиеся24 IDs, в том числе independent config-aware EVT-009, failure lifecycle, four GOLD cases и весь DET/REG release slice. Reflection inventory на актуальных test assemblies обнаруживает132 IDs без skips/duplicates/unknowns.

Locked restore green; Release/Debug build0 warnings/errors; в каждой конфигурации1443 tests (864 Core/469 Conformance/110 Integration),0 failed/skipped. WP10:536 (327U/140C/69I). Performance project остаётся пустым, это не пропущенные acceptance cases. Whole-artifact reproducibility: четыре effects×100 повторов,40 fresh CLI processes, Standard/Diagnostic/SummaryOnly, культуры/перестановки/mirror, actual netstandard2.1/net10.0 dependency checks и обе конфигурации совпадают с goldens.

Новый WP10 critical coverage gate100% branch, включая полные critical runtime/queue/store/control/math/atomic classes и generated closures, а также public effect replay witnesses; combined Core line92.74%. Fresh reports: `WP10ReleaseTerminalCore`, `WP10ReleaseIntegration`, `WP10ReleaseFilteredReplay`; полный conformance report `WP10ReleaseGuardReplay`. Legacy WP02/03/06/07/08/09 gates сохранены и green. Generated0.1/0.2 воспроизводимы; v0.1 hashes не менялись. CI дополнен WP10 inventory/actual-target/coverage gates.

Незавершённые code/acceptance/coverage пункты отсутствуют. Remote Windows/Linux × Debug/Release CI ещё не выполнен, поэтому WP10 не COMPLETED. Synthetic demo не является fighter-passive/resource kit implementation: это WP11. UnityClient и его пользовательские changes сохранены; git commit/push не выполнялись. Файлы/команды/границы результата — [WP-10 Migration](./WP-10_Migration.md).

### Предыдущий checkpoint — завершение прерванной проверки, 2026-10-05

Найден и завершён последний checkpoint WP10: повторный locked restore, Release/Debug build0 warnings/errors, полный suite1443 passed (864 Core/469 Conformance/110 Integration) в каждой конфигурации,0 failures/skips. Отдельный Release WP10 suite536 passed (327U/140C/69I), inventory132/132 green. Integration suite повторно выполнил process/profile/culture/golden/actual-target проверки. Пустой Performance project остаётся вне WP10 acceptance scope.

WP04 generated Release и WP10 native generated Release/Debug reproduce unchanged source/generated bytes. Повторно проверены сохранённые финальные coverage reports: WP02/03/06/07/08/09/10 gates green, critical branches100%, combined Core line92.74%. Новые coverage reports не собирались: production-код после финального coverage checkpoint не изменён.

CLI smoke `effects-knockdown` создал новую ignored replay/config пару `artifacts/replays/wp10-knockdown-20261005-8a1f5305.{json,config.json}`: FighterAWin на tick47, оба файла byte-identical к pinned golden/sidecar. Fixtures/hashes/DATA не перезаписаны; UnityClient и посторонние изменения сохранены. Дополнительные production-правки не потребовались, обновлена документация. Commit/push/remote CI не выполнялись; WP10 `LOCAL ACCEPTANCE PASSED / CI PENDING`, не COMPLETED.

### Текущий checkpoint — CI schema portability fix, 2026-10-05

Первый remote CI run выявил две связанные ошибки: Windows generated gate отвергал schema v0.2, Linux WP10-REG-002/DATA-009 отвергали SHA schema v0.1/v0.2. Причина подтверждена: Git blobs уже LF, локальные Windows файлы/exporter были CRLF; `JsonWriterOptions.NewLine` по умолчанию зависит от OS. Старые WP10 pins ошибочно закрепили CRLF checkout, а не persisted bytes.

`BalanceSchemaJson` теперь явно использует LF; `.gitattributes` дополнен LF policy для balance v0.1 (v0.2 policy уже был). Schema SHA pins исправлены на существующие Git blobs: v0.1 `fd7c3c1d5b52807126e260dd71150a36ef2e68fb18c3dc332dad5c1e17eb40f0`, v0.2 `a33627ce0b382fa37bbd0ff67d3fe1a793ff6dd2d7bccba71e83ee58f0441457`. Локальные schema только EOL-нормализованы; Git diff их содержимого пуст. Historical v0.1 blob совпадает с baseline81b1488. JSON fields, workbook/config/replay hashes и blocking matrix не изменены; gates не ослаблены. Добавлены2 regression executions для LF/no BOM/no EOF newline в обоих schema versions.

Фактические проверки после fix: locked restore; Release/Debug build0 warnings/errors; по1445 passed (864U/471C/110I),0 failed/skipped. Targeted affected suites —7 passed в каждой конфигурации; WP04/WP10 generated gates Release/Debug green. Git checkout filters с `core.autocrlf=true/false` сохраняют оба SHA. Actual-target/process/profile/culture/golden/historical/inventory checks в полном suite green,132/132 IDs; WP10 Conformance142 passed. Сохранённые coverage gates unchanged Core/Replay повторно green: critical100%, Core line92.74%.

Commit/push не выполнялись. Необходим новый remote прогон для fix commit: WP10 `LOCAL ACCEPTANCE PASSED / CI PENDING`, не COMPLETED. UnityClient/посторонние changes сохранены; причина и точный patch list — [WP-10 Migration](./WP-10_Migration.md).

## Следующее действие

Проверить diff, сделать отдельный CI portability fix commit без UnityClient и отправить в существующую WP10 ветку/PR. Дождаться Windows/Linux × Debug/Release CI именно для нового commit. Все132 cases/local build/test/coverage/determinism/generated/historical gates green. После четырёх green jobs обновить WP10 до COMPLETED; затем подготовить WP11 Fighters (passives, Rage/Tempo/Grip, resource/kit rules). До remote evidence статус остаётся CI PENDING.

WP-UI-01 можно продолжать отдельной веткой: full three-fixture/negative PlayMode matrix, external-path UX, Windows Standalone smoke и static checks остаются его собственными условиями завершения. Они не блокируют подготовку основного combat roadmap.

## WP-UI-01 Unity Replay Viewer

**WP-UI-01 — `IN PROGRESS`; first vertical slice implemented `2026-09-08`.**

Создан read-only UI Toolkit Viewer в `UnityClient/AnimalCombat` на Unity `6000.4.0f1`. Viewer не зависит от `Battle.Core` и не рассчитывает gameplay transitions: presentation state копируется только из replay `initial_frames`, event `after` и `final_frames`.

Текущий checkpoint:

- отдельная `ReplayViewer` scene и assemblies Contracts/Runtime/Presentation/EditMode/PlayMode tests;
- bundled byte-identical copies basic, double-KO и wall-grab Engine `0.4.0` fixtures;
- game-first UXML/USS с arena-first кадром, broadcast HUD, fighter silhouettes, transport bar, opt-in telemetry и result overlay;
- Play/Pause/Restart/speed, exact recorded position/facing, HP/state, tick/sequence, event stream и result;
- presentation-only required event cue mapping и safe unknown-event fallback;
- Unity compilation `0 errors / 0 warnings`;
- EditMode `13 passed / 0 failed / 0 skipped`;
- минимальный PlayMode game-first gate `3 passed / 0 failed / 0 skipped`;
- три runtime smoke green, fixture source/copy parity green.

Владелец `2026-09-08` утвердил [Game-First UI Spec v0.1](./Unity_Replay_Viewer_UI_Spec_v0.1.md), desktop/mobile-landscape mockups и `WPUI01-UX-001..014`. Game-first composition и presentation effects реализованы; Unity отображает записанные replay values/state и не рассчитывает gameplay.

Владелец `2026-10-01` утвердил [Illustrated Fight Screen v0.2](./Unity_Replay_Viewer_UI_Spec_v0.2.md): тёплая иллюстрированная арена, красные HP-полосы, `VS`, компактные игровые controls и бойцы без экипировки. В Unity перенесены UXML/USS и фоновый asset с сохранением replay bindings; isolated Unity 6000.4.0f1 PlayMode gate — `5/5 passed`. Фактический visual screenshot из открытого Editor и мобильный device/build gate ещё не подтверждены; batchmode не поддержал `WaitForEndOfFrame` для тестового захвата кадра.

В следующем presentation pass позы обоих cutout-бойцов привязаны к записанным `DecisionMade`, `ActionCommitted`, `AttackPrepared`, `AttackHit`, `AttackMissed`, `DamageApplied`, `PositionChanged`, `KnockbackApplied`, `GrabStarted`, `GrabEnded`, `WallImpact`, `ResourceChanged` (Stagger), `StateChanged`, `FighterDefeated` и `BattleEnded`. Скорость управляет только визуальным временем, Pause замораживает позы, Restart их сбрасывает; HP, координаты и результат по-прежнему берутся только из replay. В сцену добавлена фоновая Camera для Game View, а подписи бойцов подняты над transport bar. Isolated Unity `6000.4.0f1` PlayMode: `8/8 passed` на трёх обязательных fixtures и проверке границ подписей. Художественная проверка в открытом Editor и уникальные анимации для будущих `action_id` остаются отдельной работой.

До `COMPLETED` остаются полная three-fixture PlayMode/negative automation, external-path UX, Windows Standalone smoke и оставшиеся static dependency checks. Inventory уже green: `96` rows / `96` unique IDs / `0` duplicates. CombatLab production-код и canonical fixture bytes не менялись.

## Ограничения

- Изменения `UnityClient` ограничивать WP-UI-01; не добавлять gameplay calculations или зависимость от `Battle.Core`.
- `Battle.Core` не зависит от Unity, `Battle.Config`, `Battle.Replay`, Runner/CLI или инфраструктуры.
- Не использовать недетерминированные источники случайности, времени и порядка коллекций.
- Все игровые числа, technical limits и system actions брать из `CompiledBattleConfig`, а не хардкодить в Core.
