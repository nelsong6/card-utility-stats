using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core;

internal sealed record OrbPowerDefinition(
    string CardId,
    string PowerId,
    string DisplayName,
    string[] OrbIds);

/// <summary>
/// Defines the Defect cards whose own play or persistent Power successfully
/// channels orbs. The expected orb ids let an unused card render the same
/// zero-state lifecycle rows as a card that has already produced an orb.
/// </summary>
internal static class OrbCardRegistry
{
    internal const string DarkOrbId = "ORB.DARK_ORB";
    internal const string FrostOrbId = "ORB.FROST_ORB";
    internal const string GlassOrbId = "ORB.GLASS_ORB";
    internal const string LightningOrbId = "ORB.LIGHTNING_ORB";
    internal const string PlasmaOrbId = "ORB.PLASMA_ORB";

    internal static readonly string[] AllOrbIds =
    [
        DarkOrbId,
        FrostOrbId,
        GlassOrbId,
        LightningOrbId,
        PlasmaOrbId,
    ];

    private static readonly IReadOnlyDictionary<string, string[]> DirectByCardId =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CARD.BALL_LIGHTNING"] = [LightningOrbId],
            ["CARD.CHAOS"] = AllOrbIds,
            ["CARD.CHILL"] = [FrostOrbId],
            ["CARD.COLD_SNAP"] = [FrostOrbId],
            ["CARD.CONSUMING_SHADOW"] = [DarkOrbId],
            ["CARD.COOLHEADED"] = [FrostOrbId],
            ["CARD.DARKNESS"] = [DarkOrbId],
            ["CARD.FUSION"] = [PlasmaOrbId],
            ["CARD.GLACIER"] = [FrostOrbId],
            ["CARD.GLASSWORK"] = [GlassOrbId],
            ["CARD.HIBERNATE"] = [FrostOrbId],
            ["CARD.ICE_LANCE"] = [FrostOrbId],
            ["CARD.IGNITION"] = [PlasmaOrbId],
            ["CARD.METEOR_STRIKE"] = [PlasmaOrbId],
            ["CARD.NULL"] = [DarkOrbId],
            ["CARD.RAINBOW"] = [LightningOrbId, FrostOrbId, DarkOrbId],
            ["CARD.REFRACT"] = [GlassOrbId],
            ["CARD.SHADOW_SHIELD"] = [DarkOrbId],
            ["CARD.SPINNER"] = [GlassOrbId],
            ["CARD.TEMPEST"] = [LightningOrbId],
            ["CARD.VOLTAIC"] = [LightningOrbId],
            ["CARD.ZAP"] = [LightningOrbId],
        };

    private static readonly OrbPowerDefinition[] RecurringPowerDefinitions =
    [
        new("CARD.LIGHTNING_ROD", "POWER.LIGHTNING_ROD", "Lightning Rod", [LightningOrbId]),
        new("CARD.SPINNER", "POWER.SPINNER", "Spinner", [GlassOrbId]),
        new("CARD.STORM", "POWER.STORM", "Storm", [LightningOrbId]),
        new("CARD.TRASH_TO_TREASURE", "POWER.TRASH_TO_TREASURE", "Trash to Treasure", AllOrbIds),
    ];

    private static readonly IReadOnlyDictionary<string, OrbPowerDefinition>
        RecurringByPowerId = BuildRecurringIndex(definition => definition.PowerId);

    private static readonly IReadOnlyDictionary<string, OrbPowerDefinition>
        RecurringByCardId = BuildRecurringIndex(definition => definition.CardId);

    internal static bool IsDirectGenerator(CardModel? card)
        => TryGetCardId(card, out var cardId)
           && DirectByCardId.ContainsKey(cardId);

    internal static IReadOnlyList<string> GetExpectedOrbIds(CardModel? card)
        => TryGetCardId(card, out var cardId)
           && DirectByCardId.TryGetValue(cardId, out var orbIds)
            ? orbIds
            : Array.Empty<string>();

    internal static bool IsRecurringPowerId(string? powerId)
        => !string.IsNullOrWhiteSpace(powerId)
           && RecurringByPowerId.ContainsKey(powerId);

    internal static IReadOnlyList<string> GetExpectedOrbIdsForPower(
        string? powerId)
        => !string.IsNullOrWhiteSpace(powerId)
           && RecurringByPowerId.TryGetValue(powerId, out var definition)
            ? definition.OrbIds
            : Array.Empty<string>();

    internal static bool TryGetRecurringByCard(
        CardModel? card,
        [NotNullWhen(true)] out OrbPowerDefinition? definition)
    {
        definition = null;
        return TryGetCardId(card, out var cardId)
               && RecurringByCardId.TryGetValue(cardId, out definition);
    }

    internal static bool TryGetRecurringByPower(
        PowerModel? power,
        [NotNullWhen(true)] out OrbPowerDefinition? definition)
    {
        definition = null;
        if (power == null) return false;

        try
        {
            return RecurringByPowerId.TryGetValue(
                power.Id.ToString(),
                out definition);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, OrbPowerDefinition>
        BuildRecurringIndex(Func<OrbPowerDefinition, string> keySelector)
    {
        var result = new Dictionary<string, OrbPowerDefinition>(
            StringComparer.Ordinal);
        foreach (var definition in RecurringPowerDefinitions)
            result[keySelector(definition)] = definition;
        return result;
    }

    private static bool TryGetCardId(CardModel? card, out string cardId)
    {
        cardId = "";
        if (card == null) return false;

        try
        {
            cardId = card.Id.ToString();
            return !string.IsNullOrWhiteSpace(cardId);
        }
        catch
        {
            return false;
        }
    }
}
