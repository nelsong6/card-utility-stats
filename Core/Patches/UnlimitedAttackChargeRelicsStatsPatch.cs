using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Kusarigama counts owner Attacks and can fire on every third one in a turn.
/// The owner callback arms only the exact single-target damage command it emits.
/// </summary>
[HarmonyPatch(typeof(Kusarigama), nameof(Kusarigama.AfterCardPlayed))]
public static class KusarigamaAfterCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Kusarigama __instance, CardPlay cardPlay, out Creature? __state)
    {
        __state = null;

        try
        {
            RunTracker.RecordKusarigamaAttackPlayedAndShouldArmDamageAttribution(
                __instance,
                cardPlay,
                out __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"KusarigamaAfterCardPlayedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Creature? __state, Task __result)
    {
        if (__state == null) return;

        try
        {
            if (__result == null)
            {
                RunTracker.DisarmKusarigamaDamageAttribution(__state);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmKusarigamaDamageAttribution(__state),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"KusarigamaAfterCardPlayedStatsPatch.Postfix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Captures Kusarigama's actual damage result, including block, overkill, and
/// a combat-ending kill that the game's normal history may omit.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(DamageVar), typeof(Creature) })]
public static class KusarigamaCreatureDamageStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature dealer, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeKusarigamaDamageAttribution(dealer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"KusarigamaCreatureDamageStatsPatch.Prefix failed: {e.Message}");
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
            RunTracker.RecordKusarigamaDamage(results);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"KusarigamaCreatureDamageStatsPatch damage observation failed: {e.Message}");
        }
    }
}

/// <summary>
/// Ornamental Fan counts owner Attacks and arms its block command on every
/// third play. The command result is the block amount after modifiers, and its
/// history entry remains relic-owned rather than falling back to that Attack.
/// </summary>
[HarmonyPatch(typeof(OrnamentalFan), nameof(OrnamentalFan.AfterCardPlayed))]
public static class OrnamentalFanAfterCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(OrnamentalFan __instance, CardPlay cardPlay, out Creature? __state)
    {
        __state = null;

        try
        {
            RunTracker.RecordOrnamentalFanAttackPlayedAndShouldArmBlockAttribution(
                __instance,
                cardPlay,
                out __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OrnamentalFanAfterCardPlayedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Creature? __state, Task __result)
    {
        if (__state == null) return;

        try
        {
            if (__result == null)
            {
                RunTracker.DisarmOrnamentalFanBlockAttribution(__state);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmOrnamentalFanBlockAttribution(__state),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OrnamentalFanAfterCardPlayedStatsPatch.Postfix failed: {e.Message}");
        }
    }
}

[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.GainBlock),
    new[] { typeof(Creature), typeof(BlockVar), typeof(CardPlay), typeof(bool) })]
public static class OrnamentalFanCreatureGainBlockStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature creature, out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.TryConsumeOrnamentalFanBlockAttribution(creature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OrnamentalFanCreatureGainBlockStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<decimal> __result)
    {
        if (!__state || __result == null) return;
        ObserveBlockResultAsync(__result);
    }

    private static async void ObserveBlockResultAsync(Task<decimal> blockTask)
    {
        try
        {
            var gained = await blockTask.ConfigureAwait(false);
            RunTracker.RecordOrnamentalFanBlockGained(gained);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"OrnamentalFanCreatureGainBlockStatsPatch block observation failed: {e.Message}");
        }
    }
}

/// <summary>
/// Shuriken mirrors Kunai: snapshot Strength at the third-Attack threshold and
/// record the positive observed delta after its async callback succeeds.
/// </summary>
[HarmonyPatch(typeof(Shuriken), nameof(Shuriken.AfterCardPlayed))]
public static class ShurikenAfterCardPlayedStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Shuriken __instance, CardPlay cardPlay, out ShurikenActivationState? __state)
    {
        __state = null;

        try
        {
            if (!RunTracker.RecordShurikenAttackPlayedAndShouldObserveActivation(
                    __instance,
                    cardPlay,
                    out var ownerCreature,
                    out var strengthBefore))
            {
                return;
            }

            if (ownerCreature == null) return;
            __state = new ShurikenActivationState(ownerCreature, strengthBefore);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ShurikenAfterCardPlayedStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Task __result, ShurikenActivationState? __state)
    {
        if (__state == null) return;

        try
        {
            if (__result == null)
            {
                FinalizeActivation(__state);
                return;
            }

            if (__result.IsCompleted)
            {
                if (__result.IsCompletedSuccessfully)
                    FinalizeActivation(__state);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (task.IsCompletedSuccessfully)
                        FinalizeActivation(__state);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ShurikenAfterCardPlayedStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static void FinalizeActivation(ShurikenActivationState state)
    {
        try
        {
            var strengthAfter = state.OwnerCreature.GetPower<StrengthPower>()?.Amount ?? 0;
            RunTracker.RecordShurikenActivation(Math.Max(0m, strengthAfter - state.StrengthBefore));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ShurikenAfterCardPlayedStatsPatch finalize failed: {e.Message}");
        }
    }
}

public sealed class ShurikenActivationState
{
    public ShurikenActivationState(Creature ownerCreature, decimal strengthBefore)
    {
        OwnerCreature = ownerCreature;
        StrengthBefore = strengthBefore;
    }

    public Creature OwnerCreature { get; }
    public decimal StrengthBefore { get; }
}

/// <summary>
/// Snapshots the three repeatable Attack-counter relics before their per-turn
/// counters reset. Bound by runtime lookup for the same compatibility reason
/// as the existing Kunai and Letter Opener snapshots.
/// </summary>
[HarmonyPatch]
public static class HookBeforeSideTurnEndUnlimitedAttackChargeRelicsPatch
{
    private static MethodBase? TargetMethod()
    {
        var hookType = Sts2CoreAssembly()?.GetType("MegaCrit.Sts2.Core.Hooks.Hook", throwOnError: false);
        if (hookType == null) return null;

        return AccessTools.Method(hookType, "BeforeSideTurnEnd")
            ?? AccessTools.Method(hookType, "BeforeTurnEnd");
    }

    private static Assembly? Sts2CoreAssembly()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "sts2") return assembly;
        }

        return null;
    }

    private static bool Prepare() => TargetMethod() != null;

    [HarmonyPrefix]
    public static void Prefix(CombatSide side, IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            RunTracker.RecordUnlimitedAttackChargeRelicsTurnEnded(participants);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookBeforeSideTurnEndUnlimitedAttackChargeRelicsPatch failed: {e.Message}");
        }
    }
}
