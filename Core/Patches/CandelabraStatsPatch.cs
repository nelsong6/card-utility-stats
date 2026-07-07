using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Lantern when its owner-specific round-1 callback is about to grant
/// energy. The actual energy delta is observed by PlayerCombatState.
/// </summary>
[HarmonyPatch(typeof(Lantern), nameof(Lantern.AfterSideTurnStart))]
public static class LanternAfterSideTurnStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(Lantern __instance, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        try
        {
            if (!TurnEnergyRelicPatchHelpers.TryGetTrackedOwnerOnPlayerTurn(__instance, side, participants, out var owner)) return;
            if (combatState?.RoundNumber != 1) return;

            RunTracker.RecordTurnEnergyRelicActivationAndArmEnergyAttribution("RELIC.LANTERN", owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LanternAfterSideTurnStartPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Records Very Hot Cocoa when its owner-specific round-1 callback is about to
/// grant energy. The actual energy delta is observed by PlayerCombatState.
/// </summary>
[HarmonyPatch(typeof(VeryHotCocoa), nameof(VeryHotCocoa.AfterSideTurnStart))]
public static class VeryHotCocoaAfterSideTurnStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        VeryHotCocoa __instance,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        try
        {
            if (!TurnEnergyRelicPatchHelpers.TryGetTrackedOwnerOnPlayerTurn(__instance, side, participants, out var owner)) return;
            if (combatState?.RoundNumber != 1) return;

            RunTracker.RecordTurnEnergyRelicActivationAndArmEnergyAttribution("RELIC.VERY_HOT_COCOA", owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"VeryHotCocoaAfterSideTurnStartPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Records Candelabra when its owner-specific round-2 callback is about to
/// grant energy. The actual energy delta is observed by PlayerCombatState.
/// </summary>
[HarmonyPatch(typeof(Candelabra), nameof(Candelabra.AfterSideTurnStart))]
public static class CandelabraAfterSideTurnStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(Candelabra __instance, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        try
        {
            if (!TurnEnergyRelicPatchHelpers.TryGetTrackedOwnerOnPlayerTurn(__instance, side, participants, out var owner)) return;
            if (combatState?.RoundNumber != 2) return;

            RunTracker.RecordTurnEnergyRelicActivationAndArmEnergyAttribution("RELIC.CANDELABRA", owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CandelabraAfterSideTurnStartPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Records Chandelier when its owner-specific round-3 callback is about to
/// grant energy. The actual energy delta is observed by PlayerCombatState.
/// </summary>
[HarmonyPatch(typeof(Chandelier), nameof(Chandelier.AfterSideTurnStart))]
public static class ChandelierAfterSideTurnStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(Chandelier __instance, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        try
        {
            if (!TurnEnergyRelicPatchHelpers.TryGetTrackedOwnerOnPlayerTurn(__instance, side, participants, out var owner)) return;
            if (combatState?.RoundNumber != 3) return;

            RunTracker.RecordTurnEnergyRelicActivationAndArmEnergyAttribution("RELIC.CHANDELIER", owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ChandelierAfterSideTurnStartPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts player rounds that end with unspent energy while the matching
/// Lantern/Very Hot Cocoa/Candelabra/Chandelier turn-energy relic is held.
/// Bound by runtime lookup so a game hook rename does not break build.
/// </summary>
[HarmonyPatch]
public static class HookBeforeSideTurnEndTurnEnergyRelicsPatch
{
    private static MethodBase? TargetMethod()
    {
        var hookType = Sts2CoreAssembly()?.GetType("MegaCrit.Sts2.Core.Hooks.Hook", throwOnError: false);
        if (hookType == null) return null;

        return AccessTools.Method(hookType, "BeforeSideTurnEnd")
            ?? AccessTools.Method(hookType, "BeforeTurnEnd");
    }

    private static Assembly? Sts2CoreAssembly()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "sts2") return assembly;
        }

        return null;
    }

    private static bool Prepare() => TargetMethod() != null;

    [HarmonyPrefix]
    public static void Prefix(ICombatState combatState, CombatSide side, IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.RecordTurnEnergyRelicTurnEndedWithExcessEnergy(combatState, participants);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookBeforeSideTurnEndTurnEnergyRelicsPatch failed: {e.Message}");
        }
    }
}

internal static class TurnEnergyRelicPatchHelpers
{
    public static bool TryGetTrackedOwnerOnPlayerTurn(
        RelicModel relic,
        CombatSide side,
        IReadOnlyList<Creature>? participants,
        out MegaCrit.Sts2.Core.Entities.Players.Player owner)
    {
        owner = null!;
        if (side != CombatSide.Player) return false;
        if (relic == null || !RunTracker.IsTrackedRelic(relic)) return false;

        var relicOwner = relic.Owner;
        var ownerCreature = relicOwner?.Creature;
        if (relicOwner == null || ownerCreature == null) return false;
        if (participants == null || !participants.Contains(ownerCreature)) return false;

        owner = relicOwner;
        return true;
    }
}
