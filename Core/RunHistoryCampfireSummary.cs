using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireLens.Core.Patches;

namespace SpireLens.Core;

internal sealed record RunHistoryCampfireEntry(
    int Floor,
    IReadOnlyList<string> ChoiceIds);

/// <summary>
/// Adds one compact campfire-history entry beneath the stock act rows. The
/// game's run-history file already records each selected player's exact rest
/// site choices, so this surface only presents native history data.
/// </summary>
internal static class RunHistoryCampfireSummary
{
    private const string SummaryRowName = "SpireLensCampfireSummary";
    private const string RestIconPath =
        "res://images/atlases/ui_atlas.sprites/map/icons/map_rest.tres";
    private const float IconSize = 64f;

    private static HBoxContainer? _row;
    private static Button? _button;
    private static Action? _showHandler;
    private static Action? _hideHandler;

    public static void Refresh(
        NRunHistory runHistory,
        RunHistory history,
        RunHistoryPlayer player)
    {
        Remove(runHistory);

        if (!IsLive(runHistory)
            || history == null
            || player == null
            || !IsLive(runHistory._mapPointHistory))
        {
            return;
        }

        var entries = CollectEntries(history, player.Id);
        if (entries.Count == 0) return;

        var acts = runHistory._mapPointHistory.GetNodeOrNull<Container>("%Acts");
        if (acts == null)
        {
            CoreMain.Logger.Warn(
                "RunHistoryCampfireSummary: %Acts was not found; campfire history was not injected.");
            return;
        }

        var row = new HBoxContainer
        {
            Name = SummaryRowName,
            Alignment = BoxContainer.AlignmentMode.Begin,
        };

        var titleTemplate = acts.GetChildren()
            .OfType<NActHistoryEntry>()
            .Select(act => act.GetNodeOrNull<Label>("%Title"))
            .FirstOrDefault(title => title != null);
        var title = titleTemplate?.Duplicate() as Label ?? new Label();
        title.Name = "Title";
        title.Text = "Campfires";
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (title is MegaLabel megaTitle)
            megaTitle.SetTextAutoSize("Campfires");
        row.AddChild(title);

        var button = new Button
        {
            Name = "CampfireHistory",
            Flat = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
        };
        var icon = new TextureRect
        {
            Name = "CampfireIcon",
            Texture = ResourceLoader.Load<Texture2D>(RestIconPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(icon);
        row.AddChild(button);
        acts.AddChild(row);

        var body = BuildBodyBBCode(entries);
        _showHandler = () => ShowTooltip(button, body);
        _hideHandler = () => HideTooltipIfInactive(button);
        button.MouseEntered += _showHandler;
        button.FocusEntered += _showHandler;
        button.MouseExited += _hideHandler;
        button.FocusExited += _hideHandler;

        _row = row;
        _button = button;
    }

    public static void Remove(NRunHistory? runHistory = null)
    {
        DisposeCurrentRow();

        if (!IsLive(runHistory) || !IsLive(runHistory!._mapPointHistory))
            return;

        var acts = runHistory!._mapPointHistory.GetNodeOrNull<Node>("%Acts");
        if (acts == null) return;

        foreach (var child in acts.GetChildren()
                     .Where(child => child.Name == SummaryRowName)
                     .ToList())
        {
            acts.RemoveChild(child);
            child.QueueFree();
        }
    }

    public static void Teardown() => DisposeCurrentRow();

    public static void ReinjectIntoActiveRunHistory()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var runHistory = tree == null ? null : FindRunHistory(tree.Root);
            var history = runHistory?._history;
            var player = runHistory?._selectedPlayerIcon?.Player;
            if (runHistory != null && history != null && player != null)
                Refresh(runHistory, history, player);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error(
                $"RunHistoryCampfireSummary hot-reload reinjection failed: {e}");
        }
    }

