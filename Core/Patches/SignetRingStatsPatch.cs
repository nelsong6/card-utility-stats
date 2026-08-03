using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes the first real merchant room reached after Signet Ring enters the
/// run. Patching the resolved room hook includes shops rolled from ? nodes,
/// while excluding screens that merely display shop-like content.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]
public static class SignetRingStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(IRunState runState, AbstractRoom room)
    {
        try
        {
            RunTracker.RecordRunGoldRoomEntered(runState, room);
            if (room is MerchantRoom)
                RunTracker.RecordSignetRingShopReached(runState, room);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SignetRingStatsPatch failed: {e.Message}");
        }
    }
}
