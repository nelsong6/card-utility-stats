using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;

namespace SpireLens.Core.Patches;

/// <summary>
/// M4 phase 1: inject a sibling "View Stats" tickbox next to the game's
/// existing "View Upgrades" tickbox on the in-run deck view screen.
///
/// Scene structure (discovered by the DeckViewScreenProbePatch on 2026-04-19):
///   DeckViewScreen
///     ViewUpgrades              (MarginContainer, holds the whole block)
///       MarginContainer
///         Upgrades              (NUpgradePreviewTickbox)
///           ViewUpgradesLabel   (MegaLabel — sits INSIDE the tickbox)
///
/// We hook NCardsViewScreen.ConnectSignals (the post-ready wiring method;
/// _Ready throws on derived types), gate on NDeckViewScreen specifically
/// so we don't inject on unrelated card-view surfaces, duplicate the
/// entire ViewUpgrades subtree, rename the clone's inner nodes, change the
/// label text, offset the position, and connect our own toggle handler.
///
/// Phase-1 scope: checkbox renders and logs when toggled. Tooltip/description
/// override is phase 2.
/// </summary>
[HarmonyPatch(typeof(NCardsViewScreen), "ConnectSignals")]
public static class ViewStatsInjectorPatch
{
    // Track the most recently injected tickbox so phase 2 can read its state.
    public static NTickbox? LastInjectedTickbox { get; private set; }

    public static NTickbox? LastInjectedShowRemovedCardsTickbox { get; private set; }

    public static NTickbox? LastInjectedEnemyStatsTickbox { get; private set; }

    public static NTickbox? LastInjectedCombatCardStatsTickbox { get; private set; }

    // Track the active deck-view screen so the toggle handler can trigger
    // a live re-render (via DisplayCards) when the user flips the checkbox.
    // Without this, toggling the checkbox wouldn't show/hide removed cards
    // until the user closed and reopened the deck view.
    public static NDeckViewScreen? LastInjectedDeckView { get; private set; }

    // Track the cloned containers so Shutdown can QueueFree() them on hot-reload.
    // Otherwise the injected checkbox stays on screen after unload, referencing
    // a dead assembly and potentially causing exceptions when clicked.
    private static readonly List<Node> _injectedClones = new();
    private static readonly string[] InjectedCloneNames =
    [
        "ViewStats",
        "ViewRemovedCards",
        "ViewEnemyStats",
        "ViewCombatCardStats",
        "ViewRelicStatsBeforeCollected",
    ];

    // Persist the user's checkbox choices across deck-view open/close cycles
    // AND across Core hot reloads. Each Core load refreshes them from the
    // loader-backed mod config, and each toggle writes the complete set back
    // so changing one control never resets another.
    private static bool _persistedTicked;
    private static bool _persistedShowRemovedCardsTicked = true;
    private static bool _persistedEnemyStatsTicked;
    private static bool _persistedCombatCardStatsTicked;
    private static bool _prefsLoaded;

    public static bool ShowCardStatsDuringCombatEnabled
    {
        get
        {
            EnsurePrefsLoaded();
            return _persistedCombatCardStatsTicked;
        }
    }

    public static bool StatsVisibilityEnabled
    {
        get
        {
            EnsurePrefsLoaded();
            return _persistedTicked;
        }
    }

    /// <summary>
    /// Toggle the same persisted global visibility state as the deck-view
    /// checkbox. Input shortcuts and UI controls share this path so the live
    /// checkbox, tooltip state, and loader-backed config cannot drift apart.
    /// </summary>
    public static bool ToggleStatsVisibility(string source)
    {
        EnsurePrefsLoaded();
        SetStatsVisibilityEnabled(!_persistedTicked, source);
        return _persistedTicked;
    }

    public static void SetStatsVisibilityEnabled(bool isEnabled, string source)
    {
        EnsurePrefsLoaded();
        _persistedTicked = isEnabled;
        SetTickboxVisualState(LastInjectedTickbox, isEnabled);
        SavePreferences();

        if (!isEnabled)
            StatsTooltip.Hide();

        CoreMain.Logger.Info($"Stats visibility set to {isEnabled} ({source})");
    }

