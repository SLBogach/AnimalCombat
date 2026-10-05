# WP-10 — native XLSX migration checkpoint

> Текущий статус WP-10: `COMPLETED`, `2026-10-05`; blocking matrix132/132 passed, четыре Windows/Linux × Debug/Release CI jobs green по подтверждению владельца. Последний code head620ebed. Ниже сохранена история implementation/checkpoints; итоговое закрытие — в последнем разделе.

Дата исходного migration checkpoint: `2026-10-03`. Owner разрешил `.NET/OpenXML мигратор` вместо отсутствующего authoring runtime Spreadsheets. На этом начальном checkpoint WP-10 был **IN PROGRESS**, не COMPLETED; этот workflow закрывал physical DATA/tooling/artifact gates, не всю blocking matrix.

## Что создано

- `CombatLab/config/source/Combat_Balance_Workbook_v0.2.xlsx`: отдельный source, 36 settings (15 bound pairs + 6 control settings), 5 interrupt fields каждого action, effect/gear metadata и лист Effect Rules с пятью global bindings.
- `CombatLab/config/generated/combat.balance.v0.2.{json,map.csv,validation.json,manifest.json}` и `CombatLab/schemas/balance/v0.2/combat.balance.schema.json`: только exporter-generated, не handwritten JSON.
- `BalanceWorkbookMigrator`, `CanonicalXlsxZip`, CLI `migrate-config`/`config migrate`, version-aware exporter и `verify-wp10-generated.ps1`.

Source v0.1, его generated/schema и historical replay0.1–0.4 не изменены. Существующие profile Stable IDs, числовые поля, ячейки, формулы и styles сохранены. V0.2 добавляет 260 runtime JSON Map rows; source formulas + cached results проверяет exporter и compiler, loader сверяет hash/manifest/counts.

Мигратор принимает только SHA-pinned baseline v0.1, отказывается от source/existing destination overwrite и публикует новый файл только после validation. ZIP имеет фиксированные timestamps/order и stored entries; host metadata нормализована. Причина дополнительной нормализации: [реализация .NET ZipArchiveEntry](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.IO.Compression/src/System/IO/Compression/ZipArchiveEntry.cs) записывает текущую platform независимо от public ExternalAttributes. Это metadata собственного нового XLSX, не изменение старого workbook.

Форматирование проверено по OPC XML: old styles/shared strings/старые formula locations и untouched parts preserved; новый лист использует existing styles, freeze/header/column widths и wrapped Notes. Внешний Excel/artifact-tool visual render не выполнялся, поэтому наличие screenshot QA не заявляется.

## Проверенные hashes

| Artifact | SHA-256 |
|---|---|
| v0.2 workbook | `3628c7fecafc91622d19086675bfd935529e53ef3622286ce309bbe47d0fc8bf` |
| v0.2 canonical JSON / config hash | `5361ec68359de06a1f4ff458893ad4d537825c873ffb0252e272a5b429b4e0c4` |
| v0.2 map.csv | `218a495e35f5df16a6430921bafc457bd77bc619fabb8949914a5557b02f5b66` |
| v0.2 validation.json | `146b96da958ec881bdfdfe5e9ddcdf47c7d591f5c83e951621b20036eb59f101` |
| v0.2 schema (LF, persisted Git bytes) | `a33627ce0b382fa37bbd0ff67d3fe1a793ff6dd2d7bccba71e83ee58f0441457` |

Manifest hash намеренно не whole-file pinned: только `generated_utc` может отличаться между exports; остальные поля должны совпадать. Export: `0 errors / 0 warnings`, counts `3 fighters / 24 actions / 6 passives / 10 effects / 5 effect_rules / 4 tactics / 9 gear / 12 builds`.

## Как проверить

Из папки `CombatLab`:

```powershell
dotnet restore --locked-mode
dotnet build CombatLab.sln --configuration Release --no-restore
dotnet test CombatLab.sln --configuration Release --no-build
./scripts/verify-wp04-generated.ps1 -Configuration Release
./scripts/verify-wp10-generated.ps1 -Configuration Release
```

WP10 gate сам создаёт и удаляет проверенные собственные temp files, дважды запускает migration/export в свежих процессах и сравнивает XLSX/JSON/map/validation/schema/manifest. Он не перезаписывает рабочий workbook/schema/generated. Повторить с `-Configuration Debug` после Debug build.

Для отдельного тестового workbook можно вызвать:

```powershell
dotnet run --project src/CombatLab.Cli --configuration Release --no-build --no-restore -- migrate-config --output artifacts/workbook-v0.2-check.xlsx
```

Этот путь должен быть свободен; существующий файл мигратор не перезаписывает. Default destination v0.2 уже создан, поэтому повторный `migrate-config` без нового output корректно откажет. Для canonical reproducibility используйте verification script, а не сохраняйте XLSX в Excel без изменения данных: Excel может перепаковать ZIP и изменить source hash.

## Продолжение реализации

Добавлены независимые typed profile/math/store/queue компоненты и unit tests. Они **ещё не подключены** к authoritative Engine0.4: v0.2 JSON пока не используется runnable battle producer. Исходный migration checkpoint закрывал14/132 acceptance cases; продолжение Core materialization описано ниже.

Локально Windows: locked restore green, Release/Debug build `0 warnings / 0 errors`, tests `998/998`, без skips; generated gate green в обеих configurations. WorkPackage WP10 содержит91 тест и14 полностью исполненных blocking IDs. Pure EffectStatMath line/branch100%; legacy WP02/WP03/WP06 critical coverage gates green. Full store/queue/control/combined Core coverage, WP10 TFM/process и remote CI ещё не подтверждены; Linux/Windows × Debug/Release CI должен проверить новый checkpoint. UnityClient/postоронние изменения сохранены; commit/push не выполнялись.

## Продолжение — Core materialization/control foundation, 2026-10-03

В этом продолжении изменены только следующие code/test files; предыдущие DATA/tooling изменения сохраняются:

