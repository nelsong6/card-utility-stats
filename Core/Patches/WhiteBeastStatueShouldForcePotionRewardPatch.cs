using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures White Beast Statue's owner-specific decision to force a potion
/// reward. The concrete reward and claimed potion rarity are resolved later.
/// </summary>
[HarmonyPatch]
public static class WhiteBeastStatueShouldForcePotionRewardPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.WhiteBeastStatue");
        return t == null ? null : AccessTools.Method(t, "ShouldForcePotionReward");
    }

    [HarmonyPostfix]
    public static void Postfix(
        WhiteBeastStatue __instance,
        Player player,
        RoomType roomType,
        bool __result)
    {
        try
        {
            if (!__result) return;
            RunTracker.NoteWhiteBeastPotionRewardForced();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"WhiteBeastStatueShouldForcePotionRewardPatch.Postfix failed: {e.Message}");
        }
    }
}
