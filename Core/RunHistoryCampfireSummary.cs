using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using SpireLens.Core.Patches;

namespace SpireLens.Core;

internal sealed record RunHistoryCampfireEntry(
    int Floor,
    IReadOnlyList<string> ChoiceIds,
    PlayerMapPointHistoryEntry PlayerEntry);

internal sealed class RunHistoryCampfireButton : Button
{
    public string StatsBody { get; set; } = string.Empty;

    public bool TryBuildStatsTip(out HoverTip tip)
    {
        tip = default;
        if (string.IsNullOrWhiteSpace(StatsBody)) return false;

        tip = StatsTooltip.CreateNativeTip("Campfires", StatsBody);
        return true;
    }
}

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
    private static RunHistoryCampfireButton? _button;
    private static Action? _showHandler;
    private static Action? _mouseExitHandler;
    private static Action? _focusExitHandler;

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

        var button = new RunHistoryCampfireButton
        {
            Name = "CampfireHistory",
            Flat = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            StatsBody = BuildBodyBBCode(entries),
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

        StatsTooltipPinManager.Attach(button);
        _showHandler = () => ShowTooltip(button);
        _mouseExitHandler = () => HideTooltipOnMouseExit(button);
        _focusExitHandler = () => HideTooltip(button);
        button.MouseEntered += _showHandler;
        button.FocusEntered += _showHandler;
        button.MouseExited += _mouseExitHandler;
        button.FocusExited += _focusExitHandler;

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
                {
                    result.Add(new RunHistoryCampfireEntry(
                        floor,
                        choiceIds,
                        playerEntry ?? new PlayerMapPointHistoryEntry
                        {
                            PlayerId = playerId,
                        }));
                }

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
        var liftNumber = 0;

        foreach (var entry in entries)
        {
            if (body.Length > 0) body.Append('\n');

            liftNumber += entry.ChoiceIds.Count(choice =>
                string.Equals(choice, "LIFT", StringComparison.OrdinalIgnoreCase));
            var outcome = BuildOutcomeText(
                entry,
                liftNumber,
                choiceFormatter);
            body.Append(floorIcon)
                .Append(' ')
                .Append(Math.Max(0, entry.Floor))
                .Append("   ")
                .Append(StatsTooltip.EscapeBbcode(outcome));
        }

        return body.ToString();
    }

    internal static string BuildOutcomeText(
        RunHistoryCampfireEntry entry,
        int liftNumber,
        Func<string, string>? choiceFormatter = null)
    {
        choiceFormatter ??= FormatChoice;
        var player = entry.PlayerEntry;
        var choices = entry.ChoiceIds
            .Where(choice => !string.IsNullOrWhiteSpace(choice))
            .ToArray();
        var choiceSet = choices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pickedRelics = (player.RelicChoices ?? [])
            .Where(choice => choice.wasPicked)
            .Select(choice => choice.choice)
            .Where(id => id != null)
            .ToList();
        var hatchRelics = pickedRelics
            .Where(id => string.Equals(
                id.ToString(),
                "RELIC.BYRDPIP",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var digRelics = choiceSet.Contains("HATCH")
            ? pickedRelics.Except(hatchRelics).ToList()
            : pickedRelics;

        var consumedHealing = false;
        var consumedMaxHp = false;
        var consumedUpgrades = false;
        var consumedRemovedCards = false;
        var consumedGainedCards = false;
        var consumedRelics = false;
        var consumedTransformations = false;
        var segments = new List<string>();

        foreach (var choice in choices)
        {
            var action = choiceFormatter(choice);
            var details = new List<string>();

            switch (choice.ToUpperInvariant())
            {
                case "HEAL":
                    details.Add($"healed {Math.Max(0, player.HpHealed)} HP");
                    consumedHealing = true;
                    break;

                case "SMITH":
                    if ((player.UpgradedCards?.Count ?? 0) > 0)
                    {
                        details.Add(
                            $"upgraded {JoinNames((player.UpgradedCards ?? []).Select(FormatCardId))}");
                    }
                    consumedUpgrades = true;
                    break;

                case "DIG":
                    if (digRelics.Count > 0)
                    {
                        details.Add(
                            $"obtained {JoinNames(digRelics.Select(FormatRelicId))}");
                    }
                    consumedRelics = true;
                    break;

                case "LIFT":
                    details.Add(
                        $"gained 1 Strength (lift {Math.Clamp(liftNumber, 1, 3)} of 3)");
                    break;

                case "MEND":
                    // The history records healing on the recipient, but does
                    // not save which player's Mend caused which portion.
                    details.Add("healed another player");
                    break;

                case "HATCH":
                    var obtained = hatchRelics.Count > 0
                        ? hatchRelics
                        : choiceSet.Contains("DIG")
                            ? []
                            : pickedRelics;
                    if (obtained.Count > 0)
                    {
                        details.Add(
                            $"obtained {JoinNames(obtained.Select(FormatRelicId))}");
                    }

                    if ((player.CardsTransformed?.Count ?? 0) > 0)
                    {
                        details.Add(
                            $"transformed {JoinNames((player.CardsTransformed ?? []).Select(FormatTransformation))}");
                    }

                    consumedRelics = true;
                    consumedTransformations = true;
                    break;

                case "KINDLE":
                    details.Add("added 5 Pumpkin Candle charges");
                    break;

                case "COOK":
                    if ((player.CardsRemoved?.Count ?? 0) > 0)
                    {
                        details.Add(
                            $"removed {JoinNames((player.CardsRemoved ?? []).Select(FormatSerializableCard))}");
                    }

                    if (player.MaxHpGained > 0)
                        details.Add($"gained {player.MaxHpGained} Max HP");
                    if (!choiceSet.Contains("HEAL") && player.HpHealed > 0)
                    {
                        details.Add($"healed {player.HpHealed} HP");
                        consumedHealing = true;
                    }

                    consumedRemovedCards = true;
                    consumedMaxHp = true;
                    break;

                case "CLONE":
                    if ((player.CardsGained?.Count ?? 0) > 0)
                    {
                        details.Add(
                            $"cloned {JoinNames((player.CardsGained ?? []).Select(FormatSerializableCard))}");
                    }
                    consumedGainedCards = true;
                    break;
            }

            segments.Add(details.Count == 0
                ? action
                : $"{action} — {string.Join("; ", details)}");
        }

        if (segments.Count == 0)
            segments.Add("No choice recorded");

        var supplemental = new List<string>();
        if (!consumedHealing && player.HpHealed > 0)
            supplemental.Add($"healed {player.HpHealed} HP");
        if (!consumedMaxHp && player.MaxHpGained > 0)
            supplemental.Add($"gained {player.MaxHpGained} Max HP");
        if (player.MaxHpLost > 0)
            supplemental.Add($"lost {player.MaxHpLost} Max HP");
        if (!consumedUpgrades && (player.UpgradedCards?.Count ?? 0) > 0)
        {
            supplemental.Add(
                $"upgraded {JoinNames((player.UpgradedCards ?? []).Select(FormatCardId))}");
        }
        if ((player.DowngradedCards?.Count ?? 0) > 0)
        {
            supplemental.Add(
                $"downgraded {JoinNames((player.DowngradedCards ?? []).Select(FormatCardId))}");
        }
        if (!consumedRemovedCards && (player.CardsRemoved?.Count ?? 0) > 0)
        {
            supplemental.Add(
                $"removed {JoinNames((player.CardsRemoved ?? []).Select(FormatSerializableCard))}");
        }
        if (!consumedGainedCards && (player.CardsGained?.Count ?? 0) > 0)
        {
            supplemental.Add(
                $"gained {JoinNames((player.CardsGained ?? []).Select(FormatSerializableCard))}");
        }
        if (!consumedTransformations
            && (player.CardsTransformed?.Count ?? 0) > 0)
        {
            supplemental.Add(
                $"transformed {JoinNames((player.CardsTransformed ?? []).Select(FormatTransformation))}");
        }
        if ((player.CardsEnchanted?.Count ?? 0) > 0)
        {
            supplemental.Add(
                $"enchanted {JoinNames((player.CardsEnchanted ?? []).Select(FormatEnchantment))}");
        }
        if (!consumedRelics && pickedRelics.Count > 0)
        {
            supplemental.Add(
                $"obtained {JoinNames(pickedRelics.Select(FormatRelicId))}");
        }
        if ((player.RelicsRemoved?.Count ?? 0) > 0)
        {
            supplemental.Add(
                $"removed {JoinNames((player.RelicsRemoved ?? []).Select(FormatRelicId))}");
        }

        var pickedPotions = (player.PotionChoices ?? [])
            .Where(choice => choice.wasPicked)
            .Select(choice => choice.choice)
            .Where(id => id != null)
            .ToList();
        if (pickedPotions.Count > 0)
        {
            supplemental.Add(
                $"obtained {JoinNames(pickedPotions.Select(FormatPotionId))}");
        }
        if (player.GoldGained > 0)
            supplemental.Add($"gained {player.GoldGained} gold");
        if (player.GoldSpent > 0)
            supplemental.Add($"spent {player.GoldSpent} gold");
        if (player.GoldLost > 0)
            supplemental.Add($"lost {player.GoldLost} gold");

        if (supplemental.Count > 0)
            segments.Add(string.Join("; ", supplemental));

        return string.Join(" · ", segments);
    }

    private static string FormatCardId(ModelId id) =>
        FormatModelTitle(
            id,
            () => SaveUtil.CardOrDeprecated(id).Title);

    private static string FormatRelicId(ModelId id) =>
        FormatModelTitle(
            id,
            () => SaveUtil.RelicOrDeprecated(id).Title.GetFormattedText());

    private static string FormatPotionId(ModelId id) =>
        FormatModelTitle(
            id,
            () => SaveUtil.PotionOrDeprecated(id).Title.GetFormattedText());

    private static string FormatEnchantmentId(ModelId id) =>
        FormatModelTitle(
            id,
            () => SaveUtil.EnchantmentOrDeprecated(id).Title.GetFormattedText());

    private static string FormatSerializableCard(SerializableCard card)
    {
        var fallback = FormatModelId(card?.Id);
        try
        {
            if (card == null) return fallback;
            var title = CardModel.FromSerializable(card).Title;
            if (!string.IsNullOrWhiteSpace(title)) fallback = title;
        }
        catch
        {
            // Deprecated or modded cards may not resolve in the current build.
        }

        if (card?.CurrentUpgradeLevel > 0)
        {
            fallback += card.CurrentUpgradeLevel == 1
                ? "+"
                : $"+{card.CurrentUpgradeLevel}";
        }

        return fallback;
    }

    private static string FormatTransformation(
        CardTransformationHistoryEntry transformation) =>
        $"{FormatSerializableCard(transformation.OriginalCard)} → "
        + FormatSerializableCard(transformation.FinalCard);

    private static string FormatEnchantment(
        CardEnchantmentHistoryEntry enchantment) =>
        $"{FormatSerializableCard(enchantment.Card)} with "
        + FormatEnchantmentId(enchantment.Enchantment);

    private static string FormatModelTitle(
        ModelId? id,
        Func<string> titleProvider)
    {
        var fallback = FormatModelId(id);
        if (id == null) return fallback;

        try
        {
            var title = titleProvider();
            return string.IsNullOrWhiteSpace(title) ? fallback : title;
        }
        catch
        {
            return fallback;
        }
    }

    private static string FormatModelId(ModelId? id) =>
        id == null ? "Unknown" : HumanizeChoiceId(id.Entry);

    private static string JoinNames(IEnumerable<string> names) =>
        string.Join(", ", names.Where(name => !string.IsNullOrWhiteSpace(name)));

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

    internal static void ShowTooltip(RunHistoryCampfireButton button)
    {
        if (!IsLive(button)
            || !ViewStatsInjectorPatch.StatsVisibilityEnabled
            || !button.TryBuildStatsTip(out var tip))
        {
            return;
        }

        NHoverTipSet.Remove(button);
        var tipSet = NHoverTipSet.CreateAndShow(
            button,
            tip,
            HoverTip.GetHoverTipAlignment(button));
        if (tipSet != null)
            NativeStatsHoverTipStyler.ApplyToLastTextTip(tipSet);
    }

    private static void HideTooltipOnMouseExit(
        RunHistoryCampfireButton button)
    {
        if (!IsLive(button)) return;

        // A mouse click leaves ordinary Godot button focus behind. That must
        // not turn a transient hover tip into a de facto pin. Preserve the
        // tip only when controller navigation is actively using that focus.
        if (NControllerManager.Instance?.IsUsingDirectionalNavigation == true
            && button.HasFocus())
        {
            return;
        }

        HideTooltip(button);
    }

    private static void HideTooltip(RunHistoryCampfireButton button)
    {
        if (IsLive(button))
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

            if (_mouseExitHandler != null)
            {
                _button!.MouseExited -= _mouseExitHandler;
            }

            if (_focusExitHandler != null)
            {
                _button!.FocusExited -= _focusExitHandler;
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
        _mouseExitHandler = null;
        _focusExitHandler = null;
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
