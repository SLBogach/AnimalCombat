using Battle.Core.Initialization;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.UnitTests.Engine;

public sealed class BattleInitializationTests
{
    [Fact]
    public void WP06_INIT_001_ModifiersUseLayerPriorityAndStableIdOrder()
    {
        var applied = new List<StableId>();
        var modifiers = new[]
        {
            new StatModifier(ModifierLayer.Clamp, 0, new StableId("clamp"), "Power", ModifierOperation.Override, 60),
            new StatModifier(ModifierLayer.TemporaryEffect, 0, new StableId("temp"), "Power", ModifierOperation.Add, 1),
            new StatModifier(ModifierLayer.PermanentEffect, 0, new StableId("permanent"), "Power", ModifierOperation.Add, 2),
            new StatModifier(ModifierLayer.Gear, 10, new StableId("gear_z"), "Power", ModifierOperation.Add, 3),
            new StatModifier(ModifierLayer.Gear, 0, new StableId("gear_b"), "Power", ModifierOperation.Multiply, 2_000),
            new StatModifier(ModifierLayer.Gear, 0, new StableId("gear_a"), "Power", ModifierOperation.Add, 5),
            new StatModifier(ModifierLayer.PassiveInitialization, 0, new StableId("passive"), "Power", ModifierOperation.Override, 50),
            new StatModifier(ModifierLayer.ModeNormalization, 0, new StableId("mode"), "Power", ModifierOperation.Multiply, 1_000),
            new StatModifier(ModifierLayer.BaseAnimal, 0, new StableId("animal"), "Power", ModifierOperation.Add, 1),
        };

        var result = ModifierPipeline.Apply(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["Power"] = 10 },
            modifiers,
            1_000,
            modifier => applied.Add(modifier.SourceId));

        Assert.Equal(
            new[]
            {
                "animal",
                "mode",
                "gear_a",
                "gear_b",
                "gear_z",
                "passive",
                "permanent",
                "temp",
                "clamp",
            },
            applied.Select(id => id.Value));
        Assert.Equal(60, result["Power"]);
    }

    [Fact]
    public void WP06_INIT_003_InitialStateMatchesTheGoldenOracle()
    {
        var setup = EngineTestFixture.CreateSetup();
        var frameA = setup.State.FighterA.ToFrame();
        var frameB = setup.State.FighterB.ToFrame();

        AssertFrame(frameA, FighterId.FighterA, 2_000, Facing.Right, 1_650, "rage");
        AssertFrame(frameB, FighterId.FighterB, 4_500, Facing.Left, 1_150, "tempo");
        Assert.Equal(FighterState.DecisionReady, frameA.State);
        Assert.Equal(FighterState.DecisionReady, frameB.State);
        Assert.Null(frameA.ActionId);
        Assert.Null(frameB.ActionId);
        Assert.Empty(frameA.Effects);
        Assert.Empty(frameB.Effects);
        Assert.Empty(setup.State.FighterA.Cooldowns);
        Assert.Empty(setup.State.FighterB.Cooldowns);
        Assert.Null(setup.State.ActiveGrabId);
        Assert.Null(setup.State.ActiveControlId);
        Assert.Null(setup.State.Outcome);
        Assert.Null(setup.State.WinnerFighterId);
        Assert.Null(setup.State.EndReason);
        Assert.Equal(0, setup.State.Tick);
    }

    [Fact]
    public void WP06_INIT_004_InitializationCreatesBothRngStreamsWithoutDraws()
    {
        var setup = EngineTestFixture.CreateSetup();

        Assert.Equal(RngStream.Decision, setup.State.Rng.Decision.Stream);
        Assert.Equal(RngStream.Resolution, setup.State.Rng.Resolution.Stream);
        Assert.Equal(0UL, setup.State.Rng.Decision.NextDrawIndex);
        Assert.Equal(0UL, setup.State.Rng.Resolution.NextDrawIndex);
    }

    private static void AssertFrame(
        FighterFrame frame,
        FighterId fighterId,
        int position,
        Facing facing,
        int maximumHealth,
        string resourceId)
    {
        Assert.Equal(fighterId, frame.FighterId);
        Assert.Equal(position, frame.Position);
        Assert.Equal(facing, frame.Facing);
        Assert.Equal(maximumHealth, frame.Health);
        Assert.Equal(maximumHealth, frame.MaxHealth);
        Assert.Equal(1_000, frame.Energy);
        Assert.Equal(1_000, frame.MaxEnergy);
        Assert.Equal(resourceId, frame.UniqueResource.ResourceId.Value);
        Assert.Equal(0, frame.UniqueResource.Value);
        Assert.Equal(1_000, frame.UniqueResource.Maximum);
        Assert.Equal(0, frame.Stagger);
    }
}
