using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Leafy Poultice's pickup max-HP loss by observing the owner's max HP
/// before and after the async pickup effect resolves.
/// </summary>
[HarmonyPatch]
public static class LeafyPoulticeAfterObtainedPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.LeafyPoultice");
        return t == null ? null : AccessTools.Method(t, "AfterObtained");
    }

    [HarmonyPrefix]
    public static void Prefix(LeafyPoultice __instance, out PickupState __state)
    {
        __state = default;

        try
        {
            if (__instance == null) return;
            var creature = __instance.Owner?.Creature;
            if (creature == null || creature.IsDead) return;
            if (!RunTracker.BeginLeafyPoulticePickup(__instance, out var player)) return;

            __state = new PickupState(player, creature, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeafyPoulticeAfterObtainedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(PickupState __state, Task __result)
    {
        try
        {
            if (__state.Creature == null) return;

            if (__result == null)
            {
                CompletePickup(__state, succeeded: true);
                return;
            }

            if (__result.IsCompleted)
            {
                CompletePickup(__state, succeeded: !__result.IsCanceled && !__result.IsFaulted);
                return;
            }

            __result.ContinueWith(
                task => CompletePickup(__state, succeeded: !task.IsCanceled && !task.IsFaulted),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeafyPoulticeAfterObtainedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void CompletePickup(PickupState state, bool succeeded)
    {
        try
        {
            if (state.Creature == null) return;

            RunTracker.CompleteLeafyPoulticePickup(
                state.Player,
                succeeded,
                state.InitialMaxHp,
                state.Creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LeafyPoulticeAfterObtainedPatch.CompletePickup failed: {e.Message}");
        }
    }

    public readonly record struct PickupState(Player? Player, Creature? Creature, decimal InitialMaxHp);
}
