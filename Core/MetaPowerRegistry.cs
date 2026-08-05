using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core;

/// <summary>
/// One pooled stats identity for a Power card and the persistent combat power
/// it creates. Card ids drive deck/play membership; power ids drive outcomes.
/// </summary>
internal sealed record MetaPowerDefinition(
    string CardId,
    string PowerId,
    string DisplayName);

internal static class MetaPowerRegistry
{
    internal static readonly IReadOnlyList<MetaPowerDefinition> All =
    [
        new("CARD.AGGRESSION", "POWER.AGGRESSION", "Aggression"),
        new("CARD.CALAMITY", "POWER.CALAMITY", "Calamity"),
        new("CARD.CALL_OF_THE_VOID", "POWER.CALL_OF_THE_VOID", "Call of the Void"),
        new("CARD.CREATIVE_AI", "POWER.CREATIVE_AI", "Creative AI"),
        new("CARD.DANSE_MACABRE", "POWER.DANSE_MACABRE", "Danse Macabre"),
        new("CARD.DARK_EMBRACE", "POWER.DARK_EMBRACE", "Dark Embrace"),
        new("CARD.ENTROPY", "POWER.ENTROPY", "Entropy"),
        new("CARD.FEEL_NO_PAIN", "POWER.FEEL_NO_PAIN", "Feel No Pain"),
        new("CARD.HELLO_WORLD", "POWER.HELLO_WORLD", "Hello World"),
        new("CARD.JUGGLING", "POWER.JUGGLING", "Juggling"),
        new("CARD.RUPTURE", "POWER.RUPTURE", "Rupture"),
        new("CARD.SPECTRUM_SHIFT", "POWER.SPECTRUM_SHIFT", "Spectrum Shift"),
        new("CARD.STAMPEDE", "POWER.STAMPEDE", "Stampede"),
        new("CARD.UNMOVABLE", "POWER.UNMOVABLE", "Unmovable"),
        new("CARD.VICIOUS", "POWER.VICIOUS", "Vicious"),
    ];

    private static readonly Dictionary<string, MetaPowerDefinition> ByCardId =
        BuildIndex(definition => definition.CardId);

    private static readonly Dictionary<string, MetaPowerDefinition> ByPowerId =
        BuildIndex(definition => definition.PowerId);

    internal static bool TryGetByCard(
        CardModel? card,
        [NotNullWhen(true)] out MetaPowerDefinition? definition)
    {
        definition = null;
        if (card == null) return false;

        try
        {
            return ByCardId.TryGetValue(card.Id.ToString(), out definition);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryGetByCardId(
        string? cardId,
        [NotNullWhen(true)] out MetaPowerDefinition? definition)
    {
        definition = null;
        return !string.IsNullOrWhiteSpace(cardId)
            && ByCardId.TryGetValue(cardId, out definition);
    }

    internal static bool TryGetByPower(
        PowerModel? power,
        [NotNullWhen(true)] out MetaPowerDefinition? definition)
    {
        definition = null;
        if (power == null) return false;

        try
        {
            return ByPowerId.TryGetValue(power.Id.ToString(), out definition);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryGetByPowerId(
        string? powerId,
        [NotNullWhen(true)] out MetaPowerDefinition? definition)
    {
        definition = null;
        return !string.IsNullOrWhiteSpace(powerId)
            && ByPowerId.TryGetValue(powerId, out definition);
    }

    private static Dictionary<string, MetaPowerDefinition> BuildIndex(
        Func<MetaPowerDefinition, string> keySelector)
    {
        var result = new Dictionary<string, MetaPowerDefinition>(
            StringComparer.Ordinal);
        foreach (var definition in All)
            result[keySelector(definition)] = definition;
        return result;
    }
}
