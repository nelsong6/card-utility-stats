using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts Pumpkin Candle's Ancient max-energy contribution at the actual
/// player energy-reset boundary. KindleCount is the relic's authoritative
/// live gate; the dynamic Energy value is the same value its max-energy
/// modifier adds while at least one charge remains.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterEnergyReset))]
public static class HookAfterEnergyResetPumpkinCandlePatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, Player player)
    {
        try
        {
            if (combatState == null || player == null) return;

            var candle = player.Relics?
                .OfType<PumpkinCandle>()
                .FirstOrDefault();
            if (candle == null || candle.KindleCount <= 0) return;

            var amount = candle.DynamicVars?.Energy?.IntValue ?? 0;
            RunTracker.RecordPumpkinCandleEnergyGenerated(
                combatState,
                player,
                amount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookAfterEnergyResetPumpkinCandlePatch failed: {e.Message}");
        }
    }
}
