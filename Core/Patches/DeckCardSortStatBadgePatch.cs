using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.addons.mega_text;

namespace SpireLens.Core.Patches;

/// <summary>
/// Prints the active sort's number on each card in the deck grid, so a
/// screenshot or a stream frame explains itself. The caption carries the
/// metric's full name rather than just the value: whoever is looking at the
/// picture did not press the sort button and has no other way to know what
/// "432" counts.
///
/// Same three hooks as <see cref="MetaPowerCardBadge"/>, and for the same
/// reason: the grid RECYCLES holders, so a caption written once would follow
/// the holder onto whatever card landed there after a re-sort. All three were
/// already Harmony targets before this patch, so nothing here is a new target.
/// </summary>
[HarmonyPatch(
    typeof(NCardHolder),
    nameof(NCardHolder.ReassignToCard),
    new[]
    {
        typeof(CardModel),
        typeof(PileType),
        typeof(Creature),
        typeof(ModelVisibility),
    })]
internal static class DeckCardSortStatReassignedPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardHolder __instance, CardModel cardModel)
        => DeckCardSortStatBadge.Update(__instance, cardModel);
}

[HarmonyPatch(typeof(NCardHolder), "SetCard", new[] { typeof(NCard) })]
internal static class DeckCardSortStatSetPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardHolder __instance)
        => DeckCardSortStatBadge.UpdateFromHolder(__instance);
}

[HarmonyPatch(typeof(NCardHolder), nameof(NCardHolder.Clear))]
internal static class DeckCardSortStatClearPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardHolder __instance)
        => DeckCardSortStatBadge.Update(__instance, null);
}

internal static class DeckCardSortStatBadge
{
    private const string BadgeName = "SpireLensSortStat";
    private const string BadgeMeta = "spirelens_caption";
    private const string CaptionMarker = "\u200B";
    private const string CenterClose = "[/center]";
    // The card's own local geometry, read off the live scene: the frame is
    // 300x422 centred on the holder origin, the body text panel spans
    // y -22..181, and the description label's box ends at y 173. The caption
    // sits on the body itself, low in the text panel and under the card's own
    // text, rather than on the frame's bottom rim.
    private const float BannerWidth = 250f;
    private const float BannerTop = 140f;
    private const float BannerHeight = 32f;

    private const string LabelFontName = "font";
    private const string LabelFontSizeName = "font_size";
    private const string LabelColorName = "font_color";
    private const string RichTextFontName = "normal_font";
    private const string RichTextFontSizeName = "normal_font_size";
    private const string RichTextColorName = "default_color";
    private const int FallbackFontSize = 18;

    internal static void Update(NCardHolder? holder, CardModel? card)
        => Schedule(holder, card, readFromHolder: false);

    /// <summary>
    /// Resolve the card from the holder rather than from the caller. At the
    /// SetCard postfix the holder still reports its PREVIOUS card:
    /// NGridCardHolder.CardModel returns _baseCard, which Create assigns in
    /// UpdateCardModel AFTER SetCard returns.
    /// </summary>
    internal static void UpdateFromHolder(NCardHolder? holder)
        => Schedule(holder, null, readFromHolder: true);

    /// <summary>
    /// Always settle a frame late, and re-read everything then.
    ///
    /// Two things are false at postfix time and true by the next idle frame,
    /// and the grid hits both: a holder fresh from the NodePool has not been
    /// parented yet (so the deck-view ancestor test would say "not a deck
    /// view"), and a holder being recycled in place has not had _baseCard
    /// updated yet (so it still reports the card it displayed before). The
    /// second is why scrolling used to leave the previous card's number on a
    /// recycled holder — a Skill captioned with an Attack's damage.
    ///
    /// InitGrid parents the holder and Create updates the model inside the
    /// same frame, so one deferred hop is enough for both.
    /// </summary>
    private static void Schedule(NCardHolder? holder, CardModel? card, bool readFromHolder)
    {
        if (holder == null || !GodotObject.IsInstanceValid(holder)) return;

        var pendingHolder = holder;
        var pendingCard = card;
        var pendingRead = readFromHolder;
        Callable.From(() => Apply(pendingHolder, pendingCard, pendingRead)).CallDeferred();
    }

    private static void Apply(NCardHolder? holder, CardModel? card, bool readFromHolder)
    {
        if (holder == null || !GodotObject.IsInstanceValid(holder)) return;

        try
        {
            // A holder the grid has discarded keeps rendering until Godot
            // actually frees it. InitGrid renames those to "...-OLD" and queue
            // frees them, so a rebuilt grid briefly draws the previous set
            // underneath the new one — same card art, but the caption it was
            // last given. That is the doubled text: two labels on two holders
            // at the same position, not two labels on one holder.
            if (holder.IsQueuedForDeletion()
                || holder.Name.ToString().Contains("-OLD", StringComparison.Ordinal))
            {
                RemoveLegacyLabels(holder);
                return;
            }

            var effectiveCard = readFromHolder ? holder.CardModel : card;
            RemoveLegacyLabels(holder);

            var description = holder.CardNode?
                .GetNodeOrNull<MegaRichTextLabel>("CardContainer/DescriptionLabel");
            if (description == null) return;

            var baseText = StripCaption(description.Text);
            if (!ShouldCaption(holder, effectiveCard, out var text))
            {
                if (!string.Equals(description.Text, baseText, StringComparison.Ordinal))
                    description.SetTextAutoSize(baseText);
                return;
            }

            description.SetTextAutoSize(WithCaption(baseText, text));
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DeckCardSortStatBadge.Apply failed: {e.Message}");
        }
    }

