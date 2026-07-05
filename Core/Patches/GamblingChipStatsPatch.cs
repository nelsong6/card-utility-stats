using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks the actual cards Gambling Chip discards from its combat-start prompt.
/// </summary>
[HarmonyPatch]
public static class GamblingChipAfterPlayerTurnStartPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(GamblingChip),
            nameof(GamblingChip.AfterPlayerTurnStart),
            new[] { typeof(PlayerChoiceContext), typeof(Player) });
    }

    [HarmonyPrefix]
    public static void Prefix(GamblingChip __instance, PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (__instance?.Owner == null || player == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!ReferenceEquals(player, __instance.Owner)) return;
            if (player.PlayerCombatState?.TurnNumber != 1) return;

            RunTracker.ArmGamblingChipDiscardAttribution(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GamblingChipAfterPlayerTurnStartPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(GamblingChip __instance, PlayerChoiceContext choiceContext, Player player, Task __result)
    {
        try
        {
            if (player == null) return;

            if (__result == null || __result.IsCompleted)
            {
                RunTracker.DisarmGamblingChipDiscardAttribution(player);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmGamblingChipDiscardAttribution(player),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GamblingChipAfterPlayerTurnStartPatch.Postfix failed: {e.Message}");
        }
    }
}
