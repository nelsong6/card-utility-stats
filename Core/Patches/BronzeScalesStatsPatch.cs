using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures the actual Thorns amount Bronze Scales added after the relic's
/// async room-entry hook applies it to the owner.
/// </summary>
[HarmonyPatch(typeof(BronzeScales), nameof(BronzeScales.AfterRoomEntered))]
public static class BronzeScalesAfterRoomEnteredPatch
{
    [HarmonyPrefix]
    public static void Prefix(BronzeScales __instance, AbstractRoom room, out BronzeScalesRoomState? __state)
    {
        __state = null;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;
            if (room is not CombatRoom) return;

            var ownerCreature = __instance.Owner?.Creature;
            if (ownerCreature == null) return;

            __state = new BronzeScalesRoomState(
                ownerCreature,
                ownerCreature.GetPower<ThornsPower>()?.Amount ?? 0);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BronzeScalesAfterRoomEnteredPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Task __result, BronzeScalesRoomState? __state)
    {
        if (__state == null) return;

        try
        {
            if (__result == null)
            {
                FinalizeThornsContribution(__state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (__result.IsCompletedSuccessfully)
                    FinalizeThornsContribution(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (task.IsCompletedSuccessfully)
                        FinalizeThornsContribution(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BronzeScalesAfterRoomEnteredPatch.Postfix failed: {e.Message}");
        }
    }

    private static void FinalizeThornsContribution(BronzeScalesRoomState state)
    {
        try
        {
            var thornsPower = state.OwnerCreature.GetPower<ThornsPower>();
            if (thornsPower == null) return;

            int added = Math.Max(0, thornsPower.Amount - state.ThornsAmountBefore);
            RunTracker.RecordBronzeScalesThornsContribution(thornsPower, added);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BronzeScalesAfterRoomEnteredPatch finalize failed: {e.Message}");
        }
    }

    public sealed class BronzeScalesRoomState
    {
        public BronzeScalesRoomState(Creature ownerCreature, int thornsAmountBefore)
        {
            OwnerCreature = ownerCreature;
            ThornsAmountBefore = thornsAmountBefore;
        }

        public Creature OwnerCreature { get; }
        public int ThornsAmountBefore { get; }
    }
}

/// <summary>
/// Arms an attribution window for Thorns damage when the Thorns power instance
/// includes a Bronze Scales contribution.
/// </summary>
[HarmonyPatch(typeof(ThornsPower), nameof(ThornsPower.BeforeDamageReceived))]
public static class BronzeScalesThornsBeforeDamageReceivedPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        ThornsPower __instance,
        Creature target,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        try
        {
            if (__instance == null || target == null || dealer == null) return;
            if (!ReferenceEquals(target, __instance.Owner)) return;
            if (!props.IsPoweredAttack() && cardSource is not Omnislice) return;

            RunTracker.ArmBronzeScalesThornsDamageAttribution(__instance, target, dealer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BronzeScalesThornsBeforeDamageReceivedPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual reflected damage split from the Thorns damage command.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Creature),
        typeof(decimal),
        typeof(ValueProp),
        typeof(Creature),
        typeof(CardModel),
        typeof(CardPlay),
    })]
public static class BronzeScalesCreatureDamagePatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature target, decimal amount, Creature? dealer, out BronzeScalesDamageState? __state)
    {
        __state = null;

        try
        {
            if (!RunTracker.TryConsumeBronzeScalesThornsDamageAttribution(
                    target,
                    amount,
                    dealer,
                    out decimal attributedAmount))
            {
                return;
            }

            __state = new BronzeScalesDamageState(amount, attributedAmount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BronzeScalesCreatureDamagePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(BronzeScalesDamageState? __state, Task<IEnumerable<DamageResult>> __result)
    {
        if (__state == null || __result == null) return;
        ObserveDamageResultAsync(__state, __result);
    }

    private static async void ObserveDamageResultAsync(
        BronzeScalesDamageState state,
        Task<IEnumerable<DamageResult>> damageTask)
    {
        try
        {
            var results = await damageTask.ConfigureAwait(false);
            RunTracker.RecordBronzeScalesDamage(results, state.TotalAmount, state.AttributedAmount);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BronzeScalesCreatureDamagePatch damage observation failed: {e.Message}");
        }
    }

    public sealed class BronzeScalesDamageState
    {
        public BronzeScalesDamageState(decimal totalAmount, decimal attributedAmount)
        {
            TotalAmount = totalAmount;
            AttributedAmount = attributedAmount;
        }

        public decimal TotalAmount { get; }
        public decimal AttributedAmount { get; }
    }
}
