using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records the destination only when Winged Boots itself confirms that an
/// off-path room entry spent one of its three charges.
/// </summary>
[HarmonyPatch(typeof(WingedBoots), nameof(WingedBoots.AfterRoomEntered))]
public static class WingedBootsStatsPatch
{
    private readonly record struct EntryState(int TimesUsed, MapPointType? Destination);

    [HarmonyPrefix]
    private static void Prefix(WingedBoots __instance, out EntryState __state)
    {
        __state = new EntryState(
            __instance?.TimesUsed ?? 0,
            __instance?.Owner?.RunState?.CurrentMapPoint?.PointType);
    }

    [HarmonyPostfix]
    private static void Postfix(WingedBoots __instance, EntryState __state)
    {
        try
        {
            if (__instance == null
                || !__state.Destination.HasValue
                || __instance.TimesUsed <= __state.TimesUsed)
            {
                return;
            }

            RunTracker.RecordWingedBootsDestination(
                __instance,
                __instance.TimesUsed,
                __state.Destination.Value);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"WingedBootsStatsPatch failed: {e.Message}");
        }
    }
}