- `src/Battle.Contracts/Effects/EffectTypes.cs`: typed ActionInterruptKind vocabulary.
- `src/Battle.Contracts/Effects/ActionInterruptProfile.cs`: immutable strengths/phase/category filters; ordinary hit и hard control разделены, Grab/Defeat не игнорируются.
- `src/Battle.Core/Effects/EffectRuntimeDefinition.cs`: immutable setup/per-fighter base+initial stats, gear sources, selected-action IDs, typed profiles/rules/bounds.
- `src/Battle.Core/Effects/EffectSetupMaterializer.cs`: strict compiled-value reader, no JSON/XLSX I/O; missing keys/wrong kinds/unknown refs/tokens/groups/roles/lookup/durations reject sorted; reachability Global→selected Action→transitive Effect, dormant WP11-only targets не активируются.
- `src/Battle.Core/Effects/EffectArithmeticProof.cs`: bounded conservative envelope для intermediate Add/product/derived arithmetic, timers/cooldowns и knockdown boundaries. Clamp/Override не скрывают overflow; compatible mutually exclusive profiles не складываются как одновременно активные.
- `src/Battle.Core/Effects/EffectControlMath.cs`: pure checked ratio/stun math и Fall/Grounded/GetUp timeline без RNG или state/journal mutation.
- `tests/Battle.Core.UnitTests/Effects/EffectSetupFixture.cs`: test-only physical v0.2 compiled-snapshot adapter и controlled corruptions, без production dependency Core→Config.
- `tests/Battle.Core.UnitTests/Effects/EffectSetupMaterializerTests.cs`: positive physical DATA, missing bounds, invalid domains/refs/rules/roles/groups/lookup, dormant/selected reachability, overflow и immutable setup assertions.
- `tests/Battle.Core.UnitTests/Effects/EffectControlAndInterruptTests.cs`: explicit filters/strength/phase validation, stun8/6/4/3, stages1/4/2/total7, exact half-open boundaries, checked overflow и negative guards.

Обновлены также `Docs/Implementation_Status.md`, `WP-10_Brief.md`, `Combat_Test_Plan_WP-10_v0.1.md`, `Decisions.md`, `Index.md` и этот checkpoint. XLSX/schema/generated/historical fixture bytes не изменялись. Excel lock временно препятствовал чтению; владелец закрыл книгу, после чего hashes/reproducibility checks прошли. Lock-файлы/processes агентом не удалялись и не завершались.

Фактические результаты финального кода:

- locked restore и Release/Debug build: green, 0 warnings/errors;
- полный Release/Debug suite: **1063/1063**, Core636/Conformance377/Integration50, 0 failed/skipped; существующий Performance project пуст;
- WP10 filtered suite: **156/156**, Core99/Conformance48/Integration9; добавлено65 unit tests;
- новые fully executed IDs: `WP10-CTRL-003`, `WP10-KDN-002`, `WP10-INT-001`; итого **17/132**, остальные115 сохраняются blocking;
- WP04 generated Release, WP10 generated Release/Debug и WP09 actual netstandard2.1/net10.0 historical/target determinism: green;
- fresh coverage: WP02/03/06/07/08/09 legacy critical gates100%; combined Core line88.74%; EffectStatMath/EffectControlMath/KnockdownTimeline line/branch100%; reader branch94.83%, arithmetic proof branch92.55%.

Coverage collected в отдельных `TestResults/Coverage/WP10MaterializationCore`, `WP10MaterializationIntegration`, `WP10MaterializationWP08ReplayFinal`, `WP10MaterializationWP09ReplayFinal`. Последние два collected последовательно, чтобы исключить конкуренцию инструментации одного Conformance assembly. Новые full WP10 critical branches/store/queue/control transitions ещё не100%; existing thresholds и matrix не ослаблены.

**Граница результата:** Core materializer и conservative proof вызываются пока только test harness, не `CombatEngine.Simulate`. Это не full pre-Begin acceptance и не полный proof будущих damage/decision/event-reserve consumers. Для Engine integration потребуется завершить эти proofs, вызывать setup до Begin, обеспечить originating-group atomic closure/rollback/cleanup reserve, подключить consumers/control transitions/events/replay semantics, затем повысить Engine0.5 и создать separate fixtures. Producer по-прежнему Engine0.4/balance0.1. UnityClient не изменялся; git commit/push не выполнялись; remote CI не заявляется green.

## Продолжение — Atomic effect runtime slice, 2026-10-04

Изменены в этом срезе (пути относительно CombatLab):

- `src/Battle.Core/Engine/AtomicBattleBatch.cs` — новый isolated prepare/drafts/exact-cap/publish batch.
- `src/Battle.Core/Effects/EffectRuntime.cs` — новый per-battle runtime, closures, cache, lineage, expiry/cleanup.
- `src/Battle.Core/Effects/EffectStore.cs` — available-budget guard и последовательная public mutation projection.
- `src/Battle.Core/Engine/BattleState.cs`, `FighterRuntimeState.cs`, `src/Battle.Core/Random/GameplayRng.cs` — deep state/RNG copy и publication; dynamic stats/frame projection, immutable maxima.
- `src/Battle.Core/Engine/CombatEventEmitter.cs` — preview emitter, exact cleanup reserve, canonical last draft и batch publication.
- `src/Battle.Core/Engine/TickCoordinator.cs` — conditional Before/lifecycle/EndOfTick/After barriers.
- `src/Battle.Core/Resolution/ResolutionSystem.cs`, `ResolutionMath.cs` — frozen-group closure before defeat; damage/chance/fatigue consumers.
- `src/Battle.Core/CombatEngine.cs` — непубличный controlled test seam, start closure после canonical seq0, cleanup before summary/Complete.
- `tests/Battle.Core.UnitTests/Effects/EffectRuntimeFixture.cs`, `EffectRuntimeTests.cs`, `EffectEngineBatchTests.cs` —39 новых tests.
- Docs: `Implementation_Status.md`, `WP-10_Brief.md`, `Combat_Test_Plan_WP-10_v0.1.md`, `Decisions.md`, `Index.md` и этот файл.

47/132 blocking cases исполняются,85 remaining;195 WP10 tests (138U/48C/9I). Full Release/Debug1102: Core675, Conformance377, Integration50; restore locked и build0 warnings/errors. Performance project остаётся пустым, без новых skips/quarantine.

Generated/historical checks не требуют изменения workbook/config/manifest/fixture hashes: этот срез меняет только код/tests/docs. WP04/WP10 generated gates и WP09 actual-target/historical0.4 gate green. Legacy critical coverage gates не ослаблены; full WP10 critical100% и actual effect TFM/process/remote CI остаются открытыми.

