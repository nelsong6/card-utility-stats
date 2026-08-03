using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// One shared turn sampler for supported stats with zero-inclusive turn
/// denominators. The target is an established SpireLens Harmony surface so
/// individual cards, Powers, and relics do not invent incompatible samplers.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartMetaPowerStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        RunTracker.RecordMetaPowerTurnStarted(player);
        RunTracker.RecordStrikeDummyTurnStarted(player);
        RunTracker.RecordOrnamentalFanTurnStarted(player);
        RunTracker.RecordSpikedGauntletsTurnStarted(player);
    }
}
