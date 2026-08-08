using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.addons.mega_text;

namespace SpireLens.Core.Patches;

/// <summary>
/// Adds a fourth, history-backed combat pile above the exhaust pile. The
/// button deliberately reuses the exhaust pile's native visuals and input
/// behavior, while the opened pile contains only Power cards the local player
/// has finished playing during the current combat.
/// </summary>
internal static class PowerHistoryPileUi
{
    private const string ContainerName = "SpireLensPowerHistoryPileContainer";
    private const string ButtonName = "SpireLensPowerHistoryPile";
    private const float VerticalOffset = 112f;
    private const double FocusDurationSeconds = 0.05;
    private const double UnfocusDurationSeconds = 0.5;
    private static readonly Color PowerBlue = Color.FromHtml("7DDEFFFF");
    private static readonly Vector2 FocusedIconScale = Vector2.One * 1.25f;
    private static readonly List<InjectedPile> InjectedPiles = [];

    public static void Inject(NCombatUi combatUi)
    {
        if (!IsLive(combatUi)) return;

        var existing = InjectedPiles.FirstOrDefault(injected => injected.CombatUi == combatUi);
        if (existing != null && existing.IsLive)
        {
            existing.Refresh();
            return;
        }

        RemoveDeadInjections();

        var exhaustPile = combatUi.ExhaustPile;
        if (!IsLive(exhaustPile) || exhaustPile.GetParent() is not Control parent)
        {
            CoreMain.Logger.Warn("PowerHistoryPile: exhaust pile parent was not available.");
            return;
        }

        RemoveNamedChildren(parent, ContainerName);

        var container = new Control
        {
            Name = ContainerName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(0f, -VerticalOffset),
        };
        parent.AddChild(container);
        parent.MoveChild(container, Math.Min(exhaustPile.GetIndex() + 1, parent.GetChildCount() - 1));

        // Keep the game script and scene-instantiation data, but do not copy
        // the live exhaust button's existing signal connections. Its own
        // _Ready method reconnects the native input/animation behavior.
        const int duplicateWithoutSignals = 14;
        if (exhaustPile.Duplicate(duplicateWithoutSignals) is not NExhaustPileButton button)
        {
            container.QueueFree();
            CoreMain.Logger.Warn("PowerHistoryPile: exhaust pile could not be duplicated.");
            return;
        }

        button.Name = ButtonName;
        container.AddChild(button);

        var icon = button.GetNodeOrNull<Control>("Icon");
        var countLabel = button.GetNodeOrNull<MegaLabel>("CountContainer/Count");
        if (icon == null || countLabel == null)
        {
            container.QueueFree();
            CoreMain.Logger.Warn("PowerHistoryPile: icon or count label was not found on the native pile clone.");
            return;
        }

        // SelfModulate survives the native press animation, which animates
        // Modulate between white and dark gray.
        icon.SelfModulate = PowerBlue;

        var injected = new InjectedPile(combatUi, exhaustPile, container, button, icon, countLabel);
        InjectedPiles.Add(injected);
        injected.ConnectSignals();
        injected.Refresh(animateIn: true);
        CoreMain.Logger.Info("PowerHistoryPile: injected played-powers button");
    }

    public static void RefreshAll()
    {
        RemoveDeadInjections();
        foreach (var injected in InjectedPiles.ToArray())
            injected.Refresh();
    }

    public static void SyncEnabled(NCombatUi combatUi, bool enabled)
    {
        RemoveDeadInjections();
        foreach (var injected in InjectedPiles.Where(injected => injected.CombatUi == combatUi))
            injected.SyncEnabled(enabled);
    }

    public static void AnimOut(NCombatUi combatUi)
    {
        RemoveDeadInjections();
        foreach (var injected in InjectedPiles.Where(injected => injected.CombatUi == combatUi))
            injected.AnimOut();
    }

