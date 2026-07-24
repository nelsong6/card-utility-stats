using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace SpireLens.Core.Patches;

/// <summary>
/// Keeps the shared SpireLens panel on the same owner-scoped lifecycle as the
/// game's native hover tips. A newly created native tooltip replaces any
/// previous SpireLens owner, while removal can hide only the panel belonging
/// to the control being removed.
/// </summary>
[HarmonyPatch(
    typeof(NHoverTipSet),
    nameof(NHoverTipSet.CreateAndShow),
    new[]
    {
        typeof(Control),
        typeof(IEnumerable<IHoverTip>),
        typeof(HoverTipAlignment),
    })]
internal static class NativeHoverTipCreateStatsLifecyclePatch
{
    [HarmonyPrefix]
    public static void Prefix(Control owner)
    {
        try { StatsTooltip.BeginNativeHover(owner); }
        catch (Exception e) { CoreMain.Logger.Error($"Native hover begin failed: {e.Message}"); }
    }
}

[HarmonyPatch(
    typeof(NHoverTipSet),
    nameof(NHoverTipSet.CreateAndShowMapPointHistory),
    new[] { typeof(Control), typeof(NMapPointHistoryHoverTip) })]
internal static class NativeMapHistoryHoverCreateStatsLifecyclePatch
{
    [HarmonyPrefix]
    public static void Prefix(Control owner)
    {
        try { StatsTooltip.BeginNativeHover(owner); }
        catch (Exception e) { CoreMain.Logger.Error($"Native map hover begin failed: {e.Message}"); }
    }
}

[HarmonyPatch(
    typeof(NHoverTipSet),
    nameof(NHoverTipSet.Remove),
    new[] { typeof(Control) })]
internal static class NativeHoverTipRemoveStatsLifecyclePatch
{
    [HarmonyPrefix]
    public static void Prefix(Control owner)
    {
        try { StatsTooltip.HideIfAnchoredTo(owner); }
        catch (Exception e) { CoreMain.Logger.Error($"Native hover removal failed: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.Clear))]
internal static class NativeHoverTipClearStatsLifecyclePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try { StatsTooltip.Hide(); }
        catch (Exception e) { CoreMain.Logger.Error($"Native hover clear failed: {e.Message}"); }
    }
}
