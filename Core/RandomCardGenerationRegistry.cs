using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

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

    // Ids come from the game types rather than copied strings — see ModelIds.
    // These are compared against MetaPowerRegistry power ids, which are now
    // the canonical runtime ids, so a shortened form here would silently stop
    // matching.
    private static readonly Lazy<HashSet<string>> LazyRecurringPowerIds =
        new(BuildRecurringPowerIds);

    private static HashSet<string> RecurringPowerIds =>
        LazyRecurringPowerIds.Value;

    private static HashSet<string> BuildRecurringPowerIds()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        Add<CalamityPower>();
        Add<CallOfTheVoidPower>();
        Add<CreativeAiPower>();
        Add<HelloWorldPower>();
        Add<SpectrumShiftPower>();

        return result;

        void Add<TPower>()
            where TPower : PowerModel
        {
            var powerId = ModelIds.TryGet<TPower>();
            if (powerId == null)
            {
                CoreMain.LogDebug(
                    "RandomCardGenerationRegistry skipped "
                    + typeof(TPower).Name);
                return;
            }

            result.Add(powerId);
        }
    }

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
