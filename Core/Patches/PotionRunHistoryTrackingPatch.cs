using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Player.AddPotionInternal is the final mutation point for every successful
/// belt insertion. Its PotionProcureResult keeps the gallery outcome observed:
/// failed reward clicks and blocked/full-belt procurements remain not taken.
/// </summary>
[HarmonyPatch(typeof(Player), "AddPotionInternal")]
public static class PotionHistoryAcquiredPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        Player __instance,
        PotionModel potion,
        bool silent,
        PotionProcureResult __result)
    {
        PatchGuard.Run(nameof(PotionHistoryAcquiredPatch), () =>
        {
            if (silent) return;
            RunTracker.RecordPotionAcquired(__instance, potion, __result);
        });
    }
}

/// <summary>
/// Removal after a completed UsePotionAction is the authoritative successful
/// use boundary. It excludes canceled targeting and queued uses that never
/// consume the potion.
/// </summary>
[HarmonyPatch(typeof(Player), "RemoveUsedPotionInternal")]
public static class PotionHistoryUsedPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player __instance, PotionModel potion)
    {
        PatchGuard.Run(nameof(PotionHistoryUsedPatch), () =>
        {
            RunTracker.RecordPotionUsed(__instance, potion);
        });
    }
}

/// <summary>
/// Blood Potion owns one awaited heal command. Observe current HP around that
/// exact callback so the history records restored HP after clamping without
/// including unrelated AfterPotionUsed hook effects.
/// </summary>
[HarmonyPatch]
public static class BloodPotionHistoryHealingPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(BloodPotion),
            "OnUse",
            [typeof(PlayerChoiceContext), typeof(Creature)]);

    [HarmonyPrefix]
    public static void Prefix(
        BloodPotion __instance,
        Creature target,
        out BloodPotionUseState __state)
    {
        __state = default;
        try
        {
            var player = __instance?.Owner;
            if (player == null
                || target == null
                || target.Player == null)
            {
                return;
            }

            __state = new BloodPotionUseState(player, target, target.CurrentHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BloodPotionHistoryHealingPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        BloodPotion __instance,
        BloodPotionUseState __state,
        ref Task __result)
    {
        try
        {
            if (__state.Player == null || __state.Target == null) return;
            if (__result == null)
            {
                RunTracker.RecordBloodPotionHealing(
                    __instance,
                    __state.Player,
                    __state.InitialHp,
                    __state.Target.CurrentHp);
                return;
            }

            __result = ObserveAsync(__instance, __state, __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BloodPotionHistoryHealingPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(
        BloodPotion potion,
        BloodPotionUseState state,
        Task inner)
    {
        try
        {
            await inner.ConfigureAwait(false);
            RunTracker.RecordBloodPotionHealing(
                potion,
                state.Player,
                state.InitialHp,
                state.Target?.CurrentHp ?? state.InitialHp);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BloodPotionHistoryHealingPatch.ObserveAsync failed: {e.Message}");
            throw;
        }
    }

    public readonly record struct BloodPotionUseState(
        Player? Player,
        Creature? Target,
        int InitialHp);
}

/// <summary>
/// Swift Potion owns one non-hand draw on its branching choice context. The
/// returned card collection is the observed outcome; the shortfall from the
/// requested count is retained as blocked draw value.
/// </summary>
[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.Draw),
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(decimal),
        typeof(Player),
        typeof(bool),
    })]
public static class SwiftPotionHistoryDrawPatch
{
    private static readonly HashSet<PlayerChoiceContext> ClaimedContexts =
        new(ReferenceEqualityComparer.Instance);

    [HarmonyPrefix]
    public static void Prefix(
        PlayerChoiceContext choiceContext,
        decimal count,
        Player player,
        bool fromHandDraw,
        out SwiftPotionDrawState? __state)
    {
        __state = null;

        try
        {
            if (fromHandDraw || player == null) return;
            if (choiceContext is not BranchingPlayerChoiceContext
                {
                    Source: SwiftPotion potion,
                    LastInvolvedModel: SwiftPotion involvedPotion,
                })
                return;
            if (!ReferenceEquals(potion, involvedPotion)) return;

            lock (ClaimedContexts)
            {
                if (!ClaimedContexts.Add(choiceContext)) return;
            }

            __state = new SwiftPotionDrawState(
                potion,
                choiceContext,
                count > 0m ? (int)Math.Ceiling(count) : 0);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SwiftPotionHistoryDrawPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        SwiftPotionDrawState? __state,
        Task<IEnumerable<CardModel>> __result)
    {
        if (__state == null) return;
        if (__result == null)
        {
            ReleaseContext(__state.Context);
            return;
        }

        ObserveDrawAsync(__state, __result);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        SwiftPotionDrawState? __state)
    {
        if (__exception != null && __state != null)
            ReleaseContext(__state.Context);
        return __exception;
    }

    private static async void ObserveDrawAsync(
        SwiftPotionDrawState state,
        Task<IEnumerable<CardModel>> inner)
    {
        try
        {
            var cards = await inner.ConfigureAwait(false);
            RunTracker.RecordSwiftPotionDraw(
                state.Potion,
                state.CardsRequested,
                cards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"SwiftPotionHistoryDrawPatch.ObserveDrawAsync failed: {e.Message}");
        }
        finally
        {
            ReleaseContext(state.Context);
        }
    }

    private static void ReleaseContext(PlayerChoiceContext context)
    {
        lock (ClaimedContexts)
            ClaimedContexts.Remove(context);
    }

    public sealed record SwiftPotionDrawState(
        SwiftPotion Potion,
        PlayerChoiceContext Context,
        int CardsRequested);
}

/// <summary>
/// Fortifier does not pass its choice context into GainBlock. Keep its exact
/// direct call identifiable across the command's awaits so the observed block
/// history entry can own a potion-ledger chunk.
/// </summary>
[HarmonyPatch]
public static class FortifierHistoryUsePatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(
            typeof(Fortifier),
            "OnUse",
            [typeof(PlayerChoiceContext), typeof(Creature)]);

    [HarmonyPrefix]
    public static void Prefix(
        Fortifier __instance,
        Creature target,
        out FortifierUseFrame? __state)
    {
        __state = null;

        try
        {
            if (__instance?.Owner == null || target == null) return;
            __state = FortifierBlockFrameTracker.BeginUse(__instance, target);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"FortifierHistoryUsePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(FortifierUseFrame? __state)
    {
        if (__state != null)
            FortifierBlockFrameTracker.EndUse(__state);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        FortifierUseFrame? __state)
    {
        if (__exception != null && __state != null)
            FortifierBlockFrameTracker.EndUse(__state);
        return __exception;
    }
}

[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.GainBlock),
    new[]
    {
        typeof(Creature),
        typeof(decimal),
        typeof(ValueProp),
        typeof(CardPlay),
        typeof(bool),
    })]
public static class FortifierCreatureGainBlockHistoryPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Creature creature,
        CardPlay? cardPlay,
        out FortifierBlockCommandFrame? __state)
    {
        __state = null;

        try
        {
            __state = FortifierBlockFrameTracker.BeginCommand(
                creature,
                cardPlay);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"FortifierCreatureGainBlockHistoryPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        FortifierBlockCommandFrame? __state,
        ref Task<decimal> __result)
    {
        if (__state == null) return;
        if (__result == null)
        {
            FortifierBlockFrameTracker.EndCommand(__state);
            return;
        }

        __result = ObserveBlockAsync(__state, __result);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        FortifierBlockCommandFrame? __state)
    {
        if (__exception != null && __state != null)
            FortifierBlockFrameTracker.EndCommand(__state);
        return __exception;
    }

    private static async Task<decimal> ObserveBlockAsync(
        FortifierBlockCommandFrame state,
        Task<decimal> inner)
    {
        try
        {
            var blockGained = await inner.ConfigureAwait(false);
            if (state.Potion != null)
                RunTracker.RecordFortifierBlockGain(state.Potion, blockGained);
            return blockGained;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"FortifierCreatureGainBlockHistoryPatch.ObserveBlockAsync failed: {e.Message}");
            throw;
        }
        finally
        {
            FortifierBlockFrameTracker.EndCommand(state);
        }
    }
}

