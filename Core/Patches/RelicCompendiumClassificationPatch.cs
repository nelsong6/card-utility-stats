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
    private const string CombatIconPath =
        "res://images/atlases/ui_atlas.sprites/map/icons/map_monster.tres";
    private const string NonCombatIconPath =
        "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_map.tres";
    private static readonly List<TextureRect> Badges = new();
    private static Texture2D? _combatIcon;
    private static Texture2D? _nonCombatIcon;

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
        badge.Texture = isNonCombat ? GetNonCombatIcon() : GetCombatIcon();
        badge.TooltipText = isNonCombat ? "Non-combat" : "Combat";
        ApplyBadgeSize(badge, isNonCombat);
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
        _combatIcon = null;
        _nonCombatIcon = null;
    }

    private static TextureRect? FindBadge(NRelicCollectionEntry entry)
    {
        var node = entry.GetNodeOrNull(BadgeName);
        if (node is TextureRect badge) return badge;
        if (node != null)
        {
            entry.RemoveChild(node);
            node.QueueFree();
        }
        return null;
    }

    private static TextureRect CreateBadge(NRelicCollectionEntry entry)
    {
        var badge = new TextureRect
        {
            Name = BadgeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 500,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -40f,
            OffsetRight = -2f,
            OffsetTop = -34f,
            OffsetBottom = -2f,
        };
        entry.AddChild(badge);
        Badges.Add(badge);
        return badge;
    }

    private static Texture2D? GetCombatIcon()
        => _combatIcon ??= LoadIcon(CombatIconPath, "combat");

    private static Texture2D? GetNonCombatIcon()
        => _nonCombatIcon ??= LoadIcon(NonCombatIconPath, "non-combat");

    private static void ApplyBadgeSize(TextureRect badge, bool isNonCombat)
    {
        badge.OffsetLeft = isNonCombat ? -40f : -78f;
        badge.OffsetRight = -2f;
        badge.OffsetTop = isNonCombat ? -34f : -66f;
        badge.OffsetBottom = -2f;
    }

    private static Texture2D? LoadIcon(string path, string classification)
    {
        var texture = ResourceLoader.Load<Texture2D>(path, null, ResourceLoader.CacheMode.Reuse);
        if (texture == null)
        {
            CoreMain.Logger.Error(
                $"Could not load {classification} relic classification icon: {path}");
        }
        return texture;
    }

    private static void CleanupInvalidBadges()
    {
        for (var i = Badges.Count - 1; i >= 0; i--)
        {
            if (Badges[i] != null && GodotObject.IsInstanceValid(Badges[i])) continue;
            Badges.RemoveAt(i);
        }
    }
}
