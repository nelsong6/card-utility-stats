using System;
using System.Text.Json.Serialization;

namespace SpireLens.Core;

/// <summary>
/// Compatibility shim for the old deck-view toggle persistence path.
/// The injected deck-view UI still reads and writes PrefsStorage, but the
/// actual persisted state now lives in the loader-side BaseLib config.
/// </summary>
public class Prefs
{
    [JsonPropertyName("view_stats_ticked")]
    public bool ViewStatsTicked { get; set; }

    [JsonPropertyName("show_removed_cards_ticked")]
    public bool ShowRemovedCardsTicked { get; set; } = true;

    [JsonPropertyName("show_enemy_stats_ticked")]
    public bool ShowEnemyStatsTicked { get; set; }

    [JsonPropertyName("show_combat_card_stats_ticked")]
    public bool ShowCombatCardStatsTicked { get; set; }
}

public static class PrefsStorage
{
    public static Prefs Load()
    {
        try
        {
            var options = RuntimeOptionsProvider.Refresh();
            return new Prefs
            {
                ViewStatsTicked = options.ViewStatsToggleEnabled,
                ShowRemovedCardsTicked = options.ShowRemovedCardsInDeckView,
                ShowEnemyStatsTicked = options.ShowEnemyStatsOnHover,
                ShowCombatCardStatsTicked = options.ShowCardStatsDuringCombat,
            };
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"PrefsStorage.Load failed: {e}");
            return new Prefs();
        }
    }

    public static void Save(Prefs prefs)
    {
        try
        {
            RuntimeOptionsProvider.SetViewStatsToggleEnabled(prefs.ViewStatsTicked);
            RuntimeOptionsProvider.SetShowRemovedCardsInDeckView(prefs.ShowRemovedCardsTicked);
            RuntimeOptionsProvider.SetShowEnemyStatsOnHover(prefs.ShowEnemyStatsTicked);
            RuntimeOptionsProvider.SetShowCardStatsDuringCombat(prefs.ShowCombatCardStatsTicked);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"PrefsStorage.Save failed: {e}");
        }
    }
}
