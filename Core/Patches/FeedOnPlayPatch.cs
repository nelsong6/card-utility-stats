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
/// Records the maximum HP Feed actually grants. Feed performs its own Fatal
/// check after the damage command and awaits GainMaxHp as the final action in
/// OnPlay, so the completed callback's before/after delta is both observed and
/// attributable to this physical Feed instance.
/// </summary>
[HarmonyPatch]
public static class FeedOnPlayPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(Feed),
            "OnPlay",
            new[] { typeof(PlayerChoiceContext), typeof(CardPlay) });
    }

    [HarmonyPrefix]
    public static void Prefix(Feed __instance, out MaxHpState __state)
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
            CoreMain.LogDebug($"FeedOnPlayPatch.Prefix failed: {e.Message}");
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
            CoreMain.LogDebug($"FeedOnPlayPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, MaxHpState state)
    {
        await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordFeedMaxHpGained(
                state.Card!,
                state.InitialMaxHp,
                state.Creature!.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FeedOnPlayPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public readonly record struct MaxHpState(
        Feed? Card,
        Creature? Creature,
        int InitialMaxHp);
}