    public static void ReinjectIntoActiveCombat()
    {
        try
        {
            var combatUi = NCombatRoom.Instance?.Ui;
            if (IsLive(combatUi))
                Inject(combatUi!);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"PowerHistoryPile reinjection failed: {e}");
        }
    }

    public static void TeardownInjectedUi()
    {
        foreach (var injected in InjectedPiles.ToArray())
            injected.Dispose();
        InjectedPiles.Clear();
    }

    private static List<CardModel> GetPlayedPowers()
    {
        if (!LocalContext.NetId.HasValue || !CombatManager.Instance.IsInProgress)
            return [];

        return CombatManager.Instance.History.CardPlaysFinished
            .Where(entry =>
                entry.CardPlay.Player.NetId == LocalContext.NetId
                && entry.CardPlay.IsFirstInSeries
                && entry.CardPlay.Card.Type == CardType.Power)
            .Select(entry => entry.CardPlay.Card)
            .ToList();
    }

    private static void OpenPlayedPowers(InjectedPile injected)
    {
        if (!injected.IsLive || !CombatManager.Instance.IsInProgress) return;

        var powers = GetPlayedPowers();
        if (powers.Count == 0) return;

        if (NTargetManager.Instance.IsInSelection)
            NTargetManager.Instance.CancelTargeting();

        if (injected.OpenPile != null
            && NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCardPileScreen current
            && current.Pile == injected.OpenPile)
        {
            CoreMain.Logger.Info("PowerHistoryPile: closing played-powers pile");
            NCapstoneContainer.Instance.Close();
            return;
        }

        // PileType.None makes this a display-only pile: it does not subscribe
        // cards to combat state or change their real pile membership. The
        // native NCardPileScreen still renders ordinary, hoverable cards.
        var displayPile = new CardPile(PileType.None);
        var addedCards = new HashSet<CardModel>(ReferenceEqualityComparer.Instance);
        foreach (var power in powers)
        {
            // A physical Power normally appears once. If an unusual effect
            // returns and plays that exact instance again, the display pile
            // still needs a distinct model for the second chronological slot.
            var displayCard = addedCards.Add(power)
                ? power
                : (CardModel)power.ClonePreservingMutability();
            displayPile.AddInternal(displayCard, silent: true);
        }

        injected.OpenPile = displayPile;
        CoreMain.Logger.Info(
            $"PowerHistoryPile: opening {powers.Count} played power card(s)");
        NCardPileScreen.ShowScreen(displayPile, Array.Empty<string>());
    }

    private static bool IsOpenInput(InputEvent inputEvent)
    {
        return inputEvent is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: false,
            }
            || inputEvent.IsActionReleased(MegaInput.select);
    }

    private static void ShowHoverTip(InjectedPile injected)
    {
        if (!injected.IsLive) return;

        var tip = StatsTooltip.CreateNativeTip(
            "Powers played",
            "Power cards played this combat.");
        var tipSet = NHoverTipSet.CreateAndShow(injected.Button, tip);
        if (tipSet != null)
            tipSet.GlobalPosition = injected.Button.GlobalPosition + new Vector2(-320f, -125f);
    }

    private static void RemoveDeadInjections()
    {
        for (var i = InjectedPiles.Count - 1; i >= 0; i--)
        {
            if (InjectedPiles[i].IsLive) continue;
            InjectedPiles[i].Dispose();
            InjectedPiles.RemoveAt(i);
        }
    }

    private static void RemoveNamedChildren(Node parent, string name)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child.Name == name)
                child.QueueFree();
        }
    }

    private static bool IsLive(GodotObject? instance) =>
        instance != null && GodotObject.IsInstanceValid(instance);

    private sealed class InjectedPile
    {
        private readonly Control _icon;
        private readonly MegaLabel _countLabel;
        private readonly NodePath _originalExhaustFocusNeighborTop;
        private readonly Control.GuiInputEventHandler _guiInputHandler;
        private readonly Action _mouseEnteredHandler;
        private readonly Action _mouseExitedHandler;
        private readonly Action _focusEnteredHandler;
        private readonly Action _focusExitedHandler;
        private Tween? _interactionTween;
        private int _currentCount;
        private bool _pointerInside;
        private bool _controlFocused;
        private bool _isPresentedFocused;
        private bool _signalsConnected;

        public NCombatUi CombatUi { get; }
        public NExhaustPileButton ExhaustPile { get; }
        public Control Container { get; }
        public NExhaustPileButton Button { get; }
        public CardPile? OpenPile { get; set; }

        public bool IsLive =>
            PowerHistoryPileUi.IsLive(CombatUi)
            && PowerHistoryPileUi.IsLive(ExhaustPile)
            && PowerHistoryPileUi.IsLive(Container)
            && PowerHistoryPileUi.IsLive(Button);

        public InjectedPile(
            NCombatUi combatUi,
            NExhaustPileButton exhaustPile,
            Control container,
            NExhaustPileButton button,
            Control icon,
            MegaLabel countLabel)
        {
            CombatUi = combatUi;
            ExhaustPile = exhaustPile;
            Container = container;
            Button = button;
            _icon = icon;
            _countLabel = countLabel;
            _originalExhaustFocusNeighborTop = exhaustPile.FocusNeighborTop;
            _guiInputHandler = HandleGuiInput;
            _mouseEnteredHandler = HandleMouseEntered;
            _mouseExitedHandler = HandleMouseExited;
            _focusEnteredHandler = HandleFocusEntered;
            _focusExitedHandler = HandleFocusExited;
        }

        public void ConnectSignals()
        {
            // This clone deliberately has no CardPile bound to it. The native
            // NCombatCardPile focus handler gates both its hover tip and icon
            // tween on that pile, so supply the equivalent presentation here.
            // Direct GUI input remains gated on the real exhaust pile, which
            // is the game's authoritative combat-pile enabled state.
            Button.GuiInput += _guiInputHandler;
            Button.MouseEntered += _mouseEnteredHandler;
            Button.MouseExited += _mouseExitedHandler;
            Button.FocusEntered += _focusEnteredHandler;
            Button.FocusExited += _focusExitedHandler;
            _signalsConnected = true;
        }

        private void HandleMouseEntered()
        {
            _pointerInside = true;
            RefreshPresentedFocus();
        }

        private void HandleMouseExited()
        {
            _pointerInside = false;
            RefreshPresentedFocus();
        }

        private void HandleFocusEntered()
        {
            _controlFocused = true;
            RefreshPresentedFocus();
        }

        private void HandleFocusExited()
        {
            _controlFocused = false;
            RefreshPresentedFocus();
        }

        private void HandleGuiInput(InputEvent inputEvent)
        {
            if (!IsLive
                || _currentCount <= 0
                || !ExhaustPile.IsEnabled
                || !IsOpenInput(inputEvent))
            {
                return;
            }

            OpenPlayedPowers(this);
        }

        public void Refresh(bool animateIn = false)
        {
            if (!IsLive) return;

            var count = GetPlayedPowers().Count;
            _countLabel.SetTextAutoSize(count.ToString());
            _countLabel.PivotOffset = _countLabel.Size * 0.5f;

            if (count == 0)
            {
                _currentCount = 0;
                Button.Visible = false;
                Button.Disable();
                RestoreExhaustFocusNeighbor();
                RefreshPresentedFocus();
                return;
            }

            Button.FocusNeighborTop = Button.GetPath();
            Button.FocusNeighborBottom = ExhaustPile.GetPath();
            Button.FocusNeighborLeft = Button.GetPath();
            Button.FocusNeighborRight = Button.GetPath();
            ExhaustPile.FocusNeighborTop = Button.GetPath();

            if (!Button.Visible || (animateIn && _currentCount == 0))
                Button.AnimIn();
            else
                Button.Visible = true;

            _currentCount = count;
            SyncEnabled(ExhaustPile.IsEnabled);
        }

        public void SyncEnabled(bool enabled)
        {
            if (!IsLive || _currentCount == 0) return;
            if (enabled)
                Button.Enable();
            else
                Button.Disable();
            RefreshPresentedFocus();
        }

        public void AnimOut()
        {
            if (IsLive && Button.Visible)
                Button.AnimOut();
        }

        public void Dispose()
        {
            if (PowerHistoryPileUi.IsLive(Button))
            {
                _interactionTween?.Kill();
                _interactionTween = null;
                NHoverTipSet.Remove(Button);
                if (_signalsConnected)
                {
                    Button.GuiInput -= _guiInputHandler;
                    Button.MouseEntered -= _mouseEnteredHandler;
                    Button.MouseExited -= _mouseExitedHandler;
                    Button.FocusEntered -= _focusEnteredHandler;
                    Button.FocusExited -= _focusExitedHandler;
                }
            }

            RestoreExhaustFocusNeighbor();
            if (PowerHistoryPileUi.IsLive(Container))
                Container.QueueFree();
            OpenPile = null;
            _signalsConnected = false;
        }

        private void RefreshPresentedFocus()
        {
            var shouldPresentFocused = IsLive
                && _currentCount > 0
                && ExhaustPile.IsEnabled
                && (_pointerInside || _controlFocused);
            if (shouldPresentFocused == _isPresentedFocused) return;

            _isPresentedFocused = shouldPresentFocused;
            _interactionTween?.Kill();

            if (shouldPresentFocused)
            {
                ShowHoverTip(this);
                _interactionTween = Button.CreateTween();
                _interactionTween.TweenProperty(
                    _icon,
                    "scale",
                    FocusedIconScale,
                    FocusDurationSeconds);
                return;
            }

            NHoverTipSet.Remove(Button);
            if (!PowerHistoryPileUi.IsLive(_icon)) return;

            _interactionTween = Button.CreateTween().SetParallel();
            _interactionTween.SetTrans(Tween.TransitionType.Expo);
            _interactionTween.SetEase(Tween.EaseType.Out);
            _interactionTween.TweenProperty(
                _icon,
                "scale",
                Vector2.One,
                UnfocusDurationSeconds);
            _interactionTween.TweenProperty(
                _icon,
                "modulate",
                Colors.White,
                UnfocusDurationSeconds);
        }

        private void RestoreExhaustFocusNeighbor()
        {
            if (!PowerHistoryPileUi.IsLive(ExhaustPile)) return;
            if (!PowerHistoryPileUi.IsLive(Button)
                || ExhaustPile.FocusNeighborTop == Button.GetPath())
            {
                ExhaustPile.FocusNeighborTop = _originalExhaustFocusNeighborTop;
            }
        }
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class PowerHistoryPileActivatePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        PatchGuard.Run(nameof(PowerHistoryPileActivatePatch), () =>
            PowerHistoryPileUi.Inject(__instance));
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Enable))]
internal static class PowerHistoryPileEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        PatchGuard.Run(nameof(PowerHistoryPileEnablePatch), () =>
            PowerHistoryPileUi.SyncEnabled(__instance, enabled: true));
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Disable))]
internal static class PowerHistoryPileDisablePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        PatchGuard.Run(nameof(PowerHistoryPileDisablePatch), () =>
            PowerHistoryPileUi.SyncEnabled(__instance, enabled: false));
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.AnimOut))]
internal static class PowerHistoryPileAnimOutPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        PatchGuard.Run(nameof(PowerHistoryPileAnimOutPatch), () =>
            PowerHistoryPileUi.AnimOut(__instance));
    }
}
