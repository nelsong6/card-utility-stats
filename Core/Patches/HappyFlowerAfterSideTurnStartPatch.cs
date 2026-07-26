using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arms the energy attribution window for Happy Flower and Happy Flower??? at
/// the start of the owning player's turn. Their energy calls are captured by
/// <see cref="PlayerGainEnergyPatch"/> and kept in separate relic aggregates.
/// </summary>
[HarmonyPatch]
public static class HappyFlowerAfterSideTurnStartPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var typeName in new[]
                 {
                     "MegaCrit.Sts2.Core.Models.Relics.HappyFlower",
                     "MegaCrit.Sts2.Core.Models.Relics.FakeHappyFlower",
                 })
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null
                ? null
                : AccessTools.Method(type, "AfterSideTurnStart");
            if (method != null)
                yield return method;
        }
    }

    [HarmonyPrefix]
    public static void Prefix(
        RelicModel __instance,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        out bool __state)
    {
        __state = false;

        try
        {
            var ownerCreature = __instance.Owner?.Creature;
            if (side != CombatSide.Player
                || ownerCreature == null
                || participants == null
                || !participants.Contains(ownerCreature))
            {
                return;
            }

            RunTracker.ArmHappyFlowerEnergyAttribution(__instance);
            __state = true;
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"HappyFlowerAfterSideTurnStartPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        RelicModel __instance,
        bool __state,
        ref Task __result)
    {
        try
        {
            if (!__state) return;
            if (__result == null)
            {
                RunTracker.DisarmHappyFlowerEnergyAttribution(__instance);
                return;
            }

            __result = DisarmAfterCompletion(__result, __instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"HappyFlowerAfterSideTurnStartPatch.Postfix failed: {e.Message}");
        }
    }

    private static async Task DisarmAfterCompletion(
        Task inner,
        RelicModel relic)
    {
        try
        {
            await inner;
        }
        finally
        {
            RunTracker.DisarmHappyFlowerEnergyAttribution(relic);
        }
    }
}
