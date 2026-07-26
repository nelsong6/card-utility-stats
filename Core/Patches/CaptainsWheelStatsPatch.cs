using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Captain's Wheel when its owner-specific block-clear callback is
/// about to fire on the player's third combat turn.
/// </summary>
[HarmonyPatch(typeof(CaptainsWheel), nameof(CaptainsWheel.AfterBlockCleared))]
public static class CaptainsWheelAfterBlockClearedPatch
{
    [HarmonyPrefix]
    public static void Prefix(CaptainsWheel __instance, Creature creature)
    {
        try
        {
            var ownerCreature = __instance?.Owner?.Creature;
            if (ownerCreature == null || creature == null) return;
            if (!ReferenceEquals(creature, ownerCreature)) return;
            if (__instance!.Owner.PlayerCombatState?.TurnNumber != 3) return;

            RunTracker.ArmCaptainsWheelAttribution(ownerCreature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CaptainsWheelAfterBlockClearedPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual block gained by Captain's Wheel's gain-block command.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.GainBlock),
    new[] { typeof(Creature), typeof(BlockVar), typeof(CardPlay), typeof(bool) })]
public static class CaptainsWheelCreatureGainBlockPatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature creature, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeCaptainsWheelBlockAttribution(creature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CaptainsWheelCreatureGainBlockPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<decimal> __result)
    {
        if (!__state || __result == null) return;
        ObserveBlockResultAsync(__result);
    }

    private static async void ObserveBlockResultAsync(Task<decimal> blockTask)
    {
        try
        {
            decimal gained = await blockTask.ConfigureAwait(false);
            RunTracker.RecordCaptainsWheelBlockGained(gained);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CaptainsWheelCreatureGainBlockPatch block observation failed: {e.Message}");
        }
    }
}
