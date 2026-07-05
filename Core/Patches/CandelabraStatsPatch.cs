using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Candelabra when its owner-specific turn-2 callback is about to
/// grant energy. The actual energy delta is observed by PlayerCombatState.
/// </summary>
[HarmonyPatch(typeof(Candelabra), nameof(Candelabra.AfterSideTurnStart))]
public static class CandelabraAfterSideTurnStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(Candelabra __instance, CombatSide side, IReadOnlyList<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;

            var owner = __instance.Owner;
            var ownerCreature = owner?.Creature;
            if (owner == null || ownerCreature == null) return;
            if (participants == null || !participants.Contains(ownerCreature)) return;
            if (owner.PlayerCombatState?.TurnNumber != 2) return;

            RunTracker.RecordCandelabraActivationAndArmEnergyAttribution(owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CandelabraAfterSideTurnStartPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts turn-2 player turns that end with unspent energy while Candelabra is
/// held. Bound by runtime lookup so a game hook rename does not break build.
/// </summary>
[HarmonyPatch]
public static class HookBeforeSideTurnEndCandelabraPatch
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
    public static void Prefix(CombatSide side, IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.RecordCandelabraSecondTurnEndedWithExcessEnergy(participants);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookBeforeSideTurnEndCandelabraPatch failed: {e.Message}");
        }
    }
}
