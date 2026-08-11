# Решения и предложения

## WP-04 JSON envelope

Баланс содержит корневые элементы:

- actions
- effects
- fighters
- gear
- passives
- settings
- tactics

## Canonical JSON

- UTF-8 без BOM.
- Object keys сортируются ordinal.
- Catalog arrays сортируются по Stable ID.
- Gameplay numbers представлены целыми числами.
- config_hash считается от точных canonical JSON bytes.

## WP-05 Replay

- JSON Schema и machine fixtures остаются внешним нормативным контрактом и не генерируются из C# типов.
- `CombatEventDraft` сохраняет назначенные Core значения `sequence` и `event_id`; journal проверяет их непрерывность и соответствие, как требует раздел 15.2 Technical Design.
- Event hash projection обязательно содержит `integrity.event_digest: null`; `prev_digest` входит в hash preimage.
- Повреждённый keyframe не отменяет авторитетную event chain: verifier возвращает warning и требует replay от событий.
- Machine fixture v0.1 считает `keyframe.state_digest` по `tick`, `after_sequence`, `fighters`, `active_grab_id`, исключая `scope`. Текстовое расхождение зафиксировано в WP-05 Brief и должно быть устранено до release.

## WP-06 Engine shell

### Принятые решения

- `OPEN-05` является обязательным programming gate: содержательная реализация WP-06 не начинается без exact acceptance matrix. Gate закрыт созданием [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md); само правило gate сохраняется для истории решений.
- Публичная точка исполнения остаётся синхронной: `CombatEngine.Simulate(BattleRequest, CompiledBattleConfig, ICombatEventJournal)`. Внутри одного боя нет I/O, `async`, task scheduling и wall-clock time.
- `Battle.Core` зависит только от `Battle.Contracts` и BCL. Journal не возвращает gameplay data и не может влиять на результат выбора или разрешения боя.
- Проверка и инициализация атомарны: до `BattleStarted` journal пуст; ожидаемая ошибка возвращает `BattleResult.Rejected` и отдельный rejection artifact, а не exception, draw или loss.
- Конфликт порядка initialization modifiers разрешён по source precedence в пользу CDS §6.2: base animal profile → mode normalization → gear → passive → permanent effects → temporary effects → clamp. Внутри слоя порядок — `Priority`, затем Stable ID. Формулировка TDD §11 `mode normalization → base` для реализации WP-06 не применяется.
- Один `TickCoordinator` вызывает 12 фаз в фиксированном порядке из TDD §12. Изменение порядка требует изменения `ordering_version`.
- Оба бойца читают один `TickSnapshot`; commit одного бойца не меняет кандидаты второго в той же decision phase. Полная mutation group применяется до defeat/outcome checks.
- `battle.time_limit_ticks` завершается нормальным timeout outcome. Доли HP сравниваются без деления через перекрёстное умножение. Technical zero-progress watchdog завершается `FailedInvariant`, а не draw.
- Первый canonical event — `BattleStarted` с `sequence = 0`; последний — `BattleEnded`; после него canonical events запрещены. Fatal deterministic failure после старта не превращается в `BattleRejected` и не может выдать награду.
- WP-06 реализует только engine shell и минимальный system-action vertical slice. Геометрия WP-07, weighted decision semantics WP-08, combat resolution WP-09 и effects WP-10 остаются отдельными этапами.

### Закрытые решения для реализации

