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

    private static Font? _italicFont;
    private static Font? _italicBaseFont;

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
            var effectiveCard = readFromHolder ? holder.CardModel : card;
            var badge = holder.GetNodeOrNull<Label>(BadgeName);

            if (!ShouldCaption(holder, effectiveCard, out var text))
            {
                if (badge != null) badge.Visible = false;
                return;
            }

            badge ??= Create(holder);
            // Reassert placement every time rather than only at creation: a
            // badge built by a previous Core load survives the reload on its
            // holder and is reused, so it would otherwise keep that load's
            // geometry for the rest of the session.
            ApplyGeometry(badge);
            badge.Text = text;
            ApplyCardTextStyle(holder, badge);
            badge.Visible = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DeckCardSortStatBadge.Apply failed: {e.Message}");
        }
    }

    /// <summary>
    /// Re-caption every holder currently in a grid.
    ///
    /// The per-holder hooks cover cards being assigned, but the grid also
    /// RECYCLES holders while scrolling without going through either of them,
    /// which left a holder showing the previous card's number — a Skill
    /// captioned with an Attack's damage. Rather than patch the grid's
    /// internals (a new Harmony target, and a game-update liability), the deck
    /// view sweeps its own holders: at ~40 cards this is trivial work, and it
    /// cannot be out of step with however the grid decides to reuse them.
    /// </summary>
    internal static void RefreshAll(Node? root)
    {
        if (root == null || !GodotObject.IsInstanceValid(root)) return;

        try { RefreshRecursive(root); }
        catch (Exception e) { CoreMain.LogDebug($"DeckCardSortStatBadge.RefreshAll failed: {e.Message}"); }
    }

    private static void RefreshRecursive(Node node)
    {
        if (node is NGridCardHolder holder)
            Apply(holder, null, readFromHolder: true);

        for (var i = 0; i < node.GetChildCount(); i++)
            RefreshRecursive(node.GetChild(i));
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

    private static Label Create(NCardHolder holder)
    {
        var caption = new Label
        {
            Name = BadgeName,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 20,
            // Shrink rather than overflow the card on the longest metric names.
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        ApplyGeometry(caption);
        ApplyCardTextStyle(holder, caption);
        holder.AddChild(caption);
        return caption;
    }

    /// <summary>
    /// Draw the caption as the card's own body text rather than as a mod chip:
    /// the font, size and colour are lifted off that card's DescriptionLabel,
    /// slanted to mark it as ours. Copying the live node beats hardcoding,
    /// because the card's text style varies with rarity and border.
    ///
    /// The game's own description text is deliberately not touched. It is
    /// regenerated on every visual update and upgrade-preview swap, so an
    /// appended line would be wiped or duplicated depending on ordering, and a
    /// long description plus our line would overflow the label's box and shrink
    /// the actual card text. A sibling label in the empty space below it gets
    /// the same look with none of that.
    /// </summary>
    private static void ApplyCardTextStyle(NCardHolder holder, Label caption)
    {
        var description = holder.CardNode?
            .GetNodeOrNull<MegaRichTextLabel>("CardContainer/DescriptionLabel");

        var font = description != null && description.HasThemeFont(RichTextFontName)
            ? description.GetThemeFont(RichTextFontName)
            : null;
        var size = description != null && description.HasThemeFontSize(RichTextFontSizeName)
            ? description.GetThemeFontSize(RichTextFontSizeName)
            : FallbackFontSize;

        if (ResolveItalicFont(font) is { } italic)
            caption.AddThemeFontOverride(LabelFontName, italic);
        caption.AddThemeFontSizeOverride(LabelFontSizeName, size);

        var colour = description != null && description.HasThemeColor(RichTextColorName)
            ? description.GetThemeColor(RichTextColorName)
            : new Color(0.13f, 0.11f, 0.09f, 1f);
        caption.AddThemeColorOverride(LabelColorName, colour);
    }

    /// <summary>
    /// Explicit placement, NOT anchors. Both the holder and the NCard inside it
    /// report size (0,0): a card is drawn in centred local coordinates — its
    /// frame spans x -150..150, y -211..211 — rather than being laid out as a
    /// sized Control. Anchoring to that empty rect resolves to a NEGATIVE width
    /// and an invisible badge, which is why the first cut never showed. (The
    /// meta-power badge anchors the same way and escapes only because its
    /// offsets happen to come out positive.)
    /// </summary>
    private static void ApplyGeometry(Control badge)
    {
        badge.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        badge.Position = new Vector2(-BannerWidth / 2f, BannerTop);
        badge.Size = new Vector2(BannerWidth, BannerHeight);
    }

    /// <summary>
    /// The game's fonts ship no italic face, so slant has to be synthesised by
    /// shearing the base font. Cached against the font it was derived from, so
    /// a font change rebuilds it and ordinary redraws do not.
    /// </summary>
    private static Font? ResolveItalicFont(Font? baseFont)
    {
        baseFont ??= DeckViewSortMenu.RowFont;
        if (baseFont == null) return null;
        if (_italicFont != null && ReferenceEquals(baseFont, _italicBaseFont))
            return _italicFont;

        _italicBaseFont = baseFont;
        _italicFont = new FontVariation
        {
            BaseFont = baseFont,
            VariationTransform = new Transform2D(
                new Vector2(1f, 0f),
                new Vector2(-0.2f, 1f),
                Vector2.Zero),
        };
        return _italicFont;
    }

    internal static void ResetFontCache()
    {
        _italicFont = null;
        _italicBaseFont = null;
    }
}
