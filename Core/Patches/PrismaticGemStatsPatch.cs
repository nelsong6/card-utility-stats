using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts Prismatic Gem's max-energy benefit at the actual player energy reset
/// boundary. The relic's max-energy modifier can be queried more than once,
/// so RunTracker dedupes this by combat round.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterEnergyReset))]
public static class HookAfterEnergyResetPrismaticGemPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, Player player)
    {
        try
        {
            if (combatState == null || player == null) return;
            if (!player.Relics.Any(r => r is PrismaticGem)) return;

            RunTracker.RecordPrismaticGemEnergyGenerated(combatState, 1);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterEnergyResetPrismaticGemPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts each card reward whose creation options Prismatic Gem modifies.
/// This owner-specific hook is the reward-side counterpart to the max-energy
/// reset observation above.
/// </summary>
[HarmonyPatch(typeof(PrismaticGem), nameof(PrismaticGem.ModifyCardRewardCreationOptions))]
public static class PrismaticGemModifyCardRewardCreationOptionsPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player player, CardCreationOptions options, CardCreationOptions __result)
    {
        try
        {
            if (player == null || options == null || __result == null) return;

            RunTracker.RecordPrismaticGemCardRewardAffected();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PrismaticGemModifyCardRewardCreationOptionsPatch failed: {e.Message}");
        }
    }
}
