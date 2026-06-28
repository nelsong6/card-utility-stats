using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core.Patches;

/// <summary>
/// Counts Prismatic Gem's max-energy benefit at the actual player energy reset
/// boundary. The relic's max-energy modifier can be queried more than once,
/// so RunTracker dedupes this by combat round.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterEnergyReset))]
public static class HookAfterEnergyResetPrismaticGemPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, Player player)
    {
        try
        {
            if (combatState == null || player == null) return;
            if (!player.Relics.Any(r => r is PrismaticGem)) return;

            RunTracker.RecordPrismaticGemEnergyGenerated(combatState, 1);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookAfterEnergyResetPrismaticGemPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Counts each card reward whose creation options Prismatic Gem modifies.
/// This owner-specific hook is the reward-side counterpart to the max-energy
/// reset observation above.
/// </summary>
[HarmonyPatch(typeof(PrismaticGem), nameof(PrismaticGem.ModifyCardRewardCreationOptions))]
public static class PrismaticGemModifyCardRewardCreationOptionsPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player player, CardCreationOptions options, CardCreationOptions __result)
    {
        try
        {
            if (player == null || options == null || __result == null) return;

            RunTracker.RecordPrismaticGemCardRewardAffected();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PrismaticGemModifyCardRewardCreationOptionsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Records the final visible card-option categories in generated card rewards
/// while Prismatic Gem is owned. This is intentionally observed/meta: another
/// reward modifier may also have added colorless or off-class options.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.TryModifyCardRewardOptions))]
public static class HookTryModifyCardRewardOptionsPrismaticGemPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player player, List<CardCreationResult> cardRewardOptions)
    {
        try
        {
            if (player == null || cardRewardOptions == null || cardRewardOptions.Count == 0) return;
            if (!player.Relics.Any(r => r is PrismaticGem)) return;

            var categories = cardRewardOptions
                .Select(option => ToCategory(option.Card))
                .Where(category => !string.IsNullOrWhiteSpace(category.Key))
                .ToList();

            RunTracker.RecordPrismaticGemObservedCardRewardCategories(categories);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HookTryModifyCardRewardOptionsPrismaticGemPatch failed: {e.Message}");
        }
    }

    private static CardRewardCategoryObservation ToCategory(CardModel card)
    {
        try
        {
            var pool = card?.Pool;
            if (pool == null) return new CardRewardCategoryObservation("unknown", "Unknown");
            if (pool.IsColorless) return new CardRewardCategoryObservation("colorless", "Colorless");

            var key = NormalizeKey(pool.Id?.Entry);
            var displayName = string.IsNullOrWhiteSpace(pool.Title) ? ToDisplayName(key) : pool.Title;
            return new CardRewardCategoryObservation(key, displayName);
        }
        catch
        {
            return new CardRewardCategoryObservation("unknown", "Unknown");
        }
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        return value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
    }

    private static string ToDisplayName(string key)
    {
        var words = key.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "Unknown";

        return string.Join(" ", words.Select(word =>
            char.ToUpperInvariant(word[0]) + (word.Length > 1 ? word[1..].ToLowerInvariant() : "")));
    }
}
