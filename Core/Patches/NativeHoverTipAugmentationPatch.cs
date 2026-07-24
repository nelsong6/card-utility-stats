using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;

namespace SpireLens.Core.Patches;

/// <summary>
/// Adds SpireLens data to the same IEnumerable&lt;IHoverTip&gt; that the game
/// is about to render. The resulting node is created, positioned, associated
/// with <paramref name="owner"/>, and removed entirely by NHoverTipSet.
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
internal static class NativeHoverTipCreateStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(Control owner, ref IEnumerable<IHoverTip> hoverTips)
    {
        try
        {
            if (!NativeStatsHoverTipFactory.TryCreate(owner, out var statsTip))
                return;

            hoverTips = (hoverTips ?? Enumerable.Empty<IHoverTip>()).Append(statsTip);
        }
        catch (Exception e)
        {
            // Stats presentation must never prevent the game's own tooltip.
            CoreMain.Logger.Error($"Native stats hover-tip creation failed: {e}");
        }
    }
}

internal static class NativeStatsHoverTipFactory
{
    public static bool TryCreate(Control? owner, out IHoverTip statsTip)
    {
        statsTip = default!;
        if (owner == null || !ViewStatsInjectorPatch.StatsVisibilityEnabled)
            return false;

        HoverTip tip;
        switch (owner)
        {
            case NCardHolder cardHolder
                when CardHoverShowPatch.TryBuildNativeHoverTip(cardHolder, out tip):
                statsTip = tip;
                return true;

            case NRelicInventoryHolder relicHolder
                when RelicHoverShowPatch.TryBuildNativeHoverTip(relicHolder, out tip):
                statsTip = tip;
                return true;

            case NCreature creature
                when EnemyHoverShowPatch.TryBuildNativeHoverTip(creature, out tip):
                statsTip = tip;
                return true;

            case NRelicCollectionEntry entry
                when CompendiumRelicStatsContext.TryBuildNativeHoverTip(entry, out tip):
                statsTip = tip;
                return true;

            case NDeckHistoryEntry entry
                when RunHistoryStatsContext.TryBuildNativeCardHoverTip(entry, out tip):
                statsTip = tip;
                return true;

            case NRelicBasicHolder holder
                when RunHistoryStatsContext.TryBuildNativeRelicHoverTip(holder, out tip):
                statsTip = tip;
                return true;

            default:
                return false;
        }
    }
}
