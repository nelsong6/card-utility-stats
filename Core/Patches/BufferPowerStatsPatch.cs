using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireLens.Core.Patches;

/// <summary>
/// Observes the HP loss a Buffer stack actually zeroes. Buffer's own modifier
/// returns 0 for its owner, so the prevented amount is the drop across this
/// call — post-Block unblocked HP loss, not the attack's printed or intended
/// damage.
/// </summary>
[HarmonyPatch(
    typeof(BufferPower),
    nameof(BufferPower.ModifyHpLostAfterOstyLate),
    new[]
    {
        typeof(Creature),
        typeof(decimal),
        typeof(ValueProp),
        typeof(Creature),
        typeof(CardModel),
    })]
internal static class BufferPowerModifyHpLostStatsPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BufferPower __instance,
        Creature target,
        decimal amount,
        decimal __result)
    {
        try
        {
            RunTracker.ArmBufferDamagePrevention(
                __instance,
                target,
                amount,
                __result);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BufferPowerModifyHpLostStatsPatch failed: {e.Message}");
        }
    }
}

/// <summary>
/// Confirms the charge the modifier above consumed. The game dispatches this
/// callback only for modifiers that actually changed the amount, which is what
/// makes it the honest "a charge was spent" signal rather than the modifier
/// call itself.
///
/// Recorded on the prefix, before the power's own <c>PowerCmd.Decrement</c>:
/// that command early-returns while combat is ending, so waiting for it would
/// drop preventions that genuinely stopped HP loss in the killing-blow window.
/// </summary>
[HarmonyPatch(
    typeof(BufferPower),
    nameof(BufferPower.AfterModifyingHpLostAfterOsty))]
internal static class BufferPowerAfterModifyingHpLostStatsPatch
{
    [HarmonyPrefix]
    public static void Prefix(BufferPower __instance)
    {
        try
        {
            RunTracker.RecordBufferChargeSpent(__instance);
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"BufferPowerAfterModifyingHpLostStatsPatch failed: {e.Message}");
        }
    }
}
