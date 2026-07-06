using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures original map-point entries for relics whose stats depend on the
/// unresolved point type.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapPointInternal))]
public static class RunManagerMapPointStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(MapPointType pointType, bool saveGame)
    {
        try
        {
            RunTracker.RecordMapPointEntered(pointType, saveGame);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RunManagerMapPointStatsPatch failed: {e.Message}");
        }
    }
}
