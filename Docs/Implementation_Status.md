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

## Подготовленный WP-10 Effects

**WP-10 — `PREPARED / AWAITING APPROVAL`; preparation checkpoint `2026-10-03`.**

Подготовка выполнена в `feature/wp-10-effects` от локального `master@81b1488`: completed WP-09 и сохранённый Unity Viewer checkpoint. Read-only проверка GitHub в этот момент показала remote `master@e610c76`; локальный master опережал remote на один commit. Merge/commit Viewer не закрывает его отдельную acceptance matrix.

Созданы:

- [WP-10 Brief](./WP-10_Brief.md) — source sections, implementation boundaries, exact runtime semantics, DATA proposals и план;
- [Combat Test Plan WP-10 v0.1](./Combat_Test_Plan_WP-10_v0.1.md) — `132` unique proposed blocking acceptance IDs;
- предложения `OPEN-WP10-01..28` — ещё не приняты и не CLOSED.

Предлагается Engine `battle.core/0.5.0`, новый баланс `combat.balance/0.2`/`v0.2`, отдельные versioned workbook/schema/generated artifacts. v0.1 и replay Engine0.1–0.4 сохраняются. Обязательство `OPEN-WP07-13` переносится в физический gate WP-10: 15 stat min/max pairs должны быть в validated DATA до runtime movement modifiers. Дополнительно требуются queue caps, explicit effect rules/metadata и knockdown/interrupt DATA.

`BLOCK-WP10-APPROVAL-01`, `BLOCK-WP10-DATA-01..04`, `BLOCK-WP10-ARTIFACT-01`, `BLOCK-WP10-HISTORY-01` остаются OPEN. Подготовка WP-10 не изменяла production code, Workbook/generated artifacts, fixtures и UnityClient; существующие и появляющиеся локальные Unity-изменения сохраняются вне patch. Acceptance execution `0/132`, build/test не заявлены как выполненные.

## Следующее действие

Утвердить `OPEN-WP10-01..28` и Combat Test Plan WP-10 как обязательную blocking matrix. Затем закрепить historical Engine0.4 pins, выполнить v0.2 DATA/tooling slice, закрыть physical artifact gates и реализовать WP-10 Effects по матрице. После него — WP-11 Fighters с full passive/resource/kit semantics.

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
