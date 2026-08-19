using Battle.Contracts.Events;
using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Core.Random;

namespace Battle.Core.UnitTests.Decisions;

public sealed class DecisionSelectorTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_001_OnePositiveLegalCandidateUsesOnlyLegalWithoutDraw()
    {
        var draw = new FixedDecisionDrawSource(0);

        var result = DecisionSelector.Select(
            new[] { DecisionTestFixture.Score("action_a", 10) },
            false,
            draw);

        AssertSelection(result, "action_a", 10, 10, DecisionSelectionMode.OnlyLegalAction, false);
        Assert.Equal(0, draw.CallCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_002_OneZeroLegalCandidateIsNotZeroFallback()
    {
        var result = DecisionSelector.Select(
            new[] { DecisionTestFixture.Score("action_a", 0) },
            false,
            null);

        AssertSelection(result, "action_a", 0, 0, DecisionSelectionMode.OnlyLegalAction, false);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_003_AllZeroCandidatesUseFixedSystemPriorityWithoutDraw()
    {
        var draw = new FixedDecisionDrawSource(0);
        var candidates = new[]
        {
            DecisionTestFixture.Score("sys_wait", 0, DecisionActionSlot.System),
            DecisionTestFixture.Score("sys_retreat", 0, DecisionActionSlot.System),
            DecisionTestFixture.Score("sys_approach", 0, DecisionActionSlot.System),
        };

        var result = DecisionSelector.Select(candidates, false, draw);

        AssertSelection(result, "sys_approach", 0, 0, DecisionSelectionMode.ZeroWeightFallback, false);
        Assert.Equal(0, draw.CallCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_004_MultipleLegalWithOnePositiveStillConsumesOneDraw()
    {
        var draw = new FixedDecisionDrawSource(4);
        var result = DecisionSelector.Select(
            new[]
            {
                DecisionTestFixture.Score("action_a", 0),
                DecisionTestFixture.Score("action_b", 5),
            },
            false,
            draw);

        AssertSelection(result, "action_b", 5, 5, DecisionSelectionMode.WeightedRng, true);
        Assert.Equal(1, draw.CallCount);
    }

    [Theory]
    [InlineData(0, "action_a")]
    [InlineData(2, "action_a")]
    [InlineData(3, "action_c")]
    [InlineData(4, "action_c")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_005_ZeroWidthAndPositiveIntervalBoundariesAreExact(int drawValue, string expected)
    {
        var result = DecisionSelector.Select(
            new[]
            {
                DecisionTestFixture.Score("action_a", 3),
                DecisionTestFixture.Score("action_b", 0),
                DecisionTestFixture.Score("action_c", 2),
            },
            false,
            new FixedDecisionDrawSource(drawValue));

        Assert.Equal(expected, result.ActionId.Value);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_006_InsertionOrderCannotChangeSortedIntervalsOrChoice()
    {
        var candidates = new[]
        {
            DecisionTestFixture.Score("action_c", 2),
            DecisionTestFixture.Score("action_a", 3),
            DecisionTestFixture.Score("action_b", 0),
        };

        var forward = DecisionSelector.Select(candidates, false, new FixedDecisionDrawSource(3));
        var reverse = DecisionSelector.Select(candidates.Reverse(), false, new FixedDecisionDrawSource(3));

        Assert.Equal(forward.ActionId, reverse.ActionId);
        Assert.Equal(forward.LegalActionIds, reverse.LegalActionIds);
        Assert.Equal(new[] { "action_a", "action_b", "action_c" }, forward.LegalActionIds.Select(id => id.Value));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_007_TwoActorDrawsConsumeIndicesZeroThenOne()
    {
        var draw = new FixedDecisionDrawSource(4, 1);
        var candidates = new[]
        {
            DecisionTestFixture.Score("action_a", 3),
            DecisionTestFixture.Score("action_b", 2),
        };

        var a = DecisionSelector.Select(candidates, false, draw);
        var b = DecisionSelector.Select(candidates, false, draw);

        Assert.Equal(0UL, a.Rng?.Index);
        Assert.Equal(1UL, b.Rng?.Index);
        Assert.Equal("action_b", a.ActionId.Value);
        Assert.Equal("action_a", b.ActionId.Value);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_008_NoDrawForActorADoesNotReserveDecisionIndex()
    {
        var draw = new FixedDecisionDrawSource(1);
        var a = DecisionSelector.Select(
            new[] { DecisionTestFixture.Score("action_a", 1) },
            false,
            draw);
        var b = DecisionSelector.Select(
            new[]
            {
                DecisionTestFixture.Score("action_a", 1),
                DecisionTestFixture.Score("action_b", 1),
            },
            false,
            draw);

        Assert.Null(a.Rng);
        Assert.Equal(0UL, b.Rng?.Index);
        Assert.Equal(1, draw.CallCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_009_DecisionSelectionLeavesResolutionStreamUntouched()
    {
        var rng = new GameplayRng(0);
        var source = new GameplayDecisionDrawSource(rng);

        _ = DecisionSelector.Select(
            new[]
            {
                DecisionTestFixture.Score("action_a", 1),
                DecisionTestFixture.Score("action_b", 1),
            },
            false,
            source);

        Assert.Equal(1UL, rng.Decision.NextDrawIndex);
        Assert.Equal(0UL, rng.Resolution.NextDrawIndex);
    }

    [Theory]
    [InlineData("stream")]
    [InlineData("operation")]
    [InlineData("range")]
    [InlineData("raw")]
    [InlineData("result")]
    [InlineData("normalized")]
    [InlineData("index")]
    [InlineData("next_index")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_010_InjectedDrawRequiresSelfConsistentDecisionProvenance(string tamper)
    {
        var source = new TamperedDecisionDrawSource(tamper);

        var exception = Assert.Throws<EngineInvariantException>(() => DecisionSelector.Select(
            new[]
            {
                DecisionTestFixture.Score("action_a", 2),
                DecisionTestFixture.Score("action_b", 3),
            },
            false,
            source));

        Assert.Equal(DecisionFailureCodes.InvalidDecisionDraw, exception.Code);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_011_HardOpportunityWinsWithoutDrawing()
    {
        var draw = new FixedDecisionDrawSource(0);
        var result = DecisionSelector.Select(
            new[]
            {
                DecisionTestFixture.Score("action_a", 100),
                DecisionTestFixture.Score("action_b", 1, hard: true, debt: 4),
            },
            false,
            draw);

        AssertSelection(result, "action_b", 1, 101, DecisionSelectionMode.HardOpportunity, false);
        Assert.Equal(0, draw.CallCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SEL_012_HardTieUsesDebtThenWeightThenOrdinalActionId()
    {
        var candidates = new[]
        {
            DecisionTestFixture.Score("action_z", 100, hard: true, debt: 4),
            DecisionTestFixture.Score("action_b", 2, hard: true, debt: 5),
            DecisionTestFixture.Score("action_a", 2, hard: true, debt: 5),
            DecisionTestFixture.Score("action_heavy", 3, hard: true, debt: 5),
        };

        var result = DecisionSelector.Select(candidates, false, null);

        Assert.Equal("action_heavy", result.ActionId.Value);

        result = DecisionSelector.Select(candidates.Where(item => item.ActionId.Value != "action_heavy"), false, null);
        Assert.Equal("action_a", result.ActionId.Value);
    }

    private static void AssertSelection(
        DecisionSelection selection,
        string actionId,
        int weight,
        int sum,
        DecisionSelectionMode mode,
        bool hasRng)
    {
        Assert.Equal(actionId, selection.ActionId.Value);
        Assert.Equal(weight, selection.ChosenWeight);
        Assert.Equal(sum, selection.WeightSum);
        Assert.Equal(mode, selection.SelectionMode);
        Assert.Equal(hasRng, selection.Rng.HasValue);
    }

    private sealed class GameplayDecisionDrawSource : IDecisionDrawSource
    {
        private readonly GameplayRng _rng;

        internal GameplayDecisionDrawSource(GameplayRng rng)
        {
            _rng = rng;
        }

        public ulong NextDrawIndex => _rng.Decision.NextDrawIndex;

        public RngProvenance NextInt(int minimumInclusive, int maximumExclusive) =>
            _rng.Decision.NextInt(minimumInclusive, maximumExclusive, RngOperation.NextInt);
    }

    private sealed class TamperedDecisionDrawSource : IDecisionDrawSource
    {
        private readonly string _tamper;
        private ulong _nextIndex;

        internal TamperedDecisionDrawSource(string tamper)
        {
            _tamper = tamper;
        }

        public ulong NextDrawIndex =>
            _tamper == "next_index" && _nextIndex != 0 ? 7UL : _nextIndex;

        public RngProvenance NextInt(int minimumInclusive, int maximumExclusive)
        {
            var index = _nextIndex;
            _nextIndex++;
            return new RngProvenance(
                _tamper == "stream" ? RngStream.Resolution : RngStream.Decision,
                _tamper == "index" ? index + 1 : index,
                _tamper == "operation" ? RngOperation.TieBreak : RngOperation.NextInt,
                minimumInclusive,
                _tamper == "range" ? maximumExclusive + 1 : maximumExclusive,
                _tamper == "raw" ? 0U : 5U,
                _tamper == "result" ? 1 : 0,
                _tamper == "normalized" ? 1 : 0);
        }
    }
}
