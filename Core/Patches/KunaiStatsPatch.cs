using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Kunai owns its per-turn attack counter in AfterCardPlayed. The prefix records
/// attack-count pressure and snapshots Dexterity before the activation resolves;
/// the postfix records the observed Dexterity delta after the async callback.
/// </summary>
[HarmonyPatch(typeof(Kunai), nameof(Kunai.AfterCardPlayed))]
public static class KunaiAfterCardPlayedPatch
{
    [HarmonyPrefix]
    public static void Prefix(Kunai __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, out KunaiActivationState? __state)
    {
        __state = null;

        try
        {
            if (__instance == null || !RunTracker.IsTrackedRelic(__instance)) return;
            if (!RunTracker.RecordKunaiAttackPlayedAndShouldObserveActivation(
                    __instance,
                    cardPlay,
                    out var ownerCreature,
                    out var dexterityBefore))
            {
                return;
            }

            if (ownerCreature == null) return;
            __state = new KunaiActivationState(ownerCreature, dexterityBefore);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"KunaiAfterCardPlayedPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Task __result, KunaiActivationState? __state)
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
            CoreMain.LogDebug($"KunaiAfterCardPlayedPatch.Postfix failed: {e.Message}");
        }
    }

    private static void FinalizeActivation(KunaiActivationState state)
    {
        try
        {
            var dexterityAfter = state.OwnerCreature.GetPower<DexterityPower>()?.Amount ?? 0;
            RunTracker.RecordKunaiActivation(Math.Max(0, dexterityAfter - state.DexterityBefore));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"KunaiAfterCardPlayedPatch finalize failed: {e.Message}");
        }
    }
}

/// <summary>
/// Snapshots Kunai's per-turn charge at the end of each player turn while held.
/// Bound by runtime lookup so a game hook rename does not break build.
/// </summary>
[HarmonyPatch]
public static class HookBeforeSideTurnEndKunaiPatch
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
            RunTracker.RecordKunaiTurnEnded(participants);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookBeforeSideTurnEndKunaiPatch failed: {e.Message}");
        }
    }
}

public sealed class KunaiActivationState
{
    public KunaiActivationState(Creature ownerCreature, int dexterityBefore)
    {
        OwnerCreature = ownerCreature;
        DexterityBefore = dexterityBefore;
    }

    public Creature OwnerCreature { get; }
    public int DexterityBefore { get; }
}
