using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Pendulum now contributes to the normal hand-draw request after its
/// BeforeHandDraw callback advances TurnsSeen. The positive modifier delta is
/// the activation and arms observation of Pendulum's marginal draw result.
/// </summary>
[HarmonyPatch(typeof(Pendulum), nameof(Pendulum.ModifyHandDraw))]
public static class PendulumModifyHandDrawPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        Pendulum __instance,
        Player player,
        decimal count,
        decimal __result)
    {
        try
        {
            var added = __result - count;
            if (added <= 0m) return;

            RunTracker.RecordPendulumActivation(
                __instance,
                player,
                (int)Math.Ceiling(added));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PendulumModifyHandDrawPatch failed: {e.Message}");
        }
    }
}
