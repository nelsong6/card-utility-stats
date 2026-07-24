using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// War Hammer upgrades up to four permanent deck cards synchronously after an
/// Elite victory. Keep an attribution window around that exact callback and
/// wrap its returned task so the observed upgrades are committed before the
/// game's later CombatEnded promotion boundary.
/// </summary>
[HarmonyPatch]
public static class WarHammerAfterCombatVictoryStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(WarHammer),
            nameof(WarHammer.AfterCombatVictory),
            new[] { typeof(CombatRoom) });
    }

    [HarmonyPrefix]
    public static void Prefix(
        WarHammer __instance,
        CombatRoom room,
        out ActivationState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            if (RunTracker.BeginWarHammerActivation(__instance, room, out var player))
                __state = new ActivationState(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"WarHammerAfterCombatVictoryStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ActivationState __state, ref Task __result)
    {
        try
        {
            if (__state.Player == null) return;
            __result = CompleteAfter(__result ?? Task.CompletedTask, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"WarHammerAfterCombatVictoryStatsPatch.Postfix failed: {e.Message}");
            Complete(__state, succeeded: false);
        }
    }

    private static async Task CompleteAfter(Task original, ActivationState state)
    {
        var succeeded = false;
        try
        {
            await original;
            succeeded = true;
        }
        finally
        {
            Complete(state, succeeded);
        }
    }

    private static void Complete(ActivationState state, bool succeeded)
    {
        try
        {
            RunTracker.CompleteWarHammerActivation(state.Player, succeeded);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"WarHammerAfterCombatVictoryStatsPatch.Complete failed: {e.Message}");
        }
    }

    public readonly record struct ActivationState(Player? Player);
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartWarHammerStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordWarHammerTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartWarHammerStatsPatch failed: {e.Message}");
        }
    }
}
