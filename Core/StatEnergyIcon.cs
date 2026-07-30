using System;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLens.Core;

/// <summary>
/// Resolves the same character-colored inline energy icon used by the game's
/// localization formatter. Context-free stats surfaces deliberately fall back
/// to Ironclad rather than the game's colorless fallback.
/// </summary>
internal static class StatEnergyIcon
{
    private const string FallbackPrefix = "ironclad";
    private const string IconDirectory =
        "res://images/packed/sprite_fonts";

    internal static string GetCurrentPath()
    {
        string? prefix = null;
        try
        {
            prefix = RunManager.Instance?
                .GetLocalCharacterEnergyIconPrefix();
        }
        catch
        {
            // RunManager can be unavailable on context-free surfaces.
        }

        return GetPathForPrefix(prefix);
    }

    internal static string GetPathForPrefix(string? prefix)
    {
        var normalized = string.IsNullOrWhiteSpace(prefix)
            ? FallbackPrefix
            : prefix.Trim().ToLowerInvariant();
        return $"{IconDirectory}/{normalized}_energy_icon.png";
    }

    internal static string RenderInline(int size)
    {
        var normalizedSize = Math.Clamp(size, 8, 64);
        return $"[img={normalizedSize}x{normalizedSize}]"
               + $"{GetCurrentPath()}[/img]";
    }
}
