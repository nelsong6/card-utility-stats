using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts every distinct held player turn for Ripple Basin's per-turn average,
/// including turns where an Attack prevents its turn-end block.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartRippleBasinPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordRippleBasinTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartRippleBasinPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Records Ripple Basin when its owner-specific no-Attack turn-end callback
/// is about to grant block. The block amount itself is observed later by
/// Hook.AfterBlockGained, after the game applies block modifiers.
/// </summary>
[HarmonyPatch(typeof(RippleBasin), nameof(RippleBasin.BeforeSideTurnEnd))]
public static class RippleBasinBeforeSideTurnEndPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        RippleBasin __instance,
        CombatSide side,
        IEnumerable<Creature> participants,
        out bool __state)
    {
        __state = false;

        try
        {
            if (!ShouldArm(__instance, side, participants)) return;
            RunTracker.RecordRippleBasinActivationAndArmBlockAttribution();
            __state = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RippleBasinBeforeSideTurnEndPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task __result)
    {
        try
        {
            if (!__state) return;
            if (__result == null)
            {
                RunTracker.DisarmRippleBasinBlockAttribution();
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmRippleBasinBlockAttribution(),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RippleBasinBeforeSideTurnEndPatch.Postfix failed: {e.Message}");
        }
    }

    private static bool ShouldArm(RippleBasin relic, CombatSide side, IEnumerable<Creature>? participants)
    {
        if (relic?.Owner?.Creature == null) return false;
        if (!RunTracker.IsTrackedRelic(relic)) return false;
        if (!CombatManager.Instance.IsInProgress) return false;
        if (side != CombatSide.Player) return false;
        if (participants == null || !participants.Contains(relic.Owner.Creature)) return false;

        var combatState = relic.Owner.Creature.CombatState;
        var finishedPlays = CombatManager.Instance?.History?.CardPlaysFinished;
        if (finishedPlays == null) return false;

        return !finishedPlays.Any(e =>
            e.CardPlay?.Card != null
            && e.HappenedThisTurn(combatState)
            && e.CardPlay.Card.Type == CardType.Attack
            && ReferenceEquals(e.CardPlay.Card.Owner, relic.Owner));
    }
}
