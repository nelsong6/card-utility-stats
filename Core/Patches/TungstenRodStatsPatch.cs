using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Measures only Tungsten Rod's own contribution to an HP-loss event. The
/// modifier receives the dealer and card source before reducing the amount,
/// so classification does not need to reconstruct the event from history.
/// </summary>
[HarmonyPatch(typeof(TungstenRod), nameof(TungstenRod.ModifyHpLostAfterOsty))]
public static class TungstenRodModifyHpLostAfterOstyStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        TungstenRod __instance,
        Creature target,
        decimal amount,
        Creature? dealer,
        CardModel? cardSource,
        decimal __result)
    {
        try
        {
            RunTracker.RecordTungstenRodDamagePrevented(
                __instance,
                target,
                amount,
                __result,
                dealer,
                cardSource);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"TungstenRodModifyHpLostAfterOstyStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts every player turn where Tungsten Rod is held, including turns where
/// it prevents no HP loss.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class TungstenRodAfterPlayerTurnStartStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordTungstenRodTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"TungstenRodAfterPlayerTurnStartStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// HP-loss callbacks receive no PowerModel source. These are the current
/// player-targeting damage callbacks owned by Buff or Debuff powers. A shared
/// async-local frame preserves the exact power through awaited damage calls,
/// allowing relic trackers to distinguish Buff and Debuff HP-loss sources even
/// when the game passes the player or null as dealer.
/// </summary>
[HarmonyPatch]
public static class HpLossPowerDamageSourcePatch
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.Models.Powers.ConstrictPower", "AfterSideTurnEnd"),
        ("MegaCrit.Sts2.Core.Models.Powers.CrimsonMantlePower", "AfterPlayerTurnStart"),
        ("MegaCrit.Sts2.Core.Models.Powers.DemisePower", "AfterSideTurnEnd"),
        ("MegaCrit.Sts2.Core.Models.Powers.DisintegrationPower", "AfterSideTurnEndLate"),
        ("MegaCrit.Sts2.Core.Models.Powers.InfernoPower", "AfterPlayerTurnStart"),
        ("MegaCrit.Sts2.Core.Models.Powers.MagicBombPower", "AfterSideTurnEnd"),
        ("MegaCrit.Sts2.Core.Models.Powers.PoisonPower", "AfterSideTurnStart"),
        ("MegaCrit.Sts2.Core.Models.Powers.StranglePower", "AfterCardPlayed"),
    ];

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var (typeName, methodName) in Targets)
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null
                ? null
                : AccessTools.DeclaredMethod(type, methodName);
            if (method != null)
                yield return method;
        }
    }

    [HarmonyPrefix]
    public static void Prefix(PowerModel __instance, out object? __state)
    {
        __state = null;
        try
        {
            __state = RunTracker.PushHpLossPowerDamageSource(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HpLossPowerDamageSourcePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(object? __state)
    {
        try
        {
            RunTracker.RestoreHpLossPowerDamageSource(__state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HpLossPowerDamageSourcePatch.Postfix failed: {e.Message}");
        }
    }
}
