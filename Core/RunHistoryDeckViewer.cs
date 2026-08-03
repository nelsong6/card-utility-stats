using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Saves;
using SpireLens.Core.Patches;

namespace SpireLens.Core;

/// <summary>
/// Adds a native deck-view entry point to run history and hosts the game's
/// ordinary <see cref="NDeckViewScreen"/> outside its usual in-run capstone
/// container. Run-history saves retain every final-deck card individually;
/// the stock history list is the only layer that groups equal cards.
/// </summary>
internal static class RunHistoryDeckViewer
{
    private const string ButtonName = "SpireLensFullDeckButton";
    private const string HostName = "SpireLensRunHistoryDeckViewHost";
    private const string DeckIconPath =
        "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_deck.tres";
    private const float ButtonGapAfterHeaderText = 8f;
    private const float ButtonSize = 50f;

    private static readonly List<Button> InjectedButtons = new();

    private static NRunHistory? _source;
    private static Button? _sourceButton;
    private static Control? _host;
    private static NDeckViewScreen? _viewer;
    private static Control? _previousFocus;
    private static bool _sourceWasProcessingInput;
    private static Callable _closeCallable;
    private static bool _closeCallableBound;

    public static bool IsOpen
        => IsLive(_host) && IsLive(_viewer);

    public static bool IsHistoricalDeckViewer(NDeckViewScreen? viewer)
        => viewer != null
           && IsLive(_viewer)
           && ReferenceEquals(_viewer, viewer);

    public static void InjectButton(NRunHistory runHistory)
    {
        if (!IsLive(runHistory) || runHistory._deckHistory == null)
            return;

        RemoveButton(runHistory);

        var header = runHistory._deckHistory.GetNodeOrNull<RichTextLabel>("Header");
        if (header == null)
        {
            CoreMain.Logger.Warn(
                "RunHistoryDeckViewer: Cards header was not found; full-deck button was not injected.");
            return;
        }

        var button = new Button
        {
            Name = ButtonName,
            Flat = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = "View every card in this run's final deck",
            // Stay at the run-history surface's normal canvas depth. The
            // native deck viewer is attached later and must draw its cards
            // above this launcher without raising the entire viewer above
            // the game's global hover-tip layer.
            ZIndex = 0,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            OffsetLeft = 0f,
            OffsetRight = ButtonSize,
            OffsetTop = -6f,
            OffsetBottom = 44f,
        };

        var icon = new TextureRect
        {
            Name = "DeckIcon",
            Texture = ResourceLoader.Load<Texture2D>(DeckIconPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(icon);
        button.Pressed += () => Open(runHistory, button);
        header.AddChild(button);
        PositionButtonAfterHeaderText(header, button);
        Callable.From(() => PositionButtonAfterHeaderText(header, button))
            .CallDeferred();
        InjectedButtons.Add(button);
    }

    public static void ReinjectIntoActiveRunHistory()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var runHistory = tree == null
                ? null
                : FindRunHistory(tree.Root);
            if (runHistory == null || runHistory._history == null)
                return;

            RunHistoryStatsContext.SetRun(runHistory._history);
            StatsTooltipPinManager.AttachRunHistoryTargets(runHistory);
            InjectButton(runHistory);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error(
                $"RunHistoryDeckViewer hot-reload reinjection failed: {e}");
        }
    }

