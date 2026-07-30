using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Rupture grants Strength immediately for qualifying damage outside the
/// currently resolving owner card. Compare the owner's Strength across that
/// exact callback so modifiers are included and non-trigger callbacks remain
/// observed zeroes.
/// </summary>
[HarmonyPatch(
    typeof(RupturePower),
    nameof(RupturePower.AfterDamageReceived),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Creature),
        typeof(DamageResult),
        typeof(ValueProp),
        typeof(Creature),
        typeof(CardModel),
    })]
internal static class RupturePowerAfterDamageReceivedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        RupturePower __instance,
        out PendingRuptureStrengthObservation? __state)
    {
        __state = RunTracker.BeginRuptureStrengthObservation(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingRuptureStrengthObservation? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"RupturePowerAfterDamageReceivedStatsPatch failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingRuptureStrengthObservation observation)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.CompleteRuptureStrengthObservation(observation);
        }
    }
}

/// <summary>
/// Damage caused during an owner card play is accumulated by Rupture and
/// granted once from AfterCardPlayed. Observe that separate payoff boundary
/// so multi-hit cards count their final combined Strength exactly once.
/// </summary>
[HarmonyPatch(
    typeof(RupturePower),
    nameof(RupturePower.AfterCardPlayed),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(CardPlay),
    })]
internal static class RupturePowerAfterCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        RupturePower __instance,
        out PendingRuptureStrengthObservation? __state)
    {
        __state = RunTracker.BeginRuptureStrengthObservation(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        PendingRuptureStrengthObservation? __state,
        ref Task __result)
    {
        try
        {
            if (__state == null || __result == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"RupturePowerAfterCardPlayedStatsPatch failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        Task inner,
        PendingRuptureStrengthObservation observation)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            RunTracker.CompleteRuptureStrengthObservation(observation);
        }
    }
}

/// <summary>
/// Counts later zero-trigger turns while Rupture remains active. The
/// application turn is counted from the observed PowerReceived entry.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartRuptureStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        RunTracker.RecordRupturePowerTurnStarted(player);
    }
}