internal static class FortifierBlockFrameTracker
{
    private static readonly AsyncLocal<FortifierUseFrame?> PendingUse = new();
    private static readonly AsyncLocal<Stack<FortifierBlockCommandFrame>?>
        ActiveCommands = new();

    internal static FortifierUseFrame BeginUse(
        Fortifier potion,
        Creature target)
    {
        var frame = new FortifierUseFrame(
            potion,
            target,
            PendingUse.Value);
        PendingUse.Value = frame;
        return frame;
    }

    internal static void EndUse(FortifierUseFrame frame)
    {
        if (ReferenceEquals(PendingUse.Value, frame))
            PendingUse.Value = frame.Previous;
    }

    internal static FortifierBlockCommandFrame BeginCommand(
        Creature creature,
        CardPlay? cardPlay)
    {
        Fortifier? potion = null;
        var pending = PendingUse.Value;
        if (cardPlay == null
            && pending != null
            && ReferenceEquals(pending.Target, creature))
        {
            potion = pending.Potion;
            PendingUse.Value = pending.Previous;
        }

        var commands = ActiveCommands.Value;
        if (commands == null)
        {
            commands = new Stack<FortifierBlockCommandFrame>();
            ActiveCommands.Value = commands;
        }

        var frame = new FortifierBlockCommandFrame(potion, creature);
        commands.Push(frame);
        return frame;
    }

    internal static void EndCommand(FortifierBlockCommandFrame frame)
    {
        var commands = ActiveCommands.Value;
        if (commands == null
            || commands.Count == 0
            || !ReferenceEquals(commands.Peek(), frame))
        {
            return;
        }

        commands.Pop();
        if (commands.Count == 0)
            ActiveCommands.Value = null;
    }