Final fresh coverage: `WP10AtomicJournalFinal` (Core), `WP10AtomicIntegration`, `WP10AtomicWP08Replay`, `WP10AtomicWP09Replay`; два Replay collectors запускались последовательно. WP02/03/06/07/08/09 selected critical gates100%; combined Core line89.18%. EffectRuntime line97.43%/branch88.23%; AtomicBattleBatch line90.47%/branch56.25% — это не завершённые новые critical gates. Full132 inventory/missing-branch work остаётся обязательным. Source trait audit:47 entries/47 unique,0 duplicates/0 unknown относительно132-row matrix.

Граница atomic checkpoint: conditional Effects branches выполняются в controlled unit harness и непубличном Simulate constructor. Public Engine0.4 с legacy DATA ведёт себя по старому контракту; CLI ещё не производит новые effect replay. Перед повышением0.5 required full consumer proof/pre-Begin v0.2, remaining control/consumer work и versioned replay semantics/goldens. I/C acceptance не закрывается похожими unit assertions. UnityClient и посторонние changes сохранены; commit/push не выполнялись.

## Продолжение — control runtime, 2026-10-04

В этом срезе изменены:

- `src/Battle.Core/Effects/EffectControlSystem.cs` (новый): immunity/Unstoppable predicates; explicit cancellation/prevented/allowed transitions; canonical causes, exact current-group surviving intent identities.
- `src/Battle.Core/Effects/EffectRuntime.cs`: read-only DATA filter/role access, role end boundary, genuine action-owner resolution through Knockdown source chain.
- `src/Battle.Core/Engine/FighterRuntimeState.cs`: bounded clone-safe per-actor/category commit ledger, absolute stun end, immutable knockdown timeline, phase2 stage/countdown/wakeup, invalid-timeline guard, clearing control on defeat.
- `src/Battle.Core/Engine/BattleState.cs`: shared grab eligibility/protections; role-backed authoritative lockout in effect branch, legacy timer unchanged in0.4.
- `src/Battle.Core/Engine/TickCoordinator.cs`: conditional phase2 control expiry and phase4 skip of control-only timers.
- `src/Battle.Core/Resolution/ResolutionSystem.cs`: frozen DATA filters, ground-hit geometry, guarded control/reset, explicit Counter/Grab/Defeat cancellations, current-group intent survival, damage/force/throw→knockdown→defeat ordering.
- `tests/Battle.Core.UnitTests/Effects/EffectControlRuntimeTests.cs` (новый):48 unit tests,13 новых fully executed IDs, real resolution/coordinator assertions, repeatable drafts and invalid/cap rollback guards.
- `tests/Battle.Core.UnitTests/Effects/EffectRuntimeFixture.cs`: optional typed interrupt DATA для controlled harness.
- `tests/Battle.Core.UnitTests/Effects/EffectEngineBatchTests.cs`: controlled initial stun использует абсолютную boundary семантику вместо legacy countdown setup.
- Docs: `Implementation_Status.md`, `WP-10_Brief.md`, `Combat_Test_Plan_WP-10_v0.1.md`, `Decisions.md`, `Index.md`, этот файл.

Restore locked/Release+Debug build0 warnings/errors; полный suite1150 (723 Core/377 Conformance/50 Integration),0 failures/skips. WP10 —243 tests (186U/48C/9I); acceptance source audit60 unique/no duplicate/no unknown из132-row matrix. EffectControlSystem line/branch100%; legacy critical WP02/03/06/07/08/09 gates100%, combined Core line89.74%. Coverage evidence: `WP10ControlFinalCore`, `WP10ControlFinalIntegration`, `WP10ControlWP08ReplayFinal`, `WP10ControlWP09ReplayFinal`. Полные новые critical gates не завершены.

Generated v0.1/v0.2 и historical bytes/hashes не менялись. WP04 generated Release, WP10 fresh-process native reproducibility Release/Debug и WP09 actual-target/historical gate прошли. Extra unit determinism scenario сравнивает full control/effect/cancellation drafts независимых battle states; это не замена WP10 process/TFM conformance.

Исполнены60/132; remaining72 остаются blocking, включая I/C control cases, full consumer proof и pre-Begin v0.2, weight/grab consumers, versioned replay/goldens, full inventory/coverage/determinism/remote CI. Public Engine0.4/CLI остаются прежними. Следующий срез: remaining consumers + consumer-specific arithmetic validation. UnityClient и посторонние changes сохранены; commit/push не выполнялись.

Отдельный запуск новых тестов после build:

```powershell
dotnet test tests/Battle.Core.UnitTests/Battle.Core.UnitTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~EffectControlRuntimeTests
```

## Продолжение — consumers + controlled pre-Begin proof, 2026-10-04

Изменены в этом срезе (пути относительно CombatLab):

- `src/Battle.Core/Effects/EffectDecisionView.cs` (новый): immutable decision inputs; combined canonical matching weight fold only in Situation.
- `src/Battle.Core/Effects/EffectConsumerArithmeticProof.cs` (новый): consumer-specific bounded proof без mutation/journal/RNG.
- `src/Battle.Core/Effects/EffectArithmeticProof.cs`: reachable stat/channel envelopes и merged tag-weight product upper bound.
- `src/Battle.Core/Effects/EffectRuntime.cs`: capture copied sources и frozen grab eligibility.
- `src/Battle.Core/Decisions/DecisionSnapshot.cs`, `DecisionWeights.cs`, `DecisionCatalogAndAvailability.cs`: frozen effect inputs/frames, Situation consumer, opponent-protection availability guard.
- `src/Battle.Core/Engine/TickCoordinator.cs`: shared phase5 effect snapshot capture; legacy0.4 projection unchanged.
- `src/Battle.Core/Resolution/ImpactIntents.cs`, `ResolutionSystem.cs`: checked signed effective GrabPriority frozen in collection, used by conflict ordering/payload.
- `src/Battle.Core/CombatEngine.cs`: internal controlled pre-Begin proof → sorted typed Rejected without journal calls; public producer unchanged.
- `tests/Battle.Core.UnitTests/Effects/EffectDecisionConsumerTests.cs`, `EffectConsumerArithmeticProofTests.cs` (новые):32 tests, MOD-011 numeric/boundary acceptance, snapshot/order/overflow/consumer-proof guards.
- Docs: `Implementation_Status.md`, `WP-10_Brief.md`, `Combat_Test_Plan_WP-10_v0.1.md`, `Decisions.md`, `Index.md`, этот файл.

