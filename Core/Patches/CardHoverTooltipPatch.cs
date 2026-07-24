using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace SpireLens.Core.Patches;

/// <summary>
/// Shows our per-card stats tooltip when the user hovers a card in the deck
/// view (or other card-holder surfaces) AND the ViewStats checkbox is on.
///
/// Hook surfaces:
///   NCardHolder.CreateHoverTips  (postfix) — show on hover
///   NCardHolder.ClearHoverTips   (postfix) — hide on unhover
/// </summary>
[HarmonyPatch(typeof(NCardHolder), "CreateHoverTips")]
public static class CardHoverShowPatch
{
    private const int InlineKeywordIconSize = 16;
    private const string ShivMetaNote = "Reflects All Shiv Usage";
    private const string BlockIconPath = "res://images/ui/combat/block.png";
    private const string DrawCardsNextTurnPowerIconPath = "res://images/atlases/power_atlas.sprites/draw_cards_next_turn_power.tres";
    private const string BlockedDrawIconPath = DrawCardsNextTurnPowerIconPath;
    private const int DebtGoldLossPerTrigger = 5;
    private const string DanseMacabrePowerId = "POWER.DANSE_MACABRE";
    private const string EnergyPotionIconPath = "res://images/atlases/potion_atlas.sprites/energy_potion.tres";
    private const string FreeAttackPowerId = "POWER.FREE_ATTACK_POWER";
    private const string JugglingPowerId = "POWER.JUGGLING";
    private const string StarIconPath = "res://images/packed/sprite_fonts/star_icon.png";
    private const string SovereignBladeMetaNote = "Reflects All Sovereign Blade Usage";

