using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Rewards;

namespace SpireLens.Core.Patches;

/// <summary>
/// Player.AddPotionInternal is the final mutation point for every successful
/// belt insertion. Its PotionProcureResult keeps the gallery outcome observed:
/// failed reward clicks and blocked/full-belt procurements remain not taken.
/// </summary>
[HarmonyPatch(typeof(Player), "AddPotionInternal")]
public static class PotionHistoryAcquiredPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        Player __instance,
        PotionModel potion,
        bool silent,
        PotionProcureResult __result)
    {
        PatchGuard.Run(nameof(PotionHistoryAcquiredPatch), () =>
        {
            if (silent) return;
            RunTracker.RecordPotionAcquired(__instance, potion, __result);
        });
    }
}

/// <summary>
/// Removal after a completed UsePotionAction is the authoritative successful
/// use boundary. It excludes canceled targeting and queued uses that never
/// consume the potion.
/// </summary>
[HarmonyPatch(typeof(Player), "RemoveUsedPotionInternal")]
public static class PotionHistoryUsedPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player __instance, PotionModel potion)
    {
        PatchGuard.Run(nameof(PotionHistoryUsedPatch), () =>
        {
            RunTracker.RecordPotionUsed(__instance, potion);
        });
    }
}

/// <summary>
/// Blood Potion owns one awaited heal command. Observe current HP around that
/// exact callback so the history records restored HP after clamping without
/// including unrelated AfterPotionUsed hook effects.
/// </summary>
[HarmonyPatch]
public static class BloodPotionHistoryHealingPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(BloodPotion),
            "OnUse",
            [typeof(PlayerChoiceContext), typeof(Creature)]);

    [HarmonyPrefix]
    public static void Prefix(
        BloodPotion __instance,
        Creature target,
        out BloodPotionUseState __state)
    {
        __state = default;
        try
        {
            var player = __instance?.Owner;
            if (player == null
                || target == null
                || target.Player == null)
            {
                return;
            }

            __state = new BloodPotionUseState(player, target, target.CurrentHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BloodPotionHistoryHealingPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        BloodPotion __instance,
        BloodPotionUseState __state,
        ref Task __result)
    {
        try
        {
            if (__state.Player == null || __state.Target == null) return;
            if (__result == null)
            {
                RunTracker.RecordBloodPotionHealing(
                    __instance,
                    __state.Player,
                    __state.InitialHp,
                    __state.Target.CurrentHp);
                return;
            }

            __result = ObserveAsync(__instance, __state, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BloodPotionHistoryHealingPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        BloodPotion potion,
        BloodPotionUseState state,
        Task inner)
    {
        try
        {
            await inner.ConfigureAwait(false);
            RunTracker.RecordBloodPotionHealing(
                potion,
                state.Player,
                state.InitialHp,
                state.Target?.CurrentHp ?? state.InitialHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BloodPotionHistoryHealingPatch.ObserveAsync failed: {e.Message}");
            throw;
        }
    }

    public readonly record struct BloodPotionUseState(
        Player? Player,
        Creature? Target,
        int InitialHp);
}

[HarmonyPatch(typeof(Player), "DiscardPotionInternal")]
public static class PotionHistoryDiscardedPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player __instance, PotionModel potion)
    {
        PatchGuard.Run(nameof(PotionHistoryDiscardedPatch), () =>
        {
            RunTracker.RecordPotionDiscarded(__instance, potion);
        });
    }
}

/// <summary>
/// CreateIcon is the first reward boundary where a populated concrete potion
/// is actually presented to the player. Constructor/Populate alone can occur
/// before the outer reward page is visible.
/// </summary>
[HarmonyPatch(typeof(PotionReward), nameof(PotionReward.CreateIcon))]
public static class PotionHistoryRewardSeenPatch
{
    [HarmonyPostfix]
    public static void Postfix(PotionReward __instance)
    {
        PatchGuard.Run(nameof(PotionHistoryRewardSeenPatch), () =>
        {
            RunTracker.RecordPotionOffer(
                __instance,
                __instance.Potion,
                __instance.Player,
                "Potion reward");
        });
    }
}

/// <summary>
/// MerchantPotionEntry.FillSlot has selected the concrete stocked potion and
/// is also where the game marks it as seen. Recording after this method keeps
/// shop inventory generation and SpireLens's left lane aligned.
/// </summary>
[HarmonyPatch(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.FillSlot))]
public static class PotionHistoryShopSeenPatch
{
    [HarmonyPostfix]
    public static void Postfix(MerchantPotionEntry __instance)
    {
        PatchGuard.Run(nameof(PotionHistoryShopSeenPatch), () =>
        {
            RunTracker.RecordPotionOffer(
                __instance,
                __instance.Model,
                __instance._player,
                "Shop");
        });
    }
}
