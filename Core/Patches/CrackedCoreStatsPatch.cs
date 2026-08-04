using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Captures the exact mutable Lightning orb created by Cracked Core's
/// owner-specific turn-one callback.
/// </summary>
[HarmonyPatch(typeof(CrackedCore), nameof(CrackedCore.BeforeSideTurnStart))]
public static class CrackedCoreStartingOrbStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        CrackedCore __instance,
        IReadOnlyList<Creature> participants,
        out StartingOrbState? __state)
    {
        __state = null;

        try
        {
            if (__instance == null || participants == null) return;
            var owner = __instance.Owner;
            var playerCombatState = owner?.PlayerCombatState;
            var orbQueue = playerCombatState?.OrbQueue;
            if (owner == null || playerCombatState == null || orbQueue == null) return;
            if (!RunTracker.IsTrackedRelic(__instance)) return;
            if (!participants.Contains(owner.Creature)) return;
            if (playerCombatState.TurnNumber > 1) return;

            __state = new StartingOrbState(
                __instance,
                orbQueue,
                new HashSet<OrbModel>(
                    orbQueue.Orbs,
                    ReferenceEqualityComparer.Instance));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrackedCoreStartingOrbStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result, StartingOrbState? __state)
    {
        try
        {
            if (__result == null || __state == null) return;
            __result = ObserveAsync(__result, __state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrackedCoreStartingOrbStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, StartingOrbState state)
    {
        await inner;

        try
        {
            var startingOrbs = state.OrbQueue.Orbs
                .Where(orb =>
                    orb is LightningOrb
                    && !state.OrbsBefore.Contains(orb))
                .ToList();
            RunTracker.TrackCrackedCoreStartingOrbs(state.Relic, startingOrbs);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrackedCoreStartingOrbStatsPatch.ObserveAsync failed: {e.Message}");
        }
    }

    public sealed record StartingOrbState(
        CrackedCore Relic,
        OrbQueue OrbQueue,
        IReadOnlySet<OrbModel> OrbsBefore);
}

/// <summary>
/// Counts each completed passive activation of the tracked starting orb,
/// including additional triggers produced by other orb mechanics.
/// </summary>
[HarmonyPatch(typeof(LightningOrb), nameof(LightningOrb.Passive))]
public static class CrackedCoreStartingOrbPassiveStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(LightningOrb __instance, out bool __state)
    {
        __state = RunTracker.IsTrackedCrackedCoreStartingOrb(__instance);
        CoreMain.Logger.Info(
            $"[CrackedCore-diag] LightningOrb.Passive entered orb_ref={RuntimeHelpers.GetHashCode(__instance)} tracked={__state}");
    }

    [HarmonyPostfix]
    public static void Postfix(
        LightningOrb __instance,
        bool __state,
        ref Task __result)
    {
        try
        {
            if (!__state || __result == null) return;
            __result = ObserveAsync(__result, __instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrackedCoreStartingOrbPassiveStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task ObserveAsync(Task inner, LightningOrb orb)
    {
        await inner;
        RunTracker.RecordCrackedCoreStartingOrbPassive(orb);
    }
}

/// <summary>
/// Counts every completed evoke of the tracked starting orb. Multi-evoke
/// effects deliberately count once per actual Evoke call.
/// </summary>
[HarmonyPatch(typeof(LightningOrb), nameof(LightningOrb.Evoke))]
public static class CrackedCoreStartingOrbEvokeStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(LightningOrb __instance, out bool __state)
    {
        __state = RunTracker.IsTrackedCrackedCoreStartingOrb(__instance);
    }

    [HarmonyPostfix]
    public static void Postfix(
        LightningOrb __instance,
        bool __state,
        ref Task<IEnumerable<Creature>> __result)
    {
        try
        {
            if (!__state || __result == null) return;
            __result = ObserveAsync(__result, __instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"CrackedCoreStartingOrbEvokeStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task<IEnumerable<Creature>> ObserveAsync(
        Task<IEnumerable<Creature>> inner,
        LightningOrb orb)
    {
        var targets = await inner;
        RunTracker.RecordCrackedCoreStartingOrbEvoked(orb);
        return targets;
    }
}

/// <summary>
/// Establishes a synchronous scope around Lightning's private damage routine.
/// The routine invokes CreatureCmd.Damage before reaching its first await, so
/// the exact command can identify which tracked orb caused it without claiming
/// damage from any other Lightning orb owned at the same time.
/// </summary>
[HarmonyPatch]
public static class CrackedCoreLightningDamageScopePatch
{
    private const string ApplyLightningDamageMethodName = "ApplyLightningDamage";

    [ThreadStatic]
    private static LightningOrb? _activeTrackedOrb;

    private static MethodBase TargetMethod()
    {
        return AccessTools.DeclaredMethod(
                   typeof(LightningOrb),
                   ApplyLightningDamageMethodName,
                   [
                       typeof(decimal),
                       typeof(Creature),
                       typeof(PlayerChoiceContext),
                       typeof(bool),
                   ])
               ?? throw new MissingMethodException(
                   typeof(LightningOrb).FullName,
                   ApplyLightningDamageMethodName);
    }

    [HarmonyPrefix]
    public static void Prefix(LightningOrb __instance, out LightningOrb? __state)
    {
        __state = _activeTrackedOrb;
        _activeTrackedOrb = RunTracker.IsTrackedCrackedCoreStartingOrb(__instance)
            ? __instance
            : null;
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception? __exception, LightningOrb? __state)
    {
        _activeTrackedOrb = __state;
        return __exception;
    }

    internal static LightningOrb? GetActiveTrackedOrb(Creature? dealer)
    {
        var orb = _activeTrackedOrb;
        return orb != null
               && ReferenceEquals(orb.Owner?.Creature, dealer)
            ? orb
            : null;
    }
}

/// <summary>
/// Captures the resolved damage split from the exact CreatureCmd.Damage call
/// made by a tracked Cracked Core Lightning orb.
/// </summary>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    [
        typeof(PlayerChoiceContext),
        typeof(IEnumerable<Creature>),
        typeof(decimal),
        typeof(ValueProp),
        typeof(Creature),
    ])]
public static class CrackedCoreLightningDamageResultPatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature dealer, out LightningOrb? __state)
    {
        __state = CrackedCoreLightningDamageScopePatch.GetActiveTrackedOrb(dealer);
    }

    [HarmonyPostfix]
    public static void Postfix(
        LightningOrb? __state,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (__state == null || __result == null) return;
        __result = ObserveDamageAsync(__result, __state);
    }

    private static async Task<IEnumerable<DamageResult>> ObserveDamageAsync(
        Task<IEnumerable<DamageResult>> inner,
        LightningOrb orb)
    {
        try
        {
            var results = await inner.ConfigureAwait(false);
            var materialized = results as IReadOnlyList<DamageResult>
                ?? results.ToList();
            RunTracker.RecordCrackedCoreStartingOrbDamage(orb, materialized);
            return materialized;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"CrackedCoreLightningDamageResultPatch observation failed: {e.Message}");
            throw;
        }
    }
}

