using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core.Patches;

/// <summary>
/// Shared observer for resolved room entry. Patching the resolved room hook
/// includes rooms rolled from ? nodes, while excluding screens that merely
/// display room-like content — which is what Signet Ring's first-merchant
/// measurement, the map legend, the run gold rooms, Lucky Fysh's held-room
/// denominators, and RoomResetter's rewind target all need.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]
public static class SignetRingStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(IRunState runState, AbstractRoom room)
    {
        try
        {
            RunTracker.RecordMapLegendRoomEntered(runState, room);
            RunTracker.RecordRunGoldRoomEntered(runState, room);
            RunTracker.RecordLuckyFyshRoomEntered(runState, room);
            if (room is MerchantRoom)
                RunTracker.RecordSignetRingShopReached(runState, room);

            // Last, so the snapshot includes everything the room-entry
            // recorders above just wrote — a restart replays the room from
            // here, not from before them.
            RunTracker.CaptureRoomEntrySnapshot(runState);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SignetRingStatsPatch failed: {e.Message}");
        }
    }
}
