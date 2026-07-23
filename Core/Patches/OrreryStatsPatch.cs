using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Orrery constructs all five rewards synchronously before awaiting its custom
/// rewards page. Keep a narrow registration window so the generic
/// RewardsCmd.OfferCustom hook only binds this relic's exact reward objects.
/// </summary>
[HarmonyPatch(typeof(Orrery), nameof(Orrery.AfterObtained))]
public static class OrreryAfterObtainedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Orrery __instance, out bool __state)
    {
        __state = RunTracker.BeginOrreryRewardRegistration(__instance);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state)
            RunTracker.EndOrreryRewardRegistration();

        return __exception;
    }
}

[HarmonyPatch]
public static class RewardsCmdOrreryOfferCustomPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(RewardsCmd),
            nameof(RewardsCmd.OfferCustom),
            new[] { typeof(Player), typeof(List<Reward>) });

    [HarmonyPrefix]
    public static void Prefix(Player player, List<Reward> rewards)
    {
        try
        {
            RunTracker.RegisterOrreryCustomRewards(player, rewards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RewardsCmdOrreryOfferCustomPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Save each Orrery reward's final generated signature. A Driftwood reroll
/// refreshes the signature without changing the reward number.
/// </summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
public static class CardRewardOrreryPopulatePatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RefreshOrreryRewardOptions(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardOrreryPopulatePatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(CardReward), nameof(CardReward.Reroll))]
public static class CardRewardOrreryRerollPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RefreshOrreryRewardOptions(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardOrreryRerollPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Wrap terminal non-card alternatives for Orrery rewards. This records the
/// chosen alternative only after its action succeeds; Pael's SACRIFICE gets a
/// dedicated user-facing label while future alternatives remain identifiable.
/// </summary>
[HarmonyPatch(typeof(CardRewardAlternative), nameof(CardRewardAlternative.Generate))]
public static class CardRewardAlternativeOrreryGeneratePatch
{
    private static readonly FieldInfo? OnSelectBackingField =
        AccessTools.Field(typeof(CardRewardAlternative), "<OnSelect>k__BackingField");

    [HarmonyPostfix]
    public static void Postfix(
        CardReward cardReward,
        IReadOnlyList<CardRewardAlternative> __result)
    {
        try
        {
            if (!RunTracker.IsTrackedOrreryReward(cardReward)
                || __result == null
                || OnSelectBackingField == null)
                return;

            foreach (var alternative in __result)
            {
                if (alternative == null
                    || alternative.AfterSelected
                    != PostAlternateCardRewardAction.EndSelectionAndCompleteReward)
                    continue;

                var original = alternative.OnSelect;
                if (original == null) continue;

                async Task WrappedOnSelect()
                {
                    await original();
                    RunTracker.RecordOrreryRewardAlternative(cardReward, alternative.OptionId);
                }

                OnSelectBackingField.SetValue(alternative, (Func<Task>)WrappedOnSelect);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardAlternativeOrreryGeneratePatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch]
public static class CardRewardOrreryOnSelectPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(CardReward), "OnSelect");

    [HarmonyPrefix]
    public static void Prefix(CardReward __instance)
    {
        try
        {
            RunTracker.NoteOrreryRewardOpened(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardOrreryOnSelectPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CardReward __instance, Task<bool> __result)
    {
        if (__instance == null || __result == null) return;
        ObserveResolutionAsync(__instance, __result);
    }

    private static async void ObserveResolutionAsync(CardReward reward, Task<bool> selectionTask)
    {
        try
        {
            if (await selectionTask)
                RunTracker.RecordOrreryRewardObtainedCards(reward);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardOrreryOnSelectPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(CardReward), nameof(CardReward.OnSkipped))]
public static class CardRewardOrreryOnSkippedPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RecordOrreryRewardSkipped(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardOrreryOnSkippedPatch failed: {e.Message}");
        }
    }
}
