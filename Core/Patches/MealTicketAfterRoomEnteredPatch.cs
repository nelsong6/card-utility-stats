using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Meal Ticket's owner-specific shop-entry heal. The relic callback is
/// already gated by room type in game code; the prefix mirrors that gate so
/// only real activations arm the healing attribution window.
/// </summary>
[HarmonyPatch]
public static class MealTicketAfterRoomEnteredPatch
{
    private const string RelicId = "RELIC.MEAL_TICKET";

    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.MealTicket");
        return t == null ? null : AccessTools.Method(t, "AfterRoomEntered");
    }

    [HarmonyPrefix]
    public static void Prefix(MealTicket __instance, AbstractRoom room)
    {
        try
        {
            if (room is not MerchantRoom) return;

            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null || healedCreature.IsDead) return;

            decimal attemptedHealing = __instance.DynamicVars?.Heal?.BaseValue ?? 0m;
            RunTracker.RecordMealTicketTrigger(healedCreature, attemptedHealing);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MealTicketAfterRoomEnteredPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(MealTicket __instance, Task __result)
    {
        try
        {
            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null) return;

            if (__result == null || __result.IsCompleted)
            {
                RunTracker.FinalizeRelicHealing(healedCreature, RelicId);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.FinalizeRelicHealing(healedCreature, RelicId),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MealTicketAfterRoomEnteredPatch.Postfix failed: {e.Message}");
        }
    }
}
