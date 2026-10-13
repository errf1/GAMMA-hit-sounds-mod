# GAMMA hit sounds

Adds a hit sound effects from S.T.A.L.K.E.R. G.A.M.M.A.

<img width="1920" height="512" alt="Banner" src="https://i.postimg.cc/B63PFLHm/banner.png" />

https://www.stalkergamma.com/

Original mod: https://www.moddb.com/mods/call-of-chernobyl/addons/wepls-hit-effects

Author: wepl

## Behavior

- Bullet injuries on pawns play a random GAMMA hit sound.
- Head hits use the helmet category only when the struck head part is covered by apparel with sharp armor.
- Head hits without armored headgear use the shared `Head/NoHelmet` pool, including both `hit_gasmask_*` and `hit_none_*`.
- Body hits use armor/no-armor categories based on apparel covering the struck body part.
- Direct mechanoid hit sounds are left to the base game or other loaded mods.
- The vanilla `BulletImpact_Ground` sound is suppressed in the same cell and tick as the custom hit sound to avoid doubling.
- During bullet-like pawn damage, vanilla armor deflect sounds `Deflect_Metal` and `Deflect_General` are suppressed and replaced with the `Body/Mechanoid` pool (`hit_exo_*`) only if no normal injury hit sound was played for that same damage event.

## Combat Extended

The mod has optional Combat Extended compatibility:

- CE does not need to be installed for this mod to work.
- If CE is installed, this mod loads after it.
- CE bullet-like damage defs such as `Fragment`, `Tranquilizer`, `CE_Laser`, and `BulletToxic` are treated as firearm impacts.
- CE partial armor coverage is used when available, so the armor/no-armor sound follows CE's body-part armor calculation more closely.
