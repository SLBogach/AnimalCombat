# Текущий статус реализации

## Завершено

- WP-00 Bootstrap
- WP-01 Contracts
- WP-02 Fixed-point math
- WP-03 Deterministic RNG
- WP-04 Configuration pipeline
- WP-05 Replay
- WP-06 Engine shell

WP-05 завершил typed event journal, canonical JSON, SHA-256 event chain и replay verifier. Его требования сохранены в [WP-05_Brief.md](./WP-05_Brief.md).

## Последний завершённый этап

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

- input digest: `sha256:0edc1dbc1d8d2a09c38debed5626fba5637f7304a38df258342d1d959edc8ba2`
- final digest: `sha256:d06e3c2153a4fbfc495279cd6fcf7379d6f8d42c059e8756a5003d01acfa9ea6`
- canonical events: `8`
- result: `Draw / TimeoutEqualHealthFraction`, tick `1`

Оба golden digest остались без drift после регенерации balance artifact.

## Подготовленный следующий этап

**WP-07 Movement — `READY FOR IMPLEMENTATION`; production-код не начат.**

Scope, связанные source sections, тринадцать закрытых проектных/DATA вопросов и план реализации зафиксированы в [WP-07 Brief](./WP-07_Brief.md). Exact blocking acceptance matrix находится в [Combat Test Plan WP-07 v0.1](./Combat_Test_Plan_WP-07_v0.1.md).

Подготовка WP-07 зафиксировала:

1. body-aware 1D geometry, radius-adjusted wall bounds, surface gap и preserved side order;
2. inclusive neutral band `1500..1600` для единственного system candidate `Retreat / Wait / Approach` без Decision RNG;
3. phase-start pair snapshot, proportional largest-remainder allocation и atomic movement/separation;
4. frozen movement segment, phase-4/phase-6 ownership и canonical movement event chain;
5. exact golden `approach_band_l3`, wall/separation fixtures, regression, replay, determinism, coverage и architecture gates;
6. обязательный Engine SemVer bump `battle.core/0.1.0 → battle.core/0.2.0`, при неизменных event/replay schema и `ordering_version`.

`OPEN-WP07-13 — CLOSED`: утверждён defer общего stat clamp и DATA/schema migration до WP-10. WP-07 использует checked `base + gear` без clamp, проверяет positive derived speed в Core pre-start и не вводит runtime default. Workbook, `combat.balance/0.1`, config version/hash не меняются.

Checked-in historical `wait_equal_l1@battle.core/0.1.0` сейчас отсутствует: golden генерируется тестом в памяти. Поэтому до Engine bump реализация обязана создать новый immutable `CombatLab/fixtures/replay/v0.1/wait-equal-l1.engine-0.1.0.json`, проверить уже известные input/final digests и pin file SHA-256. Golden для `battle.core/0.2.0` затем создаётся отдельным artifact; существующие файлы не перезаписываются.

## Следующее действие

Начать реализацию по [плану Brief](./WP-07_Brief.md#план-реализации): сначала материализовать historical wait fixture до Engine bump, затем реализовать Movement, пройти blocking matrix и только после этого установить `COMPLETED`.

## Ограничения

- `UnityClient` пока не изменять.
- `Battle.Core` не зависит от Unity, `Battle.Config`, `Battle.Replay`, Runner/CLI или инфраструктуры.
- Не использовать недетерминированные источники случайности, времени и порядка коллекций.
- Все игровые числа, technical limits и system actions брать из `CompiledBattleConfig`, а не хардкодить в Core.
