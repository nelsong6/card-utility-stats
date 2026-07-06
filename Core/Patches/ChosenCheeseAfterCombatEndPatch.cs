using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Chosen Cheese's owner-specific combat-end max-HP gain by observing
/// the owner's actual max HP after the async relic callback finishes.
/// </summary>
[HarmonyPatch(typeof(ChosenCheese), nameof(ChosenCheese.AfterCombatEnd))]
public static class ChosenCheeseAfterCombatEndPatch
{
    [HarmonyPrefix]
    public static void Prefix(ChosenCheese __instance, out MaxHpState __state)
    {
        __state = default;

        try
        {
            var creature = __instance.Owner?.Creature;
            if (creature == null) return;

            __state = new MaxHpState(creature, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ChosenCheeseAfterCombatEndPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Task __result, MaxHpState __state)
    {
        try
        {
            if (__state.Creature == null || __result == null) return;

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
            CoreMain.LogDebug($"ChosenCheeseAfterCombatEndPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Observe(MaxHpState state)
    {
        try
        {
            var creature = state.Creature;
            if (creature == null) return;

            var maxHpGained = creature.MaxHp - state.InitialMaxHp;
            RunTracker.RecordChosenCheeseMaxHpGained(creature, maxHpGained, state.InitialMaxHp, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ChosenCheeseAfterCombatEndPatch.Observe failed: {e.Message}");
        }
    }

    public readonly record struct MaxHpState(Creature? Creature, int InitialMaxHp);
}