Locked restore green; Release/Debug build0 warnings/errors; full tests1182 (755 Core/377 Conformance/50 Integration), no failures/skips. WP10275 tests (218U/48C/9I). Blocking61/132, remaining71; единственный новый fully executed ID MOD-011. Source traits61 entries/61 unique/0 duplicate/0 unknown; full132 discovery inventory не закрыт. Existing Performance project остаётся без тестов.

WP04 generated Release, WP10 native migration/export/load in two fresh processes Release/Debug и WP09 five actual-target Engine0.4 fixtures/historical pins прошли. Workbook/schema/generated/manifest/fixture bytes не менялись; новых goldens или hashes не создавалось. Legacy selected WP02/03/06/07/08/09 critical gates100%; fresh combined Core line90.60%. EffectDecisionView line/branch100%; full new WP10 critical gates ещё required. Fresh coverage: `WP10ConsumersCore`, `WP10ConsumersIntegration`, `WP10ConsumersWP08Replay`, `WP10ConsumersWP09Replay`; Replay collectors последовательные.

Граница результата: consumer arithmetic proof и no-Begin rejection пока доступны в controlled internal seam. Production v0.2 materialization, full movement/event-reserve proof, I/C control/freeze cases, versioned replay0.5/goldens/full determinism/remote CI не завершены. DATA-007/MOD-008/012 остаются blocking. Public Engine0.4/CLI не выпускают effect replay; WP10 не COMPLETED. UnityClient и посторонние changes сохранены; commit/push не выполнялись.

Проверка нового среза после build:

```powershell
dotnet test tests/Battle.Core.UnitTests/Battle.Core.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~EffectDecisionConsumerTests|FullyQualifiedName~EffectConsumerArithmeticProofTests"
```

## Продолжение — versioned v0.2 setup, 2026-10-04

Изменены в этом срезе (пути относительно CombatLab):

- `src/Battle.Core/Initialization/BattleSetupFactory.cs`: explicit0.5/v0.2 version selection, strict materialization/consumer proof до Begin, aggregate initial stats/clamp при untouched legacy0.4 initializer.
- `src/Battle.Core/CombatEngine.cs`: internal explicit versioned producer constructor; real external compiled v0.2 path без injected definition. Public constructors/version/CLI остаются0.4.
- `src/Battle.Core/Effects/EffectSetupMaterializer.cs`: typed flag integer0/1 validation, без generic coercion.
- `src/Battle.Core/Effects/EffectConsumerArithmeticProof.cs`: geometry/speed/combat-move pair и event/cleanup reserve checked arithmetic; system timing без ActionSpeed scaling.
- `src/Battle.Core/Effects/EffectArithmeticProof.cs`: актуализирован комментарий о подключённом pre-Begin proof, без изменения арифметики.
- `tests/Battle.Core.UnitTests/Effects/EffectVersionedSetupTests.cs` (новый):17 full factory/Simulate tests с real v0.2 compiled DATA;8 новых acceptance IDs.
- `tests/Battle.Core.UnitTests/Effects/EffectConsumerArithmeticProofTests.cs`:4 новых controlled proof tests для reserve overflow, reachability и typed decision fold overflow.
- `tests/Battle.Core.UnitTests/Effects/EffectSetupMaterializerTests.cs`: актуализирован комментарий о границе pure tests.
- Docs: `Implementation_Status.md`, `WP-10_Brief.md`, `Combat_Test_Plan_WP-10_v0.1.md`, `Decisions.md`, `Index.md`, этот файл.

Locked restore green; Release/Debug build0 warnings/errors; full tests1203 (776 Core/377 Conformance/50 Integration),0 failures/skips. WP10296 tests (239U/48C/9I). Новые blocking IDs: `DATA-002/004/005/006/007/010/011`, `MOD-014`; суммарно69/132, осталось63. Source audit69 entries/69 unique/0 duplicate/0 unknown; full132 discovery inventory pending. Performance project без тестов.

WP04 generated Release, WP10 native migration/export/load in two fresh processes Release/Debug и WP09 five actual-target Engine0.4 fixtures/historical pins прошли. Workbook/schema/generated/manifest/fixture bytes не менялись; hashes/goldens не заменялись. Legacy selected critical WP02/03/06/07/08/09 gates100%; combined Core line92.26%. Consumer proof line100%/branch97.36%; full new WP10 critical gate ещё required. Fresh coverage: `WP10VersionedSetupFinalCore`, `WP10VersionedSetupIntegration`, sequential `WP10VersionedSetupWP08Replay` / `WP10VersionedSetupWP09Replay`.

Граница результата: strict production factory/full Simulate0.5 работает на external compiled v0.2, но version selection пока internal. Public Engine/CLI0.4 и historical semantics unchanged. Versioned replay0.5 verifier/current+effect goldens, I/C effect/control/freeze cases, remaining63 cases, full inventory/coverage/process/TFM/profile/culture/OS/remote CI ещё обязательны. WP10 не COMPLETED. UnityClient и все посторонние изменения сохранены; git commit/push не выполнялись.

Проверка нового среза после build:

```powershell
dotnet test tests/Battle.Core.UnitTests/Battle.Core.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~EffectVersionedSetupTests|FullyQualifiedName~EffectConsumerArithmeticProofTests"
```

## Продолжение — full-loop effects/control integration, 2026-10-04

Изменены в этом срезе (пути относительно CombatLab):

