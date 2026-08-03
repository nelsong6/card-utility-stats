using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures observed HP deltas after the game applies clamping, prevention, or
/// redirection. Positive deltas feed owner-specific healing attribution; Osty
/// negative deltas feed summon-body absorbed-damage attribution.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCurrentHpChanged))]
public static class HookAfterCurrentHpChangedPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        ICombatState? combatState,
        Creature creature,
        decimal delta)
    {
        try
        {
            if (creature == null || delta == 0m) return;

            if (delta > 0m)
            {
                RunTracker.RecordRelicHealingHpChanged(creature, delta);
            }
            else
            {
                RunTracker.RecordRunHpLost(combatState, creature, -delta);
                RunTracker.RecordWhisperingEarringHpLost(
                    combatState,
                    creature,
                    -delta);
                RunTracker.RecordOstyHpLost(creature, -delta);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterCurrentHpChangedPatch failed: {e.Message}");
        }
    }
}
