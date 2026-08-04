using CueGen.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CueGen.Workflow
{
    public sealed class WorkflowHotCueRuleEngine
    {
        public const double MinimumVocalMeanWaveformHeight = 2.0;
        public const int MinimumVocalSectionBeats = 4;
        public WorkflowHotCueProposal Generate(
            WorkflowPhraseTimeline timeline,
            WorkflowTaxonomy taxonomy)
        {
            if (timeline == null)
                throw new ArgumentNullException(nameof(timeline));
            if (taxonomy == null)
                throw new ArgumentNullException(nameof(taxonomy));
            if (timeline.Beats == null || timeline.Beats.Count == 0)
                throw new InvalidOperationException("A beatgrid is required to generate Hot Cues");
            if (timeline.Phrases == null || timeline.Phrases.Count == 0)
                throw new InvalidOperationException("Phrase analysis is required to generate Hot Cues");

            var phrases = timeline.Phrases
                .OrderBy(phrase => phrase.StartBeatIndex)
                .ThenBy(phrase => phrase.PhraseNumber)
                .ToList();
            ValidateTimeline(timeline, phrases);

            var proposal = new WorkflowHotCueProposal();
            AddPhraseCue(proposal, taxonomy, timeline, "A", phrases[0], "first_phrase");
            AddFirstVocalCue(proposal, taxonomy, timeline);

            var dropIndex = FindDropIndex(phrases);
            if (dropIndex < 0)
            {
                proposal.Warnings.Add("D missing: no Chorus phrase was found");
                proposal.Warnings.Add("C missing: no primary drop was found");
                proposal.Warnings.Add("E missing: no primary drop was found");
            }
            else
            {
                var drop = phrases[dropIndex];
                AddPhraseCue(proposal, taxonomy, timeline, "D", drop, "first_up_chorus_or_first_chorus");
                AddPreDropCue(proposal, taxonomy, timeline, drop);

                var breakdown = phrases
                    .Skip(dropIndex + 1)
                    .FirstOrDefault(phrase => phrase.Group == PhraseGroup.Down) ??
                    phrases
                        .Skip(dropIndex + 1)
                        .FirstOrDefault(phrase => phrase.Group == PhraseGroup.Bridge);
                if (breakdown == null)
                    proposal.Warnings.Add("E missing: no Down or Bridge phrase was found after the drop");
                else
                    AddPhraseCue(
                        proposal,
                        taxonomy,
                        timeline,
                        "E",
                        breakdown,
                        breakdown.Group == PhraseGroup.Down ? "first_down_after_drop" : "first_bridge_after_drop");
            }

            AddOutroLoop(proposal, taxonomy, timeline, phrases);

            proposal.HotCues = proposal.HotCues
                .OrderBy(cue => cue.Slot, StringComparer.Ordinal)
                .ToList();
            var present = new HashSet<string>(proposal.HotCues.Select(cue => cue.Slot), StringComparer.Ordinal);
            proposal.Complete = taxonomy.HotCues
                .Where(pair => pair.Value.Required)
                .All(pair => present.Contains(pair.Key));
            return proposal;
        }

        private static void ValidateTimeline(
            WorkflowPhraseTimeline timeline,
            IList<WorkflowPhraseSpan> phrases)
        {
            var previousStart = -1;
            foreach (var phrase in phrases)
            {
                if (phrase.StartBeatIndex <= previousStart ||
                    phrase.StartBeatIndex < 0 ||
                    phrase.EndBeatIndexExclusive <= phrase.StartBeatIndex ||
                    phrase.EndBeatIndexExclusive > timeline.Beats.Count)
                {
                    throw new InvalidOperationException("The phrase timeline is invalid or non-monotone");
                }
                previousStart = phrase.StartBeatIndex;
            }
        }

        private static int FindDropIndex(IList<WorkflowPhraseSpan> phrases)
        {
            for (var index = 1; index < phrases.Count; index++)
            {
                if (phrases[index].Group == PhraseGroup.Chorus &&
                    phrases[index - 1].Group == PhraseGroup.Up)
                {
                    return index;
                }
            }
            for (var index = 0; index < phrases.Count; index++)
            {
                if (phrases[index].Group == PhraseGroup.Chorus)
                    return index;
            }
            return -1;
        }

        private static void AddPreDropCue(
            WorkflowHotCueProposal proposal,
            WorkflowTaxonomy taxonomy,
            WorkflowPhraseTimeline timeline,
            WorkflowPhraseSpan drop)
        {
            var beatIndex = drop.StartBeatIndex - 32;
            if (beatIndex < 0)
            {
                proposal.Warnings.Add("C missing: the primary drop has fewer than 32 preceding beats");
                return;
            }

            var mapping = GetMapping(taxonomy, "C");
            proposal.HotCues.Add(new WorkflowHotCue
            {
                Slot = "C",
                Name = mapping.Name,
                Color = mapping.Color,
                PositionMs = timeline.GetTimeMs(beatIndex),
                PhraseStartVerified = timeline.Phrases.Any(phrase => phrase.StartBeatIndex == beatIndex),
                DropOffsetBeats = 32
            });
            proposal.Evidence.Add(new WorkflowHotCueEvidence
            {
                Slot = "C",
                BeatIndex = beatIndex,
                PhraseNumber = null,
                Rule = "drop_minus_32"
            });
        }

        private static void AddFirstVocalCue(
            WorkflowHotCueProposal proposal,
            WorkflowTaxonomy taxonomy,
            WorkflowPhraseTimeline timeline)
        {
            var beatIndex = FindFirstVocalSectionBeat(timeline);
            if (!beatIndex.HasValue)
            {
                var reason = timeline.VocalWaveformHeights == null || timeline.VocalWaveformHeights.Count == 0
                    ? "no vocal stem waveform analysis was provided"
                    : "no audible vocal section lasts four consecutive beats";
                proposal.Warnings.Add($"B missing: {reason}");
                return;
            }

            var mapping = GetMapping(taxonomy, "B");
            proposal.HotCues.Add(new WorkflowHotCue
            {
                Slot = "B",
                Name = mapping.Name,
                Color = mapping.Color,
                PositionMs = timeline.GetTimeMs(beatIndex.Value),
                PhraseStartVerified = timeline.Phrases.Any(phrase => phrase.StartBeatIndex == beatIndex.Value),
                VocalSectionVerified = true
            });
            proposal.Evidence.Add(new WorkflowHotCueEvidence
            {
                Slot = "B",
                BeatIndex = beatIndex.Value,
                PhraseNumber = timeline.Phrases
                    .FirstOrDefault(phrase =>
                        phrase.StartBeatIndex <= beatIndex.Value &&
                        phrase.EndBeatIndexExclusive > beatIndex.Value)?.PhraseNumber,
                Rule = "first_vocal_section_4_beats"
            });
        }

        private static int? FindFirstVocalSectionBeat(WorkflowPhraseTimeline timeline)
        {
            if (timeline.VocalWaveformHeights == null || timeline.VocalWaveformHeights.Count == 0)
                return null;

            var runStart = -1;
            var runLength = 0;
            for (var beatIndex = 0; beatIndex < timeline.Beats.Count; beatIndex++)
            {
                if (GetVocalMeanHeight(timeline, beatIndex) >= MinimumVocalMeanWaveformHeight)
                {
                    if (runLength == 0)
                        runStart = beatIndex;
                    runLength++;
                    if (runLength >= MinimumVocalSectionBeats)
                        return runStart;
                }
                else
                {
                    runStart = -1;
                    runLength = 0;
                }
            }
            return null;
        }

        private static double GetVocalMeanHeight(WorkflowPhraseTimeline timeline, int beatIndex)
        {
            var start = (long)timeline.GetTimeMs(beatIndex) * WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            var end = beatIndex + 1 < timeline.Beats.Count
                ? (long)timeline.GetTimeMs(beatIndex + 1) * WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000
                : timeline.VocalWaveformHeights.Count;
            start = Math.Max(0, Math.Min(start, timeline.VocalWaveformHeights.Count));
            end = Math.Max(start, Math.Min(end, timeline.VocalWaveformHeights.Count));
            if (end == start)
                return 0;

            long total = 0;
            for (var index = (int)start; index < (int)end; index++)
                total += timeline.VocalWaveformHeights[index];
            return total / (double)(end - start);
        }

        private static void AddOutroLoop(
            WorkflowHotCueProposal proposal,
            WorkflowTaxonomy taxonomy,
            WorkflowPhraseTimeline timeline,
            IList<WorkflowPhraseSpan> phrases)
        {
            var outro = phrases.LastOrDefault(phrase => phrase.Group == PhraseGroup.Outro);
            if (outro == null)
            {
                proposal.Warnings.Add("H missing: no Outro phrase was found");
                return;
            }

            var loopBeats = new[] { 16, 8 }.FirstOrDefault(length =>
                outro.StartBeatIndex + length <= outro.EndBeatIndexExclusive &&
                outro.StartBeatIndex + length < timeline.Beats.Count);
            if (loopBeats == 0)
            {
                proposal.Warnings.Add("H missing: the final Outro cannot contain an 8-beat loop");
                return;
            }

            var mapping = GetMapping(taxonomy, "H");
            proposal.HotCues.Add(new WorkflowHotCue
            {
                Slot = "H",
                Name = mapping.Name,
                Color = mapping.Color,
                PositionMs = timeline.GetTimeMs(outro.StartBeatIndex),
                PhraseStartVerified = true,
                LoopBeats = loopBeats,
                LoopEndMs = timeline.GetTimeMs(outro.StartBeatIndex + loopBeats)
            });
            proposal.Evidence.Add(new WorkflowHotCueEvidence
            {
                Slot = "H",
                BeatIndex = outro.StartBeatIndex,
                PhraseNumber = outro.PhraseNumber,
                Rule = $"last_outro_loop_{loopBeats}"
            });
        }

        private static void AddPhraseCue(
            WorkflowHotCueProposal proposal,
            WorkflowTaxonomy taxonomy,
            WorkflowPhraseTimeline timeline,
            string slot,
            WorkflowPhraseSpan phrase,
            string rule)
        {
            var mapping = GetMapping(taxonomy, slot);
            proposal.HotCues.Add(new WorkflowHotCue
            {
                Slot = slot,
                Name = mapping.Name,
                Color = mapping.Color,
                PositionMs = timeline.GetTimeMs(phrase.StartBeatIndex),
                PhraseStartVerified = true
            });
            proposal.Evidence.Add(new WorkflowHotCueEvidence
            {
                Slot = slot,
                BeatIndex = phrase.StartBeatIndex,
                PhraseNumber = phrase.PhraseNumber,
                Rule = rule
            });
        }

        private static WorkflowHotCueMapping GetMapping(WorkflowTaxonomy taxonomy, string slot)
        {
            if (!taxonomy.HotCues.TryGetValue(slot, out var mapping))
                throw new InvalidOperationException($"The workflow taxonomy does not define Hot Cue {slot}");
            return mapping;
        }
    }
}
