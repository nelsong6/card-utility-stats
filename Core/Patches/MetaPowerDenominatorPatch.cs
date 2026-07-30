using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// One shared turn sampler for every supported meta-power. The target is an
/// established SpireLens Harmony surface; individual Power patches no longer
/// need to invent incompatible turn denominators.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartMetaPowerStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        RunTracker.RecordMetaPowerTurnStarted(player);
    }
}