    public static void RefreshAllArrowHotkeys()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree != null)
                RefreshArrowHotkeysRecursive(tree.Root);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error(
                $"RunHistoryDeckViewer arrow-hotkey refresh failed: {e}");
        }
    }

    public static void DisableArrowHotkeys(NRunHistory? runHistory)
    {
        if (!IsLive(runHistory)) return;

        foreach (var arrow in FindArrowButtons(runHistory!))
            arrow.Disable();
    }

    public static void RestoreVisibleArrowHotkeys(NRunHistory? runHistory)
    {
        if (!IsLive(runHistory)) return;

        var shouldEnable = runHistory!.IsVisibleInTree() && !IsOpen;
        foreach (var arrow in FindArrowButtons(runHistory))
        {
            if (shouldEnable && arrow.Visible)
                arrow.Enable();
            else
                arrow.Disable();
        }
    }

    public static bool HandleInput(InputEvent inputEvent)
    {
        if (!IsOpen) return false;

        if (inputEvent.IsActionPressed("ui_cancel", allowEcho: false)
            || inputEvent is InputEventKey
            {
                Pressed: true,
                Echo: false,
                Keycode: Key.Escape,
            })
        {
            Close();
            return true;
        }

        return false;
    }

    public static void Close()
    {
        if (!IsLive(_host) && !IsLive(_viewer))
        {
            RestoreSourceButton();
            ClearViewerReferences();
            return;
        }

        var sourceToRestore = _source;
        try
        {
            StatsTooltipPinManager.ClearPin();
            NHoverTipSet.Clear();
            RunHistoryStatsContext.ClearHistoricalDeckViewer();

            if (IsLive(_viewer))
            {
                if (IsLive(_viewer!._backButton)
                    && _closeCallableBound
                    && _viewer!._backButton.IsConnected(
                        NClickableControl.SignalName.Released,
                        _closeCallable))
                {
                    _viewer._backButton.Disconnect(
                        NClickableControl.SignalName.Released,
                        _closeCallable);
                }

                _viewer!.AfterCapstoneClosed();
            }

            if (IsLive(_host))
            {
                _host!.Visible = false;
                _host.QueueFree();
            }

            if (IsLive(_source))
                _source!.SetProcessInput(_sourceWasProcessingInput);

            if (IsLive(_previousFocus))
                _previousFocus!.CallDeferred(Control.MethodName.GrabFocus);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RunHistoryDeckViewer close failed: {e}");
            if (IsLive(_host))
                _host!.QueueFree();
        }
        finally
        {
            RestoreSourceButton();
            ClearViewerReferences();
            RestoreVisibleArrowHotkeys(sourceToRestore);
        }
    }

    public static void Teardown()
    {
        Close();

        foreach (var button in InjectedButtons.ToArray())
        {
            if (IsLive(button))
                button.QueueFree();
        }
        InjectedButtons.Clear();
    }

    private static void Open(NRunHistory runHistory, Button sourceButton)
    {
        try
        {
            Close();

            var historyPlayer = runHistory._selectedPlayerIcon?.Player;
            if (historyPlayer == null)
            {
                CoreMain.Logger.Warn(
                    "RunHistoryDeckViewer: no run-history player is selected.");
                return;
            }

            var unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
            var player = Player.CreateForNewRun(
                SaveUtil.CharacterOrDeprecated(historyPlayer.Character),
                unlockState,
                historyPlayer.Id);

            player.Deck.Clear(silent: true);
            var individualCards = new List<CardModel>();
            foreach (var serializedCard in historyPlayer.Deck)
            {
                var card = CardModel.FromSerializable(serializedCard);
                card.Owner = player;
                player.Deck.AddInternal(card, -1, silent: true);
                individualCards.Add(card);
            }

            var scenePath = NDeckViewScreen.AssetPaths.Single();
            var viewer = PreloadManager.Cache
                .GetScene(scenePath)
                .Instantiate<NDeckViewScreen>(PackedScene.GenEditState.Disabled);
            viewer._player = player;

            var host = new Control
            {
                Name = HostName,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            // NRunHistory is a full-viewport Control in both the main-menu
            // and active-run submenu contexts. Hosting the viewer as its last
            // child keeps the deck above run history while preserving the
            // game's later global hover-tip layer.
            host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            _source = runHistory;
            _sourceButton = sourceButton;
            _sourceButton.Visible = false;
            _sourceWasProcessingInput = runHistory.IsProcessingInput();
            _previousFocus = runHistory.GetViewport()?.GuiGetFocusOwner();
            _host = host;
            _viewer = viewer;

            RunHistoryStatsContext.SetHistoricalDeckViewer(
                viewer,
                individualCards);

            runHistory.SetProcessInput(false);
            DisableArrowHotkeys(runHistory);
            runHistory.AddChild(host);
            host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            host.AddChild(viewer);
            viewer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            var borderGradient =
                viewer.GetNodeOrNull<CanvasItem>("CardGrid/BorderGradient");
            if (borderGradient is not null)
            {
                borderGradient.Visible = false;
            }
            else
            {
                CoreMain.Logger.Warn(
                    "RunHistoryDeckViewer: native card-grid border gradient was not found.");
            }

            viewer.AfterCapstoneOpened();

            var originalReturn =
                Callable.From<NButton>(viewer.OnReturnButtonPressed);
            if (viewer._backButton.IsConnected(
                    NClickableControl.SignalName.Released,
                    originalReturn))
            {
                viewer._backButton.Disconnect(
                    NClickableControl.SignalName.Released,
                    originalReturn);
            }
            else
            {
                CoreMain.Logger.Warn(
                    "RunHistoryDeckViewer: native deck-view back callback was not connected as expected.");
            }

            _closeCallable = Callable.From<NButton>(_ => Close());
            _closeCallableBound = true;
            viewer._backButton.Connect(
                NClickableControl.SignalName.Released,
                _closeCallable);

            if (NControllerManager.Instance?.IsUsingDirectionalNavigation == true)
                viewer.DefaultFocusedControl?.GrabFocus();

            CoreMain.LogDebug(
                $"RunHistoryDeckViewer: opened {individualCards.Count} individual cards.");
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"RunHistoryDeckViewer open failed: {e}");
            Close();
        }
    }

    private static void RemoveButton(NRunHistory runHistory)
    {
        var existing = runHistory.FindChild(
            ButtonName,
            recursive: true,
            owned: false) as Button;
        if (existing != null && IsLive(existing))
        {
            InjectedButtons.Remove(existing);
            existing.QueueFree();
        }
    }

    private static void PositionButtonAfterHeaderText(
        RichTextLabel header,
        Button button)
    {
        if (!IsLive(header) || !IsLive(button))
            return;

        var left = Math.Max(0f, header.GetContentWidth())
                   + ButtonGapAfterHeaderText;
        button.OffsetLeft = left;
        button.OffsetRight = left + ButtonSize;
    }

    private static NRunHistory? FindRunHistory(Node node)
    {
        if (node is NRunHistory runHistory && runHistory.IsVisibleInTree())
            return runHistory;

        for (var i = 0; i < node.GetChildCount(); i++)
        {
            var found = FindRunHistory(node.GetChild(i));
            if (found != null) return found;
        }

        return null;
    }

    private static void RefreshArrowHotkeysRecursive(Node node)
    {
        if (node is NRunHistory runHistory)
            RestoreVisibleArrowHotkeys(runHistory);

        for (var i = 0; i < node.GetChildCount(); i++)
            RefreshArrowHotkeysRecursive(node.GetChild(i));
    }

    private static IEnumerable<NRunHistoryArrowButton> FindArrowButtons(
        NRunHistory runHistory)
    {
        var left = runHistory.GetNodeOrNull<NRunHistoryArrowButton>("LeftArrow");
        if (left != null)
            yield return left;

        var right = runHistory.GetNodeOrNull<NRunHistoryArrowButton>("RightArrow");
        if (right != null)
            yield return right;
    }

    private static bool IsLive(GodotObject? value)
        => value != null && GodotObject.IsInstanceValid(value);

    private static void RestoreSourceButton()
    {
        if (IsLive(_sourceButton))
            _sourceButton!.Visible = true;
    }

    private static void ClearViewerReferences()
    {
        _source = null;
        _sourceButton = null;
        _host = null;
        _viewer = null;
        _previousFocus = null;
        _sourceWasProcessingInput = false;
        _closeCallable = default;
        _closeCallableBound = false;
        RunHistoryStatsContext.ClearHistoricalDeckViewer();
    }
}
