# WP-05 Battle Setup

## Цель

Подготовить и проверить конкретный Battle Case до запуска симуляции.

## Источники

- Combat Lab Technical Design v0.1 — раздел WP-05 и Battle Setup.
- Combat Design Specification v0.1 — разделы о сборках бойцов и начале боя.
- Combat Event Replay Schema v0.1 — BattleStarted и BattleRejected.

## Требования

Заполнить после изучения соответствующих разделов документов.

## Критерии готовности

- Валидный BattleRequest создаёт подготовленный battle state.
- Невалидные Stable ID отклоняются до BattleStarted.
- Проверяются выбранные actions, passive, tactic и gear slots.
- Используется immutable CompiledBattleConfig.
- Добавлены положительные и отрицательные тесты.
- Проходят dotnet build и dotnet test.

## Не входит в этап

- Изменение UnityClient.
- Полная симуляция боя.
- Production deployment.