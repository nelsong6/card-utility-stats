using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Eternal Feather's rest-site heal from the relic's owner-specific
/// room-enter callback. The game heals for Heal * floor(deck count / Cards).
/// </summary>
[HarmonyPatch(typeof(EternalFeather), nameof(EternalFeather.AfterRoomEntered))]
public static class EternalFeatherAfterRoomEnteredPatch
{
    private const string RelicId = "RELIC.ETERNAL_FEATHER";

    [HarmonyPrefix]
    public static void Prefix(EternalFeather __instance, AbstractRoom room)
    {
        try
        {
            if (__instance?.Owner?.Creature == null) return;
            if (room is not RestSiteRoom) return;

            int cardsPerHeal = Math.Max(1, __instance.DynamicVars.Cards.IntValue);
            int deckCards = __instance.Owner.Deck.Cards.Count;
            int healUnits = deckCards / cardsPerHeal;
            decimal attemptedHealing = __instance.DynamicVars.Heal.BaseValue * healUnits;

            RunTracker.RecordEternalFeatherTrigger(
                __instance.Owner.Creature,
                attemptedHealing,
                deckCards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EternalFeatherAfterRoomEnteredPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(EternalFeather __instance, Task __result)
    {
        try
        {
            var healedCreature = __instance.Owner?.Creature;
            if (healedCreature == null) return;

            if (__result == null)
            {
                RunTracker.FinalizeRelicHealing(healedCreature, RelicId);
                return;
            }

            if (__result.IsCompleted)
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
            CoreMain.LogDebug($"EternalFeatherAfterRoomEnteredPatch.Postfix failed: {e.Message}");
        }
    }
}