    /// <summary>
    /// Load the checkbox state from disk on first use. Called lazily from
    /// Inject so it happens before the first deck view opens and also
    /// after hot reload (since the static flag resets).
    /// </summary>
    private static void EnsurePrefsLoaded()
    {
        if (_prefsLoaded) return;
        _prefsLoaded = true;
        try
        {
            var prefs = PrefsStorage.Load();
            _persistedTicked = prefs.ViewStatsTicked;
            _persistedShowRemovedCardsTicked = prefs.ShowRemovedCardsTicked;
            _persistedEnemyStatsTicked = prefs.ShowEnemyStatsTicked;
            _persistedCombatCardStatsTicked = prefs.ShowCombatCardStatsTicked;
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"ViewStatsInjector: loading prefs failed: {e}");
        }
    }

    /// <summary>
    /// On hot-reload Initialize, scan the scene tree for an active
    /// NDeckViewScreen and re-inject into it. Without this, if the user
    /// was viewing the deck at reload time, their Shutdown-freed checkbox
    /// wouldn't come back until they closed and reopened the screen (the
    /// ConnectSignals hook only fires on screen open, not on live screens).
    /// </summary>
    public static void ReinjectIntoActiveDeckView()
    {
        try
        {
            var tree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
            if (tree == null) return;
            var deckView = FindDeckViewRecursive(tree.Root);
            if (deckView == null) return;

            CoreMain.Logger.Info("ViewStatsInjector: re-injecting into already-open deck view after hot reload");
            LastInjectedDeckView = deckView;
            Inject(deckView);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"ReinjectIntoActiveDeckView failed: {e}");
        }
    }

    private static NDeckViewScreen? FindDeckViewRecursive(Node node)
    {
        if (node is NDeckViewScreen dv) return dv;
        for (int i = 0; i < node.GetChildCount(); i++)
        {
            var found = FindDeckViewRecursive(node.GetChild(i));
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Called by CoreMain.Shutdown before unloading. QueueFrees the injected
    /// clone node so it's removed from the scene tree; Godot will clean up
    /// the underlying resources next frame. After this, the user won't see
    /// our checkbox until they reopen the deck view (which triggers the
    /// re-loaded patch on the next ConnectSignals) — UNLESS the deck view
    /// is still open and Initialize's ReinjectIntoActiveDeckView catches it.
    /// </summary>
    public static void TeardownInjectedUI()
    {
        foreach (var clone in _injectedClones)
        {
            if (clone != null && Godot.GodotObject.IsInstanceValid(clone))
            {
                clone.QueueFree();
            }
        }
        if (_injectedClones.Count > 0)
            CoreMain.Logger.Info($"ViewStatsInjector: {_injectedClones.Count} cloned containers queued for removal");
        _injectedClones.Clear();
        LastInjectedTickbox = null;
        LastInjectedShowRemovedCardsTickbox = null;
        LastInjectedEnemyStatsTickbox = null;
        LastInjectedCombatCardStatsTickbox = null;
    }

    [HarmonyPostfix]
    public static void Postfix(NCardsViewScreen __instance)
    {
        // Only inject on the in-run deck-view surface for now. Other CardsView
        // subclasses (compendium, card selection dialogs) aren't in scope until
        // we validate semantics per-screen — e.g. Compendium stats would need
        // lifetime aggregation (#2).
        if (__instance is not NDeckViewScreen deckView) return;

        // Remember the active deck view so OnStatsToggled can trigger a
        // live re-render (show/hide removed cards instantly on toggle).
        LastInjectedDeckView = deckView;

        try { Inject(__instance); }
        catch (Exception e) { CoreMain.Logger.Error($"ViewStatsInjector failed: {e}"); }
    }

    private static void Inject(NCardsViewScreen screen)
    {
        EnsurePrefsLoaded();

        var viewUpgradesContainer = screen.GetNodeOrNull("ViewUpgrades");
        if (viewUpgradesContainer == null)
        {
            CoreMain.Logger.Warn("ViewStatsInjector: ViewUpgrades container not found — scene structure may have changed.");
            return;
        }

        RemoveExistingInjectedControls(viewUpgradesContainer.GetParent());

        InjectTickbox(
            viewUpgradesContainer,
            "ViewStats",
            "Stats",
            "ViewStatsLabel",
            "spirelens: show stats",
            new Vector2(0, -240),
            _persistedTicked,
            OnStatsToggled,
            tickbox => LastInjectedTickbox = tickbox);

        InjectTickbox(
            viewUpgradesContainer,
            "ViewCombatCardStats",
            "CombatCardStats",
            "ViewCombatCardStatsLabel",
            "spirelens: show combat card stats",
            new Vector2(0, -180),
            _persistedCombatCardStatsTicked,
            OnCombatCardStatsToggled,
            tickbox => LastInjectedCombatCardStatsTickbox = tickbox);

        InjectTickbox(
            viewUpgradesContainer,
            "ViewEnemyStats",
            "EnemyStats",
            "ViewEnemyStatsLabel",
            "spirelens: show monster stats",
            new Vector2(0, -120),
            _persistedEnemyStatsTicked,
            OnEnemyStatsToggled,
            tickbox => LastInjectedEnemyStatsTickbox = tickbox);

        InjectTickbox(
            viewUpgradesContainer,
            "ViewRemovedCards",
            "RemovedCards",
            "ViewRemovedCardsLabel",
            "spirelens: show removed cards",
            new Vector2(0, -60),
            _persistedShowRemovedCardsTicked,
            OnShowRemovedCardsToggled,
            tickbox => LastInjectedShowRemovedCardsTickbox = tickbox);

        CoreMain.Logger.Info("ViewStatsInjector: injected deck-view SpireLens checkboxes");
    }

    private static void RemoveExistingInjectedControls(Node? parent)
    {
        if (parent == null) return;

        int removed = 0;
        foreach (var cloneName in InjectedCloneNames)
        {
            for (int i = parent.GetChildCount() - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (!string.Equals(child.Name.ToString(), cloneName, StringComparison.Ordinal))
                    continue;

                parent.RemoveChild(child);
                child.QueueFree();
                _injectedClones.Remove(child);
                removed++;
            }
        }

        if (removed > 0)
            CoreMain.Logger.Info($"ViewStatsInjector: queued {removed} stale injected controls for removal");
    }

    private static void InjectTickbox(
        Node viewUpgradesContainer,
        string cloneName,
        string tickboxName,
        string labelName,
        string labelText,
        Vector2 offset,
        bool isTicked,
        Action<NTickbox> onToggled,
        Action<NTickbox> rememberTickbox)
    {
        // Default Duplicate() copies structure + scripts. Runtime-connected signals
        // (made via .Connect() in ConnectSignals) are per-instance and NOT copied,
        // so our clone starts with no inherited toggle handlers — good.
        var clone = (Node)viewUpgradesContainer.Duplicate();
        clone.Name = cloneName;

        // Rename the inner tickbox so GetNode lookups don't collide with the original %Upgrades.
        var innerTickbox = FindChildOfType<NUpgradePreviewTickbox>(clone);
        if (innerTickbox != null)
        {
            innerTickbox.Name = tickboxName;
            // Connect our toggle handler. Use the Toggled signal defined on NTickbox.
            innerTickbox.Connect(NTickbox.SignalName.Toggled, Callable.From<NTickbox>(onToggled));
            rememberTickbox(innerTickbox);
        }
        else
        {
            CoreMain.Logger.Warn($"ViewStatsInjector: inner tickbox not found in {cloneName}");
        }

        // Update the label text. MegaLabel.SetTextAutoSize takes a raw string.
        var label = FindChildOfType<MegaLabel>(clone);
        if (label != null)
        {
            label.Name = labelName;
            label.SetTextAutoSize(labelText);
            // TODO(i18n): pull from localization once we have keys
        }
        else
        {
            CoreMain.Logger.Warn($"ViewStatsInjector: label not found in {cloneName}");
        }

        // Godot runs the clone's _Ready() during AddChild(). NTickbox._Ready()
        // calls ConnectSignals(), which looks up %TickboxVisuals before our
        // post-add field rewiring below. Duplicate() does not reliably preserve
        // the unique-name owner relationship, so restore it before insertion.
        if (innerTickbox != null)
        {
            SetOwnerRecursive(clone, clone);
            var preAddVisuals = innerTickbox.GetNodeOrNull<Control>("TickboxVisuals");
            if (preAddVisuals != null)
                preAddVisuals.UniqueNameInOwner = true;
        }

        // Insert into the same parent as the original (the DeckViewScreen).
        var parent = viewUpgradesContainer.GetParent();
        parent.AddChild(clone);

        // Track for Shutdown (hot-reload cleanup).
        _injectedClones.Add(clone);

        // Rewire the clone's visuals to point at its OWN subtree.
        //
        // NTickbox.ConnectSignals uses %TickboxVisuals unique-name lookup,
        // which scopes to the scene root. After duplication there are two
        // nodes marked %TickboxVisuals under DeckViewScreen — the clone's
        // lookup resolves to the original's, so clicking the clone toggles
        // the original's visuals (or neither, depending on resolution order).
        //
        // Publicizer gives us access to the private _tickedImage /
        // _notTickedImage / _imageContainer fields on NTickbox; we reassign
        // them to the clone's own nodes via relative paths.
        if (innerTickbox != null)
        {
            var cloneVisuals = innerTickbox.GetNodeOrNull<Control>("TickboxVisuals");
            if (cloneVisuals != null)
            {
                // Give the clone its own material instance. Godot's Duplicate()
                // shares Resource references by default (materials are Resources),
                // so without this our hover-tween shader changes would mutate
                // the ORIGINAL checkbox's rendering too. Duplicating the material
                // gives the clone independent shader state.
                if (cloneVisuals.Material != null)
                {
                    cloneVisuals.Material = (Material)cloneVisuals.Material.Duplicate();
                }

                innerTickbox._imageContainer = cloneVisuals;
                innerTickbox._tickedImage = cloneVisuals.GetNodeOrNull<Control>("Ticked");
                innerTickbox._notTickedImage = cloneVisuals.GetNodeOrNull<Control>("NotTicked");
                // Rewire the remaining NTickbox fields that OnFocus/OnRelease
                // tweens animate. Without these, mousing over the clone ran
                // shader/scale tweens against the original's material and
                // produced visual glitches (clone visuals vanishing).
                innerTickbox._hsv = cloneVisuals.Material as ShaderMaterial;
                innerTickbox._baseScale = cloneVisuals.Scale;

                // Initial render: restore the user's last choice. The tickbox
                // clone always constructs unticked, but the player expects
                // their "I want stats on" preference to survive closing and
                // re-opening the deck view within the same session.
                SetTickboxVisualState(innerTickbox, isTicked);
            }
            else
            {
                CoreMain.Logger.Warn("ViewStatsInjector: clone's TickboxVisuals not found — cannot rewire");
            }
        }

        // Offset position — put it above the original ViewUpgrades.
        // MarginContainer is a Control, so Position is set relative to the
        // original. Empirical offset; may need adjustment once we see it.
        if (clone is Control control && viewUpgradesContainer is Control origControl)
        {
            control.Position = origControl.Position + offset;
        }

        CoreMain.Logger.Info($"ViewStatsInjector: injected {cloneName} — tickbox={innerTickbox != null} label={label != null}");
    }

    private static void OnStatsToggled(NTickbox tickbox)
    {
        SetStatsVisibilityEnabled(tickbox.IsTicked, "deck-view checkbox");
    }

    private static void OnCombatCardStatsToggled(NTickbox tickbox)
    {
        _persistedCombatCardStatsTicked = tickbox.IsTicked;
        SavePreferences();
        CoreMain.Logger.Info($"CombatCardStats toggled: IsTicked={tickbox.IsTicked}");

        if (!tickbox.IsTicked)
            StatsTooltip.HideIfAnchoredToCard();
    }

    private static void OnEnemyStatsToggled(NTickbox tickbox)
    {
        _persistedEnemyStatsTicked = tickbox.IsTicked;
        SavePreferences();
        CoreMain.Logger.Info($"EnemyStats toggled: IsTicked={tickbox.IsTicked}");

        if (!tickbox.IsTicked)
            StatsTooltip.HideIfAnchoredToCreature();
    }

    private static void OnShowRemovedCardsToggled(NTickbox tickbox)
    {
        _persistedShowRemovedCardsTicked = tickbox.IsTicked;
        SavePreferences();
        CoreMain.Logger.Info($"ShowRemovedCards toggled: IsTicked={tickbox.IsTicked}");

        // Live re-render so removed cards appear/disappear the moment the
        // user flips the checkbox. DisplayCards reads our prefix-appended
        // _cards list, so it automatically picks up the current tick state.
        try
        {
            if (LastInjectedDeckView != null && Godot.GodotObject.IsInstanceValid(LastInjectedDeckView))
                LastInjectedDeckView.DisplayCards();
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"DisplayCards re-render failed: {e.Message}");
        }
    }

    private static void SavePreferences()
    {
        PrefsStorage.Save(new Prefs
        {
            ViewStatsTicked = _persistedTicked,
            ShowRemovedCardsTicked = _persistedShowRemovedCardsTicked,
            ShowEnemyStatsTicked = _persistedEnemyStatsTicked,
            ShowCombatCardStatsTicked = _persistedCombatCardStatsTicked,
        });
    }

    private static void SetTickboxVisualState(NTickbox? tickbox, bool isTicked)
    {
        if (tickbox == null || !GodotObject.IsInstanceValid(tickbox)) return;

        if (tickbox._tickedImage != null)
            tickbox._tickedImage.Visible = isTicked;
        if (tickbox._notTickedImage != null)
            tickbox._notTickedImage.Visible = !isTicked;
        tickbox.IsTicked = isTicked;
    }

    private static void SetOwnerRecursive(Node node, Node owner)
    {
        for (int i = 0; i < node.GetChildCount(); i++)
        {
            var child = node.GetChild(i);
            child.Owner = owner;
            SetOwnerRecursive(child, owner);
        }
    }

    /// <summary>
    /// Depth-first search for a child of the given type anywhere in the node's
    /// subtree. Same helper pattern STS2MCP uses for its settings injection.
    /// </summary>
    private static T? FindChildOfType<T>(Node parent) where T : class
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is T typed) return typed;
            if (child is Node childNode)
            {
                var found = FindChildOfType<T>(childNode);
                if (found != null) return found;
            }
        }
        return null;
    }
}
