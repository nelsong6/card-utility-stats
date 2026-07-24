using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

public sealed class StatsTooltipOwnershipTests : IDisposable
{
    private static readonly FieldInfo AnchorField =
        typeof(StatsTooltip).GetField("_anchor", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("StatsTooltip._anchor not found.");

    [Fact]
    public void NativeLifecycle_OldOwnerCannotHideNewOwner()
    {
        var oldOwner = UninitializedControl();
        var newOwner = UninitializedControl();

        StatsTooltip.BeginNativeHover(oldOwner);
        StatsTooltip.BeginNativeHover(newOwner);
        StatsTooltip.HideIfAnchoredTo(oldOwner);

        Assert.Same(newOwner, AnchorField.GetValue(null));

        StatsTooltip.HideIfAnchoredTo(newOwner);

        Assert.Null(AnchorField.GetValue(null));
    }

    [Fact]
    public void NativeLifecyclePatch_TargetMethodsHaveExpectedSignatures()
    {
        Assert.NotNull(typeof(NHoverTipSet).GetMethod(
            nameof(NHoverTipSet.CreateAndShow),
            new[]
            {
                typeof(Control),
                typeof(IEnumerable<IHoverTip>),
                typeof(HoverTipAlignment),
            }));
        Assert.NotNull(typeof(NHoverTipSet).GetMethod(
            nameof(NHoverTipSet.CreateAndShowMapPointHistory),
            new[] { typeof(Control), typeof(NMapPointHistoryHoverTip) }));
        Assert.NotNull(typeof(NHoverTipSet).GetMethod(
            nameof(NHoverTipSet.Remove),
            new[] { typeof(Control) }));
        Assert.NotNull(typeof(NHoverTipSet).GetMethod(
            nameof(NHoverTipSet.Clear),
            Type.EmptyTypes));
    }

    public void Dispose()
    {
        StatsTooltip.Hide();
    }

    private static Control UninitializedControl()
        => (Control)RuntimeHelpers.GetUninitializedObject(typeof(Control));
}
