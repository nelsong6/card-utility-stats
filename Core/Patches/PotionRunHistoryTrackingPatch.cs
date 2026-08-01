using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
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
