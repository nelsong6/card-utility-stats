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

    private static readonly List<Button> InjectedButtons = new();

    private static NRunHistory? _source;
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

        var header = runHistory._deckHistory.GetNodeOrNull<Control>("Header");
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
            ZIndex = 5,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -54f,
            OffsetRight = -4f,
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
        button.Pressed += () => Open(runHistory);
        header.AddChild(button);
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
            ClearViewerReferences();
            return;
        }

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
            ClearViewerReferences();
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

    private static void Open(NRunHistory runHistory)
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

            var mainMenu = NGame.Instance?.MainMenu;
            if (mainMenu == null)
            {
                CoreMain.Logger.Warn(
                    "RunHistoryDeckViewer: main-menu host was not available.");
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
            // Keep the game's native canvas ordering. NGame's global
            // HoverTipsContainer is drawn after the active scene; raising
            // this host with a positive ZIndex would put the entire deck
            // viewer, including its cards, above native hover tips.
            host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            _source = runHistory;
            _sourceWasProcessingInput = runHistory.IsProcessingInput();
            _previousFocus = runHistory.GetViewport()?.GuiGetFocusOwner();
            _host = host;
            _viewer = viewer;

            RunHistoryStatsContext.SetHistoricalDeckViewer(
                viewer,
                individualCards);

            runHistory.SetProcessInput(false);
            mainMenu.AddChild(host);
            host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            host.AddChild(viewer);
            viewer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
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

            if (NControllerManager.Instance?.IsUsingController == true)
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

    private static bool IsLive(GodotObject? value)
        => value != null && GodotObject.IsInstanceValid(value);

    private static void ClearViewerReferences()
    {
        _source = null;
        _host = null;
        _viewer = null;
        _previousFocus = null;
        _sourceWasProcessingInput = false;
        _closeCallable = default;
        _closeCallableBound = false;
        RunHistoryStatsContext.ClearHistoricalDeckViewer();
    }
}
