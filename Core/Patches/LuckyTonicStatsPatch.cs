using System;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;

namespace SpireLens.Core.Patches;

internal sealed class LuckyTonicUseFrame
{
    public required PotionModel Potion { get; init; }
    public required Creature Target { get; init; }
    public LuckyTonicUseFrame? Previous { get; init; }
}

/// <summary>
/// Keeps the used Lucky Tonic recoverable while its Buffer application
/// resolves. <c>LuckyTonic.OnUse</c> calls <c>PowerCmd.Apply</c> with a null
/// card source, so without this frame the resulting charges would arrive with
/// no way to tell them from the Buffer card's.
/// </summary>
internal static class LuckyTonicFrameTracker
{
    private static readonly AsyncLocal<LuckyTonicUseFrame?> PendingUse = new();

    internal static LuckyTonicUseFrame BeginUse(
        PotionModel potion,
        Creature target)
    {
        var frame = new LuckyTonicUseFrame
        {
            Potion = potion,
            Target = target,
            Previous = PendingUse.Value,
        };
        PendingUse.Value = frame;
        return frame;
    }

    internal static void EndUse(LuckyTonicUseFrame frame)
    {
        if (ReferenceEquals(PendingUse.Value, frame))
            PendingUse.Value = frame.Previous;
    }

    internal static bool TryGetActivePotion(
        Creature? target,
        out PotionModel? potion)
    {
        potion = null;
        var pending = PendingUse.Value;
        if (pending == null || target == null) return false;
        if (!ReferenceEquals(pending.Target, target)) return false;

        potion = pending.Potion;
        return true;
    }
}

[HarmonyPatch(
    typeof(LuckyTonic),
    "OnUse",
    new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Creature),
    })]
internal static class LuckyTonicUseStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        LuckyTonic __instance,
        Creature target,
        out LuckyTonicUseFrame? __state)
    {
        __state = null;

        try
        {
            if (__instance?.Owner == null || target == null) return;
            __state = LuckyTonicFrameTracker.BeginUse(__instance, target);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"LuckyTonicUseStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(LuckyTonicUseFrame? __state)
    {
        if (__state != null)
            LuckyTonicFrameTracker.EndUse(__state);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception? __exception,
        LuckyTonicUseFrame? __state)
    {
        if (__exception != null && __state != null)
            LuckyTonicFrameTracker.EndUse(__state);
        return __exception;
    }
}
