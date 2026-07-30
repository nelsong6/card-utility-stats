using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms Lost Wisp attribution from the relic's own qualifying Power-play
/// callback. The activation count is therefore also the Powers-played count.
/// </summary>
[HarmonyPatch(typeof(LostWisp), nameof(LostWisp.AfterCardPlayed))]
public static class LostWispAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(LostWisp __instance, CardPlay cardPlay)
    {
        try
        {
            if (__instance?.Owner == null || cardPlay?.Card == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!ReferenceEquals(cardPlay.Card.Owner, __instance.Owner)) return;
            if (!CombatManager.Instance.IsInProgress) return;
            if (cardPlay.Card.Type != CardType.Power) return;

            var ownerCreature = __instance.Owner.Creature;
            if (ownerCreature == null) return;

            RunTracker.ArmLostWispAttribution(ownerCreature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LostWispAfterCardPlayedPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual multi-target damage split from Lost Wisp's emitted
/// damage command.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(IEnumerable<Creature>),
        typeof(decimal),
        typeof(ValueProp),
        typeof(Creature),
    })]
public static class LostWispCreatureDamagePatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature dealer, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeLostWispDamageAttribution(dealer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LostWispCreatureDamagePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<IEnumerable<DamageResult>> __result)
    {
        if (!__state || __result == null) return;
        ObserveDamageResultAsync(__result);
    }

    private static async void ObserveDamageResultAsync(Task<IEnumerable<DamageResult>> damageTask)
    {
        try
        {
            var results = await damageTask.ConfigureAwait(false);
            RunTracker.RecordLostWispDamage(results);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LostWispCreatureDamagePatch damage observation failed: {e.Message}");
        }
    }
}
