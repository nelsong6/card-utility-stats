using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Orbs;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks exact card-created Frost orb lifecycles. The attribution scope also
/// marks Frost block as orb output so the generic card-block fallback cannot
/// fold it into the originating card's direct block totals.
/// </summary>
[HarmonyPatch(typeof(FrostOrb), nameof(FrostOrb.Passive))]
public static class CardSourcedFrostOrbPassiveStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(FrostOrb __instance, out bool __state)
    {
        __state = RunTracker.BeginFrostOrbBlockAttribution(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        FrostOrb __instance,
        bool __state,
        ref Task __result)
    {
        try
        {
            if (__result == null)
            {
                if (__state)
                    RunTracker.EndFrostOrbBlockAttribution(__instance);
                return;
            }

            if (!__state && !RunTracker.IsTrackedCardSourcedOrb(__instance))
                return;

            __result = ObserveAsync(__result, __instance, __state);
        }
        catch (Exception e)
        {
            if (__state)
                RunTracker.EndFrostOrbBlockAttribution(__instance);
            CoreMain.LogDebug(
                $"CardSourcedFrostOrbPassiveStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        FrostOrb orb,
        bool blockAttributionArmed)
    {
        try
        {
            await inner;
            RunTracker.RecordCardSourcedOrbPassive(orb);
        }
        finally
        {
            if (blockAttributionArmed)
                RunTracker.EndFrostOrbBlockAttribution(orb);
        }
    }
}

[HarmonyPatch(typeof(FrostOrb), nameof(FrostOrb.Evoke))]
public static class CardSourcedFrostOrbEvokeStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(FrostOrb __instance, out bool __state)
    {
        __state = RunTracker.BeginFrostOrbBlockAttribution(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        FrostOrb __instance,
        bool __state,
        ref Task<IEnumerable<Creature>> __result)
    {
        try
        {
            if (__result == null)
            {
                if (__state)
                    RunTracker.EndFrostOrbBlockAttribution(__instance);
                return;
            }

            if (!__state && !RunTracker.IsTrackedCardSourcedOrb(__instance))
                return;

            __result = ObserveAsync(__result, __instance, __state);
        }
        catch (Exception e)
        {
            if (__state)
                RunTracker.EndFrostOrbBlockAttribution(__instance);
            CoreMain.LogDebug(
                $"CardSourcedFrostOrbEvokeStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<Creature>> ObserveAsync(
        Task<IEnumerable<Creature>> inner,
        FrostOrb orb,
        bool blockAttributionArmed)
    {
        try
        {
            var targets = await inner;
            RunTracker.RecordCardSourcedOrbEvoked(orb);
            return targets;
        }
        finally
        {
            if (blockAttributionArmed)
                RunTracker.EndFrostOrbBlockAttribution(orb);
        }
    }
}

[HarmonyPatch(typeof(PlasmaOrb), nameof(PlasmaOrb.Passive))]
public static class CardSourcedPlasmaOrbPassiveStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(PlasmaOrb __instance, ref Task __result)
    {
        try
        {
            if (__result == null || !RunTracker.IsTrackedCardSourcedOrb(__instance))
                return;
            __result = ObserveAsync(__result, __instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardSourcedPlasmaOrbPassiveStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, PlasmaOrb orb)
    {
        await inner;
        RunTracker.RecordCardSourcedOrbPassive(orb);
    }
}

[HarmonyPatch(typeof(PlasmaOrb), nameof(PlasmaOrb.Evoke))]
public static class CardSourcedPlasmaOrbEvokeStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        PlasmaOrb __instance,
        ref Task<IEnumerable<Creature>> __result)
    {
        try
        {
            if (__result == null || !RunTracker.IsTrackedCardSourcedOrb(__instance))
                return;
            __result = ObserveAsync(__result, __instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardSourcedPlasmaOrbEvokeStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<Creature>> ObserveAsync(
        Task<IEnumerable<Creature>> inner,
        PlasmaOrb orb)
    {
        var targets = await inner;
        RunTracker.RecordCardSourcedOrbEvoked(orb);
        return targets;
    }
}

[HarmonyPatch(typeof(GlassOrb), nameof(GlassOrb.Passive))]
public static class CardSourcedGlassOrbPassiveStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(GlassOrb __instance, out bool __state)
    {
        __state = RunTracker.IsTrackedCardSourcedOrb(__instance)
            && __instance.PassiveVal > 0m;
    }

    [HarmonyPostfix]
    public static void Postfix(
        GlassOrb __instance,
        bool __state,
        ref Task __result)
    {
        try
        {
            if (__result == null || !__state) return;
            __result = ObserveAsync(__result, __instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardSourcedGlassOrbPassiveStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, GlassOrb orb)
    {
        await inner;
        RunTracker.RecordCardSourcedOrbPassive(orb);
    }
}

[HarmonyPatch(typeof(GlassOrb), nameof(GlassOrb.Evoke))]
public static class CardSourcedGlassOrbEvokeStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        GlassOrb __instance,
        ref Task<IEnumerable<Creature>> __result)
    {
        try
        {
            if (__result == null || !RunTracker.IsTrackedCardSourcedOrb(__instance))
                return;
            __result = ObserveAsync(__result, __instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CardSourcedGlassOrbEvokeStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<Creature>> ObserveAsync(
        Task<IEnumerable<Creature>> inner,
        GlassOrb orb)
    {
        var targets = await inner;
        RunTracker.RecordCardSourcedOrbEvoked(orb);
        return targets;
    }
}
