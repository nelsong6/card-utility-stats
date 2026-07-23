using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Gremlin Horn from its owner-specific enemy-death callback. The game
/// ignores same-side deaths, then grants energy and draws cards from this method.
/// </summary>
[HarmonyPatch(typeof(GremlinHorn), nameof(GremlinHorn.AfterDeath))]
public static class GremlinHornAfterDeathPatch
{
    [HarmonyPrefix]
    public static void Prefix(GremlinHorn __instance, Creature target)
    {
        try
        {
            if (__instance?.Owner?.Creature == null || target == null) return;
            if (target.Side == __instance.Owner.Creature.Side) return;
            // AfterDeath still reaches Gremlin Horn for the combat-ending kill,
            // but its native energy and draw commands both no-op once combat is
            // over or ending. Do not count that callback as an activation.
            if (CombatManager.Instance.IsOverOrEnding) return;

            RunTracker.ArmGremlinHornAttribution(__instance.Owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GremlinHornAfterDeathPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual number of cards drawn by Gremlin Horn's draw command.
/// The relic calls the four-argument overload with its owner, count, and
/// <c>fromHandDraw=false</c>.
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
public static class GremlinHornCardPileDrawPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeGremlinHornDrawAttribution(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GremlinHornCardPileDrawPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<IEnumerable<CardModel>> __result)
    {
        if (!__state || __result == null) return;
        ObserveDrawResultAsync(__result);
    }

    private static async void ObserveDrawResultAsync(Task<IEnumerable<CardModel>> drawTask)
    {
        try
        {
            var cards = await drawTask.ConfigureAwait(false);
            int cardsDrawn = cards?.Count(card => card != null) ?? 0;
            RunTracker.RecordGremlinHornCardsDrawn(cardsDrawn);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GremlinHornCardPileDrawPatch draw observation failed: {e.Message}");
        }
    }
}
