using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core;

/// <summary>
/// The cards whose defining outcome is a randomly selected combat card.
/// Fixed-token creation, exact copies, relic generation, and potion generation
/// deliberately live in their own attribution families.
/// </summary>
internal static class RandomCardGenerationRegistry
{
    private static readonly HashSet<string> DirectCardIds = new(
        StringComparer.Ordinal)
    {
        "CARD.ABUNDANCE",
        "CARD.BUNDLE_OF_JOY",
        "CARD.DISCOVERY",
        "CARD.DISTRACTION",
        "CARD.INFERNAL_BLADE",
        "CARD.JACK_OF_ALL_TRADES",
        "CARD.JACKPOT",
        "CARD.LARGESSE",
        "CARD.MAD_SCIENCE",
        "CARD.MANIFEST_AUTHORITY",
        "CARD.METAMORPHOSIS",
        "CARD.QUASAR",
        "CARD.SPLASH",
        "CARD.STOKE",
        "CARD.WHITE_NOISE",
    };

    private static readonly HashSet<string> RecurringPowerIds = new(
        StringComparer.Ordinal)
    {
        "POWER.CALAMITY",
        "POWER.CALL_OF_THE_VOID",
        "POWER.CREATIVE_AI",
        "POWER.HELLO_WORLD",
        "POWER.SPECTRUM_SHIFT",
    };

    internal static bool IsDirectGenerator(CardModel? card)
    {
        if (card == null) return false;

        try
        {
            return DirectCardIds.Contains(card.Id.ToString());
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsRecurringPowerId(string? powerId)
        => !string.IsNullOrWhiteSpace(powerId)
           && RecurringPowerIds.Contains(powerId);
}
