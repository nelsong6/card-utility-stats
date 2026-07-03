using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures damage the game suppresses from combat history on combat-ending
/// killing blows.
///
/// <c>CreatureCmd.Damage</c> applies HP loss and only then emits the
/// combat-history entry behind an <c>IsInProgress &amp;&amp; !IsEnding</c>
/// gate — the hit that kills the last living enemy flips <c>IsEnding</c> and
/// suppresses its own <c>DamageReceivedEntry</c>, so the master
/// <see cref="CombatHistoryAddPatch"/> hook never sees it.
///
/// <c>Hook.AfterDamageGiven</c> still fires for that hit — the game dispatches
/// it directly, documented as "it fires from the same damage event that ends
/// combat (the killing hit), so it must still resolve for that hit" — carrying
/// the same result/dealer/cardSource the entry would have carried, and it runs
/// BEFORE <c>Kill()</c> triggers CombatEnded, so the recorded damage still
/// lands in the pending combat and is promoted normally.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
public static class HookAfterDamageGivenPatch
{
    // Prefix (not postfix): the hook is async, and we only need its arguments;
    // a prefix runs synchronously at dispatch time, before any listener model
    // can react to the hit.
    [HarmonyPrefix]
    public static void Prefix(ICombatState combatState, Creature? dealer, DamageResult results, CardModel? cardSource)
    {
        try
        {
            RunTracker.RecordCombatEndingSuppressedDamage(combatState, dealer, results, cardSource);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterDamageGivenPatch failed: {e.Message}");
        }
    }
}
