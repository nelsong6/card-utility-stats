using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireLens.Core.Patches;

/// <summary>
/// Records Bone Flute from its owner-specific attack callback. The relic only
/// triggers when an Osty owned by the relic owner attacks; the actual block
/// payload is recorded later through Hook.AfterBlockGained.
/// </summary>
[HarmonyPatch]
public static class BoneFluteAfterAttackPatch
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Relics.BoneFlute");
        return t == null ? null : AccessTools.Method(t, "AfterAttack");
    }

    [HarmonyPrefix]
    public static void Prefix(BoneFlute __instance, AttackCommand command)
    {
        try
        {
            if (!IsOwnedOstyAttack(__instance, command)) return;
            RunTracker.RecordBoneFluteTriggerAndArmBlockAttribution();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BoneFluteAfterAttackPatch.Prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(BoneFlute __instance, AttackCommand command, Task __result)
    {
        try
        {
            if (!IsOwnedOstyAttack(__instance, command)) return;
            if (__result == null)
            {
                RunTracker.DisarmBoneFluteBlockAttribution();
                return;
            }

            __result.ContinueWith(
                _ => RunTracker.DisarmBoneFluteBlockAttribution(),
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"BoneFluteAfterAttackPatch.Postfix failed: {e.Message}");
        }
    }

    private static bool IsOwnedOstyAttack(BoneFlute relic, AttackCommand command)
    {
        var attacker = command?.Attacker;
        if (attacker?.Monster is not Osty) return false;

        var petOwnerCreature = attacker.PetOwner?.Creature;
        var relicOwnerCreature = relic?.Owner?.Creature;
        return petOwnerCreature != null && ReferenceEquals(petOwnerCreature, relicOwnerCreature);
    }
}
