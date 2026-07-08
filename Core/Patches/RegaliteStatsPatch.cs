using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Regalite grants block whenever its owner creates a combat card. Count the
/// created card at the owner callback and let Hook.AfterBlockGained capture the
/// actual block amount after modifiers.
/// </summary>
[HarmonyPatch(typeof(Regalite), nameof(Regalite.AfterCardGeneratedForCombat))]
public static class RegaliteAfterCardGeneratedForCombatPatch
{
    [HarmonyPrefix]
    public static void Prefix(Regalite __instance, CardModel card, Player creator)
    {
        try
        {
            if (__instance?.Owner == null || card == null || creator == null) return;
            if (!ReferenceEquals(__instance.Owner, creator)) return;

            RunTracker.RecordRegaliteCardCreatedAndArmBlockAttribution(__instance, creator);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RegaliteAfterCardGeneratedForCombatPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartRegalitePatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordRegaliteTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartRegalitePatch failed: {e.Message}");
        }
    }
}
