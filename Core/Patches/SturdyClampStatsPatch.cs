using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Sturdy Clamp's exact block-retention boundary. The prefix captures
/// block before the relic applies its 10-block cap; the postfix waits for the
/// async loss to resolve before reading how much block actually remained.
/// </summary>
[HarmonyPatch(typeof(SturdyClamp), nameof(SturdyClamp.AfterPreventingBlockClear))]
public static class SturdyClampStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        SturdyClamp __instance,
        AbstractModel preventer,
        Creature creature,
        out RetentionState __state)
    {
        __state = default;

        try
        {
            if (__instance?.Owner == null || creature == null) return;
            if (!ReferenceEquals(__instance, preventer)) return;
            if (!ReferenceEquals(__instance.Owner.Creature, creature)) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;

            __state = new RetentionState(__instance, creature, creature.Block);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SturdyClampStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(RetentionState __state, Task __result)
    {
        try
        {
            if (__state.Relic == null || __state.Creature == null) return;

            if (__result == null)
            {
                Observe(__state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (__result.IsCompletedSuccessfully)
                    Observe(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (task.IsCompletedSuccessfully)
                        Observe(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SturdyClampStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Observe(RetentionState state)
    {
        try
        {
            var relic = state.Relic;
            var creature = state.Creature;
            if (relic == null || creature == null) return;

            RunTracker.RecordSturdyClampRetention(
                relic,
                creature,
                state.StartingBlock,
                creature.Block);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SturdyClampStatsPatch.Observe failed: {e.Message}");
        }
    }

    public readonly record struct RetentionState(
        SturdyClamp? Relic,
        Creature? Creature,
        int StartingBlock);
}
