using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks Centennial Puzzle's actual cards drawn after its once-per-combat HP-loss trigger.
/// </summary>
[HarmonyPatch]
public static class CentennialPuzzleAfterDamageReceivedPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(CentennialPuzzle),
            nameof(CentennialPuzzle.AfterDamageReceived),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(Creature),
                typeof(DamageResult),
                AccessTools.TypeByName("MegaCrit.Sts2.Core.ValueProps.ValueProp")!,
                typeof(Creature),
                typeof(CardModel),
            });
    }

    [HarmonyPrefix]
    public static void Prefix(CentennialPuzzle __instance, Creature target, DamageResult result)
    {
        try
        {
            if (__instance?.Owner?.Creature == null || target == null || result == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (__instance.UsedThisCombat) return;
            if (!ReferenceEquals(target, __instance.Owner.Creature)) return;
            if (result.UnblockedDamage <= 0) return;

            RunTracker.ArmCentennialPuzzleAttribution(
                __instance.Owner,
                GetExpectedCardsToDraw(__instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CentennialPuzzleAfterDamageReceivedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CentennialPuzzle __instance, Task __result)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null) return;

            if (__result == null || __result.IsCompleted)
            {
                RunTracker.DisarmCentennialPuzzleAttribution(owner);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmCentennialPuzzleAttribution(owner),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CentennialPuzzleAfterDamageReceivedPatch.Postfix failed: {e.Message}");
        }
    }

    private static int GetExpectedCardsToDraw(CentennialPuzzle relic)
    {
        try
        {
            return Math.Max(0, relic.DynamicVars["Cards"].IntValue);
        }
        catch
        {
            return 3;
        }
    }
}

/// <summary>
/// Centennial Puzzle draws one card at a time, so count each resolved single-card draw.
/// </summary>
[HarmonyPatch]
public static class CentennialPuzzleCardPileDrawPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(CardPileCmd),
            nameof(CardPileCmd.Draw),
            new[] { typeof(PlayerChoiceContext), typeof(Player) });
    }

    [HarmonyPrefix]
    public static void Prefix(Player player, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeCentennialPuzzleDrawAttribution(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CentennialPuzzleCardPileDrawPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<CardModel> __result)
    {
        if (!__state || __result == null) return;
        ObserveDrawResultAsync(__result);
    }

    private static async void ObserveDrawResultAsync(Task<CardModel> drawTask)
    {
        try
        {
            var card = await drawTask.ConfigureAwait(false);
            if (card != null)
                RunTracker.RecordCentennialPuzzleCardsDrawn(1);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CentennialPuzzleCardPileDrawPatch draw observation failed: {e.Message}");
        }
    }
}
