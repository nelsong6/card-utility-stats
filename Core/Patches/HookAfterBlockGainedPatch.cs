using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures player block gains that occur while the Orichalcum attribution
/// window is armed, attributing them to the relic's end-of-turn effect.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockGained))]
public static class HookAfterBlockGainedPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature creature, decimal amount)
    {
        try
        {
            if (creature == null || !creature.IsPlayer) return;
            // Single arbitration: the registry decides which ONE window claims
            // this player block gain (FIFO across armed windows).
            RunTracker.DispatchPlayerBlockGain((int)amount);
            // Card-created Frost orbs arm their own one-shot window through
            // OrbModel.EvokeActivated/PassiveActivated. Close it even when
            // block modifiers reduce the observed gain to zero.
            RunTracker.CompleteFrostOrbBlockAttribution(creature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterBlockGainedPatch failed: {e.Message}");
        }
    }
}
