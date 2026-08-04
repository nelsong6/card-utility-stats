using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.PotionLab;

namespace SpireLens.Core.Patches;

internal enum CompendiumPotionViewMode
{
    Gallery = 0,
    CurrentRun = 1,
}

internal enum PotionTimelineOccurrence
{
    SeenNotTaken = 0,
    Acquired = 1,
    Used = 2,
    Discarded = 3,
    HeldAtRunEnd = 4,
}

[HarmonyPatch(typeof(NPotionLab), "_Ready")]
public static class PotionCompendiumHistoryReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NPotionLab __instance)
    {
        PatchGuard.Run(nameof(PotionCompendiumHistoryReadyPatch), () =>
        {
            PotionCompendiumHistoryUi.Inject(__instance);
        });
    }
}

[HarmonyPatch(typeof(NPotionLab), nameof(NPotionLab.OnSubmenuOpened))]
public static class PotionCompendiumHistoryOpenedPatch
{
    [HarmonyPostfix]
    public static void Postfix(NPotionLab __instance)
    {
        PatchGuard.Run(nameof(PotionCompendiumHistoryOpenedPatch), () =>
        {
            PotionCompendiumHistoryUi.Inject(__instance);
            PotionCompendiumHistoryUi.ApplyLayout(__instance);
        });
    }
}

[HarmonyPatch(typeof(NPotionLab), "LoadPotions")]
public static class PotionCompendiumHistoryLoadedPatch
{
    [HarmonyPostfix]
    public static void Postfix(NPotionLab __instance)
    {
        PatchGuard.Run(nameof(PotionCompendiumHistoryLoadedPatch), () =>
        {
            PotionCompendiumHistoryUi.ApplyLayout(__instance);
        });
    }
}

[HarmonyPatch(typeof(NPotionLab), "ClearPotions")]
public static class PotionCompendiumHistoryClearPatch
{
    [HarmonyPrefix]
    public static void Prefix(NPotionLab __instance)
    {
        PatchGuard.Run(nameof(PotionCompendiumHistoryClearPatch), () =>
        {
            PotionCompendiumHistoryUi.RestoreLayout(__instance);
        });
    }
}

internal static class PotionCompendiumHistoryUi
{
    private const string SelectorName = "SpireLensPotionViewPanel";
    private const string HistoryRootName = "SpireLensPotionRunHistory";
    private static readonly List<InjectedPotionSelector> Selectors = new();
    private static readonly List<PotionHistoryLayout> Layouts = new();
    private static readonly ConditionalWeakTable<NLabPotionHolder, PotionHoverContext>
        HolderHistory = new();
    private static CompendiumPotionViewMode _mode = CompendiumPotionViewMode.Gallery;
    private static bool _syncingControls;

    public static void Inject(NPotionLab? lab)
    {
        if (lab == null || !GodotObject.IsInstanceValid(lab)) return;
        CleanupInvalid();

        var existing = Selectors.FirstOrDefault(selector => selector.IsFor(lab));
        if (existing != null)
        {
            SyncSelector(existing);
            ApplyLayout(lab);
            return;
        }

        RemoveNamedChild(lab, SelectorName);
        var selector = BuildSelector();
        lab.AddChild(selector.Root);
        var injected = selector with { Lab = lab };
        Selectors.Add(injected);
        SyncSelector(injected);
        ApplyLayout(lab);
        CoreMain.Logger.Info("PotionCompendiumHistory: injected view dropdown");
    }

