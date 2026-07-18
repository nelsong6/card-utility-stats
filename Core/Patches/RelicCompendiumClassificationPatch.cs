using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Saves;

namespace SpireLens.Core.Patches;

[HarmonyPatch(typeof(NRelicCollectionCategory), "OnRelicEntryPressed")]
public static class RelicCompendiumClassificationInspectPatch
{
    [HarmonyPrefix]
    public static void Prefix(NRelicCollectionEntry entry)
    {
        PatchGuard.Run(nameof(RelicCompendiumClassificationInspectPatch), () =>
        {
            RelicInspectionClassificationUi.BeginInspection(entry);
        });
    }
}

[HarmonyPatch(typeof(NInspectRelicScreen), "_Ready")]
public static class RelicInspectionClassificationReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NInspectRelicScreen __instance)
    {
        PatchGuard.Run(nameof(RelicInspectionClassificationReadyPatch), () =>
        {
            RelicInspectionClassificationUi.Attach(__instance);
        });
    }
}

[HarmonyPatch(typeof(NInspectRelicScreen), "UpdateRelicDisplay")]
public static class RelicInspectionClassificationRefreshPatch
{
    [HarmonyPostfix]
    public static void Postfix(NInspectRelicScreen __instance)
    {
        PatchGuard.Run(nameof(RelicInspectionClassificationRefreshPatch), () =>
        {
            RelicInspectionClassificationUi.Refresh(__instance);
        });
    }
}

[HarmonyPatch(typeof(NInspectRelicScreen), nameof(NInspectRelicScreen.Open))]
public static class RelicInspectionClassificationOpenPatch
{
    [HarmonyPostfix]
    public static void Postfix(NInspectRelicScreen __instance)
    {
        PatchGuard.Run(nameof(RelicInspectionClassificationOpenPatch), () =>
        {
            RelicInspectionClassificationUi.Refresh(__instance);
        });
    }
}