/// <summary>
/// Orb-slot loss is the game's non-evoke removal path. Compare tracked
/// starting-relic orb references around it so ordinary combat cleanup
/// (OrbQueue.Clear) is not mislabeled as a fizzle.
/// </summary>
[HarmonyPatch(typeof(OrbQueue), nameof(OrbQueue.RemoveCapacity))]
public static class StartingRelicOrbFizzleStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(OrbQueue __instance, out IReadOnlyList<OrbModel> __state)
    {
        __state = __instance.Orbs
            .Where(orb =>
                RunTracker.IsTrackedCrackedCoreStartingOrb(orb)
                || RunTracker.IsTrackedSymbioticVirusStartingOrb(orb)
                || RunTracker.IsTrackedCardSourcedOrb(orb))
            .ToList();
    }

    [HarmonyPostfix]
    public static void Postfix(OrbQueue __instance, IReadOnlyList<OrbModel> __state)
    {
        try
        {
            if (__state.Count == 0) return;

            var removedOrbs = __state
                .Where(trackedOrb =>
                    !__instance.Orbs.Any(currentOrb =>
                        ReferenceEquals(currentOrb, trackedOrb)))
                .ToList();
            RunTracker.RecordCrackedCoreStartingOrbsFizzled(removedOrbs);
            RunTracker.RecordSymbioticVirusStartingOrbsFizzled(removedOrbs);
            RunTracker.RecordCardSourcedOrbsFizzled(removedOrbs);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"StartingRelicOrbFizzleStatsPatch.Postfix failed: {e.Message}");
        }
    }
}
