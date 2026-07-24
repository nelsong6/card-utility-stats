using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Supplies Drain Power's zero-inclusive turn denominator. Upgrade attribution
/// itself uses the existing CardModel.UpgradeInternal observation while Drain
/// Power is the currently resolving card, and later finished card plays consume
/// the combat-local raw-card ledger maintained by RunTracker.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartDrainPowerStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordDrainPowerTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartDrainPowerStatsPatch failed: {e.Message}");
        }
    }
}
