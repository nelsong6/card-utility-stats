using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts ? map points entered while Juzu Bracelet is held. The original
/// MapPointType.Unknown is the stable signal; resolved RoomType.Event is not.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapPointInternal))]
public static class RunManagerJuzuBraceletStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(MapPointType pointType, bool saveGame)
    {
        try
        {
            RunTracker.RecordJuzuQuestionSiteEntered(pointType, saveGame);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RunManagerJuzuBraceletStatsPatch failed: {e.Message}");
        }
    }
}
