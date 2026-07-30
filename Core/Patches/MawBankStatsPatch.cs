using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Maw Bank at its owner-specific room-entry callback. The completed
/// owner balance supplies the actual gold gain. Shop visits are opened on a
/// MerchantRoom entry and resolved at the next distinct room entry against the
/// relic's saved HasItemBeenBought state.
/// </summary>
[HarmonyPatch(typeof(MawBank), nameof(MawBank.AfterRoomEntered))]
public static class MawBankStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        MawBank __instance,
        AbstractRoom room,
        out MawBankActivationState __state)
    {
        __state = default;

        try
        {
            if (__instance == null || room == null) return;

            RunTracker.RecordMawBankRoomEntered(__instance, room);

            var owner = __instance.Owner;
            if (owner == null
                || !RunTracker.IsTrackedRelic(__instance)
                || !ReferenceEquals(owner.RunState?.BaseRoom, room)
                || __instance.HasItemBeenBought)
                return;

            __state = new MawBankActivationState(__instance, owner, owner.Gold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MawBankStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, MawBankActivationState __state)
    {
        try
        {
            if (__result == null || __state.Relic == null || __state.Owner == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MawBankStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, MawBankActivationState state)
    {
        await inner.ConfigureAwait(false);

        try
        {
            RunTracker.RecordMawBankActivation(
                state.Relic!,
                state.Owner!,
                state.InitialGold,
                state.Owner!.Gold);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MawBankStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public readonly record struct MawBankActivationState(
        MawBank? Relic,
        Player? Owner,
        int InitialGold);
}