- `OPEN-WP06-01 — CLOSED`: exact acceptance matrix зафиксирована в [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md). Этот gate дал исходный статус `READY`; production-реализация и acceptance теперь завершены.
- `OPEN-WP06-02 — CLOSED`: coordinator выполняет все 12 фаз для ticks `0..time_limit-1`. На следующей pre-tick boundary `tick == time_limit` выполняется timeout без нового snapshot/phase pass. Defeat/DoubleKO последнего активного tick имеет приоритет; terminal events получают `tick = end_tick = duration_ticks = time_limit`.
- `OPEN-WP06-03 — CLOSED`: fixed priority уже legal system-actions — `sys_approach > sys_retreat > sys_wait`, без RNG и снятия predicates. WP-06 engine-shell selector поддерживает только `sys_wait`; golden case фиксирует точный 8-event trace.
- `OPEN-WP06-04 — CLOSED`: `BattleRequest` получает обязательные `BattleId` и `ModeRulesSnapshot`. После атомарной initialization Core вызывает `journal.Begin(CombatJournalStart)`; concrete journal добавляет свой `ReplayId`, вычисляет input digest и возвращает `JournalBeginResult`. `input_digest` не хранится в request.
- `OPEN-WP06-05 — CLOSED`: `ICombatEventJournal.Complete` возвращает `JournalCompletion(FinalDigest, PublishedReplayId?)`. Это неигровой integrity receipt; `Append` возвращает только causal identity, а зависимость `Battle.Core → Battle.Replay` остаётся запрещённой.
- `OPEN-WP06-06 — CLOSED`: `ModeRulesSnapshot` использует version `mode.rules/0.1`, explicit sorted allowlists и только `NormalizationMode.None`. Стартовые HP/energy равны derived maxima, unique resource берётся из `start_resource`, остальные counters/cooldowns равны нулю. Обязательные settings: `global.sim.max_events_per_battle = 200000` и `global.sim.max_zero_progress_ticks = 100`; defaults в Core запрещены.
- `OPEN-WP06-07 — CLOSED`: raw external input проходит non-throwing factory с накоплением отсортированных rejection errors. Strict typed constructors сохраняют exceptions для programming misuse; Engine возвращает `Rejected` для config-dependent/version/hash/catalog/allowlist ошибок до journal Begin.

Полные payload, lifecycle и test oracles для этих решений находятся в [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md).

### Статус реализации и закрытие blocker

- `OPEN-WP06-01..07` остаются `CLOSED`: все требовавшиеся проектные решения реализованы в коде и тестах.
- `BLOCK-WP06-01 — CLOSED`: source workbook и generated balance artifacts штатно регенерированы и содержат оба обязательных technical settings; validation завершилась с `0 errors / 0 warnings`.
- Canonical config hash: `sha256:0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f`; source workbook SHA-256: `sha256:bfd8a1d70ac82d5f830a981be078ebe60772a765553d842f73f1fb6b85d54fe2`.
- WP-06 имеет статус `COMPLETED`: artifact gate закрыт, полная acceptance matrix и Release build/test green.
- Historical golden `wait_equal_l1@battle.core/0.1.0` зафиксирован как input `sha256:0edc1dbc1d8d2a09c38debed5626fba5637f7304a38df258342d1d959edc8ba2`, final `sha256:d06e3c2153a4fbfc495279cd6fcf7379d6f8d42c059e8756a5003d01acfa9ea6`, file SHA-256 `4d35559d0cd879c627328b490cb7bd99e946ef45ceb537bac1c753c8e517f292`; bytes immutable.

## WP-07 Movement

### Закрытые и реализованные решения

