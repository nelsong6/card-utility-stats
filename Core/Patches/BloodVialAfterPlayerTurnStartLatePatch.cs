using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Blood Vial's owner-specific combat-start heal through the shared
/// relic healing ledger so full-HP and other lost healing remain visible.
/// </summary>
[HarmonyPatch]
public static class BloodVialAfterPlayerTurnStartLatePatch
{
    private const string RelicId = "RELIC.BLOOD_VIAL";
    private const decimal BloodVialHeal = 2m;

    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BloodVial");
        return t == null ? null : AccessTools.Method(t, "AfterPlayerTurnStartLate");
    }

    [HarmonyPrefix]
    public static void Prefix(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (player?.Creature == null || player.Creature.IsDead) return;
            if (player.Creature.CombatState?.RoundNumber != 1) return;
            RunTracker.RecordBloodVialTrigger(player.Creature, BloodVialHeal);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BloodVialAfterPlayerTurnStartLatePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(PlayerChoiceContext choiceContext, Player player, Task __result)
    {
        try
        {
            var healedCreature = player?.Creature;
            if (healedCreature == null) return;

            if (__result == null || __result.IsCompleted)
            {
                RunTracker.FinalizeRelicHealing(healedCreature, RelicId);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.FinalizeRelicHealing(healedCreature, RelicId),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BloodVialAfterPlayerTurnStartLatePatch.Postfix failed: {e.Message}");
        }
    }
}
