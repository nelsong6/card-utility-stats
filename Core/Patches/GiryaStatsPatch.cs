using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Measures Girya's actual Strength delta across its awaited combat-room entry
/// callback. Only a positive native Lift count makes the combat eligible.
/// </summary>
[HarmonyPatch]
public static class GiryaAfterRoomEnteredStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(Girya),
            nameof(Girya.AfterRoomEntered),
            new[] { typeof(AbstractRoom) });
    }

    [HarmonyPrefix]
    public static void Prefix(
        Girya __instance,
        AbstractRoom room,
        out StrengthState __state)
    {
        __state = default;

        try
        {
            if (RunTracker.BeginGiryaStrengthGain(
                    __instance,
                    room,
                    out var ownerCreature,
                    out var strengthBefore))
            {
                __state = new StrengthState(ownerCreature, strengthBefore);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GiryaAfterRoomEnteredStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(StrengthState __state, ref Task __result)
    {
        try
        {
            if (__state.OwnerCreature == null) return;
            __result = CompleteAfter(__result ?? Task.CompletedTask, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"GiryaAfterRoomEnteredStatsPatch.Postfix failed: {e.Message}");
            Complete(__state, succeeded: false);
        }
    }

    private static async Task CompleteAfter(Task original, StrengthState state)
    {
        var succeeded = false;
        try
        {
            await original;
            succeeded = true;
        }
        finally
        {
            Complete(state, succeeded);
        }
    }

    private static void Complete(StrengthState state, bool succeeded)
    {
        RunTracker.CompleteGiryaStrengthGain(
            state.OwnerCreature,
            state.StrengthBefore,
            succeeded);
    }

    public readonly record struct StrengthState(
        Creature? OwnerCreature,
        decimal StrengthBefore);
}
