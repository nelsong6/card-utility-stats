using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace SpireLens.Core.Patches;

/// <summary>
/// Claims a card's right press before NCardHolder can remember it as an
/// alternate click. Every card-holder implementation that declares its own
/// OnMousePressed override is patched, along with the base implementation.
/// </summary>
[HarmonyPatch]
internal static class CardTooltipPinInputPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(NCardHolder).Assembly
            .GetTypes()
            .Where(type => typeof(NCardHolder).IsAssignableFrom(type))
            .Select(type => AccessTools.DeclaredMethod(
                type,
                "OnMousePressed",
                [typeof(InputEvent)]))
            .Where(method => method != null)
            .Cast<MethodBase>()
            .Distinct();
    }

    [HarmonyPrefix]
    private static bool Prefix(
        NCardHolder __instance,
        InputEvent inputEvent)
    {
        try
        {
            return !StatsTooltipPinManager.TryHandleCardRightClick(
                __instance,
                inputEvent);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error(
                $"Card tooltip right-click interception failed: {e}");
            return true;
        }
    }
}
