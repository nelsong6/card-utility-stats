using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace SpireLens.Core.Patches;

/// <summary>
/// Marks synthetic pooled Power cards in the not-in-deck grid. They reuse the
/// real card's art and name, so the badge is the visual distinction between a
/// meta-power record and a removed physical copy of that same card.
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
internal static class MetaPowerCardReassignedBadgePatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardHolder __instance, CardModel cardModel)
        => MetaPowerCardBadge.Update(__instance, cardModel);
}

[HarmonyPatch(typeof(NCardHolder), "SetCard", new[] { typeof(NCard) })]
internal static class MetaPowerCardSetBadgePatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardHolder __instance)
        => MetaPowerCardBadge.Update(__instance, __instance.CardModel);
}

[HarmonyPatch(typeof(NCardHolder), nameof(NCardHolder.Clear))]
internal static class MetaPowerCardClearBadgePatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardHolder __instance)
        => MetaPowerCardBadge.Update(__instance, null);
}

internal static class MetaPowerCardBadge
{
    private const string BadgeName = "SpireLensMetaPowerBadge";

    internal static void Update(NCardHolder? holder, CardModel? card)
    {
        if (holder == null) return;

        try
        {
            bool visible = RunTracker.TryGetMetaPowerDeckViewDefinition(
                card,
                out _);
            var badge = holder.GetNodeOrNull<PanelContainer>(BadgeName);
            if (!visible)
            {
                if (badge != null) badge.Visible = false;
                return;
            }

            badge ??= Create(holder);
            badge.Visible = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"MetaPowerCardBadge.Update failed: {e.Message}");
        }
    }

    private static PanelContainer Create(NCardHolder holder)
    {
        var badge = new PanelContainer
        {
            Name = BadgeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 20,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -104f,
            OffsetRight = -10f,
            OffsetTop = 12f,
            OffsetBottom = 42f,
        };
        badge.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.11f, 0.19f, 0.96f),
            BorderColor = new Color(0.36f, 0.68f, 0.91f, 1f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
        });

        var label = new Label
        {
            Text = "META POWER",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride(
            "font_color",
            new Color(0.64f, 0.84f, 1f, 1f));
        label.AddThemeColorOverride(
            "font_shadow_color",
            new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);

        badge.AddChild(label);
        holder.AddChild(badge);
        return badge;
    }
}
