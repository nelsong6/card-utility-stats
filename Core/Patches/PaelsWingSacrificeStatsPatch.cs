using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Tracks Pael's Wing's card-reward Sacrifice alternative. Pael's Flesh is a
/// separate max-energy relic; the sacrifice option itself is owned by
/// <see cref="PaelsWing"/>.
/// </summary>
[HarmonyPatch(typeof(PaelsWing), nameof(PaelsWing.TryModifyCardRewardAlternatives))]
public static class PaelsWingTryModifyCardRewardAlternativesPatch
{
    private static readonly FieldInfo? OnSelectBackingField =
        AccessTools.Field(typeof(CardRewardAlternative), "<OnSelect>k__BackingField");

    [HarmonyPostfix]
    public static void Postfix(
        PaelsWing __instance,
        Player player,
        CardReward cardReward,
        List<CardRewardAlternative> alternatives,
        bool __result)
    {
        try
        {
            if (!__result || cardReward == null || alternatives == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;

            var sacrifice = alternatives.Find(alt =>
                string.Equals(alt?.OptionId, PaelsWing.sacrificeAlternativeKey, StringComparison.OrdinalIgnoreCase));
            if (sacrifice == null || OnSelectBackingField == null) return;

            RunTracker.NotePaelSacrificeOffered(cardReward);

            var original = sacrifice.OnSelect;
            if (original == null) return;

            async Task WrappedOnSelect()
            {
                var owner = __instance.Owner;

                void OnRelicObtained(RelicModel relic)
                {
                    owner.RelicObtained -= OnRelicObtained;
                    RunTracker.RecordPaelsWingRelicGained(relic);
                }

                RunTracker.RecordPaelSacrificeMade(cardReward);
                owner.RelicObtained += OnRelicObtained;
                try
                {
                    await original();
                }
                finally
                {
                    owner.RelicObtained -= OnRelicObtained;
                }
            }

            OnSelectBackingField.SetValue(sacrifice, (Func<Task>)WrappedOnSelect);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PaelsWingTryModifyCardRewardAlternativesPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch]
public static class CardRewardPaelSacrificeOnSelectPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(typeof(CardReward), "OnSelect");
    }

    [HarmonyPostfix]
    public static void Postfix(CardReward __instance, Task<bool> __result)
    {
        if (__instance == null || __result == null) return;
        ObserveRewardResolutionAsync(__instance, __result);
    }

    private static async void ObserveRewardResolutionAsync(CardReward reward, Task<bool> selectionTask)
    {
        try
        {
            await selectionTask;
            RunTracker.RecordPaelSacrificeSkipped(reward);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardPaelSacrificeOnSelectPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(CardReward), "OnSkipped")]
public static class CardRewardPaelSacrificeOnSkippedPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardReward __instance)
    {
        try
        {
            RunTracker.RecordPaelSacrificeSkipped(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CardRewardPaelSacrificeOnSkippedPatch failed: {e.Message}");
        }
    }
}