    public static void ReinjectIntoActiveLabs()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;
            foreach (var lab in FindLabs(tree.Root))
                Inject(lab);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"PotionCompendiumHistory reinjection failed: {e}");
        }
    }

    public static void SelectCurrentRunMode()
    {
        _mode = CompendiumPotionViewMode.CurrentRun;
        SyncAllSelectors();
        ApplyToActiveLabs();
    }

    public static void TeardownInjectedUi()
    {
        foreach (var layout in Layouts.ToArray())
            layout.Restore();
        Layouts.Clear();

        foreach (var selector in Selectors.ToArray())
            selector.QueueFree();
        Selectors.Clear();
    }

    public static void ApplyLayout(NPotionLab? lab)
    {
        if (lab == null || !GodotObject.IsInstanceValid(lab)) return;
        if (_mode == CompendiumPotionViewMode.CurrentRun)
            ShowHistory(lab);
        else
            RestoreLayout(lab);
    }

    public static void RestoreLayout(NPotionLab? lab)
    {
        if (lab == null) return;
        for (var i = Layouts.Count - 1; i >= 0; i--)
        {
            var layout = Layouts[i];
            if (!layout.IsFor(lab)) continue;
            layout.Restore();
            Layouts.RemoveAt(i);
        }
    }

    private static InjectedPotionSelector BuildSelector()
    {
        var root = new VBoxContainer
        {
            Name = SelectorName,
            Position = new Vector2(34f, 126f),
            CustomMinimumSize = new Vector2(218f, 0f),
            ZIndex = 200,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };

        var dropdown = new OptionButton
        {
            Name = "ModeDropdown",
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        dropdown.AddItem("Potion gallery", (int)CompendiumPotionViewMode.Gallery);
        dropdown.AddItem("Current run stats", (int)CompendiumPotionViewMode.CurrentRun);
        dropdown.Connect(
            OptionButton.SignalName.ItemSelected,
            Callable.From<long>(index => OnModeSelected(dropdown, index)));
        root.AddChild(dropdown);

        return new InjectedPotionSelector(null, root, dropdown);
    }

    private static void OnModeSelected(OptionButton dropdown, long selectedIndex)
    {
        if (_syncingControls) return;
        var selectedId = dropdown.GetItemId((int)selectedIndex);
        _mode = Enum.IsDefined(typeof(CompendiumPotionViewMode), selectedId)
            ? (CompendiumPotionViewMode)selectedId
            : CompendiumPotionViewMode.Gallery;

        SyncAllSelectors();
        ApplyToActiveLabs();
    }

    private static void SyncAllSelectors()
    {
        CleanupInvalid();
        foreach (var selector in Selectors)
            SyncSelector(selector);
    }

    private static void SyncSelector(InjectedPotionSelector selector)
    {
        if (!selector.IsValid) return;
        _syncingControls = true;
        try
        {
            for (var i = 0; i < selector.Dropdown.ItemCount; i++)
            {
                if (selector.Dropdown.GetItemId(i) != (int)_mode) continue;
                selector.Dropdown.Select(i);
                break;
            }
        }
        finally
        {
            _syncingControls = false;
        }
    }

    private static void ApplyToActiveLabs()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        foreach (var lab in FindLabs(tree.Root))
            ApplyLayout(lab);
        CleanupInvalid();
    }

    private static void ShowHistory(NPotionLab lab)
    {
        RestoreLayout(lab);
        var categories = GetCategories(lab).ToList();
        var host = categories.FirstOrDefault()?.GetParent();
        if (host == null) return;

        RemoveNamedChild(host, HistoryRootName);
        var categoryStates = categories
            .Select(category => new PotionCategoryState(category, category.Visible))
            .ToList();
        foreach (var category in categories)
            category.Visible = false;

        var root = BuildTimeline();
        host.AddChild(root);
        var firstIndex = categories.Count > 0
            ? categories.Min(category => category.GetIndex())
            : host.GetChildCount() - 1;
        host.MoveChild(root, Math.Max(0, firstIndex));
        Layouts.Add(new PotionHistoryLayout(lab, root, categoryStates));
    }

    private static Control BuildTimeline()
    {
        var entries = RunTracker.GetEffectivePotionHistory(out var outcome)
            .OrderBy(entry => entry.Sequence)
            .ToList();
        var instanceNumbersBySequence =
            BuildPotionInstanceNumbersBySequence(entries);
        var root = new MarginContainer
        {
            Name = HistoryRootName,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        var connectorLayer = new Control
        {
            Name = "PotionLifecycleConnectors",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var timeline = new GridContainer
        {
            Name = "PotionTimelineGrid",
            Columns = 3,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        root.AddChild(connectorLayer);
        root.AddChild(timeline);

        timeline.AddChild(NewColumnHeader("Seen, not taken"));
        timeline.AddChild(NewColumnHeader("Taken"));
        timeline.AddChild(NewColumnHeader("Used"));

        var acquiredCells = new Dictionary<int, Control>();
        var endpointCells = new Dictionary<int, Control>();
        foreach (var row in BuildTimelineRows(
                     entries,
                     outcome,
                     instanceNumbersBySequence))
        {
            timeline.AddChild(BuildTimelineCell(row.SeenNotTaken, outcome));

            var acquiredCell = BuildTimelineCell(row.Acquired, outcome);
            timeline.AddChild(acquiredCell);
            if (row.Acquired != null)
                acquiredCells[row.Acquired.Entry.Sequence] = acquiredCell;

            var endpointCell = BuildTimelineCell(row.Endpoint, outcome);
            timeline.AddChild(endpointCell);
            if (row.Endpoint != null)
                endpointCells[row.Endpoint.Entry.Sequence] = endpointCell;
        }

        AddLifecycleConnectors(
            root,
            connectorLayer,
            acquiredCells,
            endpointCells);
        return root;
    }

    private static IReadOnlyList<PotionTimelineRow> BuildTimelineRows(
        IReadOnlyList<PotionRunHistoryEntry> entries,
        string outcome,
        IReadOnlyDictionary<int, int> instanceNumbersBySequence)
    {
        var rows = new List<PotionTimelineRow>();
        foreach (var entry in entries)
        {
            var instanceNumber = instanceNumbersBySequence.TryGetValue(
                entry.Sequence,
                out var numberedInstance)
                ? numberedInstance
                : 1;
            if (!entry.Acquired)
            {
                var seen = NewTimelineItem(
                    entry,
                    instanceNumber,
                    PotionTimelineOccurrence.SeenNotTaken,
                    entry.SeenFloor,
                    entry.SeenLocationKind,
                    entry.SeenLocationName,
                    entry.SeenTurn);
                rows.Add(new PotionTimelineRow(seen.Position, seen, null, null));
                continue;
            }

            var acquired = NewTimelineItem(
                entry,
                instanceNumber,
                PotionTimelineOccurrence.Acquired,
                entry.AcquiredFloor,
                entry.AcquiredLocationKind,
                entry.AcquiredLocationName,
                entry.AcquiredTurn);
            var endpoint = BuildEndpointItem(entry, instanceNumber, outcome);
            if (endpoint != null && SameTimelineMoment(acquired.Position, endpoint.Position))
            {
                rows.Add(new PotionTimelineRow(acquired.Position, null, acquired, endpoint));
            }
            else
            {
                rows.Add(new PotionTimelineRow(acquired.Position, null, acquired, null));
                if (endpoint != null)
                    rows.Add(new PotionTimelineRow(endpoint.Position, null, null, endpoint));
            }
        }

        return rows
            .OrderBy(row => row.Position.Floor ?? int.MaxValue)
            .ThenBy(row => row.Position.Turn ?? int.MaxValue)
            .ThenBy(row => row.Position.Sequence)
            .ThenBy(row => row.Position.Phase)
            .ToList();
    }

    private static PotionTimelineItem? BuildEndpointItem(
        PotionRunHistoryEntry entry,
        int instanceNumber,
        string outcome)
    {
        if (entry.Used)
        {
            return NewTimelineItem(
                entry,
                instanceNumber,
                PotionTimelineOccurrence.Used,
                entry.UsedFloor,
                entry.UsedLocationKind,
                entry.UsedLocationName,
                entry.UsedTurn);
        }

        if (entry.Discarded)
        {
            return NewTimelineItem(
                entry,
                instanceNumber,
                PotionTimelineOccurrence.Discarded,
                entry.DiscardedFloor,
                entry.DiscardedLocationKind,
                entry.DiscardedLocationName,
                entry.DiscardedTurn);
        }

        if (!entry.HeldAtRunEnd && outcome == "in_progress") return null;
        return NewTimelineItem(
            entry,
            instanceNumber,
            PotionTimelineOccurrence.HeldAtRunEnd,
            entry.HeldAtRunEndFloor,
            "Run end",
            null,
            null);
    }

    private static PotionTimelineItem NewTimelineItem(
        PotionRunHistoryEntry entry,
        int instanceNumber,
        PotionTimelineOccurrence occurrence,
        int? floor,
        string? kind,
        string? name,
        int? turn)
        => new(
            entry,
            instanceNumber,
            occurrence,
            new PotionTimelinePosition(
                floor,
                kind,
                name,
                turn,
                entry.Sequence,
                occurrence == PotionTimelineOccurrence.SeenNotTaken ? 0
                    : occurrence == PotionTimelineOccurrence.Acquired ? 1
                    : 2));

    internal static IReadOnlyDictionary<int, int>
        BuildPotionInstanceNumbersBySequence(
            IReadOnlyList<PotionRunHistoryEntry> entries)
    {
        var countsByPotionId = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var numbersBySequence = new Dictionary<int, int>();
        foreach (var entry in entries.OrderBy(entry => entry.Sequence))
        {
            var potionId = entry.PotionId ?? string.Empty;
            countsByPotionId.TryGetValue(potionId, out var previousCount);
            var instanceNumber = previousCount + 1;
            countsByPotionId[potionId] = instanceNumber;
            numbersBySequence[entry.Sequence] = instanceNumber;
        }

        return numbersBySequence;
    }

    private static bool SameTimelineMoment(
        PotionTimelinePosition left,
        PotionTimelinePosition right)
        => left.Floor == right.Floor
            && left.Turn == right.Turn
            && string.Equals(left.Kind, right.Kind, StringComparison.Ordinal)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal);

    private static Label NewColumnHeader(string text)
        => new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

    private static Control BuildTimelineCell(PotionTimelineItem? item, string outcome)
    {
        var cell = new CenterContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };

        if (item == null) return cell;

        var holder = CreateNativePotionHolder(item, outcome);
        if (holder != null)
            cell.AddChild(holder);
        return cell;
    }

    private static void AddLifecycleConnectors(
        Control root,
        Control connectorLayer,
        IReadOnlyDictionary<int, Control> acquiredCells,
        IReadOnlyDictionary<int, Control> endpointCells)
    {
        var connectors = acquiredCells
            .Where(pair => endpointCells.ContainsKey(pair.Key))
            .Select(pair => new PotionLifecycleConnector(
                pair.Value,
                endpointCells[pair.Key],
                new Line2D
                {
                    Name = $"PotionLifecycle{pair.Key}",
                    Width = 3f,
                    DefaultColor = new Color(0.56f, 0.68f, 0.74f, 0.55f),
                    Antialiased = true,
                }))
            .ToList();
        foreach (var connector in connectors)
            connectorLayer.AddChild(connector.Line);

        void RefreshConnectors()
        {
            if (!GodotObject.IsInstanceValid(root)
                || !GodotObject.IsInstanceValid(connectorLayer))
            {
                return;
            }

            var inverse = connectorLayer.GetGlobalTransform().AffineInverse();
            foreach (var connector in connectors)
            {
                if (!GodotObject.IsInstanceValid(connector.AcquiredCell)
                    || !GodotObject.IsInstanceValid(connector.EndpointCell)
                    || !GodotObject.IsInstanceValid(connector.Line))
                {
                    continue;
                }

                var acquired = inverse * connector.AcquiredCell.GetGlobalRect().GetCenter();
                var endpoint = inverse * connector.EndpointCell.GetGlobalRect().GetCenter();
                if (Mathf.IsEqualApprox(acquired.Y, endpoint.Y))
                {
                    connector.Line.Points = [acquired, endpoint];
                    continue;
                }

                var gutter = (acquired.X + endpoint.X) / 2f;
                connector.Line.Points =
                [
                    acquired,
                    new Vector2(gutter, acquired.Y),
                    new Vector2(gutter, endpoint.Y),
                    endpoint,
                ];
            }
        }

        root.Resized += RefreshConnectors;
        Callable.From(RefreshConnectors).CallDeferred();
    }

    internal static bool TryBuildNativeHoverTip(
        NLabPotionHolder holder,
        out HoverTip statsTip)
    {
        statsTip = default;
        if (!HolderHistory.TryGetValue(holder, out var context)) return false;

        statsTip = StatsTooltip.CreateNativeTip(
            BuildTooltipTitle(context.Entry, context.InstanceNumber),
            BuildTooltipBody(context.Entry, context.Outcome, context.Occurrence));
        return true;
    }

    private static NLabPotionHolder? CreateNativePotionHolder(
        PotionTimelineItem item,
        string outcome)
    {
        try
        {
            var potion = ModelDb.GetByIdOrNull<PotionModel>(
                ModelId.Deserialize(item.Entry.PotionId));
            if (potion == null) return null;

            var holder = NLabPotionHolder.Create(
                potion.ToMutable(),
                ModelVisibility.Visible);
            HolderHistory.Add(
                holder,
                new PotionHoverContext(
                    item.Entry,
                    item.InstanceNumber,
                    outcome,
                    item.Occurrence));
            return holder;
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error(
                $"PotionCompendiumHistory could not create native holder for {item.Entry.PotionId}: {e}");
            return null;
        }
    }

    private static string BuildTooltipTitle(
        PotionRunHistoryEntry entry,
        int instanceNumber)
    {
        var name = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? entry.PotionId
            : entry.DisplayName;
        return $"{name} {Math.Max(1, instanceNumber)}";
    }

    private static string BuildTooltipBody(
        PotionRunHistoryEntry entry,
        string outcome,
        PotionTimelineOccurrence occurrence)
    {
        var body = new StringBuilder();
        if (occurrence == PotionTimelineOccurrence.SeenNotTaken)
        {
            AppendLocationRows(
                body,
                "Seen",
                entry.SeenFloor,
                entry.SeenLocationKind,
                entry.SeenLocationName,
                entry.SeenTurn);
            AppendTooltipRow(body, "Method", entry.AcquisitionMethod);
            AppendTooltipRow(
                body,
                "Outcome",
                $"not {StatConceptGlossary.RenderHintedGlyph("taken")}",
                valueIsBbcode: true);
            return body.ToString().TrimEnd();
        }

        if (occurrence == PotionTimelineOccurrence.Acquired)
        {
            AppendLocationRows(
                body,
                "Acquired",
                entry.AcquiredFloor,
                entry.AcquiredLocationKind,
                entry.AcquiredLocationName,
                entry.AcquiredTurn);
            AppendTooltipRow(body, "Method", entry.AcquisitionMethod);
            if (!entry.Used
                && !entry.Discarded
                && !entry.HeldAtRunEnd
                && outcome == "in_progress")
            {
                AppendTooltipRow(body, "Status", "Held now");
            }
        }
        else if (occurrence == PotionTimelineOccurrence.Used)
        {
            AppendLocationRows(
                body,
                "Used",
                entry.UsedFloor,
                entry.UsedLocationKind,
                entry.UsedLocationName,
                entry.UsedTurn);
            if (entry.HpGained.HasValue)
            {
                AppendTooltipRow(
                    body,
                    "HP gained",
                    Math.Max(0, entry.HpGained.Value).ToString(),
                    "HP actually restored when this potion was used.");
            }
            if (entry.CardsDrawn.HasValue)
            {
                AppendTooltipRow(
                    body,
                    "Cards drawn",
                    Math.Max(0, entry.CardsDrawn.Value).ToString(),
                    "Cards actually drawn when this potion was used.");
                AppendTooltipRow(
                    body,
                    "Card draws blocked",
                    Math.Max(0, entry.CardDrawsBlocked ?? 0).ToString(),
                    "Card draws attempted by this potion that were blocked.");
            }
            if (entry.BlockGained.HasValue)
            {
                AppendTooltipRow(
                    body,
                    "Block gained",
                    Math.Max(0, entry.BlockGained.Value).ToString(),
                    "Block actually gained when this potion was used.");
                AppendTooltipRow(
                    body,
                    "Block absorbed",
                    Math.Max(0, entry.BlockEffective ?? 0).ToString(),
                    "Block from this potion that absorbed damage.");
                AppendTooltipRow(
                    body,
                    "Block wasted",
                    Math.Max(0, entry.BlockWasted ?? 0).ToString(),
                    "Unused Block from this potion that expired.");
            }
            if (entry.DamageAttempted.HasValue)
            {
                AppendTooltipRow(
                    body,
                    "Damage attempted",
                    Math.Max(0, entry.DamageAttempted.Value).ToString(),
                    "Damage this potion attempted before Block and overkill.");
                AppendTooltipRow(
                    body,
                    "Damage dealt",
                    Math.Max(0, entry.DamageDealt ?? 0).ToString(),
                    "HP damage actually dealt by this potion.");
                AppendTooltipRow(
                    body,
                    "Damage blocked",
                    Math.Max(0, entry.DamageBlocked ?? 0).ToString(),
                    "Damage from this potion prevented by enemy Block.");
                AppendTooltipRow(
                    body,
                    "Overkill",
                    Math.Max(0, entry.DamageOverkill ?? 0).ToString(),
                    "Damage from this potion beyond the target's remaining HP.");
                AppendTooltipRow(
                    body,
                    "Kills",
                    Math.Max(0, entry.Kills ?? 0).ToString(),
                    "Enemies killed by this potion.");
                AppendTooltipRow(
                    body,
                    "Targets hit",
                    Math.Max(0, entry.TargetsHit ?? 0).ToString(),
                    "Enemies hit by this potion.");
            }
        }
        else if (occurrence == PotionTimelineOccurrence.Discarded)
        {
            AppendLocationRows(
                body,
                "Discarded",
                entry.DiscardedFloor,
                entry.DiscardedLocationKind,
                entry.DiscardedLocationName,
                entry.DiscardedTurn);
        }
        else
        {
            AppendTooltipRow(
                body,
                "Held at run end",
                entry.HeldAtRunEndFloor.HasValue
                    ? $"Floor {entry.HeldAtRunEndFloor.Value}"
                    : "Final floor unknown");
        }
        return body.ToString().TrimEnd();
    }

    private static void AppendTooltipRow(
        StringBuilder body,
        string label,
        string value,
        string? fullDescription = null,
        bool valueIsBbcode = false)
    {
        if (!string.IsNullOrWhiteSpace(fullDescription))
        {
            var presentation = StatsTooltip.CreateStatRowPresentation(
                label,
                fullDescription);
            body.Append(StatConceptGlossary.RenderInformationHint(
                    presentation.FullDescription))
                .Append(' ');
            StatsTooltip.AppendConceptLabel(
                body,
                presentation.ConceptIds,
                presentation.DenominatorConceptIds,
                presentation.Label);
        }
        else
        {
            body.Append(StatsTooltip.EscapeBbcode(label));
        }
        body.Append("  [b]");
        body.Append(valueIsBbcode ? value : StatsTooltip.EscapeBbcode(value));
        body.Append("[/b]\n");
    }

    private static void AppendLocationRows(
        StringBuilder body,
        string timingLabel,
        int? floor,
        string? kind,
        string? name,
        int? turn)
    {
        var hasKind = !string.IsNullOrWhiteSpace(kind);
        var hasName = !string.IsNullOrWhiteSpace(name);
        AppendTooltipRow(
            body,
            timingLabel,
            floor.HasValue
                ? $"Floor {floor.Value}"
                : hasKind || hasName
                    ? "Floor unknown"
                    : "Unknown location");

        if (hasKind && hasName)
        {
            AppendTooltipRow(body, kind!, name!);
        }
        else if (hasKind || hasName)
        {
            AppendTooltipRow(body, "Location", hasName ? name! : kind!);
        }

        if (turn.HasValue)
            AppendTooltipRow(body, "Turn", turn.Value.ToString());
    }

    private static IEnumerable<NPotionLabCategory> GetCategories(NPotionLab lab)
    {
        if (lab._common != null) yield return lab._common;
        if (lab._uncommon != null) yield return lab._uncommon;
        if (lab._rare != null) yield return lab._rare;
        if (lab._special != null) yield return lab._special;
    }

    private static IEnumerable<NPotionLab> FindLabs(Node? node)
    {
        if (node == null) yield break;
        if (node is NPotionLab lab) yield return lab;
        for (var i = 0; i < node.GetChildCount(); i++)
        {
            foreach (var childLab in FindLabs(node.GetChild(i)))
                yield return childLab;
        }
    }

    private static void RemoveNamedChild(Node parent, string name)
    {
        for (var i = parent.GetChildCount() - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (!string.Equals(child.Name.ToString(), name, StringComparison.Ordinal)) continue;
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void CleanupInvalid()
    {
        for (var i = Selectors.Count - 1; i >= 0; i--)
            if (!Selectors[i].IsValid) Selectors.RemoveAt(i);
        for (var i = Layouts.Count - 1; i >= 0; i--)
            if (!Layouts[i].IsValid) Layouts.RemoveAt(i);
    }

    private sealed record InjectedPotionSelector(
        NPotionLab? Lab,
        VBoxContainer Root,
        OptionButton Dropdown)
    {
        public bool IsValid => Lab != null
            && GodotObject.IsInstanceValid(Lab)
            && GodotObject.IsInstanceValid(Root)
            && GodotObject.IsInstanceValid(Dropdown);

        public bool IsFor(NPotionLab lab) => IsValid && ReferenceEquals(Lab, lab);

        public void QueueFree()
        {
            if (GodotObject.IsInstanceValid(Root)) Root.QueueFree();
        }
    }

    private sealed class PotionHistoryLayout
    {
        private readonly NPotionLab _lab;
        private readonly Control _root;
        private readonly IReadOnlyList<PotionCategoryState> _categories;

        public PotionHistoryLayout(
            NPotionLab lab,
            Control root,
            IReadOnlyList<PotionCategoryState> categories)
        {
            _lab = lab;
            _root = root;
            _categories = categories;
        }

        public bool IsValid => GodotObject.IsInstanceValid(_lab);
        public bool IsFor(NPotionLab lab) => ReferenceEquals(_lab, lab);

        public void Restore()
        {
            if (GodotObject.IsInstanceValid(_root)) _root.QueueFree();
            foreach (var state in _categories)
                state.Restore();
        }
    }

    private sealed record PotionCategoryState(NPotionLabCategory Category, bool Visible)
    {
        public void Restore()
        {
            if (GodotObject.IsInstanceValid(Category)) Category.Visible = Visible;
        }
    }

    private sealed record PotionHoverContext(
        PotionRunHistoryEntry Entry,
        int InstanceNumber,
        string Outcome,
        PotionTimelineOccurrence Occurrence);

    private sealed record PotionTimelineItem(
        PotionRunHistoryEntry Entry,
        int InstanceNumber,
        PotionTimelineOccurrence Occurrence,
        PotionTimelinePosition Position);

    private sealed record PotionTimelineRow(
        PotionTimelinePosition Position,
        PotionTimelineItem? SeenNotTaken,
        PotionTimelineItem? Acquired,
        PotionTimelineItem? Endpoint);

    private sealed record PotionTimelinePosition(
        int? Floor,
        string? Kind,
        string? Name,
        int? Turn,
        int Sequence,
        int Phase);

    private sealed record PotionLifecycleConnector(
        Control AcquiredCell,
        Control EndpointCell,
        Line2D Line);
}
