using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using CardUtilityStats.Core;
using CardUtilityStats.Core.Patches;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Xunit;

namespace CardUtilityStats.Core.Tests;

[Collection("RunTrackerSerial")]
public class RunTrackerPowerApplicationAttributionTests
{
    private static readonly FieldInfo PendingCombatField =
        typeof(RunTracker).GetField("_pendingCombat", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("_pendingCombat not found.");

    private static readonly Type PendingCombatType =
        typeof(RunTracker).Assembly.GetType("CardUtilityStats.Core.PendingCombat")
        ?? throw new InvalidOperationException("PendingCombat type not found.");

    private static readonly PropertyInfo CombatAggregatesProperty =
        PendingCombatType.GetProperty("CombatAggregates", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatAggregates not found.");

    private static readonly PropertyInfo PlayerPowerOwnershipByModifierProperty =
        PendingCombatType.GetProperty("PlayerPowerOwnershipByModifier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PlayerPowerOwnershipByModifier not found.");

    private static readonly PropertyInfo PoisonOwnershipByTargetProperty =
        PendingCombatType.GetProperty("PoisonOwnershipByTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PoisonOwnershipByTarget not found.");

    private static readonly MethodInfo ResetCombatContextStateMethod =
        typeof(RunTracker).GetMethod("ResetCombatContextState", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResetCombatContextState not found.");

    private static readonly MethodInfo TrackPlayerPowerOwnershipLockedMethod =
        typeof(RunTracker).GetMethod("TrackPlayerPowerOwnershipLocked", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TrackPlayerPowerOwnershipLocked not found.");

    private static readonly MethodInfo PushExecutionSourceMethod =
        typeof(RunTracker).GetMethod("PushExecutionSource", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("PushExecutionSource not found.");

    private static readonly MethodInfo PopExecutionSourceMethod =
        typeof(RunTracker).GetMethod("PopExecutionSource", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("PopExecutionSource not found.");

    private static readonly MethodInfo RecordPowerReceivedMethod =
        typeof(RunTracker).GetMethod("RecordPowerReceived", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RecordPowerReceived not found.");

    private static readonly FieldInfo AbstractModelIdField =
        typeof(AbstractModel).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AbstractModel.Id backing field not found.");

    private static readonly FieldInfo AbstractModelIsMutableField =
        typeof(AbstractModel).GetField("<IsMutable>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AbstractModel.IsMutable backing field not found.");

    private static readonly FieldInfo PowerModelOwnerField =
        typeof(PowerModel).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PowerModel._owner not found.");

    private static readonly FieldInfo CreaturePlayerField =
        typeof(Creature).GetField("<Player>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Creature.Player backing field not found.");

    private static readonly FieldInfo CombatHistoryActorField =
        typeof(CombatHistoryEntry).GetField("<Actor>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatHistoryEntry.Actor backing field not found.");

    private static readonly FieldInfo CombatHistoryRoundNumberField =
        typeof(CombatHistoryEntry).GetField("<RoundNumber>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatHistoryEntry.RoundNumber backing field not found.");

    private static readonly FieldInfo CombatHistoryCurrentSideField =
        typeof(CombatHistoryEntry).GetField("<CurrentSide>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatHistoryEntry.CurrentSide backing field not found.");

    private static readonly FieldInfo CombatHistoryHistoryField =
        typeof(CombatHistoryEntry).GetField("<History>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatHistoryEntry.History backing field not found.");

    private static readonly FieldInfo PowerReceivedPowerField =
        typeof(PowerReceivedEntry).GetField("<Power>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PowerReceivedEntry.Power backing field not found.");

    private static readonly FieldInfo PowerReceivedAmountField =
        typeof(PowerReceivedEntry).GetField("<Amount>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PowerReceivedEntry.Amount backing field not found.");

    private static readonly FieldInfo PowerReceivedApplierField =
        typeof(PowerReceivedEntry).GetField("<Applier>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PowerReceivedEntry.Applier backing field not found.");

    private static readonly MethodInfo ModelDbGetIdMethod =
        typeof(ModelDb).GetMethod(nameof(ModelDb.GetId), BindingFlags.Public | BindingFlags.Static, null, [typeof(Type)], null)
        ?? throw new InvalidOperationException("ModelDb.GetId(Type) not found.");

    private static readonly FieldInfo PowerExecutionSourceTargetMethodNamesField =
        typeof(PowerExecutionSourcePatch).GetField("TargetMethodNames", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("PowerExecutionSourcePatch.TargetMethodNames not found.");

    [Fact]
    public void RecordPowerReceived_AttributesOwnedPowerBuffsToTheApplyingCard()
    {
        var previousPendingCombat = PendingCombatField.GetValue(null);
        var pendingCombat = CreatePendingCombat();
        PendingCombatField.SetValue(null, pendingCombat);
        ResetTrackerState();

        try
        {
            var player = CreatePlayer();
            var playerCreature = CreatePlayerCreature(player);
            var sourcePower = CreatePowerModel(typeof(DemonFormPower), playerCreature);
            var appliedPower = CreatePowerModel(typeof(StrengthPower), playerCreature);
            var entry = CreatePowerReceivedEntry(appliedPower, 2m, playerCreature, playerCreature);

            TrackPowerOwnership(sourcePower, "CARD.DEMON_FORM#1", player);
            _ = PushExecutionSourceMethod.Invoke(null, new object?[] { sourcePower });

            try
            {
                _ = RecordPowerReceivedMethod.Invoke(null, new object?[] { entry });
            }
            finally
            {
                _ = PopExecutionSourceMethod.Invoke(null, new object?[] { sourcePower });
            }

            var aggregates = GetAggregates(pendingCombat);
            var aggregate = Assert.Contains("CARD.DEMON_FORM#1", aggregates);
            var effect = Assert.Contains(appliedPower.Id.ToString(), aggregate.AppliedEffects);
            Assert.Equal(1, effect.TimesApplied);
            Assert.Equal(2m, effect.TotalAmountApplied);

            var ownershipByModifier = GetModifierOwnership(pendingCombat);
            var appliedOwnership = Assert.IsAssignableFrom<object>(ownershipByModifier[appliedPower]);
            Assert.Equal("CARD.DEMON_FORM#1", GetRequiredStringProperty(appliedOwnership, "CardInstanceId"));
        }
        finally
        {
            ResetTrackerState();
            PendingCombatField.SetValue(null, previousPendingCombat);
        }
    }

    [Fact]
    public void RecordPowerReceived_TracksEnemyPoisonOwnershipFromOwnedPowerSources()
    {
        var previousPendingCombat = PendingCombatField.GetValue(null);
        var pendingCombat = CreatePendingCombat();
        PendingCombatField.SetValue(null, pendingCombat);
        ResetTrackerState();

        try
        {
            var player = CreatePlayer();
            var playerCreature = CreatePlayerCreature(player);
            var enemy = CreateEnemyCreature();
            var sourcePower = CreatePowerModel(typeof(EnvenomPower), playerCreature);
            var appliedPower = CreatePowerModel(typeof(PoisonPower), enemy);
            var entry = CreatePowerReceivedEntry(appliedPower, 3m, enemy, playerCreature);

            TrackPowerOwnership(sourcePower, "CARD.ENVENOM#1", player);
            _ = PushExecutionSourceMethod.Invoke(null, new object?[] { sourcePower });

            try
            {
                _ = RecordPowerReceivedMethod.Invoke(null, new object?[] { entry });
            }
            finally
            {
                _ = PopExecutionSourceMethod.Invoke(null, new object?[] { sourcePower });
            }

            var aggregate = Assert.Contains("CARD.ENVENOM#1", GetAggregates(pendingCombat));
            var poisonEffect = Assert.Contains(appliedPower.Id.ToString(), aggregate.AppliedEffects);
            Assert.Equal(1, poisonEffect.TimesApplied);
            Assert.Equal(3m, poisonEffect.TotalAmountApplied);

            var poisonOwnershipByTarget = GetPoisonOwnershipByTarget(pendingCombat);
            Assert.True(poisonOwnershipByTarget.Contains(enemy));

            var shares = Assert.IsAssignableFrom<IDictionary>(poisonOwnershipByTarget[enemy]);
            var share = Assert.Single(shares.Values.Cast<object>());
            Assert.Equal("CARD.ENVENOM#1", GetRequiredStringProperty(share, "CardInstanceId"));
            Assert.Equal(appliedPower.Id.ToString(), GetRequiredStringProperty(share, "EffectId"));
        }
        finally
        {
            ResetTrackerState();
            PendingCombatField.SetValue(null, previousPendingCombat);
        }
    }

    [Fact]
    public void RecordPowerReceived_IgnoresUnownedPowerSources()
    {
        var previousPendingCombat = PendingCombatField.GetValue(null);
        var pendingCombat = CreatePendingCombat();
        PendingCombatField.SetValue(null, pendingCombat);
        ResetTrackerState();

        try
        {
            var player = CreatePlayer();
            var playerCreature = CreatePlayerCreature(player);
            var sourcePower = CreatePowerModel(typeof(DemonFormPower), playerCreature);
            var appliedPower = CreatePowerModel(typeof(StrengthPower), playerCreature);
            var entry = CreatePowerReceivedEntry(appliedPower, 2m, playerCreature, playerCreature);

            _ = PushExecutionSourceMethod.Invoke(null, new object?[] { sourcePower });

            try
            {
                _ = RecordPowerReceivedMethod.Invoke(null, new object?[] { entry });
            }
            finally
            {
                _ = PopExecutionSourceMethod.Invoke(null, new object?[] { sourcePower });
            }

            Assert.Empty(GetAggregates(pendingCombat));
            Assert.Empty(GetModifierOwnership(pendingCombat));
        }
        finally
        {
            ResetTrackerState();
            PendingCombatField.SetValue(null, previousPendingCombat);
        }
    }

    [Fact]
    public void PowerExecutionSourcePatch_TargetMethodNames_IncludePowerApplicationHooks()
    {
        var targetMethodNames = Assert.IsAssignableFrom<IEnumerable<string>>(
            PowerExecutionSourceTargetMethodNamesField.GetValue(null));

        Assert.Contains("AfterApplied", targetMethodNames);
        Assert.Contains("BeforeApplied", targetMethodNames);
    }

    private static object CreatePendingCombat()
    {
        return Activator.CreateInstance(PendingCombatType, nonPublic: true)
            ?? throw new InvalidOperationException("Failed to create PendingCombat.");
    }

    private static Dictionary<string, CardAggregate> GetAggregates(object pendingCombat)
    {
        return (Dictionary<string, CardAggregate>)(CombatAggregatesProperty.GetValue(pendingCombat)
            ?? throw new InvalidOperationException("CombatAggregates returned null."));
    }

    private static IDictionary GetModifierOwnership(object pendingCombat)
    {
        return (IDictionary)(PlayerPowerOwnershipByModifierProperty.GetValue(pendingCombat)
            ?? throw new InvalidOperationException("PlayerPowerOwnershipByModifier returned null."));
    }

    private static IDictionary GetPoisonOwnershipByTarget(object pendingCombat)
    {
        return (IDictionary)(PoisonOwnershipByTargetProperty.GetValue(pendingCombat)
            ?? throw new InvalidOperationException("PoisonOwnershipByTarget returned null."));
    }

    private static void ResetTrackerState()
    {
        _ = ResetCombatContextStateMethod.Invoke(null, null);
    }

    private static void TrackPowerOwnership(PowerModel power, string sourceInstanceId, Player player)
    {
        var effect = new AppliedEffectAggregate
        {
            EffectId = power.Id.ToString(),
            DisplayName = power.Id.Entry,
        };

        _ = TrackPlayerPowerOwnershipLockedMethod.Invoke(
            null,
            new object?[] { power, sourceInstanceId, effect, player });
    }

    private static Player CreatePlayer()
    {
        return (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
    }

    private static Creature CreatePlayerCreature(Player player)
    {
        var creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        CreaturePlayerField.SetValue(creature, player);
        return creature;
    }

    private static Creature CreateEnemyCreature()
    {
        return (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
    }

    private static PowerModel CreatePowerModel(Type powerType, Creature owner)
    {
        var power = (PowerModel)RuntimeHelpers.GetUninitializedObject(powerType);
        InitializeMutableModel(power, powerType);
        PowerModelOwnerField.SetValue(power, owner);
        return power;
    }

    private static PowerReceivedEntry CreatePowerReceivedEntry(
        PowerModel appliedPower,
        decimal amount,
        Creature target,
        Creature? applier)
    {
        var entry = (PowerReceivedEntry)RuntimeHelpers.GetUninitializedObject(typeof(PowerReceivedEntry));
        CombatHistoryActorField.SetValue(entry, target);
        CombatHistoryRoundNumberField.SetValue(entry, 1);
        CombatHistoryCurrentSideField.SetValue(entry, CombatSide.Player);
        CombatHistoryHistoryField.SetValue(entry, RuntimeHelpers.GetUninitializedObject(typeof(CombatHistory)));
        PowerReceivedPowerField.SetValue(entry, appliedPower);
        PowerReceivedAmountField.SetValue(entry, amount);
        PowerReceivedApplierField.SetValue(entry, applier);
        return entry;
    }

    private static string GetRequiredStringProperty(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{propertyName} not found.");

        return (string)(property.GetValue(source)
            ?? throw new InvalidOperationException($"{propertyName} returned null."));
    }

    private static void InitializeMutableModel(AbstractModel model, Type concreteType)
    {
        var modelId = (ModelId)(ModelDbGetIdMethod.Invoke(null, new object?[] { concreteType })
            ?? throw new InvalidOperationException("ModelDb.GetId returned null."));

        AbstractModelIdField.SetValue(model, modelId);
        AbstractModelIsMutableField.SetValue(model, true);
    }
}
