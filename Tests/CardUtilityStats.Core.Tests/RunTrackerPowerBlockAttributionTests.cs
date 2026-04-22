using System.Reflection;
using System.Runtime.CompilerServices;
using CardUtilityStats.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Xunit;

namespace CardUtilityStats.Core.Tests;

[Collection("RunTrackerSerial")]
public class RunTrackerPowerBlockAttributionTests
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

    private static readonly PropertyInfo CombatEventsProperty =
        PendingCombatType.GetProperty("CombatEvents", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CombatEvents not found.");

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

    private static readonly MethodInfo RecordBlockGainedEntryMethod =
        typeof(RunTracker).GetMethod("RecordBlockGainedEntry", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RecordBlockGainedEntry not found.");

    private static readonly FieldInfo AbstractModelIdField =
        typeof(AbstractModel).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AbstractModel.Id backing field not found.");

    private static readonly FieldInfo AbstractModelIsMutableField =
        typeof(AbstractModel).GetField("<IsMutable>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AbstractModel.IsMutable backing field not found.");

    private static readonly FieldInfo CreaturePlayerField =
        typeof(Creature).GetField("<Player>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Creature.Player backing field not found.");

    private static readonly FieldInfo CreatureBlockField =
        typeof(Creature).GetField("_block", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Creature._block not found.");

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

    private static readonly FieldInfo BlockGainedAmountField =
        typeof(BlockGainedEntry).GetField("<Amount>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("BlockGainedEntry.Amount backing field not found.");

    private static readonly FieldInfo BlockGainedPropsField =
        typeof(BlockGainedEntry).GetField("<Props>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("BlockGainedEntry.Props backing field not found.");

    private static readonly FieldInfo BlockGainedCardPlayField =
        typeof(BlockGainedEntry).GetField("<CardPlay>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("BlockGainedEntry.CardPlay backing field not found.");

    private static readonly MethodInfo ModelDbGetIdMethod =
        typeof(ModelDb).GetMethod(nameof(ModelDb.GetId), BindingFlags.Public | BindingFlags.Static, null, [typeof(Type)], null)
        ?? throw new InvalidOperationException("ModelDb.GetId(Type) not found.");

    [Fact]
    public void RecordBlockGainedEntry_AttributesOwnedPowerSourceToApplyingCard()
    {
        var previousPendingCombat = PendingCombatField.GetValue(null);
        var pendingCombat = CreatePendingCombat();
        PendingCombatField.SetValue(null, pendingCombat);
        ResetTrackerState();

        try
        {
            var player = CreatePlayer();
            var receiver = CreatePlayerCreature(player, block: 5);
            var power = CreatePowerModel(typeof(FeelNoPainPower));
            var entry = CreateBlockGainedEntry(receiver, amount: 5);

            TrackPowerOwnership(power, "CARD.FEEL_NO_PAIN#1", player);
            _ = PushExecutionSourceMethod.Invoke(null, new object?[] { power });

            try
            {
                _ = RecordBlockGainedEntryMethod.Invoke(null, new object?[] { entry });
            }
            finally
            {
                _ = PopExecutionSourceMethod.Invoke(null, new object?[] { power });
            }

            var aggregates = GetAggregates(pendingCombat);
            var aggregate = Assert.Contains("CARD.FEEL_NO_PAIN#1", aggregates);
            Assert.Equal(5, aggregate.TotalBlockGained);

            var events = GetEvents(pendingCombat);
            var blockEvent = Assert.Single(events);
            Assert.Equal("block_gained", blockEvent.Type);
            Assert.Equal("CARD.FEEL_NO_PAIN#1", blockEvent.CardId);
            Assert.Equal(5, blockEvent.Blocked);
        }
        finally
        {
            ResetTrackerState();
            PendingCombatField.SetValue(null, previousPendingCombat);
        }
    }

    [Fact]
    public void RecordBlockGainedEntry_IgnoresUnownedPowerSources()
    {
        var previousPendingCombat = PendingCombatField.GetValue(null);
        var pendingCombat = CreatePendingCombat();
        PendingCombatField.SetValue(null, pendingCombat);
        ResetTrackerState();

        try
        {
            var player = CreatePlayer();
            var receiver = CreatePlayerCreature(player, block: 5);
            var power = CreatePowerModel(typeof(FeelNoPainPower));
            var entry = CreateBlockGainedEntry(receiver, amount: 5);

            _ = PushExecutionSourceMethod.Invoke(null, new object?[] { power });

            try
            {
                _ = RecordBlockGainedEntryMethod.Invoke(null, new object?[] { entry });
            }
            finally
            {
                _ = PopExecutionSourceMethod.Invoke(null, new object?[] { power });
            }

            Assert.Empty(GetAggregates(pendingCombat));
            Assert.Empty(GetEvents(pendingCombat));
        }
        finally
        {
            ResetTrackerState();
            PendingCombatField.SetValue(null, previousPendingCombat);
        }
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

    private static IEnumerable<CardEvent> GetEvents(object pendingCombat)
    {
        return (IEnumerable<CardEvent>)(CombatEventsProperty.GetValue(pendingCombat)
            ?? throw new InvalidOperationException("CombatEvents returned null."));
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

    private static Creature CreatePlayerCreature(Player player, int block)
    {
        var creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        CreaturePlayerField.SetValue(creature, player);
        CreatureBlockField.SetValue(creature, block);
        return creature;
    }

    private static PowerModel CreatePowerModel(Type powerType)
    {
        var power = (PowerModel)RuntimeHelpers.GetUninitializedObject(powerType);
        InitializeMutableModel(power, powerType);
        return power;
    }

    private static BlockGainedEntry CreateBlockGainedEntry(Creature receiver, int amount)
    {
        var entry = (BlockGainedEntry)RuntimeHelpers.GetUninitializedObject(typeof(BlockGainedEntry));
        CombatHistoryActorField.SetValue(entry, receiver);
        CombatHistoryRoundNumberField.SetValue(entry, 1);
        CombatHistoryCurrentSideField.SetValue(entry, CombatSide.Player);
        CombatHistoryHistoryField.SetValue(entry, RuntimeHelpers.GetUninitializedObject(typeof(CombatHistory)));
        BlockGainedAmountField.SetValue(entry, amount);
        BlockGainedPropsField.SetValue(entry, default(ValueProp));
        BlockGainedCardPlayField.SetValue(entry, null);
        return entry;
    }

    private static void InitializeMutableModel(AbstractModel model, Type concreteType)
    {
        var modelId = (ModelId)(ModelDbGetIdMethod.Invoke(null, new object?[] { concreteType })
            ?? throw new InvalidOperationException("ModelDb.GetId returned null."));

        AbstractModelIdField.SetValue(model, modelId);
        AbstractModelIsMutableField.SetValue(model, true);
    }
}
