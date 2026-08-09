using System;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core;

/// <summary>
/// Resolves the canonical runtime id for a game model type.
///
/// SpireLens registries key on ids the game emits at runtime
/// (<c>model.Id.ToString()</c>). Hand-copying those strings is how the meta
/// power registry silently lost five months of application and active-turn
/// data: <c>ModelDb.GetId</c> slugifies the whole type name, so
/// <c>RupturePower</c> is <c>POWER.RUPTURE_POWER</c>, and the shortened
/// <c>POWER.RUPTURE</c> never matched a live power. Asking the game for the id
/// removes the copy, so a renamed model class becomes a compile error here
/// instead of a lookup that quietly returns nothing.
/// </summary>
internal static class ModelIds
{
    /// <summary>
    /// Canonical id for <typeparamref name="T"/>, or null if the game's own
    /// derivation throws. Returning null rather than propagating keeps one
    /// changed model hierarchy from taking a whole registry down at class
    /// load; the affected entry drops out and everything else still resolves.
    /// </summary>
    internal static string? TryGet<T>()
        where T : AbstractModel
    {
        try
        {
            return ModelDb.GetId(typeof(T)).ToString();
        }
        catch (Exception e)
        {
            CoreMain.LogDebug(
                $"ModelIds.TryGet<{typeof(T).Name}> failed: {e.Message}");
            return null;
        }
    }
}
