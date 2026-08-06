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
    internal static readonly string[] AllOrbIds =
    [
        "ORB.DARK",
        "ORB.FROST",
        "ORB.GLASS",
        "ORB.LIGHTNING",
        "ORB.PLASMA",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> DirectByCardId =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CARD.BALL_LIGHTNING"] = ["ORB.LIGHTNING"],
            ["CARD.CHAOS"] = AllOrbIds,
            ["CARD.CHILL"] = ["ORB.FROST"],
            ["CARD.COLD_SNAP"] = ["ORB.FROST"],
            ["CARD.CONSUMING_SHADOW"] = ["ORB.DARK"],
            ["CARD.COOLHEADED"] = ["ORB.FROST"],
            ["CARD.DARKNESS"] = ["ORB.DARK"],
            ["CARD.FUSION"] = ["ORB.PLASMA"],
            ["CARD.GLACIER"] = ["ORB.FROST"],
            ["CARD.GLASSWORK"] = ["ORB.GLASS"],
            ["CARD.HIBERNATE"] = ["ORB.FROST"],
            ["CARD.ICE_LANCE"] = ["ORB.FROST"],
            ["CARD.IGNITION"] = ["ORB.PLASMA"],
            ["CARD.METEOR_STRIKE"] = ["ORB.PLASMA"],
            ["CARD.NULL"] = ["ORB.DARK"],
            ["CARD.RAINBOW"] = ["ORB.LIGHTNING", "ORB.FROST", "ORB.DARK"],
            ["CARD.REFRACT"] = ["ORB.GLASS"],
            ["CARD.SHADOW_SHIELD"] = ["ORB.DARK"],
            ["CARD.SPINNER"] = ["ORB.GLASS"],
            ["CARD.TEMPEST"] = ["ORB.LIGHTNING"],
            ["CARD.VOLTAIC"] = ["ORB.LIGHTNING"],
            ["CARD.ZAP"] = ["ORB.LIGHTNING"],
        };

    private static readonly OrbPowerDefinition[] RecurringPowerDefinitions =
    [
        new("CARD.LIGHTNING_ROD", "POWER.LIGHTNING_ROD", "Lightning Rod", ["ORB.LIGHTNING"]),
        new("CARD.SPINNER", "POWER.SPINNER", "Spinner", ["ORB.GLASS"]),
        new("CARD.STORM", "POWER.STORM", "Storm", ["ORB.LIGHTNING"]),
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
