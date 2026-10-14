![GAMMA Hit Sounds banner](https://i.postimg.cc/bvbw5sYP/banner.png)

**GAMMA Hit Sounds** adds randomized S.T.A.L.K.E.R. G.A.M.M.A. impact audio to firearm injuries in RimWorld. Harmony required. Combat Extended is supported!

This mod is available for download on Steam Workshop. You can find it [here](https://steamcommunity.com/sharedfiles/filedetails/?id=3819390495).

## Features

- Selects sounds for body and head injuries according to sharp armor on the struck body part. When Combat Extended is loaded, its body-part-specific armor value is used when available.
- Recognizes vanilla bullet damage, damage types with a `Bullet` impact sound type, and the Combat Extended damage types `Fragment`, `Tranquilizer`, `CE_Laser`, and `BulletToxic`.
- Plays at most one regular hit sound per pawn per game tick.
- Suppresses the matching vanilla bullet-ground impact when a custom hit sound has already played.
- Replaces bullet-related `Deflect_Metal` and `Deflect_General` cues with a mechanoid impact sound when that damage event did not produce a regular injury sound.
- Leaves direct mechanoid injury sounds to the base game and other loaded mods.

## Sound files

RimWorld loads the clips from folders referenced by `Defs/SoundDefs/Gamma_Hit_Sounds.xml`:

| Folder | Clips | Used for |
| --- | ---: | --- |
| `Sounds/Body/Armor` | 20 | Body parts protected by sharp armor |
| `Sounds/Body/NoArmor` | 20 | Body parts without sharp armor |
| `Sounds/Body/Mechanoid` | 20 | Bullet-related armor deflections |
| `Sounds/Head/Helmet` | 20 | Head parts protected by sharp armor |
| `Sounds/Head/NoHelmet` | 40 | Unhelmeted head hits |

## Build

The project targets .NET Framework 4.7.2. Building requires the .NET SDK, the .NET Framework 4.7.2 Developer Pack, the RimWorld game assemblies, and Harmony's `0Harmony.dll`. Set `RIMWORLD_DIR` to the RimWorld installation directory and `HARMONY_DIR` to the directory containing `0Harmony.dll`, then run this from the project root:

```powershell
dotnet build .\GammaHitSounds.csproj -c Release
```

A Release build writes `DHM.GammaHitEffects.dll` to `GAMMA hit sounds/Assemblies`.

## Credits and license

The sound effects are from S.T.A.L.K.E.R. G.A.M.M.A. The original hit-effects mod is by [wepl](https://www.moddb.com/mods/call-of-chernobyl/addons/wepls-hit-effects). 

Learn more about GAMMA at [stalkergamma.com](https://www.stalkergamma.com/).

This repository is licensed under the [MIT License](LICENSE).
