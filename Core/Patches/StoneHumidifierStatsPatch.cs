using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records each Stone Humidifier activation from the relic's owner-specific
/// rest-site callback. The actual max-HP result is observed only after the
/// awaited GainMaxHp command completes successfully.
/// </summary>
[HarmonyPatch(typeof(StoneHumidifier), nameof(StoneHumidifier.AfterRestSiteHeal))]
public static class StoneHumidifierStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        StoneHumidifier __instance,
        Player player,
        out MaxHpState __state)
    {
        __state = default;

        try
        {
            if (__instance?.Owner == null || player == null) return;
            if (!ReferenceEquals(player, __instance.Owner)) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;

            var creature = __instance.Owner.Creature;
            if (creature == null || creature.IsDead) return;

            __state = new MaxHpState(creature, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StoneHumidifierStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(MaxHpState __state, Task __result)
    {
        try
        {
            if (__state.Creature == null) return;

            if (__result == null)
            {
                Observe(__state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (__result.IsCompletedSuccessfully)
                    Observe(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (task.IsCompletedSuccessfully)
                        Observe(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StoneHumidifierStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Observe(MaxHpState state)
    {
        try
        {
            var creature = state.Creature;
            if (creature == null) return;

            RunTracker.RecordStoneHumidifierMaxHpGain(
                creature,
                state.StartingMaxHp,
                creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"StoneHumidifierStatsPatch.Observe failed: {e.Message}");
        }
    }

    public readonly record struct MaxHpState(Creature? Creature, decimal StartingMaxHp);
}
