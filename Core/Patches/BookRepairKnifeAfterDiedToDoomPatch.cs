using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using System.Threading.Tasks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Book Repair Knife's confirmed trigger and Doom-death payload. The
/// relic receives the game's post-Doom-death creature list, which is the narrow
/// owner-specific outcome we want for the tooltip.
/// </summary>
[HarmonyPatch]
public static class BookRepairKnifeAfterDiedToDoomPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BookRepairKnife");
        return t == null ? null : AccessTools.Method(t, "AfterDiedToDoom");
    }

    [HarmonyPrefix]
    public static void Prefix(BookRepairKnife __instance, PlayerChoiceContext choiceContext, IReadOnlyList<Creature> creatures)
    {
        try
        {
            _ = choiceContext;
            if (creatures == null || creatures.Count == 0) return;

            int killCount = creatures.Count(c => c != null && !c.IsPlayer);
            if (killCount <= 0) return;

            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null) return;

            decimal healPerKill = __instance.DynamicVars?.Heal?.BaseValue ?? 0m;
            RunTracker.RecordBookRepairKnifeTrigger(killCount, healedCreature, healPerKill * killCount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BookRepairKnifeAfterDiedToDoomPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(BookRepairKnife __instance, Task __result)
    {
        try
        {
            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null) return;

            if (__result == null)
            {
                RunTracker.FinalizeRelicHealing(healedCreature, "RELIC.BOOK_REPAIR_KNIFE");
                return;
            }

            if (__result.IsCompleted)
            {
                RunTracker.FinalizeRelicHealing(healedCreature, "RELIC.BOOK_REPAIR_KNIFE");
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.FinalizeRelicHealing(healedCreature, "RELIC.BOOK_REPAIR_KNIFE"),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BookRepairKnifeAfterDiedToDoomPatch.Postfix failed: {e.Message}");
        }
    }
}
