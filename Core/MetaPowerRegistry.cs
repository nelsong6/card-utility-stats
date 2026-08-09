using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using BufferCard = MegaCrit.Sts2.Core.Models.Cards.Buffer;

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
    // Ids come from the game types, never from copied strings — see ModelIds
    // for why. Lazy so the first touch happens after CoreMain's logger exists,
    // and so a failure surfaces at a stat lookup rather than at class load.
    private static readonly Lazy<IReadOnlyList<MetaPowerDefinition>> LazyAll =
        new(BuildAll);

    internal static IReadOnlyList<MetaPowerDefinition> All => LazyAll.Value;

    private static readonly Lazy<Dictionary<string, MetaPowerDefinition>>
        LazyByCardId = new(() => BuildIndex(definition => definition.CardId));

    private static readonly Lazy<Dictionary<string, MetaPowerDefinition>>
        LazyByPowerId = new(() => BuildIndex(definition => definition.PowerId));

    private static Dictionary<string, MetaPowerDefinition> ByCardId =>
        LazyByCardId.Value;

    private static Dictionary<string, MetaPowerDefinition> ByPowerId =>
        LazyByPowerId.Value;

    private static IReadOnlyList<MetaPowerDefinition> BuildAll()
    {
        var result = new List<MetaPowerDefinition>();

        // Alphabetical by card. NotInDeckViewTests pins this order.
        Add<Aggression, AggressionPower>("Aggression");
        Add<BufferCard, BufferPower>("Buffer");
        Add<Calamity, CalamityPower>("Calamity");
        Add<CallOfTheVoid, CallOfTheVoidPower>("Call of the Void");
        Add<ConsumingShadow, ConsumingShadowPower>("Consuming Shadow");
        Add<CreativeAi, CreativeAiPower>("Creative AI");
        Add<DanseMacabre, DanseMacabrePower>("Danse Macabre");
        Add<DarkEmbrace, DarkEmbracePower>("Dark Embrace");
        Add<Entropy, EntropyPower>("Entropy");
        Add<FeelNoPain, FeelNoPainPower>("Feel No Pain");
        Add<HelloWorld, HelloWorldPower>("Hello World");
        Add<Juggling, JugglingPower>("Juggling");
        Add<Rupture, RupturePower>("Rupture");
        Add<SpectrumShift, SpectrumShiftPower>("Spectrum Shift");
        Add<Spinner, SpinnerPower>("Spinner");
        Add<Stampede, StampedePower>("Stampede");
        Add<Storm, StormPower>("Storm");
        Add<TrashToTreasure, TrashToTreasurePower>("Trash to Treasure");
        Add<Unmovable, UnmovablePower>("Unmovable");
        Add<Vicious, ViciousPower>("Vicious");

        return result;

        void Add<TCard, TPower>(string displayName)
            where TCard : CardModel
            where TPower : PowerModel
        {
            var cardId = ModelIds.TryGet<TCard>();
            var powerId = ModelIds.TryGet<TPower>();
            if (cardId == null || powerId == null)
            {
                CoreMain.LogDebug(
                    $"MetaPowerRegistry skipped {displayName}: "
                    + $"card={cardId ?? "null"} power={powerId ?? "null"}");
                return;
            }

            result.Add(new MetaPowerDefinition(cardId, powerId, displayName));
        }
    }

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

    /// <summary>
    /// Power id for a registered card id, for the few tracker and tooltip call
    /// sites that know the card but need the aggregate key. Returns null when
    /// the card is not a registered meta power.
    /// </summary>
    internal static string? PowerIdForCardId(string? cardId)
        => TryGetByCardId(cardId, out var definition)
            ? definition.PowerId
            : null;

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
