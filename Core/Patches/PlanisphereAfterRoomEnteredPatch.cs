using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Planisphere's owner-specific ?-room heal. The game callback receives
/// the entered room and heals only on event rooms, so the prefix mirrors that
/// gate and arms the standard relic-healing window before the heal resolves.
/// </summary>
[HarmonyPatch(typeof(Planisphere), nameof(Planisphere.AfterRoomEntered))]
public static class PlanisphereAfterRoomEnteredPatch
{
    private const string RelicId = "RELIC.PLANISPHERE";
    private const decimal FallbackHeal = 5m;

    [HarmonyPrefix]
    public static void Prefix(Planisphere __instance, AbstractRoom room)
    {
        try
        {
            if (__instance?.Owner?.Creature == null) return;
            if (room?.RoomType != RoomType.Event) return;

            decimal attemptedHealing = __instance.DynamicVars?.Heal?.BaseValue ?? FallbackHeal;
            RunTracker.RecordPlanisphereTrigger(__instance.Owner.Creature, attemptedHealing);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PlanisphereAfterRoomEnteredPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Planisphere __instance, Task __result)
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
            CoreMain.LogDebug($"PlanisphereAfterRoomEnteredPatch.Postfix failed: {e.Message}");
        }
    }
}
