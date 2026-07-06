using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Strawberry's owner-specific max-HP gain by observing the owner's
/// actual max HP after the async pickup effect finishes.
/// </summary>
[HarmonyPatch(typeof(Strawberry), nameof(Strawberry.AfterObtained))]
public static class StrawberryAfterObtainedPatch
{
    [HarmonyPrefix]
    public static void Prefix(Strawberry __instance, out MaxHpState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;

            var creature = __instance.Owner?.Creature;
            if (creature == null || creature.IsDead) return;

            __state = new MaxHpState(creature, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StrawberryAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(MaxHpState __state, Task __result)
    {
        try
        {
            if (__state.Creature == null) return;

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
            CoreMain.LogDebug($"StrawberryAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Observe(MaxHpState state)
    {
        try
        {
            var creature = state.Creature;
            if (creature == null) return;

            var maxHpGained = creature.MaxHp - state.InitialMaxHp;
            RunTracker.RecordStrawberryMaxHpGained(creature, maxHpGained, state.InitialMaxHp, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StrawberryAfterObtainedPatch.Observe failed: {e.Message}");
        }
    }

    public readonly record struct MaxHpState(Creature? Creature, decimal InitialMaxHp);
}
