using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SpireLens.Core;

internal sealed record RelicStatRowPresentation(
    string Label,
    string FullDescription,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyList<string> DenominatorConceptIds);

/// <summary>
/// Converts the established relic-stat wording into the shared symbol
/// vocabulary. The original wording remains in the information hint while
/// repeated concepts are removed from the visible label.
/// </summary>
internal static class RelicStatRowVocabulary
{
    private const string BlockIconPathFragment = "images/ui/combat/block.png";
    private const string DrawIconPathFragment = "draw_cards_next_turn_power.tres";
    private const string EnergyIconPathFragment = "_energy_icon.png";
    private const string StarIconPathFragment = "star_icon.png";
    private const string VigorIconPathFragment = "vigor_power.tres";
    private const string VulnerableIconPathFragment = "vulnerable_power.tres";
    private const string WeakIconPathFragment = "weak_power.tres";

    private sealed record ConceptRule(
        string Id,
        Regex Pattern,
        bool SupportsScopePrefix);

    private sealed record ConceptOccurrence(
        string Id,
        int Position,
        bool IsDenominator);

    private static readonly Regex LeadingImageRegex = new(
        @"^\s*(?<image>\[img[^\]]*\].*?\[/img\])\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex ImageRegex = new(
        @"\[img[^\]]*\].*?\[/img\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex TagRegex = new(
        @"\[[^\]]+\]",
        RegexOptions.CultureInvariant);

    private static readonly Regex PrecedingConceptPrefixRegex = new(
        @"(?:\b(?:per|in|this)\s*|/\s*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> GainedConceptBaseIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["block_gained"] = "block",
            ["energy_gained"] = "energy",
            ["gold_gained"] = "gold",
            ["max_hp_gained"] = "max_hp",
            ["potion_gained"] = "potion",
            ["relic_gained"] = "relic",
            ["vigor_gained"] = "vigor",
        };

    private static readonly IReadOnlyList<ConceptRule> Rules =
    [
        Rule(
            "block_wasted",
            @"\b(?:block\s+(?:wasted|expired|unused)|(?:wasted|expired|unused)\s+block|excess\s+block)\b"),
        Rule(
            "energy_wasted",
            @"\b(?:energy\s+(?:wasted|unused)|(?:wasted|unused)\s+energy|excess\s+energy)\b"),
        Rule(
            "healing_wasted",
            @"\b(?:(?:hp\s+)?healing\s+wasted|wasted\s+(?:hp\s+)?healing)\b"),
        Rule("max_hp_gained", @"\b(?:max|maximum)\s+hp\s+gained\b"),
        Rule("max_hp", @"\b(?:max|maximum)\s+hp\b"),
        Rule("offered", @"\boffered\b"),
        Rule(
            "healing_blocked",
            @"\b(?:hp\s+)?healing\s+(?:blocked|lost)\b"),
        Rule(
            "healing_gained",
            @"(?<!max\s)\bhp\s+(?:healed|gained|restored)\b|\bhealing\s+(?:gained|restored)\b"),
        Rule("average", @"\b(?:avg|average)\b"),
        Rule("activation", @"\b(?:activations?|activated|triggers?|triggered)\b", true),
        Rule("attack", @"\battacks?\b"),
        Rule("block_gained", @"\bblock\s+gained\b"),
        Rule("block", @"\bblock\b"),
        Rule("card_rare", @"\brare(?:s|\s+cards?)?\b"),
        Rule("card_uncommon", @"\buncommon(?:s|\s+cards?)?\b"),
        Rule("card", @"\bcards?\b|\bcommons?\b"),
        Rule("charge", @"\bcharges?\b"),
        Rule("combat", @"\bcombats?\b", true),
        Rule("damage", @"\bdamage\b"),
        Rule("dexterity_gained", @"\bdexterity\s+(?:added|gained)\b"),
        Rule("dexterity", @"\bdexterity\b"),
        Rule("discard", @"\bdiscard(?:ed|ing|s)?\b"),
        Rule("draw", @"\b(?:draw|drawn|drawing|draws)\b"),
        Rule("energy_gained", @"\benergy\s+gained\b"),
        Rule("energy", @"\benergy\b"),
        Rule("exhaust", @"\bexhaust(?:ed|ing|s)?\b"),
        Rule("floor", @"\bfloors?\b", true),
        Rule("gold_gained", @"\bgold\s+gained\b"),
        Rule("gold", @"\bgold\b"),
        Rule("potion_gained", @"\bpotions?\s+gained\b"),
        Rule("potion", @"\bpotions?\b"),
        Rule("power", @"\bpowers?\b"),
        Rule("relic_gained", @"\brelics?\s+gained\b"),
        Rule("relic", @"\brelics?\b"),
        Rule("skill", @"\bskills?\b"),
        Rule("stars", @"\bstars?\b"),
        Rule("strength_gained", @"\bstrength\s+(?:added|gained)\b"),
        Rule("strength", @"\bstrength\b"),
        Rule("taken", @"(?<!not\s)\btaken\b"),
        Rule("turn", @"\bturns?\b", true),
        Rule("upgraded", @"(?<!non-)\b(?:upgrade|upgraded|upgrades)\b"),
        Rule("vigor_gained", @"\bvigor\s+gained\b"),
        Rule("vigor", @"\bvigor\b"),
        Rule("vulnerable", @"\bvulnerable\b"),
        Rule("wasted", @"\bwast(?:e|ed|ing)\b"),
        Rule("weak", @"\bweak\b"),
    ];

