using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Rainbow Ring's own completed activation. Its callback updates the
/// three card-type counters, applies Strength and Dexterity, and only then
/// increments its activation counter, so a positive post-await counter delta
/// is the authoritative successful-trigger signal.
/// </summary>
[HarmonyPatch(
    typeof(RainbowRing),
    nameof(RainbowRing.AfterCardPlayed),
    new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
internal static class RainbowRingAfterCardPlayedStatsPatch
{
    private static readonly FieldInfo? ActivationCountThisTurnField =
        AccessTools.Field(typeof(RainbowRing), "_activationCountThisTurn");

    [HarmonyPrefix]
    public static void Prefix(
        RainbowRing __instance,
        CardPlay cardPlay,
        out int __state)
    {
        __state = -1;

        try
        {
            if (__instance?.Owner == null || cardPlay?.Card == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!ReferenceEquals(cardPlay.Card.Owner, __instance.Owner)) return;
            if (CombatManager.Instance?.IsInProgress != true) return;

            __state = ReadActivationCount(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RainbowRingAfterCardPlayedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        RainbowRing __instance,
        int __state,
        ref Task __result)
    {
        try
        {
            if (__state < 0 || __result == null) return;
            __result = ObserveAsync(__result, __instance, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RainbowRingAfterCardPlayedStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        RainbowRing relic,
        int activationCountBefore)
    {
        await inner;

        try
        {
            var activationDelta = ReadActivationCount(relic) - activationCountBefore;
            if (activationDelta > 0)
                RunTracker.RecordRainbowRingActivation(relic, activationDelta);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RainbowRingAfterCardPlayedStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    private static int ReadActivationCount(RainbowRing relic)
        => ActivationCountThisTurnField?.GetValue(relic) is int count ? count : -1;
}

/// <summary>
/// Counts every player turn where Rainbow Ring is held, including turns when
/// the player does not complete its Attack/Skill/Power set.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartRainbowRingStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordRainbowRingTurnStarted(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartRainbowRingStatsPatch failed: {e.Message}");
        }
    }
}
