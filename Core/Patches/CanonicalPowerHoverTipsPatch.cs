using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;

namespace SpireLens.Core.Patches;

/// <summary>
/// Preserves the game's power hover tips when a later mod postfix tries to
/// read mutable-only state from a canonical power model. BaseLib 3.3.3 does
/// this while expanding dynamic-var tips, which breaks callers such as
/// Gorget's Plating hover tip after the game has already built a valid result.
/// </summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
public static class CanonicalPowerHoverTipsPatch
{
    [HarmonyFinalizer]
    public static Exception? Finalizer(
        PowerModel __instance,
        IEnumerable<IHoverTip>? __result,
        Exception? __exception)
    {
        if (__exception is CanonicalModelException
            && !__instance.IsMutable
            && __result != null)
        {
            CoreMain.LogDebug(
                $"CanonicalPowerHoverTipsPatch preserved {__instance.GetType().Name} hover tips");
            return null;
        }

        return __exception;
    }
}