- `OPEN-WP07-01 — CLOSED`: [WP-07 Brief](./WP-07_Brief.md) задаёт scope, а [Combat Test Plan WP-07 v0.1](./Combat_Test_Plan_WP-07_v0.1.md) — обязательный exact pass/fail. Matrix реализована; локальные проверки и фактическая Windows/Linux CI matrix green, WP-07 — `COMPLETED`.
- `OPEN-WP07-02 — CLOSED`: canonical fixed-point key — `global.sim.fp_scale`; `global.sim.math_scale` из TDD §7.1 считается терминологической ошибкой. Runtime alias/default запрещён.
- `OPEN-WP07-03 — CLOSED`: position является центром тела. Legal bounds равны `[arena.min + collision_radius, arena.max - collision_radius]`; initial A-left/B-right order и non-overlap обязательны. Separation distance выводится из суммы radii, новый DATA key не вводится.
- `OPEN-WP07-04 — CLOSED`: neutral surface-gap band включителен и равен `[sys_approach.preferred_range_max, sys_retreat.preferred_range_min]`, сейчас `1500..1600`. Ниже выбирается Retreat при наличии outward headroom, внутри — Wait, выше — Approach. Это даёт ровно один positive candidate и не вводит weighted WP-08 semantics.
- `OPEN-WP07-05 — CLOSED`: derived `MoveSpeed` — distance units per active tick, после modifier pipeline и с FP identity state multiplier. Speed замораживается на старте segment; `tick_ms` и floating point в формулу не входят. По закрытому `OPEN-WP07-13` отсутствующий stat clamp не заменяется default.
- `OPEN-WP07-06 — CLOSED`: оба movement request рассчитываются из одного phase-start snapshot. Target budget распределяется пропорционально speed capacity методом largest remainder; exact remainder tie разрешает immutable WP-06 `InitiativeOrder`, без Side/FighterId/iteration-order и без нового RNG draw.
- `OPEN-WP07-07 — CLOSED`: pair result применяется атомарно. Overlap устраняется minimum rollback, пропорциональным inward displacement, создавшему penetration; stationary actor не сдвигается, remainder разрешает `InitiativeOrder`, crossing запрещён. Separation payload использует signed `requested_delta=actual_delta=to-from`, wall/action/decision null semantics по Test Plan.
- `OPEN-WP07-08 — CLOSED`: direction и target position замораживаются на commit. `TrackTarget` обновляет только live gap stop predicate; facing пересчитывается после полного movement/separation batch.
- `OPEN-WP07-09 — CLOSED`: phase 6 исполняет movement, выпускает `MoveEnded` и ставит internal completion marker. Только phase 4 следующего tick выполняет `Active→Recovery`, а после recovery emit `Recovery→null` и очищает action перед новой decision phase; 12-фазный порядок и `ordering_version` не меняются.
- `OPEN-WP07-10 — CLOSED`: post-commit WP-07 lifecycle/movement events actor-only: `target_id` и target frames null, RNG и resolution group null. Stop codes и их порядок: `WallReached`, `PreferredRangeReached`, `SegmentExpired`. Phase-4 и phase-6 drafts внутри своей event class упорядочиваются по `InitiativeOrder`; существующий WP-06 порядок Decision/Commit A→B сохраняется. Exact source/related/reason/frame oracle находится в Test Plan §9.3.
- `OPEN-WP07-11 — CLOSED`: `requested_delta` является signed target-limited attempt, `actual_delta = to_position - from_position`, а `blocked_by_wall` содержит только wall-clipped magnitude. Только authoritative nonzero mutation является position progress.
- `OPEN-WP07-12 — CLOSED`: WP-07 реализует voluntary movement и pure separation. WP-08 выбирает/commit combat actions; WP-09 исполняет attack/Dodge/MoveSelf, forced movement, damage/stagger/WallImpact; WP-10 владеет effects/modifiers, WP-11 — fighter passive/resource reactions. Event/replay contracts и `ordering_version` не изменены; Engine повышен `battle.core/0.1.0 → battle.core/0.2.0`. До bump создан immutable historical wait `0.1.0` (file SHA `4d35…f292`), отдельно созданы current wait `0.2.0` (`ee56…2409`) и movement golden (`7117…4873`). Existing файлы не перезаписывались.
- `OPEN-WP07-13 — CLOSED / DEFER APPROVED`: общий stat-clamp и DATA/schema migration утверждённо отложены до WP-10. В WP-07 отсутствующие `stat.move_speed.min/max` не заменяются default: используется checked base+gear pipeline без clamp, non-positive derived speed отклоняется в Core pre-start, а Workbook, `combat.balance/0.1`, config version/hash не меняются. WP-10 обязан закрыть полный stat-clamp до movement effects/modifiers.

