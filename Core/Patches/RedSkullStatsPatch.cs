using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Red Skull only after every HP-change listener has completed, so
/// the relic's own StrengthApplied flag already reflects the new threshold
/// state. Hook.AfterCurrentHpChanged is an established SpireLens target.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCurrentHpChanged))]
public static class HookAfterCurrentHpChangedRedSkullStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature creature, ref Task __result)
    {
        try
        {
            var player = creature?.Player;
            if (player == null || __result == null) return;

            __result = ObserveAsync(__result, player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookAfterCurrentHpChangedRedSkullStatsPatch failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, Player player)
    {
        await inner;
        RunTracker.RecordRedSkullActivePeriod(player);
    }
}

/// <summary>
/// Carries an already-active Red Skull into each later player turn, including
/// active turns with no attacks or hits.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartRedSkullStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        try
        {
            RunTracker.RecordRedSkullActivePeriod(player);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HookAfterPlayerTurnStartRedSkullStatsPatch failed: {e.Message}");
        }
    }
}
