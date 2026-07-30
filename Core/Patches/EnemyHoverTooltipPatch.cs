using System;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SpireLens.Core.Patches;

public static class EnemyHoverShowPatch
{
    internal static bool TryBuildNativeHoverTip(
        NCreature creatureNode,
        out HoverTip statsTip)
    {
        statsTip = default;
        var viewStatsEnabled = ViewStatsInjectorPatch.StatsVisibilityEnabled;
        if (!ResolveEnemyStatsEnabled(
                viewStatsEnabled,
                ViewStatsInjectorPatch.EnemyStatsEnabled,
                RuntimeOptionsProvider.Current.ShowEnemyStatsOnHover))
        {
            return false;
        }

        var creature = creatureNode.Entity;
        var monster = creature?.Monster;
        if (monster == null || creature!.IsPlayer) return false;
        if (!ShouldShowForCreature(creatureNode, creature)) return false;

        var enemyId = monster.Id.ToString();
        var agg = RunTracker.GetEnemyAggregate(enemyId) ?? new EnemyAggregate
        {
            EnemyId = enemyId,
            DisplayName = FormatEnemyIdForDisplay(enemyId),
        };

        var title = string.IsNullOrWhiteSpace(agg.DisplayName)
            ? FormatEnemyIdForDisplay(enemyId)
            : agg.DisplayName;
        statsTip = StatsTooltip.CreateNativeTip(title, BuildEnemyBodyBBCode(agg));
        return true;
    }

    internal static bool ResolveEnemyStatsEnabled(
        bool viewStatsEnabled,
        bool? injectedEnemyToggleState,
        bool persistedEnemyPreference)
        => viewStatsEnabled && (injectedEnemyToggleState ?? persistedEnemyPreference);

    internal static string BuildEnemyBodyBBCode(EnemyAggregate agg)
    {
        var sb = new StringBuilder();

        var blockedPct = agg.DamageAttempted > 0
            ? $"{100f * agg.DamageBlocked / agg.DamageAttempted:F0}%"
            : "";
        Row3(sb, "Damage attempted", agg.DamageAttempted.ToString(), "");
        Row3(sb, "Damage dealt", agg.DamageDealt.ToString(), "");
        Row3(sb, "Damage blocked", agg.DamageBlocked.ToString(), blockedPct);
        Row3(sb, "Damage instances", agg.DamageInstances.ToString(), "");

        if (agg.StatusCardsAdded <= 0)
            return sb.ToString();

        Row3(sb, "Status cards added", agg.StatusCardsAdded.ToString(), "");
        if (agg.StatusCardsAddedToHand > 0)
            Row3(sb, "added to hand", agg.StatusCardsAddedToHand.ToString(), "");
        if (agg.StatusCardsAddedToDraw > 0)
            Row3(sb, "added to draw pile", agg.StatusCardsAddedToDraw.ToString(), "");
        if (agg.StatusCardsAddedToDiscard > 0)
            Row3(sb, "added to discard", agg.StatusCardsAddedToDiscard.ToString(), "");
        if (agg.StatusCardsAddedToDeck > 0)
            Row3(sb, "added to deck", agg.StatusCardsAddedToDeck.ToString(), "");

        foreach (var card in agg.StatusCardsById.Values
                     .OrderByDescending(card => card.Count)
                     .ThenBy(card => card.DisplayName))
        {
            if (card.Count <= 0) continue;
            var label = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(card.DisplayName)
                ? RunTracker.FormatCardIdForDisplay(card.CardId)
                : card.DisplayName);
            Row3(sb, label, card.Count.ToString(), "");
        }

        return sb.ToString();
    }

    private static string BuildEnemyDamageBodyBBCode(EnemyAggregate agg)
    {
        return BuildEnemyBodyBBCode(agg);
    }

    internal static bool ShouldShowForCreature(NCreature node, MegaCrit.Sts2.Core.Entities.Creatures.Creature creature)
    {
        if (node == null || creature == null) return false;
        if (creature.IsDead || !creature.IsAlive) return false;
        if (node.IsPlayingDeathAnimation) return false;
        if (!node.IsInteractable) return false;
        return true;
    }

    private static string FormatEnemyIdForDisplay(string enemyId)
    {
        var value = enemyId;
        const string prefix = "MONSTER.";
        if (value.StartsWith(prefix, StringComparison.Ordinal))
            value = value[prefix.Length..];

        return string.Join(" ", value
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0
                ? part
                : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static void Row3(StringBuilder sb, string label, string value, string pct)
    {
        sb.Append("[table=3]");
        sb.Append($"[cell expand=4 padding=0,0,12,0][color=#e0e0e0]{label}[/color][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,12,0][right][b]{value}[/b][/right][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,4,0][right][color=#b5b5b5]{pct}[/color][/right][/cell]");
        sb.Append("[/table]\n");
    }
}
