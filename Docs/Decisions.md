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