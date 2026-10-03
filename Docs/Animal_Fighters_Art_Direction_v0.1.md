# Animal Fighters Art Direction v0.1

**Status:** approved concept direction, not production character assets  
**Owner approval:** 2026-09-30  
**Scope:** visual identity of the bear and kangaroo in the read-only Unity Replay Viewer

## Approved references

- [Unequipped bear and kangaroo](./Assets/WP-UI-01/animal-fighters-base-concept-v0.1.png)
- [Equipped bear and kangaroo](./Assets/WP-UI-01/animal-fighters-equipped-concept-v0.1.png)

These generated images are art references. They are not rigged sprites, animation frames, or final UI textures.

## Unequipped model-sheet concepts

- [Bear: front, right side, back and cutout-part study](./Assets/WP-UI-01/bear-unequipped-model-sheet-v0.1.png)
- [Kangaroo: front, right side, back and cutout-part study](./Assets/WP-UI-01/kangaroo-unequipped-model-sheet-v0.1.png)

Generated `2026-10-01` from the approved bare-character direction and illustrated fight key art. These sheets propose consistent silhouettes and separable head, torso, limbs and kangaroo tail. They are **design references, not production sprite atlases**: the background is opaque, the views and hidden joint surfaces need an artist consistency pass, and the pictured pieces are not independently exported with validated pivots. Do not import the sheets as animated rigs or infer replay state from them. Character-sheet approval and transparent, individually authored cutouts are the next asset step.

## Layered Viewer prototype

The project retains two transparent, right-facing side-profile reference sprites:
`Assets/Resources/ReplayViewer/Characters/bear-base.png` and
`Assets/Resources/ReplayViewer/Characters/kangaroo-base.png` (paths relative to
`UnityClient/AnimalCombat`). Both are 1254 × 1254 PNGs. The Viewer now displays
`bear-rig-atlas-v0.1.png` and `kangaroo-rig-atlas-v0.1.png` from the same
directory. These 1536 x 1024 transparent atlases supply independent cutouts:
torso, head, near arm and both legs; the kangaroo also has a separate tail.
The UXML pivots these parts independently and mirrors the assembled rig from
recorded `facing`. The Viewer currently displays both fighters unequipped:
there are no armor or weapon elements in the scene. The equipped concept
remains a reference for a later art pass, not a fixture gear mapping.
No battle or damage value is inferred from these visuals.
`AttackPrepared` drives a shoulder-pivot windup; recorded `AttackHit` or
`AttackMissed` drives a full arm swing and recovery. `AttackHit` also
triggers a short target reaction. Idle breathing plays while replay playback
is running. Pause freezes the pose, speed scales its timing, and Restart
resets it. The animation never changes recorded position, health, facing,
or outcome.

This is a first cutout-rig prototype, not a skinned production character.
Pivots and placement are authored in USS, and the bear's far arm is painted
into its torso cutout. Separate gear-to-equipment-ID mapping, refined joint
seams, animation blending, and final illustrated equipment remain for a later
art production pass.

## Prototype validation

- In a minimal temporary Unity 6000.4.0f1 project containing the Viewer and
  its runtime dependencies, PlayMode passed 5/5 tests. The cutout tests reach
  basic replay sequence 5 (windup) and sequence 6 (swing), verifies loaded
  cutout sprites and shoulder rotation, checks that Pause freezes the pose,
  that Restart restores it, and that HP remains unchanged until the recorded
  damage event. The double-KO case separately verifies the kangaroo's arm
  and tail pivots at its recorded AttackPrepared event.
- EditMode passed 10/13 in that isolated project. The three path-dependent
  canonical-fixture tests could not find `CombatLab` outside the temporary
  project; separate SHA-256 comparisons confirm all three bundled replay
  files are byte-identical to the canonical fixtures.
- A clean import of the full package manifest in a temporary copy currently
  stops at a `com.unity.shadergraph` compilation error (`GUID` type not
  found) before Viewer tests start. The main project's packages were not
  changed.
- Manual visual review in the real Unity Editor and phone-landscape sizing
  remain open; automated tests do not establish final art quality.

## Character direction

- Polished 2D cartoon animal fighters, with playful, deliberately unnatural proportions and slightly mad expressions.
- Species and combat silhouettes remain readable at game-screen size: bear has a massive crooked upper body and round ears; kangaroo has short ears, powerful hind legs, broad feet, small forearms, and a thick balancing tail.
- Start with fur-only base characters. Weapons and armor are separate, removable visual layers.
- Armor can be colorful, mismatched, and comically disproportionate. It must not obscure the base anatomy or block readable attacks.
- Weapons should be familiar and materially plausible, contrasting with the absurd fighters. The approved equipped reference uses a machete for the bear and a wooden baseball bat for the kangaroo. These are visual examples, not a gameplay loadout or a change to replay action IDs.
- Retain the layered illustrated arena direction from the approved game-first UI references. Exact HP values, event cues, and playback controls remain governed by the WP-UI-01 UI Spec and Test Plan.

## Production preparation

1. Redraw each animal as consistent side-view, separated 2D body parts on transparent layers, with clean pivots and an animation-friendly rig.
2. Create weapon and armor layers with defined hand, shoulder, torso, and shin attachment points.
3. Validate bare and equipped silhouettes at desktop and phone-landscape game size.
4. Animate one recorded replay beat (idle, preparation, attack, hit reaction, recovery) before expanding the full animation set.

The generated references do not authorize Unity combat calculation or changes to CombatLab fixtures or production code.
