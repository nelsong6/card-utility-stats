using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Dowsing Rod grants the Dowsing quest card, and that card's saved
/// RoomsEntered property is the authoritative countdown. Observe the property
/// after every mutation so the relic tooltip mirrors the quest exactly.
/// </summary>
[HarmonyPatch]
public static class DowsingRoomsEnteredStatsPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.PropertySetter(typeof(Dowsing), nameof(Dowsing.RoomsEntered));

    [HarmonyPostfix]
    public static void Postfix(Dowsing __instance)
    {
        try
        {
            RunTracker.RecordDowsingRoomsEntered(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DowsingRoomsEnteredStatsPatch failed: {e.Message}");
        }
    }
}