### Статус этапа

- `OPEN-WP07-01..13` закрыты и реализованы; проектных/DATA blockers нет.
- WP-07 имеет статус `COMPLETED`: `528` local tests и фактическая GitHub Actions Windows/Linux Debug/Release matrix green; target/coverage/generated gates прошли на обеих OS.
- Current wait pins: input `sha256:89f3cf32381147cc18bd5f842060fb73d0730607068dcc72d7fccae8f183f8e2`, final `sha256:95670ca45d0f1d9be0b72781871f23a1a44e6a7ed218306b42266c8ca3c6373b`, file `ee56e6186506b3b962c52d6f0ca3f6a22597b94b362226e7252a9f53938f2409`.
- Movement pins: config `sha256:6abd6c81701abacdb394fe637e450ae357719e5caf49ef17ccb269573e2ee7b4`, input `sha256:dae170bccf84b44e6c0c173692e6198c45ec0e0ae1484bf9c7dd989cad4a0b20`, final `sha256:956b15fd915222f8b404823dfab070c6bc2f6e1852309d1ef12dc988954cfe93`, file `7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873`.
- `UnityClient` остаётся вне scope и не изменён. Финальный `COMPLETED` зафиксирован после фактического Windows/Linux CI pass от `2026-08-11` для code head `2248ac9`.

## WP-08 Decisions

### Предложенные решения — не утверждены и не реализованы

Полное обоснование, формулы и implementation boundary находятся в [WP-08 Brief](./WP-08_Brief.md), а exact proposed acceptance — в [Combat Test Plan WP-08 v0.1](./Combat_Test_Plan_WP-08_v0.1.md).

