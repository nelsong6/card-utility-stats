using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes Art of War at its owner-specific energy-reset callback. The
/// callback awaits its exact GainEnergy command, so the successful task's
/// before/after pool delta is the energy the relic actually added.
/// </summary>
[HarmonyPatch(typeof(ArtOfWar), nameof(ArtOfWar.AfterEnergyReset))]
public static class ArtOfWarStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        ArtOfWar __instance,
        Player player,
        out EnergyState __state)
    {
        __state = default;

        try
        {
            if (__instance?.Owner == null || player?.PlayerCombatState == null) return;
            if (!ReferenceEquals(__instance.Owner, player)) return;
            if (!RunTracker.RecordArtOfWarTurnStarted(__instance, player)) return;

            __state = new EnergyState(
                __instance,
                player.PlayerCombatState,
                player.PlayerCombatState.Energy);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ArtOfWarStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(EnergyState __state, Task __result)
    {
        try
        {
            if (__state.Relic == null || __state.CombatState == null) return;

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
            CoreMain.LogDebug($"ArtOfWarStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static void Observe(EnergyState state)
    {
        try
        {
            if (state.Relic == null || state.CombatState == null) return;

            RunTracker.RecordArtOfWarEnergyGain(
                state.Relic,
                state.CombatState,
                state.StartingEnergy,
                state.CombatState.Energy);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ArtOfWarStatsPatch.Observe failed: {e.Message}");
        }
    }

    public readonly record struct EnergyState(
        ArtOfWar? Relic,
        PlayerCombatState? CombatState,
        int StartingEnergy);
}
