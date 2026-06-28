using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireLens.Core.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyCardPlayCount))]
public static class HookModifyCardPlayCountPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        CardModel card,
        int playCount,
        ref List<AbstractModel> modifyingModels,
        int __result)
    {
        try
        {
            RunTracker.NoteReplayPlayCountModifiers(card, playCount, __result, modifyingModels);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug($"Replay play-count modifier attribution failed: {e.Message}");
        }
    }
}
