# Принятые решения

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
- Golden `wait_equal_l1` зафиксирован как input `sha256:0edc1dbc1d8d2a09c38debed5626fba5637f7304a38df258342d1d959edc8ba2`, final `sha256:d06e3c2153a4fbfc495279cd6fcf7379d6f8d42c059e8756a5003d01acfa9ea6`; после регенерации drift отсутствует.
