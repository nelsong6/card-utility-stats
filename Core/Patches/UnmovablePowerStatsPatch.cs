using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks the direct extra block produced when Unmovable's power doubles a
/// real card-play block packet. Preview calculations pass no CardPlay, so they
/// are ignored here.
/// </summary>
[HarmonyPatch(typeof(UnmovablePower), nameof(UnmovablePower.ModifyBlockMultiplicative))]
public static class UnmovablePowerModifyBlockMultiplicativePatch
{
    [HarmonyPostfix]
    public static void Postfix(
        Creature target,
        decimal block,
        CardPlay? cardPlay,
        decimal __result)
    {
        try
        {
            if (target == null || block <= 0m || cardPlay == null) return;
            if (__result <= 1m) return;

            RunTracker.RecordUnmovablePowerExtraBlock(target, block * (__result - 1m));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"UnmovablePowerModifyBlockMultiplicativePatch failed: {e.Message}");
        }
    }
}
