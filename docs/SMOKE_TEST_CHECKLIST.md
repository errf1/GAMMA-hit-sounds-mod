# Smoke test checklist

Run this checklist on each game version after building its assembly. Test both with Combat Extended disabled and enabled where the case applies. Keep a disposable test save and check the game log after startup and after each scenario.

## Startup and patching

- [ ] Enable Harmony before Core and enable GAMMA Hit Sounds.
- [ ] Confirm the game loads the assembly from the expected version folder.
- [ ] Confirm startup has no red errors for missing settings, sound definitions, or Harmony patches.
- [ ] With Combat Extended enabled, confirm the optional armor integration produces no reflection errors.

## Hit sounds

- [ ] Bullet injury to an unarmored torso plays a body no-armor clip.
- [ ] Bullet injury to an armored torso plays a body armor clip.
- [ ] Bullet injury to an unhelmeted head plays a head no-helmet clip.
- [ ] Bullet injury to a helmeted head plays a head helmet clip.
- [ ] Multiple bullet injuries to one pawn during the same game tick play at most one custom injury sound.
- [ ] A second pawn hit in the same tick still gets its own custom injury sound.
- [ ] A vanilla bullet-ground sound from an unrelated event at the same map cell and tick is not suppressed.

## Deflections and unaffected damage

- [ ] A bullet deflected by armor replaces the matching `Deflect_Metal` or `Deflect_General` cue with one mechanoid impact clip when no regular custom injury sound played.
- [ ] A bullet injury that already played a regular custom sound does not also play a mechanoid deflection clip.
- [ ] Non-bullet damage and damage to non-pawn things retain their normal sounds.
- [ ] Mechanoid injury sounds remain controlled by the base game and other mods.

## Combat Extended

- [ ] A configured CE damage type such as `Fragment`, `Tranquilizer`, `CE_Laser`, or `BulletToxic` is recognized as bullet damage.
- [ ] Body-part-specific CE sharp armor selects the armored sound pool when available.
- [ ] If CE's armor helper is unavailable, the game falls back to RimWorld's apparel sharp armor stat and logs at most one warning.

## Scope cleanup

- [ ] After a damage event exits exceptionally in a controlled development scenario, a later unrelated hit is processed normally; the damage-scope stack does not suppress or replace its sounds.