- `src/Battle.Core/Properties/AssemblyInfo.cs`: test-only friend access для IntegrationTests, без новых project dependencies.
- `src/Battle.Core/CombatEngine.cs`: optional read-only observer internal versioned constructor; public producer/version/CLI не меняются.
- `src/Battle.Replay/Journal/CanonicalReplayJournal.cs`: Engine0.5 наследует role policy DecisionMade/ActionPhaseChanged от0.3/0.4. Это не standalone verifier0.5 acceptance; historical policies неизменны.
- `tests/CombatLab.IntegrationTests/Effects/EffectEngineFixture.cs` (новый): in-memory synthetic JSON → production compiler0.2 → strict setup → full Engine loop/canonical journal; phase snapshots без state/definition injection.
- `EffectLifecycleIntegrationTests.cs` (новый,11 tests): BattleStart/Before+After expiry/refresh/trigger closure/EndOfTick, frozen commits/segment speed, invalid-cap cleanup reserve.
- `EffectControlIntegrationTests.cs` (новый,5 tests): stun/grab lifecycle fatigue, crossing-only immunity, knockdown stages/wakeup/GroundHit/lethal/throw+wall ordering.
- `EffectBarrierAndTerminalIntegrationTests.cs` (новый,4 tests): equal trade/pre-group Armor, next-group stats и non-retroactive intent ordering; shared post-expiry decisions; timeout/lethal terminal suppression/cleanup и HP0 recipient guard.
- `EffectInterruptIntegrationTests.cs` (новый,7 tests): combat Active/Recovery no ordinary cancel, explicit movement phases, paid costs/cooldown без refund/future hit; protected created trade intent only.
- Docs: `Implementation_Status.md`, `WP-10_Brief.md`, `Combat_Test_Plan_WP-10_v0.1.md`, `Decisions.md`, `Index.md`, этот файл.

Новые27 integration tests закрывают22 blocking IDs: `MOD-008/012`, `EXP-003/006/008/009`, `TRG-002/011/012/014`, `CTRL-002/004`, `KDN-003/005/006/008`, `INT-005/006/007`, `SAFE-006/007/009`. Всего91/132, осталось41; source audit91 traits/91 unique/0 duplicate/0 unknown. Full132 discovery inventory ещё pending.

Locked restore green; Release/Debug build0 warnings/errors; full suite1230 (776 Core/377 Conformance/77 Integration),0 failures/skips. WP10 filtered323 tests (239U/48C/36I). Performance project пуст. WP04 generated Release, WP10 native migration/export/load в двух fresh processes Release/Debug и WP09 five actual netstandard2.1/net10.0 fixture/historical checks Release/Debug green. Source/schema/generated/manifest/fixture bytes и hashes не менялись; новых goldens не создавалось.

Fresh coverage: `WP10IntegrationCore`, `WP10IntegrationFinalIntegration`, sequential `WP10IntegrationWP08Replay`/`WP10IntegrationWP09Replay`. Legacy critical WP02/03/06/07/08/09 gates100%; combined Core line92.36%. Full WP10 critical branches ещё не100%; thresholds/matrix не ослаблены. Initial new-test assertion failures (synthetic hit_count/release token, capped lethal damage, telegraph before cancellation, exact cooldown tick decrement) исправлены в fixture/assertions согласно существующим DATA/wire/timeout rules; production gameplay semantics для обхода этих failures не менялись.

Граница результата: internal Engine0.5 full-loop producer теперь проверен real compiled v0.2 и canonical journal, но standalone verifier0.5/semantic tamper checks, current+effect goldens, оставшиеся41 IDs, full coverage/inventory/process/TFM/profile/culture/OS/remote CI обязательны. Public Engine/CLI всё ещё0.4/balance0.1. Следующий срез: remaining control/stack/queue cases и replay0.5 semantic validators. WP10 не COMPLETED; UnityClient и посторонние изменения сохранены, commit/push не выполнялись.

Проверка нового среза после build:

```powershell
dotnet test tests/CombatLab.IntegrationTests/CombatLab.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~CombatLab.IntegrationTests.Effects
```

## Продолжение — remaining cases + version-specific replay0.5, 2026-10-04

Добавлены17 blocking IDs: `STACK-007/011/012`, `TRG-003/007/013`, `CTRL-008/010/012`, `EVT-001..008`. Теперь108/132, осталось24.85 новых tests:7 Core/60 Conformance/18 Integration. WP10 filtered408 (246U/108C/54I); full Release/Debug1315 (783 Core/437 Conformance/95 Integration),0 failures/skips. Restore locked-mode/build green,0 warnings/errors. Performance project остаётся пустым.

Файлы этого среза поверх существующего незакоммиченного WP10 patch:

- `src/Battle.Core/Engine/FighterRuntimeState.cs`: absolute effect-control timer projection без state transition;
- `src/Battle.Core/Effects/EffectRuntime.cs`: current-tick frame synchronization перед timeout cleanup/control expiry;
- новый `src/Battle.Replay/Verification/EffectReplaySemanticValidator.cs`: public version-specific0.5 metadata/membership/lineage/stack/lifetime/control/keyframe/terminal witnesses;
- `src/Battle.Replay/Verification/{ReplaySemanticValidator,DecisionReplaySemanticValidator,ResolutionReplaySemanticValidator,ReplayVerificationResult}.cs`: composed0.5 policy/typed semantic.effect, defensive Active/Recovery states только0.5;
- новый `tests/Battle.Core.UnitTests/Effects/EffectRemainingAcceptanceTests.cs`:6 remaining compare/group/guard/budget/A-B cases;
- `tests/Battle.Core.UnitTests/Effects/EffectControlRuntimeTests.cs`:1 absolute countdown/expiry/defeat projection regression;
- `tests/CombatLab.IntegrationTests/Effects/EffectEngineFixture.cs`: explicit hit schedule count/defensive no-hit action и guard/weight observation;
- новый `tests/CombatLab.IntegrationTests/Effects/EffectRemainingControlAndStackTests.cs`:9 capacity replacement/rollback, cross-action grab-lockout, guard-break/maxhold/release/throw/lethal cases;
- новый `tests/CombatLab.IntegrationTests/Effects/EffectReplayIntegrationTests.cs`:9 production positive schema/verifier roundtrips;
- новый `tests/Battle.ConformanceTests/Effects/Wp10EffectReplayTests.cs`:60 positive/negative public witnesses, rehashed semantic tampering и old-policy composition;
- `Docs/{Implementation_Status,WP-10_Brief,Combat_Test_Plan_WP-10_v0.1,Decisions,Index,WP-10_Migration}.md`: checkpoint/remaining cases/verification evidence.

Replay verifier обнаружил две реальные producer inconsistencies: terminal effects countdown оставался previous-tick на timeout, control before-frame captured previous countdown до expiry projection. Исправлена только effects-path absolute projection; legacy0.4 transition/timing/fixture policy untouched. StateChanged exit nullable duration принимается; Prevented требует exact0 и identical frames. Added payload duration равен public remaining; latest application становится expiry cause. Replace adjacency/cause/policy проверяются без предположений о private CompareKey. Sparse advisory keyframe failure остаётся warning/canonical fallback, final membership является hard semantic gate.

