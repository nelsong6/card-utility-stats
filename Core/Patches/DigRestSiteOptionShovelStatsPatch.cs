using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Shovel's Dig payoff after the rest-site option finishes. The option
/// awaits <c>RelicCmd.Obtain</c>, so the owner's relic inventory is the observed
/// source of truth for which relic was actually acquired and what rarity it has.
/// </summary>
[HarmonyPatch(typeof(DigRestSiteOption), nameof(DigRestSiteOption.OnSelect))]
public static class DigRestSiteOptionShovelStatsPatch
{
    private static readonly MethodInfo? OwnerGetter =
        AccessTools.PropertyGetter(typeof(RestSiteOption), "Owner");

    [HarmonyPrefix]
    public static void Prefix(DigRestSiteOption __instance, out DigState __state)
    {
        __state = default;

        try
        {
            var owner = GetOwner(__instance);
            if (owner == null) return;

            __state = new DigState(owner, owner.Relics.Count);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DigRestSiteOptionShovelStatsPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(DigState __state, Task<bool> __result)
    {
        try
        {
            if (__state.Owner == null) return;

            if (__result == null)
            {
                Observe(__state, selected: true);
                return;
            }

            if (__result.IsCompleted)
            {
                if (!__result.IsCanceled && !__result.IsFaulted)
                    Observe(__state, __result.Result);
                return;
            }

            __result.ContinueWith(
                task =>
                {
                    if (!task.IsCanceled && !task.IsFaulted)
                        Observe(__state, task.Result);
                },
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DigRestSiteOptionShovelStatsPatch.Postfix failed: {e.Message}");
        }
    }

    private static Player? GetOwner(DigRestSiteOption option)
    {
        return OwnerGetter?.Invoke(option, Array.Empty<object>()) as Player;
    }

    private static void Observe(DigState state, bool selected)
    {
        try
        {
            if (!selected || state.Owner == null) return;

            var start = Math.Max(0, state.InitialRelicCount);
            var relics = state.Owner.Relics
                .Skip(start)
                .Where(relic => relic != null)
                .ToList();

            RunTracker.RecordShovelRelicsAcquired(state.Owner, relics);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"DigRestSiteOptionShovelStatsPatch.Observe failed: {e.Message}");
        }
    }

    public readonly record struct DigState(Player? Owner, int InitialRelicCount);
}

/// <summary>
/// Counts rest sites where Shovel's Dig option was available but the local
/// player exited after choosing something else or skipping the campfire.
/// </summary>
[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.BeforeLocalRestSiteExited))]
public static class RestSiteSynchronizerShovelStatsPatch
{
    private static readonly FieldInfo? LocalPlayerIdField =
        AccessTools.Field(typeof(RestSiteSynchronizer), "_localPlayerId");

    [HarmonyPrefix]
    public static void Prefix(RestSiteSynchronizer __instance)
    {
        try
        {
            if (__instance == null) return;

            var options = __instance.GetLocalOptions();
            if (options == null || !options.Any(option => option is DigRestSiteOption)) return;
            if (LocalPlayerIdField?.GetValue(__instance) is not ulong localPlayerId) return;

            var chosenIndex = __instance.GetChosenOptionIndex(localPlayerId);
            bool choseDig = chosenIndex is int index
                && index >= 0
                && index < options.Count
                && options[index] is DigRestSiteOption;
            if (choseDig) return;

            RunTracker.RecordShovelCampfireNotDug(__instance.LocalPlayer);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"RestSiteSynchronizerShovelStatsPatch failed: {e.Message}");
        }
    }
}
