using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Sword in the Stone advances only after a completed Elite-victory callback.
/// Wrap the returned task so its fifth-kill replacement succeeds before the
/// observed Elite is committed to the pending combat.
/// </summary>
[HarmonyPatch]
public static class SwordOfStoneAfterCombatVictoryStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(SwordOfStone),
            nameof(SwordOfStone.AfterCombatVictory),
            new[] { typeof(CombatRoom) });
    }

    [HarmonyPrefix]
    public static void Prefix(
        SwordOfStone __instance,
        CombatRoom room,
        out EliteVictoryState __state)
    {
        __state = default;

        try
        {
            if (RunTracker.BeginSwordOfStoneEliteVictory(
                    __instance,
                    room,
                    out var floorAcquired,
                    out var floor,
                    out var encounterId,
                    out var displayName))
            {
                __state = new EliteVictoryState(
                    true,
                    floorAcquired,
                    floor,
                    encounterId,
                    displayName);
            }
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SwordOfStoneAfterCombatVictoryStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(EliteVictoryState __state, ref Task __result)
    {
        try
        {
            if (!__state.Armed) return;
            __result = CompleteAfter(__result ?? Task.CompletedTask, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SwordOfStoneAfterCombatVictoryStatsPatch.Postfix failed: {e.Message}");
            Complete(__state, succeeded: false);
        }
    }

    private static async Task CompleteAfter(Task original, EliteVictoryState state)
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

    private static void Complete(EliteVictoryState state, bool succeeded)
    {
        RunTracker.CompleteSwordOfStoneEliteVictory(
            state.FloorAcquired,
            state.Floor,
            state.EncounterId,
            state.DisplayName,
            succeeded);
    }

    public readonly record struct EliteVictoryState(
        bool Armed,
        int? FloorAcquired,
        int Floor,
        string EncounterId,
        string DisplayName);
}

/// <summary>
/// Sword of Jade is Sword in the Stone's transformed state. Measure the actual
/// Strength delta across its combat-room entry callback and keep the result in
/// the original Sword in the Stone aggregate.
/// </summary>
[HarmonyPatch]
public static class SwordOfJadeAfterRoomEnteredStatsPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(
            typeof(SwordOfJade),
            nameof(SwordOfJade.AfterRoomEntered),
            new[] { typeof(AbstractRoom) });
    }

    [HarmonyPrefix]
    public static void Prefix(
        SwordOfJade __instance,
        AbstractRoom room,
        out StrengthState __state)
    {
        __state = default;

        try
        {
            if (RunTracker.BeginSwordOfJadeStrengthGain(
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
            CoreMain.LogDebug($"SwordOfJadeAfterRoomEnteredStatsPatch.Prefix failed: {e.Message}");
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
            CoreMain.LogDebug($"SwordOfJadeAfterRoomEnteredStatsPatch.Postfix failed: {e.Message}");
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
        RunTracker.CompleteSwordOfJadeStrengthGain(
            state.OwnerCreature,
            state.StrengthBefore,
            succeeded);
    }

    public readonly record struct StrengthState(
        Creature? OwnerCreature,
        decimal StrengthBefore);
}
