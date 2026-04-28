using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Blood Vial's combat-start heal so the relic tooltip can show
/// total HP healed across the run.
/// </summary>
[HarmonyPatch]
public static class BloodVialAfterPlayerTurnStartLatePatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BloodVial");
        return t == null ? null : AccessTools.Method(t, "AfterPlayerTurnStartLate");
    }

    [HarmonyPrefix]
    public static void Prefix(PlayerChoiceContext choiceContext, Player player, ref int __state)
    {
        __state = player?.Creature?.CurrentHp ?? 0;
    }

    [HarmonyPostfix]
    public static void Postfix(PlayerChoiceContext choiceContext, Player player, int __state)
    {
        try
        {
            if (player?.Creature == null) return;
            if (player.Creature.CombatState?.RoundNumber != 1) return;

            var healed = player.Creature.CurrentHp - __state;
            if (healed <= 0) return;

            RunTracker.RecordBloodVialHeal(healed);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BloodVialAfterPlayerTurnStartLatePatch failed: {e.Message}");
        }
    }
}