    public static RelicStatRowPresentation Create(
        string? bbcodeLabel,
        string? fullDescription = null)
    {
        var rawLabel = bbcodeLabel ?? string.Empty;
        var suffix = rawLabel;
        var preservedImages = new List<string>();
        var imageMeanings = new List<string>();
        var imageConceptIds = new List<string>();

        while (true)
        {
            var match = LeadingImageRegex.Match(suffix);
            if (!match.Success) break;

            var image = match.Groups["image"].Value;
            suffix = suffix[match.Length..];
            if (TryGetImageConceptId(image, out var conceptId))
            {
                imageConceptIds.Add(conceptId);
                AddImageMeaning(imageMeanings, image);
                continue;
            }

            preservedImages.Add(image);
            AddImageMeaning(imageMeanings, image);
        }

        var plainSuffix = NormalizeSpaces(StripBbcode(suffix));
        var descriptionText = BuildDescriptionText(
            NormalizeSpaces(StripBbcode(rawLabel)),
            plainSuffix,
            imageMeanings);
        var description = string.IsNullOrWhiteSpace(fullDescription)
            ? BuildDefaultDescription(descriptionText)
            : fullDescription.Trim();

        var workingText = plainSuffix;
        var removed = new bool[workingText.Length];
        var occurrences = new List<ConceptOccurrence>();

        foreach (var rule in Rules)
        {
            foreach (Match match in rule.Pattern.Matches(workingText))
            {
                if (!match.Success || OverlapsRemoval(removed, match.Index, match.Length))
                    continue;

                var removalStart = match.Index;
                var isDenominator = false;
                var prefix = workingText[..match.Index];
                var conceptPrefix = PrecedingConceptPrefixRegex.Match(prefix);
                if (conceptPrefix.Success)
                {
                    var conceptPrefixText = conceptPrefix.Value.TrimStart();
                    var hasDenominatorPrefix =
                        conceptPrefixText.StartsWith(
                            "per",
                            StringComparison.OrdinalIgnoreCase)
                        || conceptPrefixText.StartsWith(
                            "/",
                            StringComparison.Ordinal);
                    var hasSupportedScopePrefix =
                        rule.SupportsScopePrefix
                        && (conceptPrefixText.StartsWith(
                                "in",
                                StringComparison.OrdinalIgnoreCase)
                            || conceptPrefixText.StartsWith(
                                "this",
                                StringComparison.OrdinalIgnoreCase));
                    if (hasDenominatorPrefix || hasSupportedScopePrefix)
                    {
                        removalStart = conceptPrefix.Index;
                        isDenominator = hasDenominatorPrefix;
                    }
                }

                MarkRemoved(removed, removalStart, match.Index + match.Length - removalStart);
                occurrences.Add(new ConceptOccurrence(
                    rule.Id,
                    match.Index,
                    isDenominator));
            }
        }

        foreach (var imageConceptId in imageConceptIds)
        {
            if (occurrences.All(occurrence =>
                    !string.Equals(
                        occurrence.Id,
                        imageConceptId,
                        StringComparison.Ordinal)))
            {
                occurrences.Add(new ConceptOccurrence(
                    imageConceptId,
                    0,
                    IsDenominator: false));
            }
        }

        var conceptIds = occurrences
            .OrderBy(occurrence => occurrence.Position)
            .Select(occurrence => occurrence.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (conceptIds.Contains("draw", StringComparer.Ordinal))
        {
            conceptIds = conceptIds
                .Where(id => !string.Equals(id, "card", StringComparison.Ordinal))
                .ToArray();
        }
        foreach (var (gainedConceptId, baseConceptId) in GainedConceptBaseIds)
        {
            if (!conceptIds.Contains(gainedConceptId, StringComparer.Ordinal))
                continue;

            conceptIds = conceptIds
                .Where(id => !string.Equals(id, baseConceptId, StringComparison.Ordinal))
                .ToArray();
        }
        var denominatorConceptIds = occurrences
            .Where(occurrence => occurrence.IsDenominator)
            .OrderBy(occurrence => occurrence.Position)
            .Select(occurrence => occurrence.Id)
            .Where(id => conceptIds.Contains(id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var remainingText = CleanupRemainingText(
            BuildRemainingText(workingText, removed),
            conceptIds);
        var renderedLabel = string.Join(
            " ",
            preservedImages
                .Concat(string.IsNullOrWhiteSpace(remainingText)
                    ? Array.Empty<string>()
                    : [StatsTooltip.EscapeBbcode(remainingText)]));

        return new RelicStatRowPresentation(
            renderedLabel,
            description,
            conceptIds,
            denominatorConceptIds);
    }

    private static ConceptRule Rule(
        string id,
        string pattern,
        bool supportsScopePrefix = false)
    {
        return new ConceptRule(
            id,
            new Regex(
                pattern,
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant
                | RegexOptions.Compiled),
            supportsScopePrefix);
    }

    private static string BuildDescriptionText(
        string originalPlainText,
        string plainSuffix,
        IReadOnlyCollection<string> imageMeanings)
    {
        if (imageMeanings.Count == 0) return originalPlainText;

        var missingMeanings = imageMeanings
            .Where(meaning =>
                !Regex.IsMatch(
                    plainSuffix,
                    $@"\b{Regex.Escape(meaning.Split(' ')[0])}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();
        return NormalizeSpaces(string.Join(
            " ",
            missingMeanings.Append(originalPlainText)));
    }

    private static string BuildDefaultDescription(string descriptionText)
    {
        if (string.IsNullOrWhiteSpace(descriptionText))
            return "This value is tracked for this relic.";

        var withoutPeriod = descriptionText.Trim().TrimEnd('.');
        return $"{withoutPeriod} — this value is tracked for this relic.";
    }

    private static void AddImageMeaning(ICollection<string> meanings, string image)
    {
        if (ContainsPath(image, EnergyIconPathFragment))
            meanings.Add("Energy");
        else if (ContainsPath(image, DrawIconPathFragment))
            meanings.Add("Cards drawn");
        else if (ContainsPath(image, StarIconPathFragment))
            meanings.Add("Star cost");
        else if (ContainsPath(image, VigorIconPathFragment))
            meanings.Add("Vigor");
        else if (ContainsPath(image, VulnerableIconPathFragment))
            meanings.Add("Vulnerable");
        else if (ContainsPath(image, WeakIconPathFragment))
            meanings.Add("Weak");
    }

    private static bool TryGetImageConceptId(string image, out string conceptId)
    {
        conceptId = image switch
        {
            _ when ContainsPath(image, BlockIconPathFragment) => "block",
            _ when ContainsPath(image, DrawIconPathFragment) => "draw",
            _ when ContainsPath(image, EnergyIconPathFragment) => "energy",
            _ when ContainsPath(image, StarIconPathFragment) => "stars",
            _ when ContainsPath(image, VigorIconPathFragment) => "vigor",
            _ when ContainsPath(image, VulnerableIconPathFragment) => "vulnerable",
            _ when ContainsPath(image, WeakIconPathFragment) => "weak",
            _ => string.Empty,
        };
        return conceptId.Length > 0;
    }

    private static string BuildRemainingText(string text, IReadOnlyList<bool> removed)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (!removed[index]) builder.Append(text[index]);
        }

        return builder.ToString();
    }

    private static string CleanupRemainingText(
        string text,
        IReadOnlyCollection<string> conceptIds)
    {
        var cleaned = NormalizeSpaces(text);
        cleaned = Regex.Replace(
            cleaned,
            @"(?:^|\s)[/-](?=\s|$)|^[\s,;:/-]+|[\s,;:/-]+$",
            " ",
            RegexOptions.CultureInvariant);
        cleaned = NormalizeSpaces(cleaned);

        return NormalizeSpaces(cleaned);
    }

    private static string StripBbcode(string value)
    {
        const string escapedBracketPlaceholder = "\uE000";
        var protectedEscapedBrackets = value.Replace(
            "[lb]",
            escapedBracketPlaceholder,
            StringComparison.Ordinal);
        var withoutImages = ImageRegex.Replace(protectedEscapedBrackets, " ");
        return TagRegex.Replace(withoutImages, string.Empty)
            .Replace(escapedBracketPlaceholder, "[", StringComparison.Ordinal);
    }

    private static string NormalizeSpaces(string value)
        => Regex.Replace(value, @"\s+", " ").Trim();

    private static bool ContainsPath(string image, string pathFragment)
        => image.Contains(pathFragment, StringComparison.OrdinalIgnoreCase);

    private static bool OverlapsRemoval(
        IReadOnlyList<bool> removed,
        int start,
        int length)
    {
        var end = Math.Min(removed.Count, start + length);
        for (var index = Math.Max(0, start); index < end; index++)
        {
            if (removed[index]) return true;
        }

        return false;
    }

    private static void MarkRemoved(bool[] removed, int start, int length)
    {
        var end = Math.Min(removed.Length, start + length);
        for (var index = Math.Max(0, start); index < end; index++)
            removed[index] = true;
    }
}
