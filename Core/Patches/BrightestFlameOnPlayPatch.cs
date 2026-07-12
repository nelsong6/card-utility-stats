using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records the maximum HP Brightest Flame actually removes. Its LoseMaxHp
/// command is the final awaited action in OnPlay, so wrapping that exact
/// callback's returned task gives us the completed before/after delta while
/// preserving the card instance that caused it.
/// </summary>
[HarmonyPatch]
public static class BrightestFlameOnPlayPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(BrightestFlame),
            "OnPlay",
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) });
    }

    [HarmonyPrefix]
    public static void Prefix(BrightestFlame __instance, out MaxHpState __state)
    {
        __state = default;

        try
        {
            var creature = __instance?.Owner?.Creature;
            if (creature == null) return;
            __state = new MaxHpState(__instance, creature, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BrightestFlameOnPlayPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, MaxHpState __state)
    {
        try
        {
            if (__result == null || __state.Card == null || __state.Creature == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BrightestFlameOnPlayPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, MaxHpState state)
    {
        await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordBrightestFlameMaxHpLost(
                state.Card!,
                state.InitialMaxHp,
                state.Creature!.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BrightestFlameOnPlayPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public readonly record struct MaxHpState(
        BrightestFlame? Card,
        Creature? Creature,
        int InitialMaxHp);
}
