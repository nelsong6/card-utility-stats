using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;

namespace SpireLens.Core.Patches;

[HarmonyPatch(typeof(NRelicCollectionEntry), "OnPress")]
public static class RelicCompendiumClassificationPressPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRelicCollectionEntry __instance)
    {
        PatchGuard.Run(nameof(RelicCompendiumClassificationPressPatch), () =>
        {
            RelicCompendiumClassificationUi.ToggleEntry(__instance);
        });
    }
}

internal static class RelicCompendiumClassificationUi
{
    private const string BadgeName = "SpireLensCombatRelevanceBadge";
    private static readonly List<Label> Badges = new();

    public static void ToggleEntry(NRelicCollectionEntry? entry)
    {
        if (!RelicCompendiumFilterUi.IsEditingCombatRelevance) return;
        if (entry == null || !GodotObject.IsInstanceValid(entry)) return;
        if (entry.ModelVisibility != ModelVisibility.Visible) return;
        if (!CompendiumRelicStatsContext.TryGetRelicModel(entry, out var relicModel)) return;

        RelicClassificationStore.Toggle(relicModel);
        RelicCompendiumFilterUi.ApplyToActiveEntries();
    }

    public static void ApplyToEntry(NRelicCollectionEntry? entry, bool editing)
    {
        if (entry == null || !GodotObject.IsInstanceValid(entry)) return;
        CleanupInvalidBadges();

        var badge = FindBadge(entry);
        if (!editing
            || entry.ModelVisibility != ModelVisibility.Visible
            || !CompendiumRelicStatsContext.TryGetRelicModel(entry, out var relicModel))
        {
            if (badge != null) badge.Visible = false;
            return;
        }

        badge ??= CreateBadge(entry);
        var isNonCombat = RelicClassificationStore.IsNonCombat(relicModel);
        badge.Text = isNonCombat ? "NON-COMBAT" : "COMBAT";
        badge.AddThemeColorOverride(
            "font_color",
            isNonCombat
                ? new Color(0.84f, 0.9f, 1f, 1f)
                : new Color(1f, 0.88f, 0.52f, 1f));
        badge.AddThemeStyleboxOverride("normal", CreateBadgeStyle(isNonCombat));
        badge.Visible = true;
    }

    public static void TeardownBadges()
    {
        foreach (var badge in Badges.ToArray())
        {
            if (badge != null && GodotObject.IsInstanceValid(badge))
            {
                badge.GetParent()?.RemoveChild(badge);
                badge.QueueFree();
            }
        }
        Badges.Clear();
    }

    private static Label? FindBadge(NRelicCollectionEntry entry)
    {
        var node = entry.GetNodeOrNull(BadgeName);
        return node as Label;
    }

    private static Label CreateBadge(NRelicCollectionEntry entry)
    {
        var badge = new Label
        {
            Name = BadgeName,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 500,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 2f,
            OffsetRight = -2f,
            OffsetTop = -22f,
            OffsetBottom = -2f,
        };
        badge.AddThemeFontSizeOverride("font_size", 10);
        entry.AddChild(badge);
        Badges.Add(badge);
        return badge;
    }

    private static StyleBoxFlat CreateBadgeStyle(bool isNonCombat)
        => new()
        {
            BgColor = isNonCombat
                ? new Color(0.08f, 0.14f, 0.22f, 0.92f)
                : new Color(0.22f, 0.15f, 0.04f, 0.92f),
            BorderColor = isNonCombat
                ? new Color(0.48f, 0.67f, 0.9f, 0.95f)
                : new Color(0.86f, 0.64f, 0.2f, 0.95f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };

    private static void CleanupInvalidBadges()
    {
        for (var i = Badges.Count - 1; i >= 0; i--)
        {
            if (Badges[i] != null && GodotObject.IsInstanceValid(Badges[i])) continue;
            Badges.RemoveAt(i);
        }
    }
}
