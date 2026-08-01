using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Screaming Flagon when its owner ends a turn with an empty hand and
/// the relic is about to run its damage command.
/// </summary>
[HarmonyPatch(typeof(ScreamingFlagon), nameof(ScreamingFlagon.BeforeSideTurnEnd))]
public static class ScreamingFlagonBeforeSideTurnEndPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        ScreamingFlagon __instance,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        try
        {
            if (__instance?.Owner == null || participants == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (side != CombatSide.Player) return;

            var ownerCreature = __instance.Owner.Creature;
            if (ownerCreature == null) return;
            if (!participants.Contains(ownerCreature)) return;

            var hand = PileType.Hand.GetPile(__instance.Owner);
            RunTracker.RecordScreamingFlagonTurnEnded(
                __instance.Owner,
                hand.Cards.Count);
            if (!hand.IsEmpty) return;

            RunTracker.ArmScreamingFlagonAttribution(ownerCreature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ScreamingFlagonBeforeSideTurnEndPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual damage split from Screaming Flagon's emitted damage.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[] { typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(DamageVar), typeof(Creature) })]
public static class ScreamingFlagonCreatureDamagePatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature dealer, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeScreamingFlagonDamageAttribution(dealer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ScreamingFlagonCreatureDamagePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!__state || __result == null) return;
        __result = ObserveDamageResultAsync(__result);
    }

    private static async Task<IEnumerable<DamageResult>> ObserveDamageResultAsync(
        Task<IEnumerable<DamageResult>> damageTask)
    {
        try
        {
            var results = await damageTask.ConfigureAwait(false);
            RunTracker.RecordScreamingFlagonDamage(results);
            return results;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ScreamingFlagonCreatureDamagePatch damage observation failed: {e.Message}");
            throw;
        }
    }
}
