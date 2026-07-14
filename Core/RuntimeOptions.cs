using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace SpireLens.Core;

public sealed class RuntimeOptions
{
    public bool ViewStatsToggleEnabled { get; set; }
    public bool ShowRemovedCardsInDeckView { get; set; } = true;
    public bool ShowEnemyStatsOnHover { get; set; }
    public bool ShowCardStatsDuringCombat { get; set; }
    public bool ShowHandTooltips { get; set; } = true;
    public bool UseVerboseHandStats { get; set; }
    public bool DisableCardStatsDuringCombat { get; set; }
    public bool EnableDebugLogging { get; set; }
    public string BuildTimeZoneId { get; set; } = "America/Los_Angeles";
}

public static class RuntimeOptionsProvider
{
    private const string BridgeTypeName = "SpireLens.Loader.RuntimeOptionsBridge";
    private const string GetCurrentOptionsJsonMethodName = "GetCurrentOptionsJson";
    private const string SetViewStatsToggleEnabledMethodName = "SetViewStatsToggleEnabled";
    private const string SetShowRemovedCardsInDeckViewMethodName = "SetShowRemovedCardsInDeckView";
    private const string SetShowEnemyStatsOnHoverMethodName = "SetShowEnemyStatsOnHover";
    private const string SetShowCardStatsDuringCombatMethodName = "SetShowCardStatsDuringCombat";
    private const string SetVerboseHandStatsEnabledMethodName = "SetVerboseHandStatsEnabled";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static Type? _bridgeType;
    private static MethodInfo? _getCurrentOptionsJsonMethod;
    private static MethodInfo? _setViewStatsToggleEnabledMethod;
    private static MethodInfo? _setShowRemovedCardsInDeckViewMethod;
    private static MethodInfo? _setShowEnemyStatsOnHoverMethod;
    private static MethodInfo? _setShowCardStatsDuringCombatMethod;
    private static MethodInfo? _setVerboseHandStatsEnabledMethod;
    private static bool _loggedMissingBridge;
    private static bool _loggedRefreshFailure;
    private static bool _loggedToggleFailure;
    private static bool _loggedShowRemovedCardsFailure;
    private static bool _loggedShowEnemyStatsFailure;
    private static bool _loggedShowCardStatsDuringCombatFailure;
    private static bool _loggedVerboseHandStatsFailure;

    public static RuntimeOptions Current { get; private set; } = new();

    public static RuntimeOptions Refresh()
    {
        try
        {
            var getOptionsMethod = ResolveGetCurrentOptionsJsonMethod();
            if (getOptionsMethod == null) return Current;

            var json = getOptionsMethod.Invoke(null, null) as string;
            if (string.IsNullOrWhiteSpace(json)) return Current;

            Current = JsonSerializer.Deserialize<RuntimeOptions>(json, JsonOptions) ?? new RuntimeOptions();
            _loggedRefreshFailure = false;
        }
        catch (Exception e)
        {
            if (!_loggedRefreshFailure)
            {
                CoreMain.Logger.Warn($"RuntimeOptionsProvider.Refresh failed: {e.Message}");
                _loggedRefreshFailure = true;
            }
        }

        return Current;
    }

    public static void SetViewStatsToggleEnabled(bool isEnabled)
    {
        try
        {
            var setToggleMethod = ResolveSetViewStatsToggleEnabledMethod();
            setToggleMethod?.Invoke(null, new object?[] { isEnabled });
            _loggedToggleFailure = false;
        }
        catch (Exception e)
        {
            if (!_loggedToggleFailure)
            {
                CoreMain.Logger.Warn($"RuntimeOptionsProvider.SetViewStatsToggleEnabled failed: {e.Message}");
                _loggedToggleFailure = true;
            }
        }

        Refresh();
    }

    public static void SetShowRemovedCardsInDeckView(bool isEnabled)
    {
        try
        {
            var setShowRemovedCardsMethod = ResolveSetShowRemovedCardsInDeckViewMethod();
            setShowRemovedCardsMethod?.Invoke(null, new object?[] { isEnabled });
            _loggedShowRemovedCardsFailure = false;
        }
        catch (Exception e)
        {
            if (!_loggedShowRemovedCardsFailure)
            {
                CoreMain.Logger.Warn($"RuntimeOptionsProvider.SetShowRemovedCardsInDeckView failed: {e.Message}");
                _loggedShowRemovedCardsFailure = true;
            }
        }

        Refresh();
    }

