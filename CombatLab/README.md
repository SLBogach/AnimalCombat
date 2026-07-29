# Combat Event & Replay Schema v0.1

Машиночитаемый пакет к одноимённой спецификации.
`manifest.json` перечисляет все payload-файлы пакета и по определению не включает
собственный размер или SHA-256.

## Схемы

- `schemas/combat-replay.schema.json` — полный канонический повтор.
- `schemas/combat-event.schema.json` — входная точка для одного event.
- `schemas/combat-presentation.schema.json` — производная Unity timeline.
- `schemas/combat-rejection.schema.json` — отказ до `BattleStarted`.

## Примеры

- `examples/replay-standard.example.json`
- `examples/replay-diagnostic.example.json`
- `examples/presentation-timeline.example.json`
- `examples/battle-rejected.example.json`

Примеры являются синтетическими schema fixtures, а не результатом балансной
симуляции. В коротком fixture `fighter_b` начинает бой с 70 HP.

## Версии

- replay: `combat.replay/0.1`
- event: `combat.event/0.1`
- presentation: `combat.presentation/0.1`
- rejection: `combat.rejection/0.1`
- balance source: `combat.balance/0.1`
- RNG hypothesis: `pcg32/1`
- tick ordering: `tick-pipeline/1`

## Integrity fixture

- input digest: `sha256:26bd0244bada8360818da1de29c926c09ae1f2e31915654c5e86fd954b2cca5b`
- final event-chain digest: `sha256:bdf470b43b23569fbfbe053772fdc4684b531e48b4a88d6b3b577ad122a1e69e`
- event count: `13`

Канонический digest рассчитывается по правилам документа. Диагностический
overlay не входит в digest: массив `events` и `final_digest` в standard и
diagnostic примерах совпадают.