Input/event/keyframe digests для synthetic mutated traces пересчитываются только в памяти, чтобы доказать semantic rejection независимо от SHA chain. Committed historical fixture/hash/manifest/source/schema/generated bytes не менялись; новые goldens не создавались. Private stats/cooldowns/queue-budget proof остаётся config-aware `EVT-009`, standalone verifier его не подменяет.

WP04 generated Release, WP10 native reproduction в двух fresh processes Release/Debug, WP09 actual loaded netstandard2.1/net10.0 five historical/current0.4 fixtures Release/Debug green. Fresh coverage: `WP10ReplayCore`, `WP10ReplayIntegration`, `WP10ReplayFinalConformance`; legacy WP02/03/06/07/08/09 critical gates100%, combined Core line92.45%. Effect replay witness line98%/branch91.44%; full WP10 critical coverage ещё открыт. Matrix/thresholds не ослаблены; new WP10 TFM/OS/remote CI evidence отсутствует.

Граница результата: internal producer0.5/public verifier0.5 реализованы; public Engine/CLI остаются0.4/balance0.1. Remaining24 IDs перечислены в Test Plan§9: BASE/version/contracts/architecture/inventory, DATA-008, EVT-009, SAFE-008, DET, REG-003..006, four GOLD cases. Следующий срез — config-aware producer/goldens/public CLI и release gates, не WP11. UnityClient/посторонние изменения сохранены, commit/push не выполнялись.

Проверка всего WP10 после Release build из `CombatLab`:

```powershell
dotnet test CombatLab.sln --configuration Release --no-build --no-restore --filter WorkPackage=WP10
```

## Public release checkpoint — 2026-10-04

WP10 local acceptance132/132 green, remote CI pending. Public Engine/balance versions повышены до0.5/0.2. Новые fixtures нужны для нового producer, а не для замены исторических bytes. Созданы только отдельные0.5 artifacts; SHA записаны после verified production execution. Committed v0.1 workbook/schema/generated/manifest/replay hashes не изменены. Новые effects являются explicit synthetic demos, не автоматическими bindings выбранного passive/gear; fighter resource/kit execution остаётся WP11.

### Изменения этого continuation

- `src/Battle.Contracts/Versions/ContractVersions.cs`: current Engine0.5/balance0.2 + explicit historical versions.
- `src/Battle.Core/{CombatEngine,Initialization/BattleSetupFactory,Properties/AssemblyInfo}.cs`: public producer0.5; internal historical harness0.4; friend access actual-target probe. `Effects/EffectRuntime.cs`: matching control source требует non-null cause action и уже гарантированный action ID draft; семантика равенства не меняется, redundant nullable branch отсутствует.
- `src/CombatLab.Runner/Replays/{EffectDemoCatalog,BattleDemoCommand}.cs`, CLI `Program.cs`: девять synthetic demos; production compiler/engine/schema/verifier; CreateNew-only output с optional canonical config sidecar; seed/options validation. Parent output directory должен существовать; existing output отказывается перезаписываться.
- `src/Battle.Replay/Verification/ResolutionReplaySemanticValidator.cs`: effect0.5 expiry lineage не является новым impact group. Historical policy unchanged; actual impact groups по-прежнему не reopen/cross-tick.
- `fixtures/replay/v0.1/*engine-0.5.0.json`, `*engine-0.5.0.config.json`, `wp10.engine-0.5.0.manifest.json`: девять verified replay/config pairs, actual file/config SHA, input/final digest и event counts. Manifest tests проверяют compiler canonicalization, hash и standalone verification.
- `tools/Wp06.TargetProbe/{Program.cs,Wp06.TargetProbe.csproj}`, `scripts/verify-wp10-target-determinism.ps1`: linked scenario source без Runner assembly; assert actual four loaded dependency TFMs; nine fixed-metadata artifacts matching pinned bytes. Исторические target checks сохранены.
- `scripts/verify-wp10-coverage.ps1`, `.github/workflows/combatlab.yml`: exact132 inventory, actual targets, WP10 Replay collector, full critical classes/closures/witnesses100% и Core>=85%; прежние gates неизменны.
- Core Effects tests: новая AcceptanceId DET008 на exact chance/draw protocol; новые `Effect{Store,TriggerQueue,Arithmetic,Atomic,Runtime}GuardTests` покрывают limits/typed errors/no partial mutation/rollback/lineage/control conditions. Только тестовые corruption fixtures используют reflection для обхода typed constructor; production runtime не использует reflection.
- Conformance Effects: `Wp10ReleaseConformanceTests`, `Wp10BlockingCaseInventoryTests`, `Wp10GoldenManifestTests`, `Wp10EffectReplayGuardTests`; shared in-memory replay builders доступны новым guard tests. Private DATA oracle EVT009 использует config sidecar, независимый damage calculation и re-simulation; public verifier остаётся config-free.
- Integration Effects: `Wp10GoldenAndDeterminismTests`, `Wp10ReleaseSafetyAndTargetTests`; full-loop goldens, process/TFM/profile/culture/mirror/failure lifecycle. Исторический version validation test явно выбирает internal Engine0.4.
- Старые test/probe harness files, использовавшие default `ContractVersions.Engine/BalanceSchema` и `new CombatEngine()`, минимально переведены на explicit historical versions. Старые assertions/expected bytes не заменены, coverage thresholds не снижены.
- Docs: Brief/Test Plan/Implementation Status/Decisions/Index/Migration приведены к LOCAL PASSED / CI PENDING. Предыдущие checkpoints сохранены как история.

### Проверки

Locked restore green. Release/Debug build0 warnings/errors; full1443 tests (864 Core/469 Conformance/110 Integration) в каждой конфигурации,0 failed/skipped. WP10=536 (327U/140C/69I); discovery132/132. Critical effect math/store/queue/control/runtime/atomic/replay branches100%, включая generated closures/iterators; combined Core line92.74%. Parser/adapter scaffolding находится вне selected arithmetic/state critical scope, но не исключается collector. Legacy WP02/03/06/07/08/09 coverage gates сохранены.

