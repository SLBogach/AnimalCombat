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

## Текущий этап

**WP-08 Decisions — `LOCAL IMPLEMENTATION COMPLETE / CI PENDING`.**

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

`UnityClient` и generated balance artifacts не изменены.

## Следующее действие

Отправить финальный head в GitHub и дождаться зелёной matrix `windows-latest`/`ubuntu-latest` × Debug/Release. Только после фактического CI pass WP-08 можно перевести из `LOCAL IMPLEMENTATION COMPLETE / CI PENDING` в `COMPLETED`.

## Ограничения

- `UnityClient` пока не изменять.
- `Battle.Core` не зависит от Unity, `Battle.Config`, `Battle.Replay`, Runner/CLI или инфраструктуры.
- Не использовать недетерминированные источники случайности, времени и порядка коллекций.
- Все игровые числа, technical limits и system actions брать из `CompiledBattleConfig`, а не хардкодить в Core.
