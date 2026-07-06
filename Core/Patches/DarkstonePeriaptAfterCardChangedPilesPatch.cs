using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Darkstone Periapt at the same owner-specific pile-change callback
/// that grants max HP. The prefix mirrors the relic's condition; the postfix
/// records the actual max-HP delta after the async GainMaxHp command resolves.
/// </summary>
[HarmonyPatch(typeof(DarkstonePeriapt), nameof(DarkstonePeriapt.AfterCardChangedPiles))]
public static class DarkstonePeriaptAfterCardChangedPilesPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        DarkstonePeriapt __instance,
        CardModel card,
        PileType oldPileType,
        AbstractModel? clonedBy,
        out DarkstoneState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;
            if (card == null) return;
            if (card.Pile?.Type != PileType.Deck) return;
            if (card.Owner != __instance.Owner) return;
            if (card.Type != CardType.Curse) return;

            var creature = __instance.Owner?.Creature;
            if (creature == null) return;

            __state = new DarkstoneState(creature, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DarkstonePeriaptAfterCardChangedPilesPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(DarkstoneState __state, Task __result)
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
                if (!__result.IsCanceled && !__result.IsFaulted)
                    Observe(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (!task.IsCanceled && !task.IsFaulted)
                        Observe(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DarkstonePeriaptAfterCardChangedPilesPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Observe(DarkstoneState state)
    {
        try
        {
            if (state.Creature == null) return;

            int maxHpGained = Math.Max(0, state.Creature.MaxHp - state.InitialMaxHp);
            RunTracker.RecordDarkstonePeriaptCurseAcquired(maxHpGained, state.InitialMaxHp, state.Creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DarkstonePeriaptAfterCardChangedPilesPatch.Observe failed: {e.Message}");
        }
    }

    public readonly record struct DarkstoneState(Creature? Creature, int InitialMaxHp);
}
