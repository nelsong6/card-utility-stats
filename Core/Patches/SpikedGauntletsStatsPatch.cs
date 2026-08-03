using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts Spiked Gauntlets' Ancient max-energy contribution at the actual
/// player energy-reset boundary. The shared tracker dedupes repeated hook
/// queries by relic, player, and combat round.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterEnergyReset))]
public static class HookAfterEnergyResetSpikedGauntletsPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, Player player)
    {
        try
        {
            if (combatState == null || player == null) return;

            var gauntlets = player.Relics?
                .OfType<SpikedGauntlets>()
                .FirstOrDefault();
            if (gauntlets == null) return;

            var amount = gauntlets.DynamicVars?.Energy?.IntValue ?? 0;
            RunTracker.RecordSpikedGauntletsEnergyGenerated(
                combatState,
                player,
                amount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookAfterEnergyResetSpikedGauntletsPatch failed: {e.Message}");
        }
    }
}
