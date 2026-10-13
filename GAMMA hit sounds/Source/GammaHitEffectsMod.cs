using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace DHM.GammaHitEffects
{
    [StaticConstructorOnStartup]
    public static class GammaHitEffectsMod
    {
        static GammaHitEffectsMod()
        {
            Harmony harmony = new Harmony("soc1almemory.gammahitsounds");
            Type resultType = typeof(DamageWorker).GetNestedType("DamageResult", BindingFlags.Public | BindingFlags.NonPublic);
            harmony.Patch(
                AccessTools.Method(
                    typeof(DamageWorker_AddInjury),
                    "FinalizeAndAddInjury",
                    new[] { typeof(Pawn), typeof(Hediff_Injury), typeof(DamageInfo), resultType }),
                postfix: new HarmonyMethod(typeof(GammaHitEffectsMod), "PlayGammaHitSound"));

            harmony.Patch(
                AccessTools.Method(
                    typeof(SoundStarter),
                    "PlayOneShot",
                    new[] { typeof(SoundDef), typeof(SoundInfo) }),
                prefix: new HarmonyMethod(typeof(GammaHitEffectsMod), "SuppressVanillaBulletGroundSound"));

            harmony.Patch(
                AccessTools.Method(typeof(Thing), "TakeDamage", new[] { typeof(DamageInfo) }),
                prefix: new HarmonyMethod(typeof(GammaHitEffectsMod), "EnterBulletDamageScope"),
                postfix: new HarmonyMethod(typeof(GammaHitEffectsMod), "LeaveBulletDamageScope"));
        }

        private static readonly HashSet<int> PawnsWithHitSoundThisTick = new HashSet<int>();
        private static readonly MethodInfo CEPartialStatForApparel =
            AccessTools.Method("CombatExtended.ArmorUtilityCE:PartialStat", new[] { typeof(Apparel), typeof(StatDef), typeof(BodyPartRecord) });
        private static int hitSoundSetTick = -1;
        private static int lastCustomSoundTick = -1;
        private static IntVec3 lastCustomSoundCell = IntVec3.Invalid;
        private static Map lastCustomSoundMap;
        private static readonly Stack<DamageScopeState> ActiveDamageScopes = new Stack<DamageScopeState>();

        private class DamageScopeState
        {
            public bool active;
            public Pawn pawn;
            public bool customHitPlayed;
            public bool deflectSuppressed;
            public SoundInfo deflectInfo;
        }

        private static void EnterBulletDamageScope(Thing __instance, DamageInfo dinfo, ref DamageScopeState __state)
        {
            Pawn pawn = __instance as Pawn;
            __state = new DamageScopeState
            {
                active = pawn != null && IsBulletDamage(dinfo),
                pawn = pawn
            };

            if (__state.active)
            {
                ActiveDamageScopes.Push(__state);
            }
        }

        private static void LeaveBulletDamageScope(DamageScopeState __state)
        {
            if (__state == null || !__state.active)
            {
                return;
            }

            if (ActiveDamageScopes.Count > 0 && ActiveDamageScopes.Peek() == __state)
            {
                ActiveDamageScopes.Pop();
            }
            else
            {
                RemoveDamageScope(__state);
            }

            if (__state.deflectSuppressed && !__state.customHitPlayed)
            {
                PlayCustomSound(SoundDef.Named("DHM_GammaHit_Body_Mechanoid"), __state.deflectInfo);
            }
        }

        private static void RemoveDamageScope(DamageScopeState state)
        {
            if (ActiveDamageScopes.Count == 0)
            {
                return;
            }

            Stack<DamageScopeState> temp = new Stack<DamageScopeState>();
            while (ActiveDamageScopes.Count > 0)
            {
                DamageScopeState current = ActiveDamageScopes.Pop();
                if (current == state)
                {
                    break;
                }
                temp.Push(current);
            }
            while (temp.Count > 0)
            {
                ActiveDamageScopes.Push(temp.Pop());
            }
        }

        private static void PlayGammaHitSound(Pawn pawn, Hediff_Injury injury, DamageInfo dinfo)
        {
            if (pawn == null || injury == null || injury.Part == null || !pawn.Spawned || !IsBulletDamage(dinfo))
            {
                return;
            }

            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : -1;
            if (tick >= 0)
            {
                if (hitSoundSetTick != tick)
                {
                    PawnsWithHitSoundThisTick.Clear();
                    hitSoundSetTick = tick;
                }

                if (PawnsWithHitSoundThisTick.Contains(pawn.thingIDNumber))
                {
                    return;
                }
            }

            SoundDef soundDef = SelectSound(pawn, injury.Part);
            if (soundDef == null)
            {
                return;
            }

            if (tick >= 0)
            {
                PawnsWithHitSoundThisTick.Add(pawn.thingIDNumber);
            }
            MarkCustomHitPlayed(pawn);
            lastCustomSoundTick = tick;
            lastCustomSoundCell = pawn.Position;
            lastCustomSoundMap = pawn.Map;
            PlayCustomSound(soundDef, new TargetInfo(pawn.Position, pawn.Map, false));
        }

        private static bool SuppressVanillaBulletGroundSound(SoundDef soundDef, SoundInfo info)
        {
            if (ActiveDamageScopes.Count > 0 && IsArmorDeflectSound(soundDef))
            {
                DamageScopeState current = ActiveDamageScopes.Peek();
                current.deflectSuppressed = true;
                current.deflectInfo = info;
                return false;
            }

            if (soundDef != SoundDefOf.BulletImpact_Ground)
            {
                return true;
            }

            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : -1;
            if (tick != lastCustomSoundTick)
            {
                return true;
            }

            TargetInfo maker = info.Maker;
            return maker.Map != lastCustomSoundMap || maker.Cell != lastCustomSoundCell;
        }

        private static bool IsArmorDeflectSound(SoundDef soundDef)
        {
            return soundDef != null
                && (soundDef.defName == "Deflect_Metal" || soundDef.defName == "Deflect_General");
        }

        private static void MarkCustomHitPlayed(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            foreach (DamageScopeState state in ActiveDamageScopes)
            {
                if (state.pawn == pawn)
                {
                    state.customHitPlayed = true;
                    return;
                }
            }
        }

        private static void PlayCustomSound(SoundDef soundDef, SoundInfo info)
        {
            if (soundDef == null)
            {
                return;
            }

            TargetInfo maker = info.Maker;
            if (maker.IsValid)
            {
                lastCustomSoundTick = Find.TickManager != null ? Find.TickManager.TicksGame : -1;
                lastCustomSoundCell = maker.Cell;
                lastCustomSoundMap = maker.Map;
            }

            SoundStarter.PlayOneShot(soundDef, info);
        }

        private static bool IsBulletDamage(DamageInfo dinfo)
        {
            DamageDef def = dinfo.Def;
            return def == DamageDefOf.Bullet
                || (def != null && def.impactSoundType != null && def.impactSoundType.defName == "Bullet")
                || IsKnownCEBulletDamageDef(def);
        }

        private static bool IsKnownCEBulletDamageDef(DamageDef def)
        {
            if (def == null || def.defName == null)
            {
                return false;
            }

            return def.defName == "Fragment"
                || def.defName == "Tranquilizer"
                || def.defName == "CE_Laser"
                || def.defName == "BulletToxic";
        }

        private static SoundDef SelectSound(Pawn pawn, BodyPartRecord part)
        {
            if (pawn.RaceProps != null && pawn.RaceProps.IsMechanoid)
            {
                return null;
            }

            bool headHit = IsHeadPart(part);
            bool armored = HasSharpArmorOnPart(pawn, part);

            if (headHit)
            {
                return SoundDef.Named(armored ? "DHM_GammaHit_Head_Helmet" : "DHM_GammaHit_Head_NoHelmet");
            }

            return SoundDef.Named(armored ? "DHM_GammaHit_Body_Armor" : "DHM_GammaHit_Body_NoArmor");
        }

        private static bool IsHeadPart(BodyPartRecord part)
        {
            return part.def == BodyPartDefOf.Head
                || part.IsInGroup(BodyPartGroupDefOf.FullHead)
                || part.IsInGroup(BodyPartGroupDefOf.UpperHead)
                || part.IsInGroup(BodyPartGroupDefOf.Eyes);
        }

        private static bool HasSharpArmorOnPart(Pawn pawn, BodyPartRecord part)
        {
            if (pawn.apparel == null)
            {
                return false;
            }

            Apparel apparel;
            return pawn.apparel.TryGetFirstApparelOnBodyPart(part, out apparel, delegate(Apparel wornApparel)
                {
                    return IsProtectiveApparel(wornApparel, part);
                })
                && apparel != null;
        }

        private static bool IsProtectiveApparel(Apparel apparel, BodyPartRecord part)
        {
            if (apparel == null || apparel.def == null || apparel.def.apparel == null)
            {
                return false;
            }

            if (CEPartialStatForApparel != null)
            {
                try
                {
                    object value = CEPartialStatForApparel.Invoke(null, new object[] { apparel, StatDefOf.ArmorRating_Sharp, part });
                    if (value != null)
                    {
                        return Convert.ToSingle(value) > 0.01f;
                    }
                }
                catch (TargetInvocationException)
                {
                    // CE is optional; fall back to RimWorld's apparel stat if its helper is unavailable at runtime.
                }
            }

            return apparel.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0.01f;
        }
    }
}
