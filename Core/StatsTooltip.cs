using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;

namespace SpireLens.Core;

/// <summary>
/// Creates SpireLens statistics as ordinary game hover-tip data.
///
/// SpireLens does not own a panel, scene-tree node, anchor, or hover lifecycle.
/// The game's NHoverTipSet receives the returned HoverTip together with its
/// native tips and therefore owns creation, positioning, and removal.
/// </summary>
public static class StatsTooltip
{
    private const string NativeStatsTipId = "SPIRELENS.STATS";
    private const string NativeHintTipId = "SPIRELENS.HINT";
    private const int BodyFontSize = 20;

    private static readonly PropertyInfo TitleProperty =
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title));

    private static readonly PropertyInfo DescriptionProperty =
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description));

    internal static HoverTip CreateNativeTip(
        string titleText,
        string bodyBBCode,
        bool stretchHorizontally = false)
    {
        var tip = new HoverTip
        {
            Id = NativeStatsTipId,
            ShouldOverrideTextOverflow = stretchHorizontally,
        };

        // HoverTip is a record struct whose raw-string Title and Description
        // setters are private. Box it so the same native fields used by the
        // game can be populated without inventing a parallel UI node.
        object boxed = tip;
        TitleProperty.SetValue(boxed, titleText);
        DescriptionProperty.SetValue(
            boxed,
            $"[font_size={BodyFontSize}]{bodyBBCode}[/font_size]");
        return (HoverTip)boxed;
    }

    internal static HoverTip CreateNativeHint(string bodyText)
    {
        var tip = new HoverTip
        {
            Id = NativeHintTipId,
        };

        // Leave Title null so the native scene omits its header entirely.
        object boxed = tip;
        DescriptionProperty.SetValue(
            boxed,
            $"[left][font_size={BodyFontSize}]{EscapeBbcode(bodyText)}[/font_size][/left]");
        return (HoverTip)boxed;
    }

    internal static string RenderRowInformationHint(
        string label,
        string? fullDescription = null)
    {
        var description = RelicStatRowVocabulary.Create(
            label,
            fullDescription).FullDescription;
        return StatConceptGlossary.RenderInformationHint(description);
    }

    internal static RelicStatRowPresentation CreateStatRowPresentation(
        string label,
        string? fullDescription = null,
        IReadOnlyList<string>? conceptIds = null,
        IReadOnlyList<string>? denominatorConceptIds = null)
    {
        var inferred = RelicStatRowVocabulary.Create(label, fullDescription);
        return RelicStatRowVocabulary.MergeConcepts(
            inferred,
            conceptIds ?? Array.Empty<string>(),
            denominatorConceptIds ?? Array.Empty<string>());
    }

    internal static void AppendConceptLabel(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label)
    {
        for (var index = 0; index < conceptIds.Count; index++)
        {
            if (index > 0) body.Append(' ');
            if (denominatorConceptIds.Contains(
                    conceptIds[index],
                    StringComparer.Ordinal))
            {
                body.Append("[color=#b5b5b5]/[/color] ");
            }
            body.Append(StatConceptGlossary.RenderHintedGlyph(conceptIds[index]));
        }

        if (string.IsNullOrWhiteSpace(label)) return;

        if (conceptIds.Count > 0) body.Append(' ');
        body.Append(label);
    }

    internal static void AppendInlineStatRow(
        StringBuilder body,
        IReadOnlyList<string> conceptIds,
        IReadOnlyList<string> denominatorConceptIds,
        string label,
        string value,
        string fullDescription)
    {
        var presentation = CreateStatRowPresentation(
            label,
            fullDescription,
            conceptIds,
            denominatorConceptIds);
        if (body.Length > 0) body.Append('\n');
        body.Append(StatConceptGlossary.RenderInformationHint(
                presentation.FullDescription))
            .Append(' ');
        AppendConceptLabel(
            body,
            presentation.ConceptIds,
            presentation.DenominatorConceptIds,
            presentation.Label);
        body.Append("   [b]")
            .Append(value)
            .Append("[/b]");
    }

    /// <summary>
    /// Escape a dynamic string for safe inclusion in native hover-tip BBCode.
    /// Godot renders "[lb]" as a literal opening bracket.
    /// </summary>
    public static string EscapeBbcode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return text.Replace("[", "[lb]", StringComparison.Ordinal);
    }
}
