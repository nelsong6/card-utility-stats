using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Toolbox from its owner-specific opening-hand callback. The actual
/// offered rarities are observed at the choose-card command that the relic
/// creates immediately inside that callback.
/// </summary>
[HarmonyPatch]
public static class ToolboxBeforeHandDrawPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.Toolbox");
        return t == null ? null : AccessTools.Method(t, "BeforeHandDraw");
    }

    [HarmonyPrefix]
    public static void Prefix(object __instance, Player player, ICombatState combatState)
    {
        try
        {
            var owner = AccessTools.Property(__instance.GetType(), "Owner")?.GetValue(__instance) as Player;
            if (player == null || !ReferenceEquals(player, owner)) return;
            if (player.PlayerCombatState?.TurnNumber != 1) return;
            RunTracker.RecordToolboxTrigger();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ToolboxBeforeHandDrawPatch failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
public static class ToolboxChooseACardScreenPatch
{
    [HarmonyPrefix]
    public static void Prefix(
        PlayerChoiceContext context,
        IReadOnlyList<CardModel> cards,
        Player player,
        bool canSkip,
        out bool __state)
    {
        __state = false;

        try
        {
            __state = RunTracker.RecordToolboxOffers(cards);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ToolboxChooseACardScreenPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(bool __state, Task<CardModel> __result)
    {
        if (!__state || __result == null) return;
        ObserveSelectionAsync(__result);
    }

    private static async void ObserveSelectionAsync(Task<CardModel> selectionTask)
    {
        try
        {
            var selectedCard = await selectionTask.ConfigureAwait(false);
            RunTracker.RecordToolboxTaken(selectedCard);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"ToolboxChooseACardScreenPatch selection observation failed: {e.Message}");
        }
    }
}