    public static void SetShowEnemyStatsOnHover(bool isEnabled)
    {
        try
        {
            var setShowEnemyStatsMethod = ResolveSetShowEnemyStatsOnHoverMethod();
            setShowEnemyStatsMethod?.Invoke(null, new object?[] { isEnabled });
            _loggedShowEnemyStatsFailure = false;
        }
        catch (Exception e)
        {
            if (!_loggedShowEnemyStatsFailure)
            {
                CoreMain.Logger.Warn($"RuntimeOptionsProvider.SetShowEnemyStatsOnHover failed: {e.Message}");
                _loggedShowEnemyStatsFailure = true;
            }
        }

        Refresh();
    }

    public static void SetShowCardStatsDuringCombat(bool isEnabled)
    {
        try
        {
            var setShowCardStatsDuringCombatMethod = ResolveSetShowCardStatsDuringCombatMethod();
            setShowCardStatsDuringCombatMethod?.Invoke(null, new object?[] { isEnabled });
            _loggedShowCardStatsDuringCombatFailure = false;
        }
        catch (Exception e)
        {
            if (!_loggedShowCardStatsDuringCombatFailure)
            {
                CoreMain.Logger.Warn($"RuntimeOptionsProvider.SetShowCardStatsDuringCombat failed: {e.Message}");
                _loggedShowCardStatsDuringCombatFailure = true;
            }
        }

        Refresh();
    }

    public static void SetVerboseHandStatsEnabled(bool isEnabled)
    {
        try
        {
            var setVerboseMethod = ResolveSetVerboseHandStatsEnabledMethod();
            setVerboseMethod?.Invoke(null, new object?[] { isEnabled });
            _loggedVerboseHandStatsFailure = false;
        }
        catch (Exception e)
        {
            if (!_loggedVerboseHandStatsFailure)
            {
                CoreMain.Logger.Warn($"RuntimeOptionsProvider.SetVerboseHandStatsEnabled failed: {e.Message}");
                _loggedVerboseHandStatsFailure = true;
            }
        }

        Refresh();
    }

    private static MethodInfo? ResolveGetCurrentOptionsJsonMethod()
    {
        _getCurrentOptionsJsonMethod ??= ResolveBridgeType()?.GetMethod(
            GetCurrentOptionsJsonMethodName,
            BindingFlags.Public | BindingFlags.Static);
        return _getCurrentOptionsJsonMethod;
    }

    private static MethodInfo? ResolveSetViewStatsToggleEnabledMethod()
    {
        _setViewStatsToggleEnabledMethod ??= ResolveBridgeType()?.GetMethod(
            SetViewStatsToggleEnabledMethodName,
            BindingFlags.Public | BindingFlags.Static);
        return _setViewStatsToggleEnabledMethod;
    }

    private static MethodInfo? ResolveSetShowRemovedCardsInDeckViewMethod()
    {
        _setShowRemovedCardsInDeckViewMethod ??= ResolveBridgeType()?.GetMethod(
            SetShowRemovedCardsInDeckViewMethodName,
            BindingFlags.Public | BindingFlags.Static);
        return _setShowRemovedCardsInDeckViewMethod;
    }

    private static MethodInfo? ResolveSetShowEnemyStatsOnHoverMethod()
    {
        _setShowEnemyStatsOnHoverMethod ??= ResolveBridgeType()?.GetMethod(
            SetShowEnemyStatsOnHoverMethodName,
            BindingFlags.Public | BindingFlags.Static);
        return _setShowEnemyStatsOnHoverMethod;
    }

    private static MethodInfo? ResolveSetShowCardStatsDuringCombatMethod()
    {
        _setShowCardStatsDuringCombatMethod ??= ResolveBridgeType()?.GetMethod(
            SetShowCardStatsDuringCombatMethodName,
            BindingFlags.Public | BindingFlags.Static);
        return _setShowCardStatsDuringCombatMethod;
    }

    private static MethodInfo? ResolveSetVerboseHandStatsEnabledMethod()
    {
        _setVerboseHandStatsEnabledMethod ??= ResolveBridgeType()?.GetMethod(
            SetVerboseHandStatsEnabledMethodName,
            BindingFlags.Public | BindingFlags.Static);
        return _setVerboseHandStatsEnabledMethod;
    }

    private static Type? ResolveBridgeType()
    {
        if (_bridgeType != null) return _bridgeType;

        _bridgeType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(BridgeTypeName, throwOnError: false))
            .FirstOrDefault(type => type != null);

        if (_bridgeType == null && !_loggedMissingBridge)
        {
            CoreMain.Logger.Warn("RuntimeOptionsProvider could not find the loader bridge; using default options.");
            _loggedMissingBridge = true;
        }

        return _bridgeType;
    }
}