    /// <summary>
    /// Our line lives INSIDE the card's own description label rather than in a
    /// label of our own floating over the card.
    ///
    /// A separate label has to guess where the card's text ends, and cards
    /// carry wildly different amounts of it — so a fixed position either
    /// collides with a wordy card or leaves a gap under a terse one. The
    /// description label is a MegaRichTextLabel with AutoSizeEnabled and a
    /// font range of 8..100: it already shrinks its contents to fit its box.
    /// Appending to it means our line is measured and scaled with the card's
    /// own text, by the game's own logic, instead of competing with it.
    ///
    /// The marker makes the append idempotent. The game rewrites this label on
    /// every visual update and upgrade-preview swap, and our sweep runs five
    /// times a second, so the line must be strippable and re-appendable
    /// without ever stacking up. A zero-width space keeps the marker invisible
    /// if it somehow survives into rendered text.
    /// </summary>
    /// <summary>
    /// Re-caption every holder currently in a grid. The grid recycles holders
    /// while scrolling without going through the per-holder hooks, so captions
    /// have to be swept rather than pushed.
    /// </summary>
    internal static void RefreshAll(Node? root)
    {
        if (root == null || !GodotObject.IsInstanceValid(root)) return;
        try { RefreshRecursive(root); }
        catch (Exception e) { CoreMain.LogDebug($"RefreshAll failed: {e.Message}"); }
    }

    private static void RefreshRecursive(Node node)
    {
        if (node is NGridCardHolder holder)
            Apply(holder, null, readFromHolder: true);

        for (var i = 0; i < node.GetChildCount(); i++)
            RefreshRecursive(node.GetChild(i));
    }

    /// <summary>
    /// Insert our line INSIDE the card's centre block.
    ///
    /// The card's description is BBCode of the form
    /// "[center]Deal 7 damage twice.[/center]", and the label's own
    /// HorizontalAlignment is Left — the centring comes entirely from that
    /// tag. Appending after the closing tag therefore drops the line out of
    /// the centred block and left-aligns it. No markup of our own either: an
    /// [i] tag makes Godot switch to the theme's italics font, which is a
    /// different typeface from the card's. Inside the block and unstyled, the
    /// line is simply another line of card text.
    /// </summary>
    private static string WithCaption(string baseText, string caption)
    {
        var segment = $"{CaptionMarker}\n{caption}";
        var close = baseText.LastIndexOf(CenterClose, StringComparison.Ordinal);
        return close < 0 ? baseText + segment : baseText.Insert(close, segment);
    }

    /// <summary>
    /// Remove a previously inserted line, restoring the card's own text
    /// exactly. The segment runs from the marker to the closing centre tag it
    /// was inserted before.
    /// </summary>
    private static string StripCaption(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var marker = text.IndexOf(CaptionMarker, StringComparison.Ordinal);
        if (marker < 0) return text;

        var close = text.IndexOf(CenterClose, marker, StringComparison.Ordinal);
        return close < 0 ? text[..marker] : text.Remove(marker, close - marker);
    }

    /// <summary>
    /// Clear captions drawn the old way — a Label parented straight to the
    /// holder — including any left by an earlier Core load in this session.
    /// </summary>
    private static void RemoveLegacyLabels(NCardHolder holder)
    {
        for (var i = holder.GetChildCount() - 1; i >= 0; i--)
        {
            var child = holder.GetChild(i);
            if (child is not Label
                && !child.HasMeta(BadgeMeta)
                && !child.Name.ToString().Contains(BadgeName, StringComparison.Ordinal))
            {
                continue;
            }

            holder.RemoveChild(child);
            child.QueueFree();
        }
    }

        private static bool ShouldCaption(NCardHolder holder, CardModel? card, out string text)
    {
        text = string.Empty;
        if (card == null) return false;

        // NCardHolder is used everywhere — combat hand, draw pile, rewards.
        // Only the deck grid gets captions.
        if (holder is not NGridCardHolder) return false;
        if (!RunHistoryStatsContext.HasAncestor<NDeckViewScreen>(holder)) return false;

        // A stats overlay obeys the master stats switch like every other one.
        if (!ViewStatsInjectorPatch.StatsVisibilityEnabled) return false;

        return DeckViewSpireLensSort.TryGetCaption(card, out text);
    }

            /// <summary>
    /// The game's fonts ship no italic face, so slant has to be synthesised by
    /// shearing the base font. Cached against the font it was derived from, so
    /// a font change rebuilds it and ordinary redraws do not.
    /// </summary>
        }
