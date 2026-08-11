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

Current Engine `battle.core/0.2.0`:

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

## Следующее действие

Опубликовать documentation closure в текущем Pull Request, объединить green PR с `master`, синхронизировать локальный `master` и подготовить WP-08 Decisions: Brief, exact Test Plan и решения для всех `OPEN-WP08-*` до начала реализации.

## Ограничения

- `UnityClient` пока не изменять.
- `Battle.Core` не зависит от Unity, `Battle.Config`, `Battle.Replay`, Runner/CLI или инфраструктуры.
- Не использовать недетерминированные источники случайности, времени и порядка коллекций.
- Все игровые числа, technical limits и system actions брать из `CompiledBattleConfig`, а не хардкодить в Core.
