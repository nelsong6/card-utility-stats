using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Debt's owner-specific end-of-turn callback. Debt itself clamps
/// the amount sent to LoseGold to available gold, so capture the intended
/// dynamic-var amount before the callback and the completed balance delta
/// afterwards to preserve both actual loss and the unaffordable remainder.
/// </summary>
[HarmonyPatch]
public static class DebtStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(Debt),
            "OnTurnEndInHand",
            new[] { typeof(PlayerChoiceContext) });
    }

    [HarmonyPrefix]
    public static void Prefix(Debt __instance, out DebtState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            var owner = __instance.Owner;
            if (owner == null) return;

            __state = new DebtState(
                __instance,
                owner,
                Math.Max(0, __instance.DynamicVars.Gold.IntValue),
                owner.Gold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DebtStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, DebtState __state)
    {
        try
        {
            if (__result == null || __state.Card == null || __state.Owner == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DebtStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, DebtState state)
    {
        await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordDebtTrigger(
                state.Card!,
                state.Owner,
                state.IntendedGoldLoss,
                state.InitialGold,
                state.Owner!.Gold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DebtStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public readonly record struct DebtState(
        Debt? Card,
        Player? Owner,
        int IntendedGoldLoss,
        int InitialGold);
}