- `OPEN-WP08-01 — OPEN / PROPOSED`: Brief задаёт scope; Test Plan WP-08 становится blocking matrix только после явного approval.
- `OPEN-WP08-02 — OPEN / PROPOSED`: checked catalog — all System + actor-animal Basic + actor-animal Special в ordinal ActionId order; mode/loadout остаются predicates и видны в diagnostic trace. Mode/config collections sort canonically, но порядок двух build Specials является canonical input и может менять input digest.
- `OPEN-WP08-03 — OPEN / PROPOSED`: один immutable phase-5 `DecisionBatchSnapshot` снимается после phases 2–4 и до draw/commit; оба решения читают его, selector не читает mutable state; `tick-pipeline/1` не меняется.
- `OPEN-WP08-04 — OPEN / PROPOSED`: для текущего `combat.balance/0.1` target/range выводятся только из System/slot/hit schedule/movement mode/range fields по Brief, без branches по конкретному ActionId. Non-System Opponent допускает `None/Approach/Follow/Push/Pull/Swap`, Self — `None/Approach/Retreat/Adaptive`; иная pair получает `AmbiguousTargetProfile`. Explicit target DATA review отложен до WP-11.
- `OPEN-WP08-05 — OPEN / PROPOSED`: availability predicates и first rejection code имеют фиксированный порядок state/category → mode → owner/slot/loadout → cooldown → costs → target → decision/system range → headroom → observed telegraph → MaxConsecutive. Rejected WP-07 system profile uses `SystemBandUnavailable`/`NoMovementHeadroom`; fighter-specific prerequisites остаются WP-09/WP-11 seams.
- `OPEN-WP08-06 — OPEN / PROPOSED`: final weight — checked sequential fixed-point `Base × Tactic × Situation × Synergy × Counter × Variety × Opportunity`, floor после каждого stage, multiplier/final clamps из DATA; reachable overflow/sum risk rejected pre-start, runtime `DecisionArithmeticOverflow` guards only corrupted internal input.
- `OPEN-WP08-07 — OPEN / PROPOSED`: tactic fields отображаются на exact tags/stages и fold order из Brief; `counter_fp`, context fields и `repeat_penalty_fp` не дублируются в Tactic stage; canonical key `low_hpfp` сохраняется.
- `OPEN-WP08-08 — OPEN / PROPOSED`: Situation использует только DATA-backed low-HP/wall/recovery contexts; range влияет на availability, несуществующий distance multiplier не выдумывается. Synergy — passive tag multiplier и gear `normalized_value` в slot order.
- `OPEN-WP08-09 — OPEN / PROPOSED`: Counter читает только committed observable telegraph после tactic perception delay; required tactic value авторитетен, global default не является runtime fallback; future/uncommitted opponent choice и direct AnimalId bonus запрещены.
- `OPEN-WP08-10 — OPEN / PROPOSED`: precedence — one legal, emergency suppression of hard, HardOpportunity, zero sum, weighted RNG. При multiple legal и positive sum один draw выполняется даже с одним positive-weight candidate. Candidates/intervals ordinal; draws A→B без reserved indices.
- `OPEN-WP08-11 — OPEN / PROPOSED`: Variety хранит immediate consecutive ActionId/category; same-action, same-category, tactic repeat multipliers применяются в этом порядке. MaxConsecutive использует двухпроходное правило и не может самостоятельно удалить все base-legal candidates.
- `OPEN-WP08-12 — OPEN / PROPOSED`: opportunity debt per Special: legal miss increments, illegal unchanged, selected commit resets; growth/cap exact. Action hard value `0` disables override; ready legal Special may hard-select even at final weight `0`, before zero-sum fallback; multiple hard tie — debt DESC, weight DESC, ActionId ASC. Emergency threat detection поставляет WP-09.
- `OPEN-WP08-13 — OPEN / PROPOSED`: оба commit descriptors и previewed Decision RNG freeze до mutation; exact whole-batch event-cap preflight предотвращает partial decision/commit. Затем authoritative submutations идут A→B без повторной rule evaluation: Decisions A/B, Commits A/B, costs A/B, telegraphs A/B. Frames снимаются из каждой submutation; B descriptor остаётся snapshot-frozen. Cost списывается один раз; cooldown first decrements next tick; combat timing freeze использует CDS §10.5; periodic gains остаются WP-11.
- `OPEN-WP08-14 — OPEN / PROPOSED`: Opponent target/direction freeze по decision snapshot; Self target fields null. Non-empty hit schedule emit `AttackPrepared`. Generic combat lifecycle uses actor-only phase events with `StartupCompleted`/`ActiveCompleted`/`RecoveryCompleted` and exact source chain; WP-07 `MovementCompleted`/`MoveEnded` semantics remain unchanged. No combat intents/Resolution RNG/HP or position mutation.
- `OPEN-WP08-15 — OPEN / PROPOSED`: canonical event/replay shape/version достаточны; legal diagnostic candidate содержит ровно шесть folded stage traces, illegal — none, оставаясь внутри schema cap. Optional diagnostic sink публикует DecisionTrace и общий версионированный `decision.batch-snapshot/0.1` commitment вне canonical chain; contracts/verifier усиливают mode/weight/RNG semantics, Standard/Diagnostic parity обязательна.
- `OPEN-WP08-16 — OPEN / PROPOSED`: Engine bump до `battle.core/0.3.0`; event/replay/balance/RNG/ordering versions сохраняются; historical fixtures immutable, current wait и `decision_weighted_l1` создаются отдельно.
- `OPEN-WP08-17 — OPEN / PROPOSED`: никаких Bear/Kangaroo/Gorilla или combat ActionId switches. Resolution — WP-09, effects/stat clamp — WP-10, полный fighter-kit availability/resource/passive semantics и explicit target DATA review — WP-11.

### Статус этапа

- WP-08 — `PREPARED / DECISIONS PROPOSED; IMPLEMENTATION NOT STARTED`.
- `OPEN-WP08-01..17` ещё не `CLOSED`; owner approval является blocking decision gate.
- Production code, tests, versions, config artifacts и fixtures WP-08 не изменялись; `UnityClient` не изменён.
