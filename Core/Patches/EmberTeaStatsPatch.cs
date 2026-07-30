using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures Ember Tea's own successful charge consumption before the relic's
/// visible counter reaches zero. That combat-local marker is the source of
/// truth for every active-only play, hit, turn, and combat stat.
/// </summary>
[HarmonyPatch(typeof(EmberTea), nameof(EmberTea.AfterRoomEntered))]
public static class EmberTeaAfterRoomEnteredStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        EmberTea __instance,
        AbstractRoom room,
        out ActivationState __state)
    {
        __state = default;

        try
        {
            if (__instance?.Owner == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (room is not CombatRoom || __instance.IsUsedUp || __instance.CombatsLeft <= 0)
                return;

            __state = new ActivationState(__instance, __instance.CombatsLeft);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EmberTeaAfterRoomEnteredStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, ActivationState __state)
    {
        try
        {
            if (__state.Relic == null || __state.CombatsLeftBefore <= 0) return;
            if (__result == null) return;

            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EmberTeaAfterRoomEnteredStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, ActivationState state)
    {
        await inner;

        if (state.Relic != null
            && state.Relic.CombatsLeft < state.CombatsLeftBefore)
        {
            RunTracker.RecordEmberTeaCombatActivated(state.Relic);
        }
    }

    public readonly record struct ActivationState(
        EmberTea? Relic,
        int CombatsLeftBefore);
}

/// <summary>
/// Counts every turn inside a combat where Ember Tea consumed a charge,
/// including active turns with no attack plays or hits.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartEmberTeaStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordEmberTeaActiveTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartEmberTeaStatsPatch failed: {e.Message}");
        }
    }
}