    internal static IReadOnlyList<RunHistoryCampfireEntry> CollectEntries(
        RunHistory history,
        ulong playerId)
    {
        var result = new List<RunHistoryCampfireEntry>();
        var floor = 1;

        foreach (var act in history.MapPointHistory)
        {
            foreach (var mapPoint in act)
            {
                var playerEntry = mapPoint.PlayerStats
                    .FirstOrDefault(entry => entry.PlayerId == playerId);
                var choiceIds = playerEntry?.RestSiteChoices?
                    .Where(choice => !string.IsNullOrWhiteSpace(choice))
                    .ToArray() ?? Array.Empty<string>();
                var isCampfire = mapPoint.MapPointType == MapPointType.RestSite
                    || mapPoint.HasRoomOfType(RoomType.RestSite)
                    || choiceIds.Length > 0;

                if (isCampfire)
                    result.Add(new RunHistoryCampfireEntry(floor, choiceIds));

                floor++;
            }
        }

        return result;
    }

    internal static string BuildBodyBBCode(
        IReadOnlyList<RunHistoryCampfireEntry> entries,
        Func<string, string>? choiceFormatter = null)
    {
        choiceFormatter ??= FormatChoice;
        var floorIcon = StatConceptGlossary.RenderHintedGlyph("floor");
        var body = new StringBuilder();

        foreach (var entry in entries)
        {
            if (body.Length > 0) body.Append('\n');

            var choices = entry.ChoiceIds.Count == 0
                ? "No choice recorded"
                : string.Join(
                    " · ",
                    entry.ChoiceIds.Select(choiceFormatter));
            body.Append(floorIcon)
                .Append(' ')
                .Append(Math.Max(0, entry.Floor))
                .Append("   ")
                .Append(StatsTooltip.EscapeBbcode(choices));
        }

        return body.ToString();
    }

    internal static string HumanizeChoiceId(string choiceId)
    {
        if (string.IsNullOrWhiteSpace(choiceId)) return "Unknown";

        var normalized = choiceId
            .Replace('-', '_')
            .Trim('_');
        var words = normalized.Split(
            '_',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return choiceId;

        return string.Join(
            " ",
            words.Select(word => word.Length == 1
                ? word.ToUpperInvariant()
                : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string FormatChoice(string choiceId)
    {
        try
        {
            var localized = new LocString(
                    "rest_site_ui",
                    $"OPTION_{choiceId}.name")
                .GetFormattedText();
            if (!string.IsNullOrWhiteSpace(localized)) return localized;
        }
        catch
        {
            // Old or modded run-history choice ids may have no current
            // localization entry; retain a readable record instead.
        }

        return HumanizeChoiceId(choiceId);
    }

    private static void ShowTooltip(Button button, string body)
    {
        if (!IsLive(button)) return;

        NHoverTipSet.Remove(button);
        var tip = StatsTooltip.CreateNativeTip("Campfires", body);
        var tipSet = NHoverTipSet.CreateAndShow(
            button,
            tip,
            HoverTip.GetHoverTipAlignment(button));
        if (tipSet != null)
            NativeStatsHoverTipStyler.ApplyToLastTextTip(tipSet);
    }

    private static void HideTooltipIfInactive(Button button)
    {
        if (!IsLive(button)) return;
        if (!button.IsHovered() && !button.HasFocus())
            NHoverTipSet.Remove(button);
    }

    private static void DisposeCurrentRow()
    {
        if (IsLive(_button))
        {
            NHoverTipSet.Remove(_button!);
            if (_showHandler != null)
            {
                _button!.MouseEntered -= _showHandler;
                _button.FocusEntered -= _showHandler;
            }

            if (_hideHandler != null)
            {
                _button!.MouseExited -= _hideHandler;
                _button.FocusExited -= _hideHandler;
            }
        }

        if (IsLive(_row))
        {
            _row!.GetParent()?.RemoveChild(_row);
            _row.QueueFree();
        }

        _row = null;
        _button = null;
        _showHandler = null;
        _hideHandler = null;
    }

    private static NRunHistory? FindRunHistory(Node node)
    {
        if (node is NRunHistory runHistory
            && runHistory.IsVisibleInTree())
        {
            return runHistory;
        }

        foreach (var child in node.GetChildren())
        {
            var found = FindRunHistory(child);
            if (found != null) return found;
        }

        return null;
    }

    private static bool IsLive(GodotObject? instance) =>
        instance != null && GodotObject.IsInstanceValid(instance);
}
