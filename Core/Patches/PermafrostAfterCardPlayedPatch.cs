using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Permafrost when its owner-specific first-Power callback is about to
/// grant block. The actual block gained is observed by Hook.AfterBlockGained.
/// </summary>
[HarmonyPatch(typeof(Permafrost), nameof(Permafrost.AfterCardPlayed))]
public static class PermafrostAfterCardPlayedPatch
{
    private static readonly FieldInfo? ActivatedThisCombatField =
        AccessTools.Field(typeof(Permafrost), "_activatedThisCombat");

    [HarmonyPrefix]
    public static void Prefix(Permafrost __instance, CardPlay cardPlay, out bool __state)
    {
        __state = false;

        try
        {
            if (!ShouldArm(__instance, cardPlay)) return;
            RunTracker.RecordPermafrostActivationAndArmBlockAttribution();
            __state = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PermafrostAfterCardPlayedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task __result)
    {
        try
        {
            if (!__state) return;
            if (__result == null)
            {
                RunTracker.DisarmPermafrostBlockAttribution();
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmPermafrostBlockAttribution(),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PermafrostAfterCardPlayedPatch.Postfix failed: {e.Message}");
        }
    }

    private static bool ShouldArm(Permafrost relic, CardPlay cardPlay)
    {
        if (relic?.Owner == null || cardPlay?.Card == null) return false;
        if (!RunTracker.IsTrackedRelic(relic)) return false;
        if (!CombatManager.Instance.IsInProgress) return false;
        if (!ReferenceEquals(cardPlay.Card.Owner, relic.Owner)) return false;
        if (cardPlay.Card.Type != CardType.Power) return false;
        if (IsActivatedThisCombat(relic)) return false;
        return true;
    }

    private static bool IsActivatedThisCombat(Permafrost relic)
    {
        if (ActivatedThisCombatField == null) return true;
        return ActivatedThisCombatField.GetValue(relic) is not bool activated || activated;
    }
}
