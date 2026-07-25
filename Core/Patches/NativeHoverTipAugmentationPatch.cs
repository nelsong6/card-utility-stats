using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;

namespace SpireLens.Core.Patches;

/// <summary>
/// Adds SpireLens data to the same IEnumerable&lt;IHoverTip&gt; that the game
/// is about to render. The resulting node is created, positioned, associated
/// with <paramref name="owner"/>, and removed entirely by NHoverTipSet.
/// </summary>
[HarmonyPatch(
    typeof(NHoverTipSet),
    nameof(NHoverTipSet.CreateAndShow),
    new[]
    {
        typeof(Control),
        typeof(IEnumerable<IHoverTip>),
        typeof(HoverTipAlignment),
    })]
internal static class NativeHoverTipCreateStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        Control owner,
        ref IEnumerable<IHoverTip> hoverTips,
        out bool __state)
    {
        __state = false;
        try
        {
            if (!NativeStatsHoverTipFactory.TryCreate(owner, out var statsTip))
                return;

            // Materialize the sequence before native rendering. NHoverTipSet
            // preserves this order, so the SpireLens tip becomes the final
            // text control in the native set and can be styled in Postfix.
            hoverTips = (hoverTips ?? Enumerable.Empty<IHoverTip>())
                .Append(statsTip)
                .ToList();
            __state = true;
        }
        catch (Exception e)
        {
            // Stats presentation must never prevent the game's own tooltip.
            CoreMain.Logger.Error($"Native stats hover-tip creation failed: {e}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(NHoverTipSet? __result, bool __state)
    {
        if (!__state || __result == null) return;

        try
        {
            NativeStatsHoverTipStyler.ApplyToLastTextTip(__result);
        }
        catch (Exception e)
        {
            // Visual identity is supplemental. A styling failure must not
            // interfere with the native tooltip or its owner lifecycle.
            CoreMain.Logger.Error($"Native stats hover-tip styling failed: {e}");
        }
    }
}

/// <summary>
/// Restores SpireLens's visual identity on the native control created for the
/// appended stats tip. The control remains a child of NHoverTipSet; this class
/// retains no node reference and owns none of its lifecycle.
/// </summary>
internal static class NativeStatsHoverTipStyler
{
    private const string BrandNodeName = "SpireLensBrand";
    private const string RegularFontPath =
        "res://themes/kreon_regular_glyph_space_one.tres";
    private const float BrandWidth = 100f;
    private const float BrandHeight = 24f;

    private static readonly Color PanelTint = new(0.6f, 0.68f, 0.88f, 1f);
    private static readonly Color BrandColor = new(0.408f, 0.408f, 0.408f, 1f);
    private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.251f);

    private static Font? _regularFont;
    private static bool _fontLoadAttempted;

    public static void ApplyToLastTextTip(NHoverTipSet tipSet)
    {
        var container = tipSet._textHoverTipContainer;
        if (container == null || container.GetChildCount() == 0) return;

        if (container.GetChild(container.GetChildCount() - 1) is not Control statsTip)
            return;

        // Tint only this tip's background. The rest of the native set keeps
        // the game's standard appearance, making the SpireLens page distinct.
        var background = statsTip.GetNodeOrNull<CanvasItem>("%Bg");
        if (background != null)
            background.SelfModulate = PanelTint;

        AddBrand(statsTip, background as Control);
    }

    private static void AddBrand(Control statsTip, Control? background)
    {
        if (statsTip.GetNodeOrNull<Label>(BrandNodeName) != null) return;

        LoadFontOnce();

        var title = statsTip.GetNodeOrNull<Control>("%Title");
        var panelTopLeft = background == null
            ? Vector2.Zero
            : ConvertPointToTip(statsTip, background, Vector2.Zero);
        var panelTopRight = background == null
            ? new Vector2(statsTip.Size.X, 0f)
            : ConvertPointToTip(
                statsTip,
                background,
                new Vector2(background.Size.X, 0f));
        var titleTopLeft = title == null
            ? panelTopLeft + new Vector2(18f, 18f)
            : ConvertPointToTip(statsTip, title, Vector2.Zero);

        var rightInset = Math.Max(12f, titleTopLeft.X - panelTopLeft.X);
        var right = panelTopRight.X - rightInset;
        var top = titleTopLeft.Y;

        var brand = new Label
        {
            Name = BrandNodeName,
            Text = "SpireLens",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = right - BrandWidth,
            OffsetRight = right,
            OffsetTop = top,
            OffsetBottom = top + BrandHeight,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            ZIndex = 1,
        };

        brand.AddThemeColorOverride("font_color", BrandColor);
        if (_regularFont != null)
            brand.AddThemeFontOverride("font", _regularFont);
        brand.AddThemeFontSizeOverride("font_size", 14);
        brand.AddThemeColorOverride("font_shadow_color", ShadowColor);
        brand.AddThemeConstantOverride("shadow_offset_x", 3);
        brand.AddThemeConstantOverride("shadow_offset_y", 2);

        statsTip.AddChild(brand);
    }

    private static Vector2 ConvertPointToTip(
        Control statsTip,
        Control child,
        Vector2 point)
    {
        var globalPoint = child.GetGlobalTransformWithCanvas() * point;
        return statsTip.GetGlobalTransformWithCanvas().AffineInverse() * globalPoint;
    }

    private static void LoadFontOnce()
    {
        if (_fontLoadAttempted) return;
        _fontLoadAttempted = true;

        try
        {
            _regularFont = ResourceLoader.Load<Font>(RegularFontPath);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"SpireLens brand font load failed: {e.Message}");
        }
    }
}

internal static class NativeStatsHoverTipFactory
{
    public static bool TryCreate(Control? owner, out IHoverTip statsTip)
    {
        statsTip = default!;
        if (owner == null || !ViewStatsInjectorPatch.StatsVisibilityEnabled)
            return false;

        HoverTip tip;
        switch (owner)
        {
            case NCardHolder cardHolder
                when CardHoverShowPatch.TryBuildNativeHoverTip(cardHolder, out tip):
                statsTip = tip;
                return true;

            case NRelicInventoryHolder relicHolder
                when RelicHoverShowPatch.TryBuildNativeHoverTip(relicHolder, out tip):
                statsTip = tip;
                return true;

            case NCreature creature
                when EnemyHoverShowPatch.TryBuildNativeHoverTip(creature, out tip):
                statsTip = tip;
                return true;

            case NRelicCollectionEntry entry
                when CompendiumRelicStatsContext.TryBuildNativeHoverTip(entry, out tip):
                statsTip = tip;
                return true;

            case NDeckHistoryEntry entry
                when RunHistoryStatsContext.TryBuildNativeCardHoverTip(entry, out tip):
                statsTip = tip;
                return true;

            case NRelicBasicHolder holder
                when RunHistoryStatsContext.TryBuildNativeRelicHoverTip(holder, out tip):
                statsTip = tip;
                return true;

            default:
                return false;
        }
    }
}
