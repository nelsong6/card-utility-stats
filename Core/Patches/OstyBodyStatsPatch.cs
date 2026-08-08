using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Supplies the zero-inclusive turn denominator shared by Bound Phylactery
/// and Phylactery Unbound. The combat denominator is captured by the normal
/// held-relic combat baseline.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
internal static class HookAfterPlayerTurnStartOstyBodyStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordOstyBodyTurnStarted(player);
        }
        catch (System.Exception e)
        {
            CoreMain.LogDebug($"HookAfterPlayerTurnStartOstyBodyStatsPatch failed: {e.Message}");
        }
    }
}
