using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures the exact mutable Dark orb created by Symbiotic Virus's
/// owner-specific turn-one callback.
/// </summary>
[HarmonyPatch(
    typeof(SymbioticVirus),
    nameof(SymbioticVirus.AfterSideTurnStart))]
public static class SymbioticVirusStartingOrbStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        SymbioticVirus __instance,
        IReadOnlyList<Creature> participants,
        out StartingOrbState? __state)
    {
        __state = null;

        try
        {
            if (__instance == null || participants == null) return;
            var owner = __instance.Owner;
            var playerCombatState = owner?.PlayerCombatState;
            var orbQueue = playerCombatState?.OrbQueue;
            if (owner == null
                || playerCombatState == null
                || orbQueue == null)
            {
                return;
            }

            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!participants.Contains(owner.Creature)) return;
            if (playerCombatState.TurnNumber > 1) return;

            __state = new StartingOrbState(
                __instance,
                orbQueue,
                new HashSet<OrbModel>(
                    orbQueue.Orbs,
                    ReferenceEqualityComparer.Instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SymbioticVirusStartingOrbStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, StartingOrbState? __state)
    {
        try
        {
            if (__result == null || __state == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SymbioticVirusStartingOrbStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, StartingOrbState state)
    {
        await inner;

        try
        {
            var startingOrbs = state.OrbQueue.Orbs
                .Where(orb =>
                    orb is DarkOrb
                    && !state.OrbsBefore.Contains(orb))
                .ToList();
            RunTracker.TrackSymbioticVirusStartingOrbs(
                state.Relic,
                startingOrbs);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SymbioticVirusStartingOrbStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public sealed record StartingOrbState(
        SymbioticVirus Relic,
        OrbQueue OrbQueue,
        IReadOnlySet<OrbModel> OrbsBefore);
}

/// <summary>
/// Counts each completed passive activation of the tracked Virus Dark orb,
/// including additional triggers produced by other orb mechanics.
/// </summary>
[HarmonyPatch(typeof(DarkOrb), nameof(DarkOrb.Passive))]
public static class SymbioticVirusStartingOrbPassiveStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(DarkOrb __instance, out bool __state)
    {
        __state = RunTracker.IsTrackedSymbioticVirusStartingOrb(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        DarkOrb __instance,
        bool __state,
        ref Task __result)
    {
        try
        {
            if (__result == null) return;
            if (!__state && !RunTracker.IsTrackedCardSourcedOrb(__instance)) return;
            __result = ObserveAsync(__result, __instance, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SymbioticVirusStartingOrbPassiveStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        DarkOrb orb,
        bool isSymbioticVirusOrb)
    {
        await inner;
        if (isSymbioticVirusOrb)
            RunTracker.RecordSymbioticVirusStartingOrbPassive(orb);
        RunTracker.RecordCardSourcedOrbPassive(orb);
    }
}

/// <summary>
/// Counts every completed evoke of the tracked Virus Dark orb. Multi-evoke
/// effects deliberately count once per actual Evoke call.
/// </summary>
[HarmonyPatch(typeof(DarkOrb), nameof(DarkOrb.Evoke))]
public static class SymbioticVirusStartingOrbEvokeStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(DarkOrb __instance, out bool __state)
    {
        __state = RunTracker.IsTrackedSymbioticVirusStartingOrb(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        DarkOrb __instance,
        bool __state,
        ref Task<IEnumerable<Creature>> __result)
    {
        try
        {
            if (__result == null) return;
            if (!__state && !RunTracker.IsTrackedCardSourcedOrb(__instance)) return;
            __result = ObserveAsync(__result, __instance, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SymbioticVirusStartingOrbEvokeStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<Creature>> ObserveAsync(
        Task<IEnumerable<Creature>> inner,
        DarkOrb orb,
        bool isSymbioticVirusOrb)
    {
        var targets = await inner;
        if (isSymbioticVirusOrb)
            RunTracker.RecordSymbioticVirusStartingOrbEvoked(orb);
        RunTracker.RecordCardSourcedOrbEvoked(orb);
        return targets;
    }
}
