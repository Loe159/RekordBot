using CueGen.Analysis;
using CueGen.Workflow;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace CueGen.Test
{
    [TestFixture]
    public class WorkflowHotCueRuleEngineTests
    {
        [Test]
        public void GeneratesCanonicalSlotsFromPhraseRolesAndExactBeatgridTimes()
        {
            var timeline = Timeline(
                161,
                (PhraseGroup.Intro, 0, 16),
                (PhraseGroup.Up, 16, 64),
                (PhraseGroup.Chorus, 64, 96),
                (PhraseGroup.Down, 96, 128),
                (PhraseGroup.Outro, 128, 160));
            timeline.Beats[32].TimeMs = 17123;
            timeline.Beats[144].TimeMs = 77999;
            SetVocalRange(timeline, 16, 20, 4);

            var proposal = Generate(timeline);

            Assert.That(proposal.Complete, Is.True);
            Assert.That(proposal.HotCues.Select(cue => cue.Slot), Is.EqualTo(new[] { "A", "B", "C", "D", "E", "H" }));
            Assert.That(proposal.HotCues.Select(cue => cue.Name), Is.EqualTo(new[]
            {
                "INTRO", "VOCAL", "DROP -32", "DROP 1", "BREAKDOWN", "LOOP"
            }));
            Assert.That(proposal.HotCues.Select(cue => cue.Color), Is.EqualTo(new[]
            {
                "Yellow", "Pink", "Green", "Red", "Purple", "Orange"
            }));
            Assert.That(proposal.HotCues.Single(cue => cue.Slot == "C").PositionMs, Is.EqualTo(17123));
            Assert.That(proposal.HotCues.Single(cue => cue.Slot == "B").PositionMs, Is.EqualTo(16 * 500));
            Assert.That(proposal.HotCues.Single(cue => cue.Slot == "B").VocalSectionVerified, Is.True);
            var loop = proposal.HotCues.Single(cue => cue.Slot == "H");
            Assert.That(loop.LoopBeats, Is.EqualTo(16));
            Assert.That(loop.LoopEndMs, Is.EqualTo(77999));
        }

        [Test]
        public void KeepsMinus32WhenItCoincidesWithTheVocalCue()
        {
            var timeline = Timeline(
                129,
                (PhraseGroup.Intro, 0, 32),
                (PhraseGroup.Up, 32, 64),
                (PhraseGroup.Chorus, 64, 96),
                (PhraseGroup.Down, 96, 112),
                (PhraseGroup.Outro, 112, 129));
            SetVocalRange(timeline, 32, 36, 4);

            var proposal = Generate(timeline);

            var cue = proposal.HotCues.Single(item => item.Slot == "C");
            Assert.That(cue.Name, Is.EqualTo("DROP -32"));
            Assert.That(cue.PositionMs, Is.EqualTo(32 * 500));
            Assert.That(cue.DropOffsetBeats, Is.EqualTo(32));
            Assert.That(cue.PhraseStartVerified, Is.True);
            Assert.That(proposal.Evidence.Single(item => item.Slot == "C").Rule, Is.EqualTo("drop_minus_32"));
        }

        [Test]
        public void UsesFirstFourBeatVocalSectionAndBridgeFallbackAndEightBeatOutroLoop()
        {
            var timeline = Timeline(
                105,
                (PhraseGroup.Intro, 0, 16),
                (PhraseGroup.Verse, 16, 48),
                (PhraseGroup.Chorus, 48, 80),
                (PhraseGroup.Bridge, 80, 96),
                (PhraseGroup.Outro, 96, 105));
            SetVocalRange(timeline, 20, 23, 3);
            SetVocalRange(timeline, 32, 36, 4);

            var proposal = Generate(timeline);

            Assert.That(proposal.Complete, Is.True);
            Assert.That(proposal.HotCues.Single(item => item.Slot == "B").PositionMs, Is.EqualTo(32 * 500));
            Assert.That(proposal.Evidence.Single(item => item.Slot == "B").Rule, Is.EqualTo("first_vocal_section_4_beats"));
            Assert.That(proposal.Evidence.Single(item => item.Slot == "E").Rule, Is.EqualTo("first_bridge_after_drop"));
            Assert.That(proposal.HotCues.Single(item => item.Slot == "H").LoopBeats, Is.EqualTo(8));
        }

        [Test]
        public void OmitsVocalCueWhenNoAudibleRunLastsFourBeats()
        {
            var timeline = Timeline(
                129,
                (PhraseGroup.Intro, 0, 16),
                (PhraseGroup.Up, 16, 64),
                (PhraseGroup.Chorus, 64, 96),
                (PhraseGroup.Down, 96, 112),
                (PhraseGroup.Outro, 112, 129));
            SetVocalRange(timeline, 8, 11, 4);

            var proposal = Generate(timeline);

            Assert.That(proposal.HotCues.Select(cue => cue.Slot), Does.Not.Contain("B"));
            Assert.That(proposal.Warnings, Has.Some.Contains("four consecutive beats"));
            Assert.That(proposal.Complete, Is.False);
        }

        [Test]
        public void IncludesTheFinalBeatInAnEndOfTrackVocalSection()
        {
            var timeline = Timeline(
                65,
                (PhraseGroup.Intro, 0, 8),
                (PhraseGroup.Up, 8, 32),
                (PhraseGroup.Chorus, 32, 48),
                (PhraseGroup.Down, 48, 56),
                (PhraseGroup.Outro, 56, 65));
            SetVocalRange(timeline, 61, 65, 2);

            var proposal = Generate(timeline);

            Assert.That(proposal.HotCues.Single(cue => cue.Slot == "B").PositionMs, Is.EqualTo(61 * 500));
        }

        [Test]
        public void TreatsTheVocalMeanThresholdAsInclusive()
        {
            var atThreshold = Timeline(
                65,
                (PhraseGroup.Intro, 0, 8),
                (PhraseGroup.Up, 8, 32),
                (PhraseGroup.Chorus, 32, 48),
                (PhraseGroup.Down, 48, 56),
                (PhraseGroup.Outro, 56, 65));
            SetVocalRange(atThreshold, 8, 12, 2);

            var belowThreshold = Timeline(
                65,
                (PhraseGroup.Intro, 0, 8),
                (PhraseGroup.Up, 8, 32),
                (PhraseGroup.Chorus, 32, 48),
                (PhraseGroup.Down, 48, 56),
                (PhraseGroup.Outro, 56, 65));
            SetVocalRange(belowThreshold, 8, 12, 1);

            Assert.That(Generate(atThreshold).HotCues.Select(cue => cue.Slot), Does.Contain("B"));
            Assert.That(Generate(belowThreshold).HotCues.Select(cue => cue.Slot), Does.Not.Contain("B"));
        }

        [Test]
        public void OmitsPreDropCueWhenDropHasFewerThan32PrecedingBeats()
        {
            var timeline = Timeline(
                57,
                (PhraseGroup.Intro, 0, 8),
                (PhraseGroup.Up, 8, 24),
                (PhraseGroup.Chorus, 24, 40),
                (PhraseGroup.Down, 40, 48),
                (PhraseGroup.Outro, 48, 57));
            SetVocalRange(timeline, 8, 12, 2);

            var proposal = Generate(timeline);

            Assert.That(proposal.HotCues.Select(cue => cue.Slot), Does.Not.Contain("C"));
            Assert.That(proposal.Warnings, Has.Some.Contains("fewer than 32 preceding beats"));
        }

        [Test]
        public void LeavesMissingRolesUnassignedInsteadOfInventingCues()
        {
            var proposal = Generate(Timeline(
                49,
                (PhraseGroup.Intro, 0, 16),
                (PhraseGroup.Verse, 16, 32),
                (PhraseGroup.Bridge, 32, 49)));

            Assert.That(proposal.Complete, Is.False);
            Assert.That(proposal.HotCues.Select(cue => cue.Slot), Is.EqualTo(new[] { "A" }));
            Assert.That(proposal.Warnings, Has.Some.Contains("D missing"));
            Assert.That(proposal.Warnings, Has.Some.Contains("H missing"));
        }

        private static WorkflowHotCueProposal Generate(WorkflowPhraseTimeline timeline)
        {
            return new WorkflowHotCueRuleEngine().Generate(timeline, WorkflowTaxonomy.LoadDefault());
        }

        private static WorkflowPhraseTimeline Timeline(
            int beatCount,
            params (PhraseGroup Group, int Start, int End)[] phrases)
        {
            return new WorkflowPhraseTimeline
            {
                Beats = Enumerable.Range(0, beatCount)
                    .Select(index => new WorkflowBeatPoint
                    {
                        Index = index,
                        TimeMs = index * 500,
                        BeatNumber = index % 4 + 1
                    })
                    .ToList(),
                Phrases = phrases.Select((phrase, index) => new WorkflowPhraseSpan
                {
                    PhraseNumber = index + 1,
                    Group = phrase.Group,
                    StartBeatIndex = phrase.Start,
                    EndBeatIndexExclusive = phrase.End
                }).ToList(),
                VocalWaveformHeights = Enumerable.Repeat(
                    (byte)0,
                    beatCount * 500 * WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000)
                    .ToList()
            };
        }

        private static void SetVocalRange(
            WorkflowPhraseTimeline timeline,
            int startBeatIndex,
            int endBeatIndexExclusive,
            byte height)
        {
            var start = timeline.GetTimeMs(startBeatIndex) *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            var end = endBeatIndexExclusive < timeline.Beats.Count
                ? timeline.GetTimeMs(endBeatIndexExclusive) *
                    WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000
                : timeline.VocalWaveformHeights.Count;
            for (var index = start; index < end; index++)
                timeline.VocalWaveformHeights[index] = height;
        }
    }
}
