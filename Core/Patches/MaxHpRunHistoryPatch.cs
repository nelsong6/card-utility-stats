using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Potions;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes the player's final maximum-HP command. SetMaxHp is shared by gains,
/// losses, and direct set commands, while excluding creature construction,
/// save restoration, and monster scaling that use the internal setter.
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.SetMaxHp))]
public static class MaxHpRunHistoryPatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature creature, out int __state)
    {
        __state = creature?.MaxHp ?? 0;
    }

    [HarmonyPostfix]
    public static void Postfix(Creature creature, int __state)
    {
        try
        {
            if (creature == null) return;
            RunTracker.RecordMaxHpChanged(creature, __state, creature.MaxHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MaxHpRunHistoryPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Fruit Juice's source is no longer present by the later belt-removal hook.
/// Observe its exact OnUse window so the generic max-HP entry can retain the
/// potion name without claiming an unrelated same-floor change.
/// </summary>
[HarmonyPatch]
public static class FruitJuiceMaxHpHistorySourcePatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(FruitJuice),
            "OnUse",
            [typeof(PlayerChoiceContext), typeof(Creature)]);

    [HarmonyPrefix]
    public static void Prefix(
        FruitJuice __instance,
        Creature? target,
        out FruitJuiceMaxHpState __state)
    {
        __state = target == null
            ? default
            : new FruitJuiceMaxHpState(target, target.MaxHp);
    }

    [HarmonyPostfix]
    public static void Postfix(
        FruitJuiceMaxHpState __state,
        ref Task __result)
    {
        try
        {
            if (__state.Target == null) return;
            if (__result == null)
            {
                Annotate(__state);
                return;
            }

            __result = ObserveAsync(__state, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"FruitJuiceMaxHpHistorySourcePatch failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        FruitJuiceMaxHpState state,
        Task inner)
    {
        await inner.ConfigureAwait(false);
        Annotate(state);
    }

    private static void Annotate(FruitJuiceMaxHpState state)
    {
        var target = state.Target;
        if (target == null) return;
        RunTracker.AnnotateMaxHpChanged(
            target,
            state.PreviousMaxHp,
            target.MaxHp,
            "Fruit Juice");
    }

    public readonly record struct FruitJuiceMaxHpState(
        Creature? Target,
        int PreviousMaxHp);
}
