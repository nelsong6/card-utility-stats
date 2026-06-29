using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireLens.Core.Patches;

/// <summary>
/// Opens a narrow attribution window around enemy-owned status-card moves.
/// The actual card/pile counts are observed at CardPileCmd generated-card
/// results while this owner-specific window is active.
/// </summary>
[HarmonyPatch]
public static class EnemyStatusMoveSourcePatch
{
    private static readonly (string TypeName, string MethodName)[] Targets =
    [
        ("MegaCrit.Sts2.Core.Models.Monsters.Aeonglass", "IncreasingIntensityMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.Chomper", "ScreechMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.EyeWithTeeth", "DistractMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.HauntedShip", "HauntMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.LeafSlimeM", "StickyShotMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.LeafSlimeS", "GoopMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.MechaKnight", "FlamethrowerMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.Myte", "ToxicMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.Noisebot", "NoiseMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.PhrogParasite", "InfectMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.SlimedBerserker", "VomitIchorMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.SoulFysh", "BeckonMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.SoulFysh", "GazeMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.TestSubject", "BurningGrowlMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.TheInsatiable", "LiquifyMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.TwigSlimeM", "StickyShotMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.Vantom", "DismemberMove"),
        ("MegaCrit.Sts2.Core.Models.Monsters.Wriggler", "WriggleMove"),
    ];

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var (typeName, methodName) in Targets)
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null ? null : AccessTools.Method(type, methodName);
            if (method != null)
                yield return method;
        }
    }

    [HarmonyPrefix]
    public static void Prefix(object __instance, out object? __state)
    {
        __state = null;
        try
        {
            var creature = (__instance as MegaCrit.Sts2.Core.Models.MonsterModel)?.Creature;
            if (creature?.Monster == null) return;

            __state = RunTracker.PushEnemyStatusCardSource(creature);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EnemyStatusMoveSourcePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(object? __state)
    {
        try
        {
            RunTracker.RestoreEnemyStatusCardSource(__state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"EnemyStatusMoveSourcePatch.Postfix failed: {e.Message}");
        }
    }
}

/// <summary>
/// Entomancer's Dazed pollution is not a normal move. Entomancer applies
/// Personal Hive when added to the room, and the power generates Dazed cards
/// from its owner-specific AfterDamageReceived callback.
/// </summary>
[HarmonyPatch(typeof(PersonalHivePower), nameof(PersonalHivePower.AfterDamageReceived))]
public static class PersonalHiveStatusSourcePatch
{
    [HarmonyPrefix]
    public static void Prefix(PersonalHivePower __instance, Creature target, out object? __state)
    {
        __state = null;
        try
        {
            var owner = __instance.Owner;
            if (owner == null || target == null || !ReferenceEquals(owner, target)) return;
            if (owner.Monster is not MegaCrit.Sts2.Core.Models.Monsters.Entomancer) return;

            __state = RunTracker.PushEnemyStatusCardSource(owner);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PersonalHiveStatusSourcePatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(object? __state)
    {
        try
        {
            RunTracker.RestoreEnemyStatusCardSource(__state);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"PersonalHiveStatusSourcePatch.Postfix failed: {e.Message}");
        }
    }
}