    [HarmonyPostfix]
    public static void Postfix(NCardHolder __instance)
    {
        // Card tooltips are an explicit display-only opt-in on every surface.
        // Keep this gate ahead of tracker locks, aggregate merging, and tooltip
        // markup; attribution continues normally while the UI is disabled.
        var cardStatsEnabled = ViewStatsInjectorPatch.CardStatsEnabled;
        var viewStatsEnabled = ViewStatsInjectorPatch.StatsVisibilityEnabled;
        if (!ResolveCardStatsEnabled(viewStatsEnabled, cardStatsEnabled)) return;

        if (IsCardRewardSelectionSurface(__instance))
        {
            StatsTooltip.HideIfAnchoredTo(__instance);
            return;
        }

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            var cardModel = __instance.CardModel;
            if (cardModel == null) return;

            // Per-instance display: every deck card gets a stable "#N" number
            // (per Nelson: "all cards should have a bit of an ID attached"),
            // assigned at RunStarted for the starting deck and lazily for
            // any card added mid-run.
            //
            // Upgrade marker strip: the game's Title field includes a trailing
            // "+" (or "++") when the card is upgraded — "Defend" becomes
            // "Defend+" in the game's rendering. Per Nelson's call, we strip
            // that for the tooltip header so the instance name stays stable
            // across upgrade ("Defend #1" is always "Defend #1"). Upgrade
            // state is already shown in the Lineage section below.
            // Gated on CurrentUpgradeLevel > 0 so we don't accidentally
            // strip a legitimate trailing "+" from a card whose base name
            // happens to end that way.
            var rawTitle = cardModel.Title;
            if (cardModel.CurrentUpgradeLevel > 0 && !string.IsNullOrEmpty(rawTitle))
            {
                rawTitle = rawTitle.TrimEnd('+').TrimEnd();
            }
            var title = !string.IsNullOrWhiteSpace(rawTitle) ? rawTitle : cardModel.Id.ToString();
            var instanceNum = RunTracker.GetInstanceNumber(cardModel);
            var displayName = instanceNum > 0 ? $"{title} #{instanceNum}" : title;

            // The hover card is the deck original (DeckVersion is null), so
            // its hash IS the canonical hash. Compare against the play-time
            // log's canonicalHash to verify attribution lands on the right
            // key. If they match → dict lookup must succeed.
            CoreMain.LogDebug($"hover: id={cardModel.Id} rawTitle='{rawTitle}' instance={instanceNum} displayName='{displayName}' hash={cardModel.GetHashCode()} deckVersionNull={cardModel.DeckVersion == null}");

            // Hand hovers stay compact by default. Automated verify runs and
            // power users can opt into the full breakdown for hand tooltips.
            bool compact = __instance is NHandCardHolder
                && !RuntimeOptionsProvider.Current.UseVerboseHandStats;
            var body = BuildBodyBBCode(cardModel, displayName, compact);
            // Reuse the gold title slot for the hovered card's instance name
            // on every surface. That's the highest-signal identity marker,
            // and it keeps the compact and full tooltips visually aligned.
            StatsTooltip.Show(
                tree,
                __instance,
                displayName,
                "SpireLens",
                body);
        }
        catch (System.Exception e)
        {
            CoreMain.Logger.Error($"CardHoverShow failed: {e.Message}");
        }
    }

    internal static bool ResolveCardStatsEnabled(
        bool viewStatsEnabled,
        bool cardStatsEnabled)
        => viewStatsEnabled && cardStatsEnabled;

    private static bool IsCardRewardSelectionSurface(Node? node)
    {
        for (var current = node; current != null; current = current.GetParent())
        {
            if (IsCardRewardSelectionSurfaceType(current.GetType()))
                return true;
        }

        return false;
    }

    internal static bool IsCardRewardSelectionSurfaceType(Type? nodeType)
    {
        return nodeType != null && typeof(NCardRewardSelectionScreen).IsAssignableFrom(nodeType);
    }

    /// <summary>
    /// Render the BODY portion of the tooltip — the stats block. Title and
    /// brand are set separately on the Label nodes inside StatsTooltip so
    /// they can use proper Kreon font + gold/grey coloring instead of BBCode
    /// inline hacks. This method only produces what goes in the RichTextLabel.
    ///
    /// <paramref name="compact"/> controls density:
    ///   - true (hand hovers): just the high-signal numbers the player
    ///     needs mid-combat — Played/Drawn, Total damage (if attack),
    ///     Energy gained (if any), Block gained (if any), Kills (if any). Skips lineage, energy
    ///     details, averages, percentages.
    ///   - false (deck view, graveyard, draw pile, etc.): full breakdown
    ///     including lineage and all tabled stats.
    /// </summary>
    private static string BuildBodyBBCode(MegaCrit.Sts2.Core.Models.CardModel cardModel, string displayName, bool compact = false)
    {
        var run = RunTracker.Current;
        var sb = new StringBuilder();
        bool isShivMetaCard = RunTracker.IsShivDeckViewCard(cardModel);
        bool isSovereignBladeMetaCard = RunTracker.IsSovereignBladeDeckViewCard(cardModel);
        bool isSupplementalMetaCard = isShivMetaCard || isSovereignBladeMetaCard;

        // The card identity now lives in the gold title slot for both compact
        // and full views, so repeating it again in the body just adds noise.
        // Supplemental meta cards (pooled Shiv / Sovereign Blade)
        // get a red explanatory banner instead of the generic ephemeral
        // "not present in deck" note.
        if (isShivMetaCard)
            sb.Append($"[color=#e04c4c][b]{ShivMetaNote}[/b][/color]\n");
        else if (isSovereignBladeMetaCard)
            sb.Append($"[color=#e04c4c][b]{SovereignBladeMetaNote}[/b][/color]\n");

        // Merges committed run + current pending combat so mid-combat plays
        // show up immediately (don't wait for CombatEnded). If we have no
        // aggregate entry yet for this card (unplayed), treat it as an
        // empty/zero aggregate and render the normal stats layout with
        // zeros. Per Nelson: "no data this run" was an awkward escape
        // hatch — zeros are more informative and structurally consistent.
        var agg = RunTracker.GetEffectiveAggregate(cardModel) ?? new CardAggregate();
        CoreMain.LogDebug($"  lookup: Plays={agg.Plays} Intended={agg.TotalIntended}");

        // Compact mode (hand hovers) — skip lineage and everything tabular,
        // return just the signals a player needs mid-combat.
        if (compact)
        {
            AppendCompactBody(sb, cardModel, agg);
            return sb.ToString();
        }

        // Lineage: when/how the card entered the deck, and any upgrades
        // since. Label/value style matches the stats tables below — no
        // colons, bold numbers, subdued surrounding prose.
        //
        // Special case: cards that aren't in the player's permanent deck
        // (combat-generated Souls, Shivs, short-lived transformed cards,
        // etc.). For those, "Received floor X" is misleading — the card
        // isn't a deck member, it just exists transiently. Render a
        // distinct "Card not present in deck" note instead.
        //
        // FloorAdded: the game sets this to 1 for starter cards (see
        // Player.PopulateStartingDeck). Mid-run adds get current floor.
        // No special "starting deck" text — starters just show "floor 1".
        if (IsCardInDeck(cardModel))
        {
            //   "Received floor 1"                        → starter or floor-1 acquisition
            //   "Received floor 22, came upgraded +1"      → pre-upgraded (shop/event)
            //   "Received floor 22" + "Upgraded floor 22 → +1" → got and upgraded same floor
            string floorStr = agg.FloorAdded.HasValue
                ? $"floor [b]{agg.FloorAdded.Value}[/b]"
                : "unknown floor";
            if (agg.InitialUpgradeLevel > 0)
                sb.Append($"[color=#b5b5b5]Received {floorStr}, came upgraded +[b]{agg.InitialUpgradeLevel}[/b][/color]\n");
            else
                sb.Append($"[color=#b5b5b5]Received {floorStr}[/color]\n");

            foreach (var ue in RunTracker.GetUpgradeEvents(cardModel))
            {
                string ufloor = ue.Floor.HasValue
                    ? $"floor [b]{ue.Floor.Value}[/b]"
                    : "?";
                int level = ue.UpgradeLevel ?? 0;
                sb.Append($"[color=#b5b5b5]Upgraded {ufloor} → +[b]{level}[/b][/color]\n");
            }

            // Removal marker. Shown in the tooltip so users can tell at a
            // glance that this is a removed card even without the visual
            // grouping in the deck view. Floor 0 defaults to "?" text.
            if (agg.Removed)
            {
                string rfloor = agg.RemovedAtFloor.HasValue
                    ? $"floor [b]{agg.RemovedAtFloor.Value}[/b]"
                    : "[b]?[/b]";
                sb.Append($"[color=#b5b5b5]Removed {rfloor}[/color]\n");
            }
        }
        else
        {
            // Distinguish "was removed from deck" from "never entered deck"
            // (combat-generated ephemerals like Souls/Shivs). Removed gets
            // a red bold banner since it's an important run decision to
            // flag at a glance. Supplemental deck-view meta cards already
            // emitted their own explanatory red banner above, so we suppress
            // the generic "not present" line for them. Other ephemerals keep
            // the subdued grey note.
            if (agg.Removed)
                sb.Append("[color=#e04c4c][b]Card Removed[/b][/color]\n");
            else if (!isSupplementalMetaCard)
                sb.Append("[color=#b5b5b5]Card not present in deck[/color]\n");
        }

        AppendFullStatRows(
            sb,
            cardModel,
            agg,
            RunTracker.GetEffectiveMetaStats(),
            RunTracker.GetEtherealCardsPlayedThisCombat());

        // No footer. Previously we rendered "A4 · DEFECT · this run" here
        // as a mirror of SlayTheStats' filter-context footer — but they need
        // that line because their data aggregates across many runs with
        // configurable filters. Ours is scoped to one run by construction,
        // so the line was repeating back run info the user already knows.
        // Reintroduce a scope marker when/if we add cross-run lifetime stats.
        _ = run;  // silence unused-variable warning; keeps RunTracker reference live for debug.

        return sb.ToString();
    }

    internal static string BuildHistoricalBodyBBCode(
        MegaCrit.Sts2.Core.Models.CardModel cardModel,
        CardAggregate agg,
        RunMetaStats? metaStats,
        IEnumerable<CardEvent>? upgradeEvents = null,
        bool compact = false)
    {
        agg ??= new CardAggregate();
        metaStats ??= new RunMetaStats();

        var sb = new StringBuilder();
        if (compact)
        {
            AppendCompactBodyWithMetaStats(sb, cardModel, agg, metaStats);
            return sb.ToString();
        }

        string floorStr = agg.FloorAdded.HasValue
            ? $"floor [b]{agg.FloorAdded.Value}[/b]"
            : "unknown floor";
        if (agg.InitialUpgradeLevel > 0)
            sb.Append($"[color=#b5b5b5]Received {floorStr}, came upgraded +[b]{agg.InitialUpgradeLevel}[/b][/color]\n");
        else
            sb.Append($"[color=#b5b5b5]Received {floorStr}[/color]\n");

        if (upgradeEvents != null)
        {
            foreach (var ue in upgradeEvents)
            {
                string ufloor = ue.Floor.HasValue
                    ? $"floor [b]{ue.Floor.Value}[/b]"
                    : "?";
                int level = ue.UpgradeLevel ?? 0;
                sb.Append($"[color=#b5b5b5]Upgraded {ufloor} → +[b]{level}[/b][/color]\n");
            }
        }

        if (agg.Removed)
        {
            string rfloor = agg.RemovedAtFloor.HasValue
                ? $"floor [b]{agg.RemovedAtFloor.Value}[/b]"
                : "[b]?[/b]";
            sb.Append($"[color=#b5b5b5]Removed {rfloor}[/color]\n");
        }

        AppendFullStatRows(sb, cardModel, agg, metaStats);
        return sb.ToString();
    }

    private static void AppendFullStatRows(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel cardModel,
        CardAggregate agg,
        RunMetaStats metaStats,
        int? etherealCardsPlayedThisCombat = null)
    {
        // Per-play averages — the actual "utility" signal. Guard against
        // div-by-zero for the unplayed case.
        float avgIntended = agg.Plays > 0 ? (float)agg.TotalIntended / agg.Plays : 0f;
        float avgEffective = agg.Plays > 0 ? (float)agg.TotalEffective / agg.Plays : 0f;
        float overkillPct = agg.TotalIntended > 0 ? 100f * agg.TotalOverkill / agg.TotalIntended : 0f;
        float blockedPct = agg.TotalIntended > 0 ? 100f * agg.TotalBlocked / agg.TotalIntended : 0f;

        // All stat rows use the same 3-col table layout for visual
        // consistency: label | value | (optional percent). Rows without a
        // percentage get an empty 3rd cell so the label and value columns
        // align vertically across every row in the tooltip. Cell padding
        // prevents adjacent cells from crowding against each other (was
        // observed as "Played/Drawn1/1100%" with zero space between).
        bool isUnplayable = cardModel.Type == CardType.Curse
            || (cardModel.Keywords != null && cardModel.Keywords.Contains(CardKeyword.Unplayable));

        if (isUnplayable)
        {
            // For unplayable cards, just show Drawn. Played is always 0.
            Row3(sb, GetDrawStatLabel("drawn"), agg.TimesDrawn.ToString(), "");
        }
        else
        {
            // Playable cards: show Played/Drawn ratio with play rate %.
            float playRate = agg.TimesDrawn > 0 ? 100f * agg.Plays / agg.TimesDrawn : 0f;
            Row3(sb, "Played/Drawn", $"{agg.Plays}/{agg.TimesDrawn}", $"{playRate:F0}%");
        }

        Row3(sb, "Combats in deck", agg.CombatsInDeck.ToString(), "");

        if (etherealCardsPlayedThisCombat.HasValue)
            AppendPullFromBelowStats(sb, cardModel, etherealCardsPlayedThisCombat.Value);
        AppendMakeItSoStats(sb, cardModel, agg, compact: false);
        AppendUnleashStats(sb, cardModel, agg, compact: false);
        AppendOstySummonStats(sb, cardModel, agg, metaStats, compact: false);
        AppendSoulPileStats(sb, agg);
        AppendUnmovablePowerStats(sb, cardModel, metaStats);
        AppendAlchemizePotionStats(sb, cardModel, agg, compact: false);
        AppendJackOfAllTradesStats(sb, cardModel, agg, compact: false);
        AppendDiscoveryStats(sb, cardModel, agg, compact: false);
        AppendDrainPowerStats(sb, cardModel, agg, compact: false);
        AppendJugglingPowerStats(sb, cardModel, metaStats, compact: false);
        AppendDanseMacabrePowerStats(sb, cardModel, metaStats, compact: false);
        AppendUnrelentingFreeAttackStats(sb, cardModel, metaStats, compact: false);
        AppendDebtStats(sb, cardModel, agg);
        AppendReplayStats(sb, agg);

        bool hasDedicatedPoison = AppendDedicatedPoisonStats(sb, agg, compact: false);
        AppendAppliedEffects(sb, agg, compact: false, excludePoison: hasDedicatedPoison);
        AppendArtifactBlockedSummary(sb, agg, excludePoison: hasDedicatedPoison);

        // Energy-gain rows — cards like Adrenaline / Concentrate / energy
        // pot-style effects need a direct "what did this card give me?"
        // stat, independent of the existing energy-spent cost tracking.
        if (agg.TotalEnergyGenerated > 0)
        {
            float avgGenerated = agg.Plays > 0 ? (float)agg.TotalEnergyGenerated / agg.Plays : 0f;
            Row3(sb, GetEnergyStatLabel("gained"), agg.TotalEnergyGenerated.ToString(), "");
            Row3(sb, GetEnergyStatLabel("avg gained"), $"{avgGenerated:F1}", "");
        }

        if (agg.TotalStarsGenerated > 0)
        {
            float avgGenerated = agg.Plays > 0 ? (float)agg.TotalStarsGenerated / agg.Plays : 0f;
            Row3(sb, GetStarStatLabel("gained"), agg.TotalStarsGenerated.ToString(), "");
            Row3(sb, GetStarStatLabel("avg gained"), $"{avgGenerated:F1}", "");
        }

        if (agg.TotalForgeGenerated > 0m)
        {
            decimal avgGenerated = agg.Plays > 0 ? agg.TotalForgeGenerated / agg.Plays : 0m;
            Row3(sb, GetForgeStatLabel("gained"), FormatDecimal(agg.TotalForgeGenerated), "");
            Row3(sb, GetForgeStatLabel("avg gained"), FormatDecimal(avgGenerated), "");
        }

        // Energy-spent rows — only rendered when the card's cost is actually
        // variable (see IsEnergyInteresting). Same 3-col layout as every
        // other stat row; percent column stays empty since there's nothing
        // to percentage-ify here.
        if (IsEnergyInteresting(cardModel, agg))
        {
            float avgEnergy = agg.Plays > 0 ? (float)agg.TotalEnergySpent / agg.Plays : 0f;
            Row3(sb, GetEnergyStatLabel("total spent"), agg.TotalEnergySpent.ToString(), "");
            Row3(sb, GetEnergyStatLabel("avg cost"), $"{avgEnergy:F1}", "");
        }

        if (IsStarInteresting(cardModel, agg))
        {
            float avgStars = agg.Plays > 0 ? (float)agg.TotalStarsSpent / agg.Plays : 0f;
            Row3(sb, GetStarStatLabel("total spent"), agg.TotalStarsSpent.ToString(), "");
            Row3(sb, GetStarStatLabel("avg cost"), $"{avgStars:F1}", "");
        }

        // Damage section rules:
        //   - Attack cards: always show the damage block. With zeros for
        //     unplayed or 0-damage attacks (target died / fully blocked
        //     case), with the full breakdown once damage has been dealt.
        //   - Non-attack (Skill/Power/Status/Curse): skip the section
        //     entirely unless we somehow accumulated damage (edge case;
        //     shouldn't happen but respects the data if it does).
        bool isAttack = cardModel.Type == CardType.Attack;
        bool showDamage = isAttack || agg.TotalIntended > 0;
        if (showDamage)
        {
            // Total damage = effective damage = HP actually removed by this
            // card across the whole run. "Effective" over "intended" because
            // that's what players mean by "this card has done X damage" —
            // block and overkill waste don't count.
            // Damage section in the same 3-col layout. Total damage =
            // effective damage = HP actually removed. "Effective" over
            // "intended" because that's what players mean by "X damage".
            // Avg intended intentionally omitted pending issue #15.
            Row3(sb, "Total damage", agg.TotalEffective.ToString(), "");
            Row3(sb, "Avg effective", $"{avgEffective:F1}", "");
            _ = avgIntended;  // still computed above; silence unused warning

            // Clarify 0 damage for attacks that played without dealing any:
            // the game skips DamageReceivedEntry when the target is in the
            // "dead but not yet removed" state, so the play is real but
            // we have no damage event to attribute. Rendered as a subdued
            // own-line annotation rather than inline with Total damage.
            if (isAttack && agg.Plays > 0 && agg.TotalIntended == 0)
                sb.Append("[color=#7a7a85]  (target died / fully blocked)[/color]\n");
            // Overkill and Blocked: whole number + %. Same 3-col row as
            // everything else; here the percent cell is populated.
            Row3(sb, "Overkill", agg.TotalOverkill.ToString(), $"{overkillPct:F0}%");
            Row3(sb, "Blocked", agg.TotalBlocked.ToString(), $"{blockedPct:F0}%");
            if (agg.Kills > 0) Row3(sb, "Kills", agg.Kills.ToString(), "");
        }

        // Block gained — rendered for cards that have actually produced
        // block. Absorbed uses FIFO consumption across the player's block
        // ledger; wasted uses LIFO across whatever survived until clear/
        // expiry, which matches the "later block was redundant overfill"
        // mental model described in issue #6.
        if (agg.TotalBlockGained > 0)
        {
            float avgBlock = agg.Plays > 0 ? (float)agg.TotalBlockGained / agg.Plays : 0f;
            float absorbedPct = 100f * agg.TotalBlockEffective / agg.TotalBlockGained;
            float wastedPct = 100f * agg.TotalBlockWasted / agg.TotalBlockGained;
            RowDual(sb, GetBlockStatLabel("gained"), agg.TotalBlockGained.ToString(), GetBlockStatLabel("avg"), $"{avgBlock:F1}");
            Row3(sb, GetBlockStatLabel("absorbed"), agg.TotalBlockEffective.ToString(), $"{absorbedPct:F0}%");
            Row3(sb, GetBlockStatLabel("wasted"), agg.TotalBlockWasted.ToString(), $"{wastedPct:F0}%");
        }

        // Discarded count — shown only when > 0 because for most cards
        // discarding doesn't happen. When it does (end-of-turn with card
        // still in hand, discard-triggering effects), the number is
        // useful signal — a card you keep discarding without playing is
        // probably dead weight.
        if (agg.TimesDiscarded > 0)
            Row3(sb, "Discarded", agg.TimesDiscarded.ToString(), "");

        // Pile-top placements — signals draw-order manipulation. Only
        // rendered when > 0 to keep noise down on normal cards.
        if (agg.TimesPlacedOnTopFromHand > 0)
            Row3(sb, "Top from hand", agg.TimesPlacedOnTopFromHand.ToString(), "");
        if (agg.TimesPlacedOnTopFromDiscard > 0)
            Row3(sb, "Top from graveyard", agg.TimesPlacedOnTopFromDiscard.ToString(), "");

        // Exhausted other cards — Havoc-style side-effect stat. Only
        // shown for cards that have actually caused an exhaust.
        if (agg.TimesExhaustedOtherCards > 0)
            Row3(sb, "Exhausted others", agg.TimesExhaustedOtherCards.ToString(), "");

        // How often THIS card itself got exhausted. Full-view only; useful
        // for exhaust-tag cards and ephemeral generated cards, but not worth
        // the space in the compact in-hand view.
        if (agg.TimesExhausted > 0)
            Row3(sb, "Exhausted", agg.TimesExhausted.ToString(), "");

        AppendCardDrawStats(sb, agg);

        // HP lost from playing this card — Ironclad self-damage cards.
        // POST-reduction value, so Tungsten Rod / buffer interactions
        // show as reduced HP loss, which is the true cost signal.
        if (agg.TotalHpLost > 0)
            Row3(sb, "HP lost", agg.TotalHpLost.ToString(), "");
        if (agg.TotalMaxHpLost > 0)
            Row3(sb, "Max HP lost", agg.TotalMaxHpLost.ToString(), "");
    }

    /// <summary>
    /// Compact stats body for hand hovers during combat. High-signal
    /// numbers only — the player's deciding what to play, not studying
    /// lifetime performance.
    ///
    /// Shows: Played/Drawn ratio, Total damage (if attack), Energy gained
    /// (if any), Block gained (if any), Kills (if any). Skips: lineage, most energy details, per-play
    /// averages, overkill/blocked percentages. Everything uses the same
    /// 3-col layout as the full view for visual consistency.
    /// </summary>
    private static void AppendCompactBody(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel cardModel,
        CardAggregate agg)
    {
        AppendCompactBodyWithMetaStats(
            sb,
            cardModel,
            agg,
            RunTracker.GetEffectiveMetaStats(),
            RunTracker.GetEtherealCardsPlayedThisCombat());
    }

    private static void AppendCompactBodyWithMetaStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel cardModel,
        CardAggregate agg,
        RunMetaStats metaStats,
        int? etherealCardsPlayedThisCombat = null)
    {
        bool isAttack = cardModel.Type == CardType.Attack;

        bool isUnplayable = cardModel.Type == CardType.Curse
            || (cardModel.Keywords != null && cardModel.Keywords.Contains(CardKeyword.Unplayable));

        if (isUnplayable)
        {
            // For unplayable cards, just show Drawn. Played is always 0.
            Row3(sb, GetDrawStatLabel("drawn"), agg.TimesDrawn.ToString(), "");
        }
        else
        {
            float playRate = agg.TimesDrawn > 0 ? 100f * agg.Plays / agg.TimesDrawn : 0f;
            Row3(sb, "Played/Drawn", $"{agg.Plays}/{agg.TimesDrawn}", $"{playRate:F0}%");
        }

        bool hasDedicatedPoison = AppendDedicatedPoisonStats(sb, agg, compact: true);
        AppendAppliedEffects(sb, agg, compact: true, excludePoison: hasDedicatedPoison);

        if (agg.TotalEnergyGenerated > 0)
            Row3(sb, GetEnergyStatLabel("gained"), agg.TotalEnergyGenerated.ToString(), "");

        if (agg.TotalStarsGenerated > 0)
            Row3(sb, GetStarStatLabel("gained"), agg.TotalStarsGenerated.ToString(), "");

        if (agg.TotalForgeGenerated > 0m)
            Row3(sb, GetForgeStatLabel("gained"), FormatDecimal(agg.TotalForgeGenerated), "");

        if (etherealCardsPlayedThisCombat.HasValue)
            AppendPullFromBelowStats(sb, cardModel, etherealCardsPlayedThisCombat.Value);
        AppendMakeItSoStats(sb, cardModel, agg, compact: true);
        AppendUnleashStats(sb, cardModel, agg, compact: true);
        AppendOstySummonStats(sb, cardModel, agg, metaStats, compact: true);
        AppendSoulPileStats(sb, agg);
        AppendUnmovablePowerStats(sb, cardModel, metaStats);
        AppendAlchemizePotionStats(sb, cardModel, agg, compact: true);
        AppendJackOfAllTradesStats(sb, cardModel, agg, compact: true);
        AppendDiscoveryStats(sb, cardModel, agg, compact: true);
        AppendDrainPowerStats(sb, cardModel, agg, compact: true);
        AppendJugglingPowerStats(sb, cardModel, metaStats, compact: true);
        AppendDanseMacabrePowerStats(sb, cardModel, metaStats, compact: true);
        AppendUnrelentingFreeAttackStats(sb, cardModel, metaStats, compact: true);
        AppendDebtStats(sb, cardModel, agg);
        AppendReplayStats(sb, agg);

        bool showDamage = isAttack || agg.TotalIntended > 0;
        if (showDamage)
        {
            Row3(sb, "Total damage", agg.TotalEffective.ToString(), "");
            if (agg.Kills > 0) Row3(sb, "Kills", agg.Kills.ToString(), "");
        }

        if (agg.TotalBlockGained > 0)
            Row3(sb, GetBlockStatLabel("gained"), agg.TotalBlockGained.ToString(), "");

        if (agg.TotalHpLost > 0)
            Row3(sb, "HP lost", agg.TotalHpLost.ToString(), "");
        if (agg.TotalMaxHpLost > 0)
            Row3(sb, "Max HP lost", agg.TotalMaxHpLost.ToString(), "");
    }

    /// <summary>
    /// A deck card is "in deck" if its canonical deck reference still
    /// exists in the player's permanent deck list. Combat clones point back
    /// to the deck original via DeckVersion; deck-view cards are already the
    /// canonical object. Removed cards are intentionally NOT in the list.
    ///
    /// If we can't read the deck state (no active run, etc.) we default to
    /// TRUE so we fall back to the normal lineage display. That's the safer
    /// path — mis-reporting a deck card as "not present" is worse than the
    /// reverse.
    /// </summary>
    private static bool IsCardInDeck(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        try
        {
            var player = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.State?.Players.FirstOrDefault();
            if (player?.Deck?.Cards == null) return true;  // unknown → assume deck

            var canonical = card.DeckVersion ?? card;
            foreach (var c in player.Deck.Cards)
            {
                var cCanonical = c.DeckVersion ?? c;
                if (System.Object.ReferenceEquals(cCanonical, canonical)) return true;
            }
            return false;
        }
        catch
        {
            return true;  // error → assume deck
        }
    }

    private static void AppendUnleashStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        CardAggregate agg,
        bool compact)
    {
        if (agg.TotalOstyHpAttackBonus <= 0) return;
        if (card is not MegaCrit.Sts2.Core.Models.Cards.Unleash
            && !IsCardId(card, "CARD.UNLEASH"))
            return;

        Row3(sb, "Osty HP damage", agg.TotalOstyHpAttackBonus.ToString(), "");
        if (compact) return;

        decimal avgBonus = agg.TimesOstyHpAttackBonusApplied > 0
            ? (decimal)agg.TotalOstyHpAttackBonus / agg.TimesOstyHpAttackBonusApplied
            : 0m;
        Row3(sb, "avg Osty HP damage", FormatDecimal(avgBonus), "");
    }

    private static void AppendOstySummonStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        CardAggregate agg,
        RunMetaStats metaStats,
        bool compact)
    {
        agg ??= new CardAggregate();
        metaStats ??= new RunMetaStats();

        if (!IsOstySummonStatsHome(card, agg)) return;
        if (agg.TotalOstyHpSummoned <= 0m
            && metaStats.TotalOstyHpSummoned <= 0m
            && metaStats.TotalOstyDamageAbsorbed <= 0m)
            return;

        if (agg.TotalOstyHpSummoned > 0m)
            Row3(sb, "Summon gained", FormatDecimal(agg.TotalOstyHpSummoned), "");

        if (!compact && agg.TimesOstySummoned > 0)
            Row3(sb, "Osty summons", agg.TimesOstySummoned.ToString(), "");

        if (metaStats.TotalOstyHpSummoned > 0m)
            Row3(sb, "All Osty total summon", FormatDecimal(metaStats.TotalOstyHpSummoned), "");

        if (metaStats.TotalOstyDamageAbsorbed > 0m)
            Row3(sb, "All Osty damage absorbed", FormatDecimal(metaStats.TotalOstyDamageAbsorbed), "");
    }

    private static void AppendSoulPileStats(StringBuilder sb, CardAggregate agg)
    {
        if (agg.SoulsAddedToDrawPile > 0)
            Row3(sb, "Souls added to draw pile", agg.SoulsAddedToDrawPile.ToString(), "");
        if (agg.SoulsAddedToHand > 0)
            Row3(sb, "Souls added to hand", agg.SoulsAddedToHand.ToString(), "");
        if (agg.SoulsAddedToDiscardPile > 0)
            Row3(sb, "Souls added to discard pile", agg.SoulsAddedToDiscardPile.ToString(), "");
    }

    private static bool IsOstySummonStatsHome(
        MegaCrit.Sts2.Core.Models.CardModel card,
        CardAggregate agg)
    {
        return agg.TimesOstySummoned > 0
            || agg.TotalOstyHpSummoned > 0m
            || card is SummonForth
            || IsCardId(card, "CARD.SUMMON_FORTH");
    }

    private static void AppendUnmovablePowerStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        RunMetaStats metaStats)
    {
        metaStats ??= new RunMetaStats();
        if (card is not Unmovable && !IsCardId(card, "CARD.UNMOVABLE")) return;

        Row3(
            sb,
            "Extra block gained from unmovable's power",
            FormatDecimal(metaStats.ExtraBlockGainedFromUnmovablePower),
            "");
    }

    private static void AppendAlchemizePotionStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        CardAggregate agg,
        bool compact)
    {
        if (card is not Alchemize && !IsCardId(card, "CARD.ALCHEMIZE")) return;

        // Match White Beast Statue's potion outcome rows. Alchemize has no
        // reward screen, so its skipped count means the observed procure
        // result failed (for example, a full potion belt or Sozu).
        Row3(sb, "Potions gained", agg.PotionsGained.ToString(), "");
        Row3(sb, "Potions skipped", agg.PotionsSkipped.ToString(), "");
        if (compact) return;

        Row3(sb, "common potions", agg.CommonPotionsGained.ToString(), "");
        Row3(sb, "uncommon potions", agg.UncommonPotionsGained.ToString(), "");
        Row3(sb, "rare potions", agg.RarePotionsGained.ToString(), "");
    }

    private static void AppendJackOfAllTradesStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        CardAggregate agg,
        bool compact)
    {
        if (card is not JackOfAllTrades && !IsCardId(card, "CARD.JACK_OF_ALL_TRADES")) return;

        Row3(sb, "Colorless cards added", agg.JackColorlessCardsAdded.ToString(), "");
        if (compact) return;

        Row3(sb, "uncommons added", agg.JackUncommonCardsAdded.ToString(), "");
        Row3(sb, "rares added", agg.JackRareCardsAdded.ToString(), "");
        Row3(sb, "Attacks added", agg.JackAttacksAdded.ToString(), "");
        Row3(sb, "Skills added", agg.JackSkillsAdded.ToString(), "");
        Row3(sb, "Powers added", agg.JackPowersAdded.ToString(), "");

        var averageCost = agg.JackColorlessCardsAdded <= 0
            ? 0m
            : (decimal)agg.JackAddedCardCostTotal / agg.JackColorlessCardsAdded;
        Row3(sb, "Avg cost of cards added", FormatDecimal(averageCost), "");
    }

    private static void AppendDiscoveryStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        CardAggregate agg,
        bool compact)
    {
        if (card is not Discovery && !IsCardId(card, "CARD.DISCOVERY")) return;

        Row3(sb, "Cards picked", agg.DiscoveryCardsPicked.ToString(), "");
        if (compact) return;

        Row3(sb, "commons picked", agg.DiscoveryCommonCardsPicked.ToString(), "");
        Row3(sb, "uncommons picked", agg.DiscoveryUncommonCardsPicked.ToString(), "");
        Row3(sb, "rares picked", agg.DiscoveryRareCardsPicked.ToString(), "");
        Row3(sb, "Attacks picked", agg.DiscoveryAttacksPicked.ToString(), "");
        Row3(sb, "Skills picked", agg.DiscoverySkillsPicked.ToString(), "");
        Row3(sb, "Powers picked", agg.DiscoveryPowersPicked.ToString(), "");

        var averageDiscount = agg.DiscoveryCardsPicked <= 0
            ? 0m
            : (decimal)agg.DiscoveryEnergyDiscountTotal / agg.DiscoveryCardsPicked;
        Row3(
            sb,
            GetEnergyStatLabel("avg discount of picked card"),
            FormatDecimal(averageDiscount),
            "");
    }

    private static void AppendDrainPowerStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        CardAggregate agg,
        bool compact)
    {
        if (card is not DrainPower && !IsCardId(card, "CARD.DRAIN_POWER")) return;

        Row3(sb, "Cards upgraded", agg.DrainPowerCardsUpgraded.ToString(), "");
        if (compact) return;

        var upgradesPerTurn = agg.DrainPowerTurnsInDeck <= 0
            ? 0m
            : (decimal)agg.DrainPowerCardsUpgraded / agg.DrainPowerTurnsInDeck;
        var upgradesPerCombat = agg.CombatsInDeck <= 0
            ? 0m
            : (decimal)agg.DrainPowerCardsUpgraded / agg.CombatsInDeck;
        var upgradedPlaysPerTurn = agg.DrainPowerTurnsInDeck <= 0
            ? 0m
            : (decimal)agg.DrainPowerUpgradedCardPlays / agg.DrainPowerTurnsInDeck;
        var upgradedPlaysPerCombat = agg.CombatsInDeck <= 0
            ? 0m
            : (decimal)agg.DrainPowerUpgradedCardPlays / agg.CombatsInDeck;

        Row3(sb, "Avg cards upgraded per turn", FormatDecimal(upgradesPerTurn), "");
        Row3(sb, "Avg cards upgraded per combat", FormatDecimal(upgradesPerCombat), "");
        Row3(sb, "Avg upgraded-card plays per turn", FormatDecimal(upgradedPlaysPerTurn), "");
        Row3(sb, "Avg upgraded-card plays per combat", FormatDecimal(upgradedPlaysPerCombat), "");
    }

    private static void AppendJugglingPowerStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        RunMetaStats metaStats,
        bool compact)
    {
        if (card is not Juggling && !IsCardId(card, "CARD.JUGGLING")) return;

        metaStats ??= new RunMetaStats();
        PowerAggregate? powerAgg = null;
        if (metaStats.PowerAggregates != null)
        {
            metaStats.PowerAggregates.TryGetValue(JugglingPowerId, out powerAgg);
            powerAgg ??= metaStats.PowerAggregates.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.PowerId, JugglingPowerId, StringComparison.Ordinal)
                || string.Equals(candidate.DisplayName, "Juggling", StringComparison.OrdinalIgnoreCase));
        }
        powerAgg ??= new PowerAggregate();

        Row3(sb, "Total attacks copied", powerAgg.AttacksCopied.ToString(), "");
        if (compact) return;

        Row3(sb, "commons copied", powerAgg.CommonAttacksCopied.ToString(), "");
        Row3(sb, "uncommons copied", powerAgg.UncommonAttacksCopied.ToString(), "");
        Row3(sb, "rares copied", powerAgg.RareAttacksCopied.ToString(), "");

        decimal averagePerTurn = powerAgg.TurnsActive > 0
            ? (decimal)powerAgg.AttacksCopied / powerAgg.TurnsActive
            : 0m;
        decimal averagePerCombat = powerAgg.CombatsActive > 0
            ? (decimal)powerAgg.AttacksCopied / powerAgg.CombatsActive
            : 0m;
        Row3(sb, "avg copies per turn", FormatDecimal(averagePerTurn), "");
        Row3(sb, "avg copies per combat", FormatDecimal(averagePerCombat), "");
    }

    private static void AppendDanseMacabrePowerStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        RunMetaStats metaStats,
        bool compact)
    {
        if (card is not DanseMacabre
            && !IsCardId(card, "CARD.DANSE_MACABRE"))
            return;

        metaStats ??= new RunMetaStats();
        PowerAggregate? powerAgg = null;
        if (metaStats.PowerAggregates != null)
        {
            metaStats.PowerAggregates.TryGetValue(
                DanseMacabrePowerId,
                out powerAgg);
            powerAgg ??= metaStats.PowerAggregates.Values.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.PowerId,
                    DanseMacabrePowerId,
                    StringComparison.Ordinal)
                || string.Equals(
                    candidate.DisplayName,
                    "Danse Macabre",
                    StringComparison.OrdinalIgnoreCase));
        }
        powerAgg ??= new PowerAggregate();

        Row3(sb, "Times triggered", powerAgg.TimesTriggered.ToString(), "");
        if (compact)
        {
            Row3(
                sb,
                GetBlockStatLabel("Block gained"),
                FormatDecimal(powerAgg.BlockGained),
                "");
            return;
        }

        decimal triggersPerTurn = powerAgg.TurnsActive > 0
            ? (decimal)powerAgg.TimesTriggered / powerAgg.TurnsActive
            : 0m;
        decimal triggersPerCombat = powerAgg.CombatsActive > 0
            ? (decimal)powerAgg.TimesTriggered / powerAgg.CombatsActive
            : 0m;
        decimal blockPerTurn = powerAgg.TurnsActive > 0
            ? powerAgg.BlockGained / powerAgg.TurnsActive
            : 0m;
        decimal blockPerCombat = powerAgg.CombatsActive > 0
            ? powerAgg.BlockGained / powerAgg.CombatsActive
            : 0m;

        Row3(
            sb,
            "Avg triggers per turn once active",
            FormatDecimal(triggersPerTurn),
            "");
        Row3(
            sb,
            "Avg triggers per combat",
            FormatDecimal(triggersPerCombat),
            "");
        Row3(
            sb,
            GetBlockStatLabel("Block gained"),
            FormatDecimal(powerAgg.BlockGained),
            "");
        Row3(
            sb,
            GetBlockStatLabel("Avg block gained per turn once active"),
            FormatDecimal(blockPerTurn),
            "");
        Row3(
            sb,
            GetBlockStatLabel("Avg block gained per combat"),
            FormatDecimal(blockPerCombat),
            "");
    }

    private static void AppendUnrelentingFreeAttackStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        RunMetaStats metaStats,
        bool compact)
    {
        if (card is not Unrelenting && !IsCardId(card, "CARD.UNRELENTING")) return;

        metaStats ??= new RunMetaStats();
        PowerAggregate? powerAgg = null;
        if (metaStats.PowerAggregates != null)
        {
            metaStats.PowerAggregates.TryGetValue(FreeAttackPowerId, out powerAgg);
            powerAgg ??= metaStats.PowerAggregates.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.PowerId, FreeAttackPowerId, StringComparison.Ordinal)
                || string.Equals(candidate.DisplayName, "Free Attack", StringComparison.OrdinalIgnoreCase));
        }
        powerAgg ??= new PowerAggregate();

        var utilization = powerAgg.FreeAttackChargesGranted <= 0
            ? 0m
            : 100m * powerAgg.FreeAttackChargesUsed / powerAgg.FreeAttackChargesGranted;
        Row3(
            sb,
            "Free Attack charges used/granted",
            $"{powerAgg.FreeAttackChargesUsed}/{powerAgg.FreeAttackChargesGranted}",
            $"{utilization:F0}%");
        Row3(
            sb,
            GetEnergyStatLabel("total saved"),
            FormatDecimal(powerAgg.FreeAttackEnergySaved),
            "");
        if (compact) return;

        var averageEnergySaved = powerAgg.FreeAttackChargesUsed <= 0
            ? 0m
            : powerAgg.FreeAttackEnergySaved / powerAgg.FreeAttackChargesUsed;
        Row3(
            sb,
            "Charges used with 0 energy saved",
            powerAgg.FreeAttackZeroEnergySavingsUses.ToString(),
            "");
        Row3(
            sb,
            GetEnergyStatLabel("avg saved per charge used"),
            FormatDecimal(averageEnergySaved),
            "");
        Row3(
            sb,
            "Basic Attacks discounted",
            powerAgg.FreeAttackBasicAttacksDiscounted.ToString(),
            "");
        Row3(
            sb,
            "Common Attacks discounted",
            powerAgg.FreeAttackCommonAttacksDiscounted.ToString(),
            "");
        Row3(
            sb,
            "Uncommon Attacks discounted",
            powerAgg.FreeAttackUncommonAttacksDiscounted.ToString(),
            "");
        Row3(
            sb,
            "Rare Attacks discounted",
            powerAgg.FreeAttackRareAttacksDiscounted.ToString(),
            "");
    }

    private static void AppendDebtStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel card,
        CardAggregate agg)
    {
        if (card is not Debt && !IsCardId(card, "CARD.DEBT")) return;

        Row3(sb, "Times triggered", agg.DebtTriggers.ToString(), "");
        Row3(sb, "Gold loss attempted", (agg.DebtTriggers * DebtGoldLossPerTrigger).ToString(), "");
        Row3(sb, "Gold lost", agg.DebtGoldLost.ToString(), "");
        Row3(sb, "Gold loss blocked", agg.DebtGoldLossBlocked.ToString(), "");
    }

    private static bool IsCardId(MegaCrit.Sts2.Core.Models.CardModel? card, string id)
    {
        try
        {
            return card?.Id != null
                && string.Equals(card.Id.ToString(), id, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void AppendReplayStats(StringBuilder sb, CardAggregate agg)
    {
        if (agg.TimesReplayExtraPlanned <= 0
            && agg.TimesReplayExtraPlayed <= 0
            && agg.TimesReplayAttackNoDamage <= 0)
            return;

        if (agg.TimesReplayExtraPlanned > 0)
            Row3(sb, "Replay planned/played", $"{agg.TimesReplayExtraPlanned}/{agg.TimesReplayExtraPlayed}", "");
        else
            Row3(sb, "Replay extra plays", agg.TimesReplayExtraPlayed.ToString(), "");
        foreach (var reason in agg.ReplayExtraPlayReasons.Values
                     .Where(r => r.Count > 0)
                     .OrderByDescending(r => r.Count)
                     .ThenBy(r => r.DisplayName))
        {
            var displayName = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(reason.DisplayName)
                ? reason.ReasonId
                : reason.DisplayName);
            Row3(sb, $"Replay from {displayName}", reason.Count.ToString(), "");
        }

        if (agg.TimesReplayAttackNoDamage <= 0) return;

        Row3(sb, "Replay no-damage attacks", agg.TimesReplayAttackNoDamage.ToString(), "");
        foreach (var reason in agg.ReplayAttackNoDamageReasons.Values
                     .Where(r => r.Count > 0)
                     .OrderByDescending(r => r.Count)
                     .ThenBy(r => r.DisplayName))
        {
            var displayName = StatsTooltip.EscapeBbcode(string.IsNullOrWhiteSpace(reason.DisplayName)
                ? reason.ReasonId
                : reason.DisplayName);
            Row3(sb, $"No damage from {displayName}", reason.Count.ToString(), "");
        }
    }

    /// <summary>
    /// Emit a single stat row in the canonical 3-column layout used for
    /// every stat line in the tooltip. <paramref name="pct"/> can be empty
    /// — the cell's still present so the label and value columns align
    /// vertically with rows that DO have a percentage (Overkill, Blocked,
    /// Played/Drawn). The cell padding keeps adjacent columns from
    /// crowding visually (fixes "Played/Drawn1/1100%"-style crowding).
    ///
    /// Column weights: label=4, value=1, percent=1. Label dominates
    /// (~66% of width) so the label text always fits; numeric columns
    /// are narrow since their content is typically 1-5 chars.
    /// Padding: label gets right-padding (12px), value gets right-padding
    /// (12px) so it sits off the percent column, percent gets left-side
    /// padding from value's right-padding and small right-padding (4px).
    /// </summary>
    private static void Row3(StringBuilder sb, string label, string value, string pct)
    {
        sb.Append("[table=3]");
        sb.Append($"[cell expand=4 padding=0,0,12,0][color=#e0e0e0]{label}[/color][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,12,0][right][b]{value}[/b][/right][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,4,0][right][color=#b5b5b5]{pct}[/color][/right][/cell]");
        sb.Append("[/table]\n");
    }

    /// <summary>
    /// Emit a two-stat row for closely-related values that read better side by
    /// side than stacked vertically. Used for compact pairs like
    /// "Block gained" / "Avg block" where both numbers belong to the same
    /// section and neither needs a percentage column.
    /// </summary>
    private static void RowDual(StringBuilder sb, string leftLabel, string leftValue, string rightLabel, string rightValue)
    {
        sb.Append("[table=4]");
        sb.Append($"[cell expand=3 padding=0,0,12,0][color=#e0e0e0]{leftLabel}[/color][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,18,0][right][b]{leftValue}[/b][/right][/cell]");
        sb.Append($"[cell expand=3 padding=0,0,12,0][color=#e0e0e0]{rightLabel}[/color][/cell]");
        sb.Append($"[cell expand=1 padding=0,0,4,0][right][b]{rightValue}[/b][/right][/cell]");
        sb.Append("[/table]\n");
    }

    private static bool AppendDedicatedPoisonStats(StringBuilder sb, CardAggregate agg, bool compact)
    {
        var poison = GetPoisonSummary(agg);
        if (poison == null) return false;

        if (compact)
        {
            if (poison.Value.TimesApplied <= 0 && poison.Value.TotalAmountApplied == 0m)
                return false;

            var extra = poison.Value.TimesApplied > 0
                ? poison.Value.TimesApplied > 1 ? $"{poison.Value.TimesApplied}x" : "1x"
                : "";
            Row3(sb, GetPoisonStatLabel(poison.Value, "applied"), FormatDecimal(poison.Value.TotalAmountApplied), extra);
            return true;
        }

        decimal avgPoison = agg.Plays > 0 ? poison.Value.TotalAmountApplied / agg.Plays : 0m;

        Row3(sb, GetPoisonStatLabel(poison.Value, "total applied"), FormatDecimal(poison.Value.TotalAmountApplied), "");
        Row3(sb, GetPoisonStatLabel(poison.Value, "avg applied"), FormatDecimal(avgPoison), "");
        Row3(sb, GetPoisonStatLabel(poison.Value, "applications"), poison.Value.TimesApplied.ToString(), "");

        if (poison.Value.TotalTriggeredEffectiveDamage > 0m || poison.Value.TotalTriggeredOverkill > 0m)
        {
            decimal avgPoisonDamage = agg.Plays > 0 ? poison.Value.TotalTriggeredEffectiveDamage / agg.Plays : 0m;
            Row3(sb, GetPoisonStatLabel(poison.Value, "damage"), FormatDecimal(poison.Value.TotalTriggeredEffectiveDamage), "");
            Row3(sb, GetPoisonStatLabel(poison.Value, "avg damage"), FormatDecimal(avgPoisonDamage), "");

            if (poison.Value.TotalTriggeredOverkill > 0m)
                Row3(sb, GetPoisonStatLabel(poison.Value, "overkill"), FormatDecimal(poison.Value.TotalTriggeredOverkill), "");
        }

        if (poison.Value.TimesBlockedByArtifact > 0)
        {
            string extra = poison.Value.TimesBlockedByArtifact > 1
                ? $"{poison.Value.TimesBlockedByArtifact}x"
                : "1x";
            Row3(sb, GetPoisonStatLabel(poison.Value, "blocked by Artifact"), FormatDecimal(poison.Value.TotalAmountBlockedByArtifact), extra);
        }

        return true;
    }

    private static PoisonEffectSummary? GetPoisonSummary(CardAggregate agg)
    {
        if (agg.AppliedEffects == null || agg.AppliedEffects.Count == 0) return null;

        int timesApplied = 0;
        decimal totalAmountApplied = 0m;
        int timesBlockedByArtifact = 0;
        decimal totalAmountBlockedByArtifact = 0m;
        decimal totalTriggeredEffectiveDamage = 0m;
        decimal totalTriggeredOverkill = 0m;
        string? iconPath = null;

        foreach (var effect in agg.AppliedEffects.Values)
        {
            if (!IsPoisonEffect(effect)) continue;

            timesApplied += effect.TimesApplied;
            totalAmountApplied += effect.TotalAmountApplied;
            timesBlockedByArtifact += effect.TimesBlockedByArtifact;
            totalAmountBlockedByArtifact += effect.TotalAmountBlockedByArtifact;
            totalTriggeredEffectiveDamage += effect.TotalTriggeredEffectiveDamage;
            totalTriggeredOverkill += effect.TotalTriggeredOverkill;
            if (string.IsNullOrWhiteSpace(iconPath) && !string.IsNullOrWhiteSpace(effect.IconPath))
                iconPath = effect.IconPath;
        }

        if (timesApplied <= 0 &&
            totalAmountApplied == 0m &&
            timesBlockedByArtifact <= 0 &&
            totalAmountBlockedByArtifact == 0m &&
            totalTriggeredEffectiveDamage == 0m &&
            totalTriggeredOverkill == 0m)
            return null;

        return new PoisonEffectSummary(
            timesApplied,
            totalAmountApplied,
            timesBlockedByArtifact,
            totalAmountBlockedByArtifact,
            totalTriggeredEffectiveDamage,
            totalTriggeredOverkill,
            iconPath);
    }

    private static string GetPoisonStatLabel(PoisonEffectSummary poison, string suffix)
    {
        if (!string.IsNullOrWhiteSpace(poison.IconPath))
            return GetInlineIconStatLabel(poison.IconPath, suffix);

        return $"Poison {suffix}";
    }

    private static string GetBlockStatLabel(string suffix)
    {
        return GetInlineIconStatLabel(BlockIconPath, suffix);
    }

    private static string GetDrawStatLabel(string suffix)
    {
        return GetInlineIconStatLabel(DrawCardsNextTurnPowerIconPath, suffix);
    }

    private static string GetEnergyStatLabel(string suffix)
    {
        return GetInlineIconStatLabel(EnergyPotionIconPath, suffix);
    }

    private static string GetStarStatLabel(string suffix)
    {
        return GetInlineIconStatLabel(StarIconPath, suffix);
    }

    private static string GetForgeStatLabel(string suffix)
    {
        return suffix switch
        {
            "avg gained" => "Forge avg",
            _ => $"Forge {suffix}",
        };
    }

    private static void AppendMakeItSoStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel cardModel,
        CardAggregate agg,
        bool compact)
    {
        if (cardModel is not MakeItSo) return;

        int? currentCounter = null;
        int threshold = 0;
        if (RunTracker.TryGetMakeItSoSkillCounter(cardModel, out var current, out var currentThreshold))
        {
            currentCounter = current;
            threshold = currentThreshold;
        }

        AppendMakeItSoStats(sb, agg, compact, currentCounter, threshold);
    }

    private static void AppendMakeItSoStats(
        StringBuilder sb,
        CardAggregate agg,
        bool compact,
        int? currentCounter,
        int threshold)
    {
        if (currentCounter.HasValue && threshold > 0)
            Row3(sb, "Skills this turn", $"{currentCounter.Value}/{threshold}", "");

        if (!compact && agg.TimesSummonedToHand > 0)
            Row3(sb, "Times triggered", agg.TimesSummonedToHand.ToString(), "");
    }

    private static void AppendPullFromBelowStats(
        StringBuilder sb,
        MegaCrit.Sts2.Core.Models.CardModel cardModel,
        int etherealCardsPlayedThisCombat)
    {
        if (cardModel is not PullFromBelow) return;

        Row3(sb, "Ethereal cards played this combat", etherealCardsPlayedThisCombat.ToString(), "");
    }

    private static string GetInlineIconStatLabel(string iconPath, string suffix)
    {
        var normalizedPath = NormalizeResourcePath(iconPath);
        return $"[img={InlineKeywordIconSize}x{InlineKeywordIconSize}]{normalizedPath}[/img] {suffix}";
    }

    private static string NormalizeResourcePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.StartsWith("res://", StringComparison.Ordinal)
            ? path
            : $"res://{path.TrimStart('/')}";
    }

    private static void AppendAppliedEffects(StringBuilder sb, CardAggregate agg, bool compact, bool excludePoison)
    {
        if (agg.AppliedEffects == null || agg.AppliedEffects.Count == 0) return;
        bool hasArtifactBlockedSummary = GetArtifactBlockedTotals(agg, excludePoison).Times > 0;
        var visibleEffects = agg.AppliedEffects.Values
            .Where(effect => ShouldShowAppliedEffectRow(effect, hasArtifactBlockedSummary, excludePoison))
            .OrderByDescending(e => e.TimesApplied)
            .ThenBy(e => e.DisplayName)
            .ToList();

        if (visibleEffects.Count == 0) return;

        if (!compact)
            sb.Append("[color=#b5b5b5]Effects applied[/color]\n");

        int shown = 0;
        foreach (var effect in visibleEffects)
        {
            if (compact && shown >= 2) break;

            var label = GetAppliedEffectLabel(effect);
            var value = FormatDecimal(effect.TotalAmountApplied);
            var extra = effect.TimesApplied > 1 ? $"{effect.TimesApplied}x" : "1x";
            Row3(sb, label, value, extra);

            if (!compact && effect.TotalTriggeredCardsDrawBlocked > 0)
                Row3(sb, GetAppliedEffectBlockedDrawLabel(effect), effect.TotalTriggeredCardsDrawBlocked.ToString(), "");

            shown++;
        }
    }

    private static void AppendArtifactBlockedSummary(StringBuilder sb, CardAggregate agg, bool excludePoison)
    {
        var (times, amount) = GetArtifactBlockedTotals(agg, excludePoison);
        if (times <= 0) return;

        var label = GetArtifactStrippedLabel(agg, excludePoison);
        var value = times.ToString();
        var extra = amount != times ? $"{FormatDecimal(amount)} amt" : "";
        Row3(sb, label, value, extra);
    }

    private static (int Times, decimal Amount) GetArtifactBlockedTotals(CardAggregate agg, bool excludePoison)
    {
        if (agg.AppliedEffects == null || agg.AppliedEffects.Count == 0)
            return (0, 0m);

        int times = 0;
        decimal amount = 0m;
        foreach (var effect in agg.AppliedEffects.Values)
        {
            if (excludePoison && IsPoisonEffect(effect)) continue;
            times += effect.TimesBlockedByArtifact;
            amount += effect.TotalAmountBlockedByArtifact;
        }

        return (times, amount);
    }

    private static bool ShouldShowAppliedEffectRow(AppliedEffectAggregate effect, bool hasArtifactBlockedSummary, bool excludePoison)
    {
        if (excludePoison && IsPoisonEffect(effect))
            return false;

        if (effect.TotalAmountApplied == 0m && effect.TimesBlockedByArtifact > 0)
            return false;

        if (hasArtifactBlockedSummary && IsArtifactEffect(effect) && effect.TotalAmountApplied < 0m)
            return false;

        return true;
    }

    private static string GetArtifactStrippedLabel(CardAggregate agg, bool excludePoison)
    {
        if (agg.AppliedEffects != null)
        {
            foreach (var effect in agg.AppliedEffects.Values)
            {
                if (excludePoison && IsPoisonEffect(effect)) continue;
                if (!IsArtifactEffect(effect) || string.IsNullOrWhiteSpace(effect.IconPath)) continue;
                return $"[img={InlineKeywordIconSize}x{InlineKeywordIconSize}]{effect.IconPath}[/img] stripped";
            }
        }

        return "Artifact stripped";
    }

    private static bool IsArtifactEffect(AppliedEffectAggregate effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.EffectId) &&
            effect.EffectId.Contains("ARTIFACT_POWER", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(effect.DisplayName, "Artifact", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPoisonEffect(AppliedEffectAggregate effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.EffectId) &&
            effect.EffectId.Contains("POISON", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(effect.DisplayName, "Poison", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAppliedEffectLabel(AppliedEffectAggregate effect)
    {
        // Escape the game/data-derived name before it lands in a BBCode cell:
        // a display name containing '[' would otherwise be parsed as markup.
        var label = StatsTooltip.EscapeBbcode(
            string.IsNullOrWhiteSpace(effect.DisplayName) ? effect.EffectId : effect.DisplayName);
        if (!string.IsNullOrWhiteSpace(effect.IconPath))
            return GetInlineIconStatLabel(effect.IconPath, label);
        if (IsEnergyEffect(effect))
            return GetEnergyEffectLabel(label);
        if (IsStarEffect(effect))
            return GetStarEffectLabel(label);
        if (IsNoxiousFumesEffect(effect))
            return GetIconBackedEffectLabel(label, effect.IconPath);

        return label;
    }

    private static string GetAppliedEffectBlockedDrawLabel(AppliedEffectAggregate effect)
    {
        return GetBlockedDrawStatLabel("cards blocked");
    }

    private static string GetBlockedDrawStatLabel(string suffix)
    {
        return GetInlineIconStatLabel(BlockedDrawIconPath, suffix);
    }

    private static void AppendCardDrawStats(StringBuilder sb, CardAggregate agg)
    {
        int attempted = agg.TimesCardsDrawAttempted;
        if (attempted <= 0)
            attempted = agg.TimesCardsDrawn + agg.TimesCardsDrawBlocked;

        if (attempted > agg.TimesCardsDrawn)
        {
            Row3(sb, GetDrawStatLabel("drawn / tried"), $"{agg.TimesCardsDrawn}/{attempted}", "");
            AppendBlockedDrawReasonRows(sb, agg, attempted - agg.TimesCardsDrawn);
            return;
        }

        if (agg.TimesCardsDrawn > 0)
            Row3(sb, GetDrawStatLabel("cards drawn"), agg.TimesCardsDrawn.ToString(), "");
    }

    private static void AppendBlockedDrawReasonRows(StringBuilder sb, CardAggregate agg, int blockedGap)
    {
        if (blockedGap <= 0) return;

        int categorized = 0;
        foreach (var reason in agg.BlockedDrawReasons.Values
                     .OrderByDescending(r => r.Count)
                     .ThenBy(r => r.DisplayName))
        {
            if (reason.Count <= 0) continue;
            Row3(sb, GetBlockedDrawReasonLabel(reason.DisplayName), reason.Count.ToString(), "");
            categorized += reason.Count;
        }

        int uncategorized = Math.Max(0, blockedGap - categorized);
        if (uncategorized > 0)
            Row3(sb, GetBlockedDrawReasonLabel("other"), uncategorized.ToString(), "");
    }

    private static string GetBlockedDrawReasonLabel(string reasonDisplayName)
    {
        return GetBlockedDrawStatLabel($"blocked by {StatsTooltip.EscapeBbcode(reasonDisplayName)}");
    }

    private static bool IsEnergyEffect(AppliedEffectAggregate effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.EffectId) &&
            effect.EffectId.Contains("ENERGY", StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(effect.DisplayName) &&
               effect.DisplayName.Contains("Energy", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetEnergyEffectLabel(string label)
    {
        const string energyPrefix = "Energy ";
        if (label.StartsWith(energyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = label.Substring(energyPrefix.Length).Trim();
            if (!string.IsNullOrWhiteSpace(suffix))
                return GetInlineIconStatLabel(EnergyPotionIconPath, suffix);
        }

        return GetInlineIconStatLabel(EnergyPotionIconPath, label);
    }

    private static bool IsStarEffect(AppliedEffectAggregate effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.EffectId) &&
            effect.EffectId.Contains("STAR", StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(effect.DisplayName) &&
               effect.DisplayName.StartsWith("Star", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStarEffectLabel(string label)
    {
        const string pluralPrefix = "Stars ";
        const string singularPrefix = "Star ";

        if (label.StartsWith(pluralPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = label.Substring(pluralPrefix.Length).Trim();
            if (!string.IsNullOrWhiteSpace(suffix))
                return GetInlineIconStatLabel(StarIconPath, suffix);
        }

        if (label.StartsWith(singularPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = label.Substring(singularPrefix.Length).Trim();
            if (!string.IsNullOrWhiteSpace(suffix))
                return GetInlineIconStatLabel(StarIconPath, suffix);
        }

        return GetInlineIconStatLabel(StarIconPath, label);
    }

    private static bool IsNoxiousFumesEffect(AppliedEffectAggregate effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.EffectId) &&
            effect.EffectId.Contains("NOXIOUS_FUMES", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(effect.DisplayName, "Noxious Fumes", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetIconBackedEffectLabel(string label, string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
            return label;

        return GetInlineIconStatLabel(iconPath, label);
    }

    private static string FormatDecimal(decimal value)
    {
        return decimal.Truncate(value) == value
            ? value.ToString("0")
            : value.ToString("0.##");
    }

    /// <summary>
    /// Whether the energy-spent stats are worth showing. Rule (per Nelson):
    /// show only when empirical variance exists between the actual energy
    /// paid across all plays and what you'd expect if every play cost the
    /// listed amount. If the rows aren't there, the user can safely assume
    /// 1 play = 1 listed cost and not think about it.
    ///
    /// This single rule subsumes every specific trigger we previously
    /// enumerated (Snecko, Master Planner, Sly, Corruption, upgrade cost
    /// change, X-cost, ...): they ALL manifest as a TotalEnergySpent that
    /// doesn't equal listed-cost × plays. If any of those mechanics is
    /// active and the card's been played, variance will show up and the
    /// rows will appear.
    ///
    /// Consequence: unplayed cards don't show energy stats even under a
    /// cost-variance relic. Acceptable — play the card once and it starts
    /// showing. Simpler than maintaining an enumeration of triggers the
    /// game's balance team might add to in a future patch.
    /// </summary>
    private static bool IsEnergyInteresting(
        MegaCrit.Sts2.Core.Models.CardModel card, CardAggregate agg)
    {
        try
        {
            if (agg.Plays <= 0) return false;
            int expectedPerPlay = card.EnergyCost.GetWithModifiers(CostModifiers.None);
            if (expectedPerPlay < 0) return true;  // X-cost / negative sentinel — show if played
            return agg.TotalEnergySpent != expectedPerPlay * agg.Plays;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IsEnergyInteresting failed: {e.Message}");
            return false;
        }
    }

    private static bool IsStarInteresting(
        MegaCrit.Sts2.Core.Models.CardModel card, CardAggregate agg)
    {
        try
        {
            if (agg.Plays <= 0 || agg.TotalStarsSpent <= 0) return false;
            if (card.HasStarCostX) return true;

            int expectedPerPlay = Math.Max(0, card.GetStarCostWithModifiers());
            return agg.TotalStarsSpent != expectedPerPlay * agg.Plays;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"IsStarInteresting failed: {e.Message}");
            return false;
        }
    }
}

internal readonly record struct PoisonEffectSummary(
    int TimesApplied,
    decimal TotalAmountApplied,
    int TimesBlockedByArtifact,
    decimal TotalAmountBlockedByArtifact,
    decimal TotalTriggeredEffectiveDamage,
    decimal TotalTriggeredOverkill,
    string? IconPath);

[HarmonyPatch(typeof(NCardHolder), "ClearHoverTips")]
public static class CardHoverHidePatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardHolder __instance)
    {
        try { StatsTooltip.HideIfAnchoredTo(__instance); }
        catch (System.Exception e) { CoreMain.Logger.Error($"CardHoverHide failed: {e.Message}"); }
    }
}