[HarmonyPatch(typeof(NInspectRelicScreen), nameof(NInspectRelicScreen.Close))]
public static class RelicInspectionClassificationClosePatch
{
    [HarmonyPostfix]
    public static void Postfix(NInspectRelicScreen __instance)
    {
        PatchGuard.Run(nameof(RelicInspectionClassificationClosePatch), () =>
        {
            RelicInspectionClassificationUi.EndInspection(__instance);
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
            // Keep the game's default z=0. Since this is appended after the
            // entry's RelicHolder it still draws over the relic art, while the
            // game's later hover-tip draw order remains above the badge.
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
        badge.OffsetLeft = -40f;
        badge.OffsetRight = -2f;
        badge.OffsetTop = -34f;
        badge.OffsetBottom = -2f;
        badge.PivotOffset = new Vector2(19f, 16f);
        badge.Scale = isNonCombat ? Vector2.One : Vector2.One * 2f;
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

internal static class RelicInspectionClassificationUi
{
    private const string RootName = "SpireLensRelicClassificationChoices";
    private const int AlwaysRelevantItemId = 0;
    private static readonly FieldInfo? RelicsField =
        AccessTools.Field(typeof(NInspectRelicScreen), "_relics");
    private static readonly FieldInfo? IndexField =
        AccessTools.Field(typeof(NInspectRelicScreen), "_index");
    private static readonly List<InspectorControls> Controls = new();
    private static bool _inspectionFromEditor;
    private static bool _syncingControls;

    public static void BeginInspection(NRelicCollectionEntry? entry)
    {
        _inspectionFromEditor = RelicCompendiumFilterUi.IsEditingCombatRelevance
                                && entry != null
                                && GodotObject.IsInstanceValid(entry)
                                && entry.ModelVisibility == ModelVisibility.Visible;
    }

    public static void Attach(NInspectRelicScreen? screen)
    {
        if (screen == null || !GodotObject.IsInstanceValid(screen)) return;
        CleanupInvalidControls();

        var tracked = Controls.FirstOrDefault(controls => controls.IsFor(screen));
        if (tracked != null) return;

        var popup = screen.GetNodeOrNull<Control>("%Popup");
        if (popup == null) return;

        var staleRoot = popup.GetNodeOrNull<Control>(RootName);
        if (staleRoot != null)
        {
            popup.RemoveChild(staleRoot);
            staleRoot.QueueFree();
        }

        var root = new PanelContainer
        {
            Name = RootName,
            MouseFilter = Control.MouseFilterEnum.Stop,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -360f,
            OffsetRight = 360f,
            OffsetTop = 42f,
            OffsetBottom = 82f,
            Visible = false,
        };
        root.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.049f, 0.043f, 0.92f),
            BorderColor = new Color(0.56f, 0.46f, 0.25f, 0.78f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 12f,
            ContentMarginRight = 12f,
            ContentMarginTop = 3f,
            ContentMarginBottom = 3f,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });
        popup.AddChild(root);

        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row.AddThemeConstantOverride("separation", 14);
        root.AddChild(row);

        var label = new Label
        {
            Text = "Combat relevance",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(145f, 0f),
        };
        label.AddThemeFontSizeOverride("font_size", 17);
        label.AddThemeColorOverride("font_color", new Color(0.918f, 0.745f, 0.318f, 1f));
        row.AddChild(label);

        var group = new ButtonGroup { AllowUnpress = false };
        var combat = CreateRadioButton("Combat", group);
        var nonCombat = CreateRadioButton("Non-combat", group);
        combat.CustomMinimumSize = new Vector2(105f, 32f);
        nonCombat.CustomMinimumSize = new Vector2(135f, 32f);
        row.AddChild(combat);

        var duration = new OptionButton
        {
            Name = "CombatRelevanceDuration",
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(185f, 32f),
            TooltipText = "How long this relic should remain visible in a filtered combat relic bar.",
        };
        duration.AddItem("Always", AlwaysRelevantItemId);
        duration.AddItem("Until turn 1", 1);
        duration.AddItem("Until turn 2", 2);
        duration.AddItem("Until turn 3", 3);
        duration.AddThemeFontSizeOverride("font_size", 17);
        row.AddChild(duration);
        row.AddChild(nonCombat);

        KeepHorizontalNavigationOnControl(combat);
        KeepHorizontalNavigationOnControl(duration);
        KeepHorizontalNavigationOnControl(nonCombat);

        combat.Toggled += selected => Assign(screen, isNonCombat: false, selected);
        nonCombat.Toggled += selected => Assign(screen, isNonCombat: true, selected);
        duration.Connect(
            OptionButton.SignalName.ItemSelected,
            Callable.From<long>(index => AssignDuration(screen, duration, index)));

        var controls = new InspectorControls(screen, root, combat, duration, nonCombat);
        Controls.Add(controls);
        UpdateFocusNeighbors(controls, isNonCombat: false);
    }

    public static void RefreshActiveScreen()
    {
        var screen = NGame.Instance?.InspectRelicScreen;
        if (screen != null) Refresh(screen);
    }

    public static void Refresh(NInspectRelicScreen? screen)
    {
        if (screen == null || !GodotObject.IsInstanceValid(screen)) return;
        AttachIfMissing(screen);

        var controls = Controls.FirstOrDefault(candidate => candidate.IsFor(screen));
        if (controls == null) return;

        var hasCurrentRelic = TryGetCurrentSeenRelic(screen, out var relicModel);
        var shouldShow = _inspectionFromEditor
                         && RelicCompendiumFilterUi.IsEditingCombatRelevance
                         && hasCurrentRelic;
        controls.Root.Visible = shouldShow;
        if (!shouldShow) return;

        var isNonCombat = RelicClassificationStore.IsNonCombat(relicModel);
        var relevantUntilTurn = RelicClassificationStore.GetCombatRelevantUntilTurn(relicModel);
        var hadFocusWithinControls = HasFocusWithinControls(controls);
        _syncingControls = true;
        try
        {
            controls.Combat.SetPressedNoSignal(!isNonCombat);
            controls.NonCombat.SetPressedNoSignal(isNonCombat);
            controls.Duration.Visible = !isNonCombat;
            SelectItemById(
                controls.Duration,
                relevantUntilTurn ?? AlwaysRelevantItemId);
            UpdateFocusNeighbors(controls, isNonCombat);
        }
        finally
        {
            _syncingControls = false;
        }

        if (screen.Visible && !hadFocusWithinControls)
            (isNonCombat ? controls.NonCombat : controls.Combat).GrabFocus();
    }

    public static void EndInspection(NInspectRelicScreen? screen)
    {
        _inspectionFromEditor = false;
        if (screen == null || !GodotObject.IsInstanceValid(screen)) return;
        var controls = Controls.FirstOrDefault(candidate => candidate.IsFor(screen));
        if (controls != null) controls.Root.Visible = false;
    }

    public static void ReinjectIntoActiveScreen()
    {
        var screen = NGame.Instance?.InspectRelicScreen;
        if (screen == null || !GodotObject.IsInstanceValid(screen)) return;

        _inspectionFromEditor = screen.Visible
                                && RelicCompendiumFilterUi.IsEditingCombatRelevance
                                && RelicCompendiumFilterUi.HasVisibleRelicCollection();
        Attach(screen);
        Refresh(screen);
    }

    public static void Teardown()
    {
        foreach (var controls in Controls.ToArray())
        {
            if (controls.Root != null && GodotObject.IsInstanceValid(controls.Root))
            {
                controls.Root.GetParent()?.RemoveChild(controls.Root);
                controls.Root.QueueFree();
            }
        }
        Controls.Clear();
        _inspectionFromEditor = false;
        _syncingControls = false;
    }

    private static CheckBox CreateRadioButton(string text, ButtonGroup group)
    {
        var button = new CheckBox
        {
            Text = text,
            ButtonGroup = group,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(145f, 32f),
        };
        button.AddThemeFontSizeOverride("font_size", 18);
        return button;
    }

    private static void Assign(
        NInspectRelicScreen screen,
        bool isNonCombat,
        bool selected)
    {
        if (_syncingControls || !selected) return;
        if (!TryGetCurrentSeenRelic(screen, out var relicModel)) return;

        RelicClassificationStore.SetNonCombat(relicModel, isNonCombat);
        RelicCompendiumFilterUi.ApplyToActiveEntries();
        Refresh(screen);
    }

    private static void AssignDuration(
        NInspectRelicScreen screen,
        OptionButton duration,
        long selectedIndex)
    {
        if (_syncingControls) return;
        if (!TryGetCurrentSeenRelic(screen, out var relicModel)) return;

        var itemId = duration.GetItemId((int)selectedIndex);
        int? relevantUntilTurn = itemId is >= 1 and <= 3 ? itemId : null;
        RelicClassificationStore.SetCombatRelevantUntilTurn(relicModel, relevantUntilTurn);
        RelicCompendiumFilterUi.ApplyToActiveEntries();
        Refresh(screen);
    }

    private static void KeepHorizontalNavigationOnControl(Control control)
    {
        control.FocusNeighborLeft = control.GetPath();
        control.FocusNeighborRight = control.GetPath();
    }

    private static void UpdateFocusNeighbors(InspectorControls controls, bool isNonCombat)
    {
        var middle = isNonCombat ? (Control)controls.NonCombat : controls.Duration;
        controls.Combat.FocusNeighborTop = controls.NonCombat.GetPath();
        controls.Combat.FocusNeighborBottom = middle.GetPath();
        controls.Duration.FocusNeighborTop = controls.Combat.GetPath();
        controls.Duration.FocusNeighborBottom = controls.NonCombat.GetPath();
        controls.NonCombat.FocusNeighborTop = middle == controls.NonCombat
            ? controls.Combat.GetPath()
            : controls.Duration.GetPath();
        controls.NonCombat.FocusNeighborBottom = controls.Combat.GetPath();
    }

    private static void SelectItemById(OptionButton dropdown, int itemId)
    {
        for (var index = 0; index < dropdown.ItemCount; index++)
        {
            if (dropdown.GetItemId(index) != itemId) continue;
            dropdown.Select(index);
            return;
        }
        dropdown.Select(0);
    }

    private static bool HasFocusWithinControls(InspectorControls controls)
    {
        var focusOwner = controls.Root.GetViewport()?.GuiGetFocusOwner();
        return focusOwner != null
               && (ReferenceEquals(focusOwner, controls.Root)
                   || controls.Root.IsAncestorOf(focusOwner));
    }

    private static bool TryGetCurrentSeenRelic(
        NInspectRelicScreen screen,
        out RelicModel relicModel)
    {
        relicModel = null!;
        if (RelicsField?.GetValue(screen) is not IReadOnlyList<RelicModel> relics
            || IndexField?.GetValue(screen) is not int index
            || index < 0
            || index >= relics.Count)
            return false;

        var current = relics[index];
        if (!SaveManager.Instance.IsRelicSeen(current)) return false;
        relicModel = current;
        return true;
    }

    private static void AttachIfMissing(NInspectRelicScreen screen)
    {
        CleanupInvalidControls();
        if (Controls.Any(controls => controls.IsFor(screen))) return;
        Attach(screen);
    }

    private static void CleanupInvalidControls()
    {
        for (var index = Controls.Count - 1; index >= 0; index--)
        {
            if (Controls[index].IsValid) continue;
            Controls.RemoveAt(index);
        }
    }

    private sealed record InspectorControls(
        NInspectRelicScreen Screen,
        PanelContainer Root,
        CheckBox Combat,
        OptionButton Duration,
        CheckBox NonCombat)
    {
        public bool IsValid =>
            Screen != null
            && GodotObject.IsInstanceValid(Screen)
            && Root != null
            && GodotObject.IsInstanceValid(Root);

        public bool IsFor(NInspectRelicScreen screen) => ReferenceEquals(Screen, screen);
    }
}
