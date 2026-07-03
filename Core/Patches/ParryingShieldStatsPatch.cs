using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Parrying Shield when its owner-specific end-of-turn callback is
/// about to satisfy the block threshold and run its damage command.
/// </summary>
[HarmonyPatch(typeof(ParryingShield), nameof(ParryingShield.AfterSideTurnEnd))]
public static class ParryingShieldAfterSideTurnEndPatch
{
    [HarmonyPrefix]
    public static void Prefix(ParryingShield __instance, IEnumerable<Creature> participants)
    {
        try
        {
            if (__instance == null) return;
            var ownerCreature = __instance.Owner?.Creature;
            if (ownerCreature == null || participants == null) return;
            if (!participants.Contains(ownerCreature)) return;
            if (ownerCreature.Block < __instance.DynamicVars.Block.BaseValue) return;
            if (ownerCreature.CombatState?.HittableEnemies?.Any() != true) return;

            RunTracker.ArmParryingShieldAttribution(ownerCreature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ParryingShieldAfterSideTurnEndPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual damage split from Parrying Shield's emitted damage.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(DamageVar), typeof(Creature) })]
public static class ParryingShieldCreatureDamagePatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature dealer, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeParryingShieldDamageAttribution(dealer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ParryingShieldCreatureDamagePatch.Prefix failed: {e.Message}");
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
            RunTracker.RecordParryingShieldDamage(results);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ParryingShieldCreatureDamagePatch damage observation failed: {e.Message}");
        }
    }
}
