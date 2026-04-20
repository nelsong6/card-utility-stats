using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace CardUtilityStats.CardUtilityStatsCode.Patches;

/// <summary>
/// Diagnostic-only patch: when the Deck View screen is ready, walks the nodes
/// around the "%Upgrades" tickbox (parent, siblings, grandparent) and logs
/// them. Used once to discover the scene structure so we can write the real
/// "add a sibling View Stats tickbox" code without guessing.
///
/// Delete this file once we know the layout — it's research scaffolding.
/// </summary>
[HarmonyPatch(typeof(NCardsViewScreen), "_Ready")]
public static class DeckViewScreenProbePatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardsViewScreen __instance)
    {
        try
        {
            var tickbox = __instance.GetNodeOrNull("%Upgrades");
            if (tickbox == null)
            {
                MainFile.Logger.Warn("DeckViewProbe: %Upgrades not found on this screen");
                return;
            }

            MainFile.Logger.Info($"DeckViewProbe: screen={__instance.GetType().Name} tickbox={tickbox.GetType().Name} tickbox_path={tickbox.GetPath()}");

            var parent = tickbox.GetParent();
            if (parent != null)
            {
                MainFile.Logger.Info($"DeckViewProbe: parent={parent.GetType().Name} parent_name={parent.Name} parent_path={parent.GetPath()}");

                // Log all siblings (children of parent)
                foreach (var sibling in parent.GetChildren())
                {
                    MainFile.Logger.Info($"DeckViewProbe:   sibling name={sibling.Name} type={sibling.GetType().Name}");
                }

                var grandparent = parent.GetParent();
                if (grandparent != null)
                {
                    MainFile.Logger.Info($"DeckViewProbe: grandparent={grandparent.GetType().Name} gp_name={grandparent.Name} gp_path={grandparent.GetPath()}");
                }
            }

            // Also dump the label if we can find it
            var label = __instance.GetNodeOrNull("%ViewUpgradesLabel");
            if (label != null)
            {
                MainFile.Logger.Info($"DeckViewProbe: label={label.GetType().Name} label_path={label.GetPath()} label_parent={label.GetParent()?.Name}");
            }
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"DeckViewProbe threw: {e}");
        }
    }
}
