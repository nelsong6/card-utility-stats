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
        => DeckCardSortStatBadge.Update(__instance, __instance.CardModel);
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
    private const int FontSize = 13;

    private static Font? _italicFont;
    private static Font? _italicBaseFont;

    internal static void Update(NCardHolder? holder, CardModel? card)
    {
        if (holder == null) return;

        try
        {
            var badge = holder.GetNodeOrNull<PanelContainer>(BadgeName);

            if (!ShouldCaption(holder, card, out var text))
            {
                if (badge != null) badge.Visible = false;
                return;
            }

            badge ??= Create(holder);
            badge.GetNodeOrNull<Label>("Value")?.Set(Label.PropertyName.Text, text);
            badge.Visible = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DeckCardSortStatBadge.Update failed: {e.Message}");
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

    private static PanelContainer Create(NCardHolder holder)
    {
        var badge = new PanelContainer
        {
            Name = BadgeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 20,
            // A banner along the card's lower edge: the widest run of space on
            // the card, and clear of the top-right meta-power badge.
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 10f,
            OffsetRight = -10f,
            OffsetTop = -42f,
            OffsetBottom = -14f,
        };

        // Near-opaque plate rather than plain text over art: the caption has to
        // stay readable in a screenshot and through stream compression, over
        // whatever the card happens to look like underneath.
        badge.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.03f, 0.05f, 0.86f),
            BorderColor = new Color(0.62f, 0.58f, 0.48f, 0.9f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });

        var label = new Label
        {
            Name = "Value",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // Shrink rather than overflow the card on the longest metric names.
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        label.AddThemeFontSizeOverride("font_size", FontSize);
        if (ResolveItalicFont() is { } italic)
            label.AddThemeFontOverride("font", italic);
        label.AddThemeColorOverride("font_color", new Color(0.96f, 0.94f, 0.86f, 1f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.95f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);

        badge.AddChild(label);
        holder.AddChild(badge);
        return badge;
    }

    /// <summary>
    /// The game's fonts ship no italic face, so slant has to be synthesised by
    /// shearing the base font. Cached against the font it was derived from, so
    /// a font change rebuilds it and ordinary redraws do not.
    /// </summary>
    private static Font? ResolveItalicFont()
    {
        var baseFont = DeckViewSortMenu.RowFont;
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