    internal static bool TryGetActiveFortifier(
        Creature creature,
        out Fortifier? potion)
    {
        potion = null;
        var commands = ActiveCommands.Value;
        if (commands == null || commands.Count == 0) return false;

        var frame = commands.Peek();
        if (frame.Potion == null
            || !ReferenceEquals(frame.Creature, creature))
        {
            return false;
        }

        potion = frame.Potion;
        return true;
    }
}

public sealed record FortifierUseFrame(
    Fortifier Potion,
    Creature Target,
    FortifierUseFrame? Previous);

public sealed record FortifierBlockCommandFrame(
    Fortifier? Potion,
    Creature Creature);

/// <summary>
/// Explosive Ampoule passes its branching choice context into one multi-target
/// damage command while the potion remains the last involved model. That
/// context identifies the exact potion without a dealer-wide attribution
/// window, and the command result supplies the observed AOE damage split.
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
        typeof(CardModel),
        typeof(CardPlay),
    })]
public static class ExplosiveAmpouleHistoryDamagePatch
{
    private static readonly HashSet<PlayerChoiceContext> ClaimedContexts =
        new(ReferenceEqualityComparer.Instance);

    [HarmonyPrefix]
    public static void Prefix(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        out ExplosiveAmpouleDamageState? __state)
    {
        __state = null;

        try
        {
            if (choiceContext is not BranchingPlayerChoiceContext
                {
                    Source: ExplosiveAmpoule potion,
                    LastInvolvedModel: ExplosiveAmpoule involvedPotion,
                })
                return;
            if (!ReferenceEquals(potion, involvedPotion)) return;
            if (!ReferenceEquals(potion.Owner?.Creature, dealer)) return;
            if (cardSource != null || cardPlay != null) return;

            lock (ClaimedContexts)
            {
                if (!ClaimedContexts.Add(choiceContext)) return;
            }

            __state = new ExplosiveAmpouleDamageState(potion, choiceContext);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ExplosiveAmpouleHistoryDamagePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        ExplosiveAmpouleDamageState? __state,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (__state == null) return;
        if (__result == null)
        {
            ReleaseContext(__state.Context);
            return;
        }

        __result = ObserveDamageAsync(__state, __result);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        ExplosiveAmpouleDamageState? __state)
    {
        if (__exception != null && __state != null)
            ReleaseContext(__state.Context);
        return __exception;
    }

    private static async Task<IEnumerable<DamageResult>> ObserveDamageAsync(
        ExplosiveAmpouleDamageState state,
        Task<IEnumerable<DamageResult>> inner)
    {
        try
        {
            var results = await inner.ConfigureAwait(false);
            var materialized = results as IReadOnlyList<DamageResult>
                ?? new List<DamageResult>(results);
            RunTracker.RecordExplosiveAmpouleDamage(state.Potion, materialized);
            return materialized;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ExplosiveAmpouleHistoryDamagePatch.ObserveDamageAsync failed: {e.Message}");
            throw;
        }
        finally
        {
            ReleaseContext(state.Context);
        }
    }

    private static void ReleaseContext(PlayerChoiceContext context)
    {
        lock (ClaimedContexts)
            ClaimedContexts.Remove(context);
    }

    public sealed record ExplosiveAmpouleDamageState(
        ExplosiveAmpoule Potion,
        PlayerChoiceContext Context);
}

[HarmonyPatch(typeof(Player), "DiscardPotionInternal")]
public static class PotionHistoryDiscardedPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player __instance, PotionModel potion)
    {
        PatchGuard.Run(nameof(PotionHistoryDiscardedPatch), () =>
        {
            RunTracker.RecordPotionDiscarded(__instance, potion);
        });
    }
}

/// <summary>
/// CreateIcon is the first reward boundary where a populated concrete potion
/// is actually presented to the player. Constructor/Populate alone can occur
/// before the outer reward page is visible.
/// </summary>
[HarmonyPatch(typeof(PotionReward), nameof(PotionReward.CreateIcon))]
public static class PotionHistoryRewardSeenPatch
{
    [HarmonyPostfix]
    public static void Postfix(PotionReward __instance)
    {
        PatchGuard.Run(nameof(PotionHistoryRewardSeenPatch), () =>
        {
            RunTracker.RecordPotionOffer(
                __instance,
                __instance.Potion,
                __instance.Player,
                "Potion reward");
        });
    }
}

/// <summary>
/// MerchantPotionEntry.FillSlot has selected the concrete stocked potion and
/// is also where the game marks it as seen. Recording after this method keeps
/// shop inventory generation and SpireLens's left lane aligned.
/// </summary>
[HarmonyPatch(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.FillSlot))]
public static class PotionHistoryShopSeenPatch
{
    [HarmonyPostfix]
    public static void Postfix(MerchantPotionEntry __instance)
    {
        PatchGuard.Run(nameof(PotionHistoryShopSeenPatch), () =>
        {
            RunTracker.RecordPotionOffer(
                __instance,
                __instance.Model,
                __instance._player,
                "Shop");
        });
    }
}
