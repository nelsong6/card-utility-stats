using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
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
    private static readonly Color PowerBlue = Color.FromHtml("7DDEFFFF");
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

        var localPlayer = exhaustPile._localPlayer;
        if (localPlayer == null)
        {
            button.Free();
            container.QueueFree();
            CoreMain.Logger.Warn("PowerHistoryPile: local player was not bound to the exhaust pile.");
            return;
        }

        // Bind a real, persistent pile before the clone enters the scene tree.
        // NCombatCardPile._EnterTree then installs its ordinary add/remove
        // listeners, and every interaction from this point uses the game's
        // native pile-button lifecycle. Exhaust is the closest presentation
        // type understood by NCombatCardPile.OnFocus; cards are inserted
        // directly below so this display-only pile never joins combat state.
        var historyPile = new CardPile(PileType.Exhaust);
        var addedCards = new HashSet<CardModel>(ReferenceEqualityComparer.Instance);
        foreach (var power in GetPlayedPowers())
            AppendDisplayCard(historyPile, addedCards, power, notify: false);

        button.Name = ButtonName;
        button._pile = historyPile;
        button._localPlayer = localPlayer;
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

        var injected = new InjectedPile(
            combatUi,
            exhaustPile,
            container,
            button,
            historyPile,
            addedCards,
            countLabel);
        InjectedPiles.Add(injected);
        injected.ConnectSignals();
        injected.Initialize();
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

    private static void AppendDisplayCard(
        CardPile historyPile,
        HashSet<CardModel> addedCards,
        CardModel power,
        bool notify)
    {
        // A physical Power normally appears once. If an unusual effect
        // returns and plays that exact instance again, the display pile still
        // needs a distinct model for the second chronological slot.
        var displayCard = addedCards.Add(power)
            ? power
            : (CardModel)power.ClonePreservingMutability();

        // CardPile.AddInternal subscribes Exhaust cards to combat state. This
        // pile is UI history, so update its backing collection without that
        // gameplay side effect and emit the same completion notifications the
        // native pile button consumes.
        historyPile._cards.Add(displayCard);
        if (!notify) return;

        historyPile.InvokeContentsChanged();
        historyPile.InvokeCardAddFinished();
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
        private readonly HashSet<CardModel> _addedCards;
        private readonly MegaLabel _countLabel;
        private readonly NodePath _originalExhaustFocusNeighborTop;
        private readonly Action _replaceMouseHoverTipHandler;
        private readonly Action _replaceControllerHoverTipHandler;
        private bool _signalsConnected;

        public NCombatUi CombatUi { get; }
        public NExhaustPileButton ExhaustPile { get; }
        public Control Container { get; }
        public NExhaustPileButton Button { get; }
        public CardPile HistoryPile { get; }

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
            CardPile historyPile,
            HashSet<CardModel> addedCards,
            MegaLabel countLabel)
        {
            CombatUi = combatUi;
            ExhaustPile = exhaustPile;
            Container = container;
            Button = button;
            HistoryPile = historyPile;
            _addedCards = addedCards;
            _countLabel = countLabel;
            _originalExhaustFocusNeighborTop = exhaustPile.FocusNeighborTop;
            _replaceMouseHoverTipHandler = ReplaceNativeHoverTip;
            _replaceControllerHoverTipHandler = ReplaceNativeHoverTip;
        }

        public void ConnectSignals()
        {
            // NClickableControl's handlers were connected during _Ready and
            // therefore run first. They retain all native focus animation;
            // these later handlers only replace the Exhaust wording produced
            // for the history pile's presentation type.
            Button.MouseEntered += _replaceMouseHoverTipHandler;
            Button.FocusEntered += _replaceControllerHoverTipHandler;
            _signalsConnected = true;
        }

        public void Initialize()
        {
            if (!IsLive) return;

            Button._currentCount = HistoryPile.Cards.Count;
            _countLabel.SetTextAutoSize(HistoryPile.Cards.Count.ToString());
            _countLabel.PivotOffset = _countLabel.Size * 0.5f;
            Refresh(animateIn: HistoryPile.Cards.Count > 0);
        }

        public void Refresh(bool animateIn = false)
        {
            if (!IsLive) return;

            var powers = GetPlayedPowers();
            var previousCount = HistoryPile.Cards.Count;
            for (var i = previousCount; i < powers.Count; i++)
            {
                AppendDisplayCard(HistoryPile, _addedCards, powers[i], notify: true);
                SuppressDuplicateExhaustHotkey();
            }

            if (powers.Count < previousCount)
            {
                CoreMain.Logger.Warn(
                    $"PowerHistoryPile: combat history shrank from {previousCount} to {powers.Count}; retaining native pile state.");
            }

            if (HistoryPile.Cards.Count == 0)
            {
                Button.Visible = false;
                Button.Disable();
                RestoreExhaustFocusNeighbor();
                return;
            }

            Button.FocusNeighborTop = Button.GetPath();
            Button.FocusNeighborBottom = ExhaustPile.GetPath();
            Button.FocusNeighborLeft = Button.GetPath();
            Button.FocusNeighborRight = Button.GetPath();
            ExhaustPile.FocusNeighborTop = Button.GetPath();

            if (!Button.Visible || animateIn)
                Button.AnimIn();
            else
                Button.Visible = true;

            SyncEnabled(ExhaustPile.IsEnabled);
        }

        public void SyncEnabled(bool enabled)
        {
            if (!IsLive || HistoryPile.Cards.Count == 0) return;
            if (enabled)
            {
                Button.Enable();
                SuppressDuplicateExhaustHotkey();
            }
            else
                Button.Disable();
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
                NHoverTipSet.Remove(Button);
                if (_signalsConnected)
                {
                    Button.MouseEntered -= _replaceMouseHoverTipHandler;
                    Button.FocusEntered -= _replaceControllerHoverTipHandler;
                }
            }

            if (NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCardPileScreen current
                && ReferenceEquals(current.Pile, HistoryPile))
            {
                NCapstoneContainer.Instance.Close();
            }

            RestoreExhaustFocusNeighbor();
            if (PowerHistoryPileUi.IsLive(Container))
                Container.QueueFree();
            _signalsConnected = false;
        }

        private void ReplaceNativeHoverTip()
        {
            if (!IsLive || !Button.IsFocused) return;
            NHoverTipSet.Remove(Button);
            ShowHoverTip(this);
        }

        private void SuppressDuplicateExhaustHotkey()
        {
            // The cloned presentation class advertises the real Exhaust hotkey.
            // This fourth pile has no shortcut, so keep native mouse/controller
            // behavior while removing its competing global binding.
            Button.UnregisterHotkeys();
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
