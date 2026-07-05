using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts Blood-Soaked Rose's max-energy benefit at the actual player energy
/// reset boundary. The paired Enthralled curse is pooled onto the relic tooltip
/// from the card aggregate side.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterEnergyReset))]
public static class HookAfterEnergyResetBloodSoakedRosePatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, Player player)
    {
        try
        {
            if (combatState == null || player == null) return;

            var rose = player.Relics.FirstOrDefault(r => r is BloodSoakedRose);
            if (rose == null) return;

            RunTracker.RecordBloodSoakedRoseEnergyGenerated(
                combatState,
                player,
                rose.Id.ToString(),
                1);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterEnergyResetBloodSoakedRosePatch failed: {e.Message}");
        }
    }
}