Fresh reports: `WP10ReleaseTerminalCore`, `WP10ReleaseIntegration`, `WP10ReleaseFilteredReplay`; full replay regression `WP10ReleaseGuardReplay`. Generated WP04 Release и WP10 native Release/Debug reproduce unchanged committed bytes. Nine0.5 goldens совпадают across actual netstandard2.1/net10.0 dependencies и Debug/Release;4×100 in-process,4×10 fresh CLI processes, profile/culture/permutation/mirror/RNG green. Historical0.1–0.4 SHA/semantic verification green. Remote Windows/Linux × Debug/Release CI ещё не запускался: WP10 не COMPLETED.

### Как получить replay

Из `CombatLab`:

```powershell
New-Item -ItemType Directory -Path artifacts/replays -Force
dotnet run --project src/CombatLab.Cli --configuration Release --no-build -- run-demo effects-knockdown --output artifacts/replays/wp10-knockdown.json --config-output artifacts/replays/wp10-knockdown.config.json
```

Запускайте с новыми output именами: существующие файлы не перезаписываются. Другие effect scenarios: `effects-stack-expiry`, `effects-impact-snapshot`, `effects-control-chain`; baseline scenarios: `wait`, `decision`, `resolution-basic`, `resolution-double-ko`, `resolution-wall-grab`. `--seed` принимает unsigned64 integer и по умолчанию0. Fixture metadata/battle IDs фиксированы для воспроизводимости, это не production battle-ID allocation API.

В knockdown replay: grab tick9 → throw/knockdown10 → обычный miss11 → GroundHit12 → Grounded12 → GetUp18 → Ready21; lockout expires29, wakeup immunity expires45. Открытый Viewer может читать replay как presentation, однако отдельная визуальная поддержка новых effect/control cues в Unity не была реализована/проверена этим этапом.

### Git handoff

Commit/push не выполнялись. Сначала из корня репозитория проверьте `git status --short` и diff. Unity ProjectSettings/untracked files принадлежат владельцу и не входят в WP10 staging. Команды ниже — только рекомендации для reviewed WP10 changes:

```powershell
git add -- CombatLab .gitattributes .github/workflows/combatlab.yml Docs/Implementation_Status.md Docs/WP-10_Brief.md Docs/Combat_Test_Plan_WP-10_v0.1.md Docs/Decisions.md Docs/Index.md Docs/WP-10_Migration.md
git diff --cached --stat
git diff --cached --name-only
git commit -m "feat: implement WP-10 effects and deterministic replay gates"
git push -u origin feature/wp-10-effects
```

Если в CombatLab есть новые посторонние изменения, добавляйте только конкретные WP10 files из reviewed diff вместо всего каталога. После push откройте/обновите PR, дождитесь четырёх green jobs и только затем отмечайте WP10 COMPLETED. UnityClient не изменялся; существующие пользовательские changes сохранены.

## Возобновление и заключительная локальная проверка — 2026-10-05

Работа была остановлена во время последнего WP10 filtered run/coverage handoff, а реализация public Engine0.5 и локальная blocking matrix были сохранены. При возобновлении production-код, DATA, schema, generated artifacts и fixtures менять не потребовалось. Обновлены только шесть status/report Markdown-файлов: Brief, Test Plan, Implementation Status, Decisions, Index и этот Migration. В Brief устранены устаревшие текущие пометки о недостающем pre-Begin proof/Engine0.4; historical checkpoint записи сохранены.

Повторные фактические результаты:

- `dotnet restore CombatLab.sln --locked-mode` — passed.
- Release/Debug build `--no-restore -m:1 -nr:false` —0 warnings/errors.
- Release/Debug full test `--no-build --no-restore` — по1443 passed (864U/469C/110I),0 failed/skipped.
- Release `--filter 'WorkPackage=WP10'` —536 passed (327U/140C/69I), inventory132/132.
- WP04 generated Release, WP10 native generated Release/Debug — passed, worktree source/generated bytes unchanged.
- WP02/03/06/07/08/09/10 coverage gates на сохранённых final Core/Integration/Replay reports — passed; critical branches100%, combined Core line92.74%. Новая collection не выполнялась, production-код после прежней collection неизменён.
- Детерминизм, actual loaded targets, historical/golden/process/profile/culture регрессии повторно исполнились в полном integration/conformance suite — passed.

CLI smoke создал только новую ignored пару `artifacts/replays/wp10-knockdown-20261005-8a1f5305.json` и `artifacts/replays/wp10-knockdown-20261005-8a1f5305.config.json`; SHA обоих файлов совпадают с pinned golden/sidecar. Outcome `FighterAWin`, `end_tick=47`, final digest `sha256:416da88e97e8362c2e2ee2de256490421156bfe48d974e50f4f8be2b9981c4ca`. Эти outputs не входят в commit и не заменяют fixtures.

Осталось только получить remote Windows/Linux × Debug/Release evidence через reviewed commit/push/PR. Локальные Windows результаты не доказывают Linux CI; WP10 остаётся `LOCAL ACCEPTANCE PASSED / CI PENDING`. Git handoff выше актуален, staging/commit/push не выполнялись. UnityClient и посторонние незакоммиченные changes сохранены.

## CI follow-up — schema LF/CRLF portability, 2026-10-05

Первый CI после7e28560 выявил Windows `Generated v0.2 schema is stale` и Linux WP10-REG-002/DATA-009 schema SHA failures. Источник проблемы — OS default `JsonWriterOptions.NewLine` при indented JSON. Локальные Windows schemas были CRLF, а Git сохранял LF; local export и pin tests совпадали с checkout, скрывая расхождение с persisted bytes.

| Schema | Ошибочно pinned Windows CRLF SHA | Persisted LF Git SHA, now pinned |
|---|---|---|
| v0.1 | `b503b8e5d03ea5fbaed2deda6c5c5e1bc40bd1c4a86a4cbd18060468baf8b7ca` | `fd7c3c1d5b52807126e260dd71150a36ef2e68fb18c3dc332dad5c1e17eb40f0` |
| v0.2 | `068b1886e548f45d817cb9c8e6a88a6754c876036af934d49b2cd903bbed53ee` | `a33627ce0b382fa37bbd0ff67d3fe1a793ff6dd2d7bccba71e83ee58f0441457` |

