using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace GammaHitSounds
{
    [StaticConstructorOnStartup]
    public static class GammaHitEffectsMod
    {
        private const string SettingsDefName = "Soc1alMemory_GammaHitSounds_Settings";
        private const string HeadNoHelmetSoundDefName = "Soc1alMemory_GammaHitSounds_Head_NoHelmet";
        private const string HeadHelmetSoundDefName = "Soc1alMemory_GammaHitSounds_Head_Helmet";
        private const string BodyNoArmorSoundDefName = "Soc1alMemory_GammaHitSounds_Body_NoArmor";
        private const string BodyArmorSoundDefName = "Soc1alMemory_GammaHitSounds_Body_Armor";
        private const string BodyMechanoidSoundDefName = "Soc1alMemory_GammaHitSounds_Body_Mechanoid";

        private static readonly HashSet<int> PawnsWithHitSoundThisTick = new HashSet<int>();
        private static readonly HashSet<string> AdditionalBulletDamageDefNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly MethodInfo CEPartialStatForApparel =
            AccessTools.Method("CombatExtended.ArmorUtilityCE:PartialStat", new[] { typeof(Apparel), typeof(StatDef), typeof(BodyPartRecord) });
        private static readonly Stack<DamageScopeState> ActiveDamageScopes = new Stack<DamageScopeState>();
        private static int hitSoundSetTick = -1;
        private static bool cePartialStatWarningLogged;

        private class DamageScopeState
        {
            public Pawn pawn;
            public bool customHitPlayed;
            public bool deflectSuppressed;
            public SoundInfo deflectInfo;
        }

        static GammaHitEffectsMod()
        {
            LoadAdditionalBulletDamageDefs();

            Harmony harmony = new Harmony("soc1almemory.gammahitsounds");
            Type resultType = typeof(DamageWorker).GetNestedType("DamageResult", BindingFlags.Public | BindingFlags.NonPublic);
            if (resultType == null)
            {
                Log.Error("GAMMA Hit Sounds could not find DamageWorker.DamageResult; the injury sound patch was skipped.");
            }
            else
            {
                TryPatch(
                    harmony,
                    "injury sound",
                    typeof(DamageWorker_AddInjury),
                    "FinalizeAndAddInjury",
                    new[] { typeof(Pawn), typeof(Hediff_Injury), typeof(DamageInfo), resultType },
                    postfix: new HarmonyMethod(typeof(GammaHitEffectsMod), "PlayGammaHitSound"));
            }

            TryPatch(
                harmony,
                "vanilla impact sound suppression",
                typeof(SoundStarter),
                "PlayOneShot",
                new[] { typeof(SoundDef), typeof(SoundInfo) },
                prefix: new HarmonyMethod(typeof(GammaHitEffectsMod), "SuppressVanillaBulletGroundSound"));

            TryPatch(
                harmony,
                "bullet damage scope",
                typeof(Thing),
                "TakeDamage",
                new[] { typeof(DamageInfo) },
                prefix: new HarmonyMethod(typeof(GammaHitEffectsMod), "EnterBulletDamageScope"),
                finalizer: new HarmonyMethod(typeof(GammaHitEffectsMod), "FinalizeBulletDamageScope"));
        }

        private static void TryPatch(
            Harmony harmony,
            string patchDescription,
            Type declaringType,
            string methodName,
            Type[] parameterTypes,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod finalizer = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(declaringType, methodName, parameterTypes);
                if (target == null)
                {
                    Log.Error("GAMMA Hit Sounds could not find the target for its " + patchDescription + " patch: "
                        + declaringType.FullName + "." + methodName + ".");
                    return;
                }

                harmony.Patch(target, prefix, postfix, finalizer: finalizer);
            }
            catch (Exception exception)
            {
                Log.Error("GAMMA Hit Sounds failed to install its " + patchDescription + " patch: " + exception);
            }
        }

        private static void LoadAdditionalBulletDamageDefs()
        {
            GammaHitSoundSettingsDef settings = DefDatabase<GammaHitSoundSettingsDef>.GetNamedSilentFail(SettingsDefName);
            if (settings == null || settings.additionalBulletDamageDefs == null)
            {
                Log.Error("GAMMA Hit Sounds could not load its damage type settings definition: " + SettingsDefName + ".");
                return;
            }

            foreach (string defName in settings.additionalBulletDamageDefs)
            {
                if (!string.IsNullOrWhiteSpace(defName))
                {
                    AdditionalBulletDamageDefNames.Add(defName.Trim());
                }
            }
        }

        private static void EnterBulletDamageScope(Thing __instance, DamageInfo dinfo, ref DamageScopeState __state)
        {
            Pawn pawn = __instance as Pawn;
            __state = null;
            if (pawn == null || !IsBulletDamage(dinfo))
            {
                return;
            }

            __state = new DamageScopeState { pawn = pawn };
            ActiveDamageScopes.Push(__state);
        }

        private static Exception FinalizeBulletDamageScope(Exception __exception, DamageScopeState __state)
        {
            if (__state == null)
            {
                return __exception;
            }

            RemoveDamageScope(__state);
            if (__state.deflectSuppressed && !__state.customHitPlayed)
            {
                try
                {
                    PlayCustomSound(SoundDef.Named(BodyMechanoidSoundDefName), __state.deflectInfo);
                }
                catch (Exception soundException)
                {
                    Log.Error("GAMMA Hit Sounds could not play the replacement deflection sound: " + soundException);
                }
            }

            return __exception;
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
            PlayCustomSound(soundDef, new TargetInfo(pawn.Position, pawn.Map, false));
        }

        private static bool SuppressVanillaBulletGroundSound(SoundDef soundDef, SoundInfo info)
        {
            if (IsArmorDeflectSound(soundDef))
            {
                DamageScopeState deflectScope = FindDamageScope(info.Maker);
                if (deflectScope == null)
                {
                    return true;
                }

                deflectScope.deflectSuppressed = true;
                deflectScope.deflectInfo = info;
                return false;
            }

            if (soundDef != SoundDefOf.BulletImpact_Ground)
            {
                return true;
            }

            DamageScopeState injuryScope = FindDamageScope(info.Maker);
            return injuryScope == null || !injuryScope.customHitPlayed;
        }

        private static DamageScopeState FindDamageScope(TargetInfo maker)
        {
            if (!maker.IsValid || maker.Map == null)
            {
                return null;
            }

            foreach (DamageScopeState state in ActiveDamageScopes)
            {
                if (state.pawn != null && state.pawn.Map == maker.Map && state.pawn.Position == maker.Cell)
                {
                    return state;
                }
            }

            return null;
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
            if (soundDef != null)
            {
                SoundStarter.PlayOneShot(soundDef, info);
            }
        }

        private static bool IsBulletDamage(DamageInfo dinfo)
        {
            DamageDef def = dinfo.Def;
            return def == DamageDefOf.Bullet
                || (def != null && def.impactSoundType != null && def.impactSoundType.defName == "Bullet")
                || (def != null && AdditionalBulletDamageDefNames.Contains(def.defName));
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
                return SoundDef.Named(armored ? HeadHelmetSoundDefName : HeadNoHelmetSoundDefName);
            }

            return SoundDef.Named(armored ? BodyArmorSoundDefName : BodyNoArmorSoundDefName);
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
                catch (TargetInvocationException exception)
                {
                    LogCEPartialStatFallback(exception);
                }
                catch (ArgumentException exception)
                {
                    LogCEPartialStatFallback(exception);
                }
                catch (MethodAccessException exception)
                {
                    LogCEPartialStatFallback(exception);
                }
            }

            return apparel.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0.01f;
        }

        private static void LogCEPartialStatFallback(Exception exception)
        {
            if (cePartialStatWarningLogged)
            {
                return;
            }

            cePartialStatWarningLogged = true;
            Log.Warning("GAMMA Hit Sounds could not query Combat Extended's part-specific armor value and will use RimWorld's apparel armor stat instead. " + exception);
        }
    }
}
