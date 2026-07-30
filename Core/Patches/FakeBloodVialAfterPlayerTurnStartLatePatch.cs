using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Blood Vial??? through the same observed-healing ledger as Blood
/// Vial while keeping the obscured relic's aggregate separate.
/// </summary>
[HarmonyPatch(typeof(FakeBloodVial), nameof(FakeBloodVial.AfterPlayerTurnStartLate))]
public static class FakeBloodVialAfterPlayerTurnStartLatePatch
{
    private const string RelicId = "RELIC.FAKE_BLOOD_VIAL";

    [HarmonyPrefix]
    public static void Prefix(FakeBloodVial __instance, Player player, out bool __state)
    {
        __state = false;

        try
        {
            if (__instance?.Owner == null || player?.Creature == null || player.Creature.IsDead) return;
            if (!ReferenceEquals(__instance.Owner, player)) return;
            if (player.PlayerCombatState == null || player.PlayerCombatState.TurnNumber > 1) return;

            var attemptedHealing = __instance.DynamicVars.Heal.BaseValue;
            if (attemptedHealing <= 0m) return;

            RunTracker.RecordFakeBloodVialTrigger(player.Creature, attemptedHealing);
            __state = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FakeBloodVialAfterPlayerTurnStartLatePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(FakeBloodVial __instance, Player player, Task __result, bool __state)
    {
        try
        {
            if (!__state) return;
            if (__instance?.Owner == null || !ReferenceEquals(__instance.Owner, player)) return;

            var healedCreature = player?.Creature;
            if (healedCreature == null) return;

            if (__result == null || __result.IsCompleted)
            {
                RunTracker.FinalizeRelicHealing(healedCreature, RelicId);
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.FinalizeRelicHealing(healedCreature, RelicId),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"FakeBloodVialAfterPlayerTurnStartLatePatch.Postfix failed: {e.Message}");
        }
    }
}