Git blob v0.1 `8d2b769c54b573f509f3adb4df8f7146546c3d0a` одинаков в81b1488 и7e28560; schema content исторически не менялся. CRLF→LF преобразование полностью воспроизводит уже существующие Git blobs обеих schemas; локальные файлы только EOL-нормализованы без BOM/trailing newline, их Git diff пуст. Existing generated config/workbook/replay hashes и fixtures не перезаписаны. Table выше объясняет correction неправильных test pins, а не разрешает новые historical bytes.

Patch:

- `.gitattributes`: добавлен `CombatLab/schemas/balance/v0.1/*.json text eol=lf`; v0.2 уже имел LF policy.
- `CombatLab/src/Battle.Config/Schema/BalanceSchemaJson.cs`: explicit `JsonWriterOptions.NewLine = "\n"`, одинаковый schema export на обоих OS/TFMs.
- `Wp10HistoricalBaselineTests.cs`, `Wp10DataArtifactTests.cs`: existing Git SHA pins вместо host CRLF;2 дополнительные LF/no BOM/no final newline regression executions. Byte-exact equality/SHA assertions сохранены, сравнение не нормализует input.
- Docs Brief/Test Plan/Status/Decisions/Index/Migration: причина CI failures, patch/evidence и CI PENDING handoff. Scope/OPEN decisions/matrix неизменны.

После fix: locked restore; Release/Debug build0 warnings/errors; full suites по1445 passed (864U/471C/110I),0 failures/skips. Affected historical/data suites7 passed в каждой конфигурации; Release WP10 Conformance142 passed/inventory132/132. WP10 всего538 executions (327U/142C/69I) в полном suite. WP04/WP10 generated Release/Debug green. Read-only `git -c core.autocrlf=true/false cat-file --filters HEAD:<schema>` подтвердил одинаковые LF bytes для обеих policies; это local checkout-filter evidence, не запуск Linux runner. Full integration/conformance повторно проверили process/actual-target/goldens/historical. Saved gates неизменённых critical Core/Replay scopes100%, line92.74%; новая collection не выполнялась.

Необходим reviewed fix commit/push в ту же `feature/wp-10-effects`; существующий PR обновится. Дождаться четырёх remote green jobs именно для fix commit. COMPLETED пока не ставить. Commit/push не выполнялись; UnityClient/посторонние changes сохранены.

## CI follow-up — target hash helper without PowerShell module dependency, 2026-10-05

Remote Windows Debug после740fd91 прошёл864 Core и471 Conformance tests. DET004 остановился после успешного probe build на `Get-FileHash is not recognized`. Тест на Windows запускает legacy Windows PowerShell, а отдельный workflow step — pwsh; наличие функции Get-FileHash локально не доказывает её наличие в child runner. Log подтверждает missing command, но точная module/environment причина не установлена. Empty Performance project не связан с этим failure.

Patch ограничен tooling/test и status docs:

- `CombatLab/scripts/verify-wp10-target-determinism.ps1`: оба SHA calls используют `Get-Wp10TargetSha256` на .NET FileStream/SHA256; stream/algorithm гарантированно disposed. Сохраняются lowercase64 hex, strict manifest/committed-golden comparisons и9 scenarios×2 actual dependency targets. Подход уже используется existing WP09 gate.
- `CombatLab/tests/CombatLab.IntegrationTests/Effects/Wp10ReleaseSafetyAndTargetTests.cs`: DET004 theory normal/forbidden-Get-FileHash; second row устанавливает throwing global function и запускает тот же полный script, без skips. Оба rows требуют exit0 и final success marker. Process non-interactive/hidden, timeout180s/tree kill/stdout+stderr capture сохранены; script path корректно quoted для пробелов/Unicode/apostrophe.
- Docs Status/Brief/Test Plan/Decisions/Index/Migration: новый checkpoint/evidence, без изменения OPEN decisions или blocking matrix.

После fix locked restore и Release/Debug build0 warnings/errors. Targeted DET0042 passed в каждой конфигурации, normal и forbidden variants проверены и в полном suite. Release/Debug full suites по1446 passed (864U/471C/111I),0 failed/skipped. WP10 total539 (327U/142C/70I), inventory132/132. WP04/WP10 generated Release/Debug green; process/target/golden/historical regressions green. Saved coverage unchanged critical Core/Replay100%, line92.74%; новая collection не выполнялась, production Core/Replay не изменены. Manifest/SHA pins/source/generated/schema/fixtures не менялись.

Commit/push не выполнялись; UnityClient и посторонние changes сохранены. Отправить reviewed fix в ту же ветку/PR, затем дождаться четырёх Windows/Linux × Debug/Release green jobs для нового commit. Локальный Windows run не является Linux/remote evidence; WP10 остаётся CI PENDING.

## Completion — 2026-10-05

Владелец сообщил: «Все четыре CI jobs WP-10 зелёные, обнови WP-10 до COMPLETED». Подтверждены ubuntu-latest и windows-latest, Debug и Release после последнего target hash fix; локальная ветка feature/wp-10-effects имеет последний code commit620ebed. Run URL/ID не предоставлены, самостоятельная проверка GitHub в status-update сессии не выполнялась. Источник remote evidence явно фиксируется как сообщение владельца.

Все completion gates закрыты:132/132 blocking IDs, actual discovery/no skips, latest local Release/Debug1446 passed (864U/471C/111I), WP10539 executions (327U/142C/70I), critical coverage100%/Core line92.74%, generated0.1/0.2, historical replay и nine goldens/process/TFM/profile/culture checks. Code/fixture/DATA/hash changes в этом patch отсутствуют. WP10=COMPLETED; ограничения fighter resources/passive kits, batch/deployment и Unity presentation относятся к следующим/отдельным этапам, а не remaining WP10 blockers.

Обновлены только шесть Markdown-документов: Implementation Status, Brief, Test Plan, Decisions, Index и Migration. Previous checkpoints и CI failures/fixes сохранены как история. UnityClient/посторонние изменения сохранены; staging/commit/push агент не выполнял. Docs-only patch не требует повторного full build/test; blocking inventory и diff проверяются отдельно. Следующее действие: reviewed docs commit/push, green CI для последнего commit перед merge существующего PR, затем подготовка WP11 Fighters Brief/Test Plan. Саму реализацию WP11 этот запрос не запускает.
