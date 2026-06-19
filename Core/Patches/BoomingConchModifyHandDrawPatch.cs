using System;
using System.Reflection;
using HarmonyLib;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records the additional cards Booming Conch draws at the start of an Elite
/// combat so the relic tooltip can show the total draw contribution across the
/// run.
///
/// BoomingConch.ModifyHandDraw returns the modified draw count; on the relic's
/// Elite-start trigger the return value exceeds the incoming count by the bonus
/// it adds. The difference is the cards Booming Conch drew, mirroring how
/// <see cref="PocketwatchModifyHandDrawPatch"/> captures Pocketwatch's bonus
/// (and self-gating: the delta is zero on every draw the relic does not modify).
/// </summary>
[HarmonyPatch]
public static class BoomingConchModifyHandDrawPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BoomingConch");
        return t == null ? null : AccessTools.Method(t, "ModifyHandDraw");
    }

    [HarmonyPostfix]
    public static void Postfix(decimal count, decimal __result)
    {
        try
        {
            var added = __result - count;
            if (added <= 0m) return;
            RunTracker.RecordBoomingConchDraw((int)added);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BoomingConchModifyHandDrawPatch failed: {e.Message}");
        }
    }
}
