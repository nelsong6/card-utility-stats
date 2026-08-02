using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// RelicCmd.Melt performs the authoritative synchronous IsMelted mutation
/// before awaiting the relic's removal callback. Observe that completed state
/// for the exact physical wax relic and retain the normal combat-boundary
/// persistence semantics in RunTracker.
/// </summary>
[HarmonyPatch(
    typeof(RelicCmd),
    nameof(RelicCmd.Melt),
    new[] { typeof(RelicModel) })]
public static class ToyBoxWaxRelicMeltPatch
{
    [HarmonyPostfix]
    public static void Postfix(RelicModel relic)
    {
        try
        {
            if (relic?.IsWax != true || relic.IsMelted != true) return;
            RunTracker.RecordToyBoxWaxRelicMelted(relic);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ToyBoxWaxRelicMeltPatch.Postfix failed: {e.Message}");
        }
    }
}
