using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures actual HP restored for owner-specific healing attribution windows.
/// Attempted healing is recorded at the owner callback; this hook records what
/// the game actually applied after max-HP clamping or prevention.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCurrentHpChanged))]
public static class HookAfterCurrentHpChangedPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature creature, decimal delta)
    {
        try
        {
            if (creature == null || delta <= 0m) return;
            RunTracker.RecordRelicHealingHpChanged(creature, delta);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterCurrentHpChangedPatch failed: {e.Message}");
        }
    }
}
