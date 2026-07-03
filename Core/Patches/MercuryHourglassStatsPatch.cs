using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Mercury Hourglass when its owner-specific player-turn callback is
/// about to run its damage command.
/// </summary>
[HarmonyPatch(typeof(MercuryHourglass), nameof(MercuryHourglass.AfterPlayerTurnStart))]
public static class MercuryHourglassAfterPlayerTurnStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(MercuryHourglass __instance, Player player)
    {
        try
        {
            if (__instance?.Owner == null || player == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!ReferenceEquals(player, __instance.Owner)) return;

            var ownerCreature = __instance.Owner.Creature;
            if (ownerCreature == null) return;
            if (ownerCreature.CombatState?.HittableEnemies?.Any() != true) return;

            RunTracker.ArmMercuryHourglassAttribution(ownerCreature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MercuryHourglassAfterPlayerTurnStartPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual damage split from Mercury Hourglass's emitted damage.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[] { typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(DamageVar), typeof(Creature) })]
public static class MercuryHourglassCreatureDamagePatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature dealer, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeMercuryHourglassDamageAttribution(dealer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MercuryHourglassCreatureDamagePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<IEnumerable<DamageResult>> __result)
    {
        if (!__state || __result == null) return;
        ObserveDamageResultAsync(__result);
    }

    private static async void ObserveDamageResultAsync(Task<IEnumerable<DamageResult>> damageTask)
    {
        try
        {
            var results = await damageTask.ConfigureAwait(false);
            RunTracker.RecordMercuryHourglassDamage(results);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MercuryHourglassCreatureDamagePatch damage observation failed: {e.Message}");
        }
    }
}
