using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Mr. Struggles when its owner-specific turn-start callback is about
/// to run its scaling damage command.
/// </summary>
[HarmonyPatch(typeof(MrStruggles), nameof(MrStruggles.AfterPlayerTurnStart))]
public static class MrStrugglesAfterPlayerTurnStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(MrStruggles __instance, Player player)
    {
        try
        {
            if (__instance?.Owner == null || player == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!ReferenceEquals(player, __instance.Owner)) return;

            var ownerCreature = __instance.Owner.Creature;
            if (ownerCreature == null) return;
            if (ownerCreature.CombatState?.HittableEnemies?.Any() != true) return;

            RunTracker.ArmMrStrugglesAttribution(ownerCreature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MrStrugglesAfterPlayerTurnStartPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures the actual damage split from Mr. Struggles's emitted damage.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(IEnumerable<Creature>),
        typeof(decimal),
        typeof(ValueProp),
        typeof(Creature),
    })]
public static class MrStrugglesCreatureDamagePatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature dealer, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeMrStrugglesDamageAttribution(dealer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MrStrugglesCreatureDamagePatch.Prefix failed: {e.Message}");
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
            RunTracker.RecordMrStrugglesDamage(results);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MrStrugglesCreatureDamagePatch damage observation failed: {e.Message}");
        }
    }
}
