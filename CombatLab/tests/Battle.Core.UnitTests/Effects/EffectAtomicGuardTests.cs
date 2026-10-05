using Battle.Contracts.Events;
using Battle.Contracts.Results;
using Battle.Core.Effects;
using Battle.Core.Engine;
using Battle.Core.Safety;
using Battle.Core.UnitTests.Engine;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectAtomicGuardTests
{
    [Theory]
    [InlineData("state")]
    [InlineData("emitter")]
    [InlineData("prepare")]
    [InlineData("reserve")]
    public void InvalidBatchInputsCannotPublishOrMutate(string invalid)
    {
        var test = new EffectRuntimeFixture([], []); var before = ProgressStamp.Capture(test.State);
        Assert.ThrowsAny<ArgumentException>(() => AtomicBattleBatch.Execute(invalid == "state" ? null! : test.State,
            invalid == "emitter" ? null! : test.Emitter, TickPhase.Resolve, invalid == "prepare" ? null! : (_, _, _) => { }, invalid == "reserve" ? -1 : 0));
        Assert.Equal(before, ProgressStamp.Capture(test.State)); Assert.Empty(test.Journal.Drafts);
    }

    [Theory]
    [InlineData("overflow")]
    [InlineData("division")]
    [InlineData("argument")]
    [InlineData("operation")]
    [InlineData("key")]
    public void CorruptedPreviewFailuresAreTypedAndFullyRolledBack(string failure)
    {
        var test = new EffectRuntimeFixture([], []); var before = ProgressStamp.Capture(test.State);
        var error = Assert.Throws<EngineInvariantException>(() => AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Resolve, (state, _, _) =>
        {
            state.FighterA.SetHealthForTesting(1);
            throw failure switch { "overflow" => new OverflowException(), "division" => new DivideByZeroException(),
                "argument" => new ArgumentException(), "operation" => new InvalidOperationException(), _ => new KeyNotFoundException() };
        }));
        Assert.Equal(failure is "overflow" or "division" ? EngineFailureCodes.EffectArithmeticOverflow : EngineFailureCodes.EffectInvalidMutation, error.Code);
        Assert.Equal(before, ProgressStamp.Capture(test.State)); Assert.Empty(test.Journal.Drafts);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void ResolutionDrawReserveDependsOnBothHealthValuesWithoutAddingCanonicalEvents(int a, int b)
    {
        var setup = EngineTestFixture.CreateSetup(); var state = setup.State;
        state.FighterA.SetHealthForTesting(a); state.FighterB.SetHealthForTesting(b);
        var journal = new RecordingJournal();
        var emitter = new CombatEventEmitter(EngineTestFixture.CreateRequest(), EngineTestFixture.CreateConfig(), journal, 200000);
        AtomicBattleBatch.Execute(state, emitter, TickPhase.Resolve, (_, _, _) => { });
        Assert.Empty(journal.Drafts); Assert.Equal(a, state.FighterA.Health); Assert.Equal(b, state.FighterB.Health);
    }

    [Fact]
    public void MissingEndTickSourceAndNullDefinitionFailBeforeMutation()
    {
        Assert.Throws<ArgumentNullException>(() => new EffectRuntime(null!));
        var test = new EffectRuntimeFixture([], []);
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, Assert.Throws<EngineInvariantException>(() => test.EndTick()).Code);
        Assert.Empty(test.Journal.Drafts);
    }

    [Fact]
    public void SourceFreeInvalidCleanupStillRemovesAllEffectsWithoutTriggers()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule()]); test.Start(); var initial = test.Journal.Drafts.Count;
        test.Runtime.Cleanup(test.State, test.Emitter, null);
        Assert.Equal(0, test.Runtime.ActiveCount);
        Assert.All(test.Journal.Drafts.Skip(initial), x => { Assert.Null(x.SourceEventId); Assert.Empty(x.Payload.RelatedEventIds); Assert.IsType<EffectRemovedPayload>(x.Payload); });
    }

    [Fact]
    public void RejectedHookDiagnosticsKeepOnlyTheirBoundedMostRecentTail()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(condition: Battle.Contracts.Effects.EffectCondition.PositiveDamage)], triggers: 1);
        test.Start(); Assert.Equal(0, test.Runtime.ActiveCount);
        Assert.Equal(EffectAdmission.ConditionRejected, Assert.Single(test.Runtime.Diagnostics));
    }

    [Fact]
    public void GameplayPreviewCannotPublishBattleEndedOrTerminateTheAuthoritativeJournal()
    {
        var test = new EffectRuntimeFixture([], []); var before = ProgressStamp.Capture(test.State);
        var failure = Assert.Throws<EngineInvariantException>(() => AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Resolve,
            (state, emitter, _) => emitter.Emit(0, new BattleEndedPayload([], new BattleSummary(BattleOutcome.Draw, null,
                BattleEndReason.TimeoutEqualHealthFraction, 0, 0, 2, [], state.FinalFrames())))));
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, failure.Code);
        Assert.Contains("gameplay preview cannot terminate", failure.Message);
        Assert.Equal(before, ProgressStamp.Capture(test.State)); Assert.Empty(test.Journal.Drafts); Assert.False(test.Emitter.IsTerminal);
    }
}
