using CueGen.Analysis;
using CueGen.Workflow;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace CueGen.Test
{
    [TestFixture]
    public class WorkflowMemoryCueRuleEngineTests
    {
        [Test]
        public void MergesConsecutivePhraseGroupsAndCountsBackwardFromTheirEnd()
        {
            var timeline = Timeline(
                181,
                (PhraseGroup.Intro, 0, 32),
                (PhraseGroup.Intro, 32, 96),
                (PhraseGroup.Chorus, 96, 176));

            var proposal = Generate(timeline);

            Assert.That(Cue(proposal, "IN-64").BeatIndex, Is.EqualTo(32));
            Assert.That(Cue(proposal, "IN-32").BeatIndex, Is.EqualTo(64));
            Assert.That(Cue(proposal, "CH-64").BeatIndex, Is.EqualTo(112));
            Assert.That(Cue(proposal, "CH-32").BeatIndex, Is.EqualTo(144));
            Assert.That(proposal.MemoryCues.Any(cue => cue.BeatIndex == 96), Is.False);
        }

        [Test]
        public void UsesConfirmedAbbreviationsForEveryPhraseGroup()
        {
            var groups = new[]
            {
                PhraseGroup.Intro,
                PhraseGroup.Verse,
                PhraseGroup.Bridge,
                PhraseGroup.Chorus,
                PhraseGroup.Up,
                PhraseGroup.Down,
                PhraseGroup.Outro
            };
            var phrases = groups
                .Select((group, index) => (group, index * 64, (index + 1) * 64))
                .ToArray();

            var names = Generate(Timeline(453, phrases)).MemoryCues.Select(cue => cue.Name).ToList();

            Assert.That(names, Does.Contain("IN-32"));
            Assert.That(names, Does.Contain("VE-32"));
            Assert.That(names, Does.Contain("BR-32"));
            Assert.That(names, Does.Contain("CH-32"));
            Assert.That(names, Does.Contain("BU-32"));
            Assert.That(names, Does.Contain("BD-32"));
            Assert.That(names, Does.Contain("OUT-32"));
        }

        [Test]
        public void PrioritizesDistanceThenChronologyWithinTenCueLimit()
        {
            var timeline = Timeline(
                485,
                (PhraseGroup.Intro, 0, 96),
                (PhraseGroup.Verse, 96, 192),
                (PhraseGroup.Bridge, 192, 288),
                (PhraseGroup.Chorus, 288, 384),
                (PhraseGroup.Outro, 384, 480));

            var proposal = Generate(timeline);
            var generated = proposal.MemoryCues
                .Where(cue => cue.DistanceBeats.HasValue)
                .OrderBy(cue => cue.DistanceBeats)
                .ThenBy(cue => cue.PositionMs)
                .ToList();

            Assert.That(proposal.MemoryCues, Has.Count.EqualTo(10));
            Assert.That(generated.Count(cue => cue.DistanceBeats == 32), Is.EqualTo(5));
            Assert.That(generated.Count(cue => cue.DistanceBeats == 64), Is.EqualTo(3));
            Assert.That(generated.Where(cue => cue.DistanceBeats == 64).Select(cue => cue.Name),
                Is.EqualTo(new[] { "IN-64", "VE-64", "BR-64" }));
        }

        [Test]
        public void PreservesVocalCueAndTreatsItsAutomaticPositionAsCovered()
        {
            var timeline = Timeline(101, (PhraseGroup.Intro, 0, 96));
            var vocalPosition = timeline.GetTimeMs(64);

            var proposal = Generate(timeline, new WorkflowMemoryCueState
            {
                Name = WorkflowMemoryCueRuleEngine.ManualVocalName,
                PositionMs = vocalPosition,
                Managed = true
            });

            Assert.That(Cue(proposal, WorkflowMemoryCueRuleEngine.ManualVocalName).PositionMs, Is.EqualTo(vocalPosition));
            Assert.That(proposal.MemoryCues.Any(cue => cue.Name == "IN-32"), Is.False);
            Assert.That(proposal.MemoryCues.Any(cue => cue.Name == "IN-64"), Is.True);
        }

        [Test]
        public void CreatesFourBeatSafetyLoopOnLastCompleteBeatgridSpan()
        {
            var timeline = Timeline(101, (PhraseGroup.Outro, 0, 96));

            var safety = Cue(Generate(timeline), WorkflowMemoryCueRuleEngine.SafetyLoopName);

            Assert.That(safety.BeatIndex, Is.EqualTo(96));
            Assert.That(safety.LoopBeats, Is.EqualTo(4));
            Assert.That(safety.LoopEndMs, Is.EqualTo(timeline.GetTimeMs(100)));
        }

        [Test]
        public void KeepsSafetyLoopInsideDeclaredTrackDuration()
        {
            var timeline = Timeline(101, (PhraseGroup.Outro, 0, 100));

            var proposal = new WorkflowMemoryCueRuleEngine().Generate(
                timeline,
                new List<WorkflowMemoryCueState>(),
                trackLengthMs: 49000);
            var safety = Cue(proposal, WorkflowMemoryCueRuleEngine.SafetyLoopName);

            Assert.That(safety.BeatIndex, Is.EqualTo(92));
            Assert.That(safety.LoopEndMs, Is.EqualTo(48000));
            Assert.That(proposal.MemoryCues.Last().Name, Is.EqualTo(WorkflowMemoryCueRuleEngine.SafetyLoopName));
        }

        [Test]
        public void ChoosesCleanestFourBeatDownbeatLoopNearTrackEnd()
        {
            var timeline = Timeline(25, (PhraseGroup.Outro, 0, 24));
            SetBoundaryWindow(timeline, beatIndex: 24, height: 15);

            var safety = Cue(Generate(timeline), WorkflowMemoryCueRuleEngine.SafetyLoopName);

            Assert.That(safety.BeatIndex, Is.EqualTo(16));
            Assert.That(timeline.Beats[safety.BeatIndex].BeatNumber, Is.EqualTo(1));
            Assert.That(safety.LoopEndMs, Is.EqualTo(timeline.GetTimeMs(20)));
            Assert.That(timeline.Beats[20].BeatNumber, Is.EqualTo(1));
        }

        [Test]
        public void OmitsSafetyLoopWhenNoSeamPassesThreshold()
        {
            var timeline = Timeline(25, (PhraseGroup.Outro, 0, 24));
            for (var beatIndex = 0; beatIndex < timeline.Beats.Count; beatIndex += 4)
            {
                SetBoundaryWindow(
                    timeline,
                    beatIndex,
                    (byte)((beatIndex / 4) % 2 == 0 ? 0 : 15));
            }

            var proposal = Generate(timeline);

            Assert.That(
                proposal.MemoryCues.Any(cue => cue.Name == WorkflowMemoryCueRuleEngine.SafetyLoopName),
                Is.False);
            Assert.That(proposal.Warnings, Has.Some.Contains("no seamless four-beat downbeat loop"));
        }

        [Test]
        public void OmitsSafetyLoopWhenMatchingSeamContainsOnlySilence()
        {
            var timeline = Timeline(25, (PhraseGroup.Outro, 0, 24));
            for (var index = 0; index < timeline.WaveformHeights.Count; index++)
                timeline.WaveformHeights[index] = 0;

            var proposal = Generate(timeline);

            Assert.That(
                proposal.MemoryCues.Any(cue => cue.Name == WorkflowMemoryCueRuleEngine.SafetyLoopName),
                Is.False);
            Assert.That(proposal.Warnings, Has.Some.Contains("audible content"));
        }

        [Test]
        public void SkipsSilentFinalLoopAndUsesEarlierAudibleSeam()
        {
            var timeline = Timeline(25, (PhraseGroup.Outro, 0, 24));
            SetWaveformRange(timeline, startBeatIndex: 20, endBeatIndex: 24, height: 0);

            var safety = Cue(Generate(timeline), WorkflowMemoryCueRuleEngine.SafetyLoopName);

            Assert.That(safety.BeatIndex, Is.EqualTo(12));
            Assert.That(safety.LoopEndMs, Is.EqualTo(timeline.GetTimeMs(16)));
        }

        [Test]
        public void FindsAudibleSeamWithinFinalOneHundredTwentyEightBeats()
        {
            var timeline = Timeline(145, (PhraseGroup.Outro, 0, 144));
            for (var index = 0; index < timeline.WaveformHeights.Count; index++)
                timeline.WaveformHeights[index] = 0;
            SetWaveformRange(timeline, startBeatIndex: 20, endBeatIndex: 24, height: 8);

            var safety = Cue(Generate(timeline), WorkflowMemoryCueRuleEngine.SafetyLoopName);

            Assert.That(safety.BeatIndex, Is.EqualTo(20));
            Assert.That(safety.LoopEndMs, Is.EqualTo(timeline.GetTimeMs(24)));
        }

        [Test]
        public void DoesNotSelectCleanSeamOutsideFinalSearchWindow()
        {
            var timeline = Timeline(161, (PhraseGroup.Outro, 0, 160));
            for (var index = 0; index < timeline.WaveformHeights.Count; index++)
                timeline.WaveformHeights[index] = 0;
            SetWaveformRange(timeline, startBeatIndex: 28, endBeatIndex: 32, height: 8);

            var proposal = Generate(timeline);

            Assert.That(
                proposal.MemoryCues.Any(cue => cue.Name == WorkflowMemoryCueRuleEngine.SafetyLoopName),
                Is.False);
        }

        private static WorkflowMemoryCueProposal Generate(
            WorkflowPhraseTimeline timeline,
            params WorkflowMemoryCueState[] existing)
        {
            return new WorkflowMemoryCueRuleEngine().Generate(timeline, existing.ToList());
        }

        private static WorkflowMemoryCue Cue(WorkflowMemoryCueProposal proposal, string name)
        {
            return proposal.MemoryCues.Single(cue => cue.Name == name);
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
                WaveformHeights = Enumerable.Repeat(
                    (byte)8,
                    (beatCount * 500 + 500) * WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000)
                    .ToList()
            };
        }

        private static void SetBoundaryWindow(
            WorkflowPhraseTimeline timeline,
            int beatIndex,
            byte height)
        {
            var center = timeline.GetTimeMs(beatIndex) *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            var radius = WorkflowMemoryCueRuleEngine.SeamWindowMilliseconds *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            for (var index = center - radius; index <= center + radius; index++)
            {
                if (index >= 0 && index < timeline.WaveformHeights.Count)
                    timeline.WaveformHeights[index] = height;
            }
        }

        private static void SetWaveformRange(
            WorkflowPhraseTimeline timeline,
            int startBeatIndex,
            int endBeatIndex,
            byte height)
        {
            var radius = WorkflowMemoryCueRuleEngine.SeamWindowMilliseconds *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            var start = timeline.GetTimeMs(startBeatIndex) *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000 - radius;
            var end = timeline.GetTimeMs(endBeatIndex) *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000 + radius;
            for (var index = start; index <= end; index++)
            {
                if (index >= 0 && index < timeline.WaveformHeights.Count)
                    timeline.WaveformHeights[index] = height;
            }
        }
    }
}
