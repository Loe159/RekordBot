using CueGen.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CueGen.Workflow
{
    public sealed class WorkflowMemoryCueRuleEngine
    {
        public const int MaximumMemoryCues = 10;
        public const int SeamWindowMilliseconds = 100;
        public const int SafetyLoopSearchBeats = 128;
        public const double MaximumSeamMeanAbsoluteDifference = 2.0;
        public const double MinimumLoopMeanWaveformHeight = 2.0;
        public const string ManualVocalName = "VOCAL MANUEL";
        public const string SafetyLoopName = "FIN";

        private static readonly IReadOnlyDictionary<PhraseGroup, string> Abbreviations =
            new Dictionary<PhraseGroup, string>
            {
                [PhraseGroup.Intro] = "IN",
                [PhraseGroup.Verse] = "VE",
                [PhraseGroup.Bridge] = "BR",
                [PhraseGroup.Chorus] = "CH",
                [PhraseGroup.Up] = "BU",
                [PhraseGroup.Down] = "BD",
                [PhraseGroup.Outro] = "OUT"
            };

        public WorkflowMemoryCueProposal Generate(
            WorkflowPhraseTimeline timeline,
            IList<WorkflowMemoryCueState> existingCues,
            int? trackLengthMs = null)
        {
            if (timeline == null)
                throw new ArgumentNullException(nameof(timeline));
            if (existingCues == null)
                throw new ArgumentNullException(nameof(existingCues));
            if (timeline.Beats == null || timeline.Beats.Count < 5)
                throw new InvalidOperationException("At least five beatgrid points are required for the FIN loop");
            if (timeline.Phrases == null || timeline.Phrases.Count == 0)
                throw new InvalidOperationException("Phrase analysis is required to generate Memory Cues");

            var vocalCues = existingCues
                .Where(cue => string.Equals(cue.Name, ManualVocalName, StringComparison.Ordinal))
                .ToList();
            if (vocalCues.Count > 1)
                throw new InvalidOperationException($"Multiple Memory Cues named '{ManualVocalName}' exist");

            var unmanaged = existingCues.Where(cue => !cue.Managed).ToList();
            var unmanagedVocal = vocalCues.SingleOrDefault()?.Managed == false;
            var safetyLoop = FindSafetyLoop(timeline, trackLengthMs);
            var reservedCount = unmanaged.Count + (unmanagedVocal ? 0 : 1) + (safetyLoop == null ? 0 : 1);
            if (reservedCount > MaximumMemoryCues)
            {
                throw new InvalidOperationException(
                    "Manual Memory Cues leave no room for the generated Memory Cues");
            }

            var proposal = new WorkflowMemoryCueProposal();
            if (safetyLoop == null)
            {
                proposal.Warnings.Add(
                    "FIN missing: no seamless four-beat downbeat loop with audible content was found near track end");
            }
            var vocal = vocalCues.SingleOrDefault();
            proposal.MemoryCues.Add(new WorkflowMemoryCue
            {
                Name = ManualVocalName,
                PositionMs = vocal?.PositionMs ?? timeline.GetTimeMs(0),
                BeatIndex = vocal == null ? 0 : FindNearestBeat(timeline, vocal.PositionMs)
            });

            var manualPositions = new HashSet<int>(unmanaged.Select(cue => cue.PositionMs));
            if (vocal != null)
                manualPositions.Add(vocal.PositionMs);

            var candidates = BuildCandidates(timeline)
                .Where(cue => !manualPositions.Contains(cue.PositionMs))
                .Where(cue => safetyLoop == null || cue.PositionMs < safetyLoop.StartMs)
                .OrderBy(cue => cue.DistanceBeats)
                .ThenBy(cue => cue.PositionMs)
                .Take(MaximumMemoryCues - reservedCount)
                .ToList();
            foreach (var candidate in candidates.OrderBy(cue => cue.PositionMs))
                proposal.MemoryCues.Add(candidate);

            if (safetyLoop != null)
            {
                proposal.MemoryCues.Add(new WorkflowMemoryCue
                {
                    Name = SafetyLoopName,
                    PositionMs = safetyLoop.StartMs,
                    BeatIndex = safetyLoop.StartBeatIndex,
                    LoopBeats = 4,
                    LoopEndMs = safetyLoop.EndMs
                });
            }
            proposal.MemoryCues = proposal.MemoryCues
                .OrderBy(cue => cue.PositionMs)
                .ThenBy(cue => cue.Name, StringComparer.Ordinal)
                .ToList();
            return proposal;
        }

        private static SafetyLoopCandidate FindSafetyLoop(
            WorkflowPhraseTimeline timeline,
            int? trackLengthMs)
        {
            var lastBeatIndex = timeline.Beats.Count - 1;
            if (trackLengthMs.HasValue)
            {
                while (lastBeatIndex >= 0 && timeline.Beats[lastBeatIndex].TimeMs > trackLengthMs.Value)
                    lastBeatIndex--;
            }
            if (lastBeatIndex < 4)
                throw new InvalidOperationException("The beatgrid has no complete four-beat span inside the track duration");
            if (timeline.WaveformHeights == null || timeline.WaveformHeights.Count == 0)
                return null;

            SafetyLoopCandidate best = null;
            var earliestEndBeatIndex = Math.Max(
                4,
                lastBeatIndex - SafetyLoopSearchBeats + 4);
            for (var endBeatIndex = lastBeatIndex; endBeatIndex >= earliestEndBeatIndex; endBeatIndex--)
            {
                var startBeatIndex = endBeatIndex - 4;
                if (timeline.Beats[startBeatIndex].BeatNumber != 1 ||
                    timeline.Beats[endBeatIndex].BeatNumber != 1)
                {
                    continue;
                }

                var meanHeight = CalculateLoopMeanWaveformHeight(
                    timeline,
                    startBeatIndex,
                    endBeatIndex);
                if (!meanHeight.HasValue || meanHeight.Value < MinimumLoopMeanWaveformHeight)
                    continue;

                var score = CalculateSeamScore(timeline, startBeatIndex, endBeatIndex);
                if (!score.HasValue || score.Value > MaximumSeamMeanAbsoluteDifference)
                    continue;
                if (best != null && score.Value >= best.Score)
                    continue;

                best = new SafetyLoopCandidate
                {
                    StartBeatIndex = startBeatIndex,
                    StartMs = timeline.GetTimeMs(startBeatIndex),
                    EndMs = timeline.GetTimeMs(endBeatIndex),
                    Score = score.Value
                };
            }

            return best;
        }

        private static double? CalculateLoopMeanWaveformHeight(
            WorkflowPhraseTimeline timeline,
            int startBeatIndex,
            int endBeatIndex)
        {
            var start = timeline.GetTimeMs(startBeatIndex) *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            var end = timeline.GetTimeMs(endBeatIndex) *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            if (start < 0 || end <= start || end > timeline.WaveformHeights.Count)
                return null;

            long height = 0;
            for (var index = start; index < end; index++)
                height += timeline.WaveformHeights[index];
            return height / (double)(end - start);
        }

        private static double? CalculateSeamScore(
            WorkflowPhraseTimeline timeline,
            int startBeatIndex,
            int endBeatIndex)
        {
            var startCenter = timeline.GetTimeMs(startBeatIndex) *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            var endCenter = timeline.GetTimeMs(endBeatIndex) *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            var radius = SeamWindowMilliseconds *
                WorkflowPhraseTimeline.WaveformPointsPerSecond / 1000;
            if (startCenter - radius < 0 ||
                endCenter + radius >= timeline.WaveformHeights.Count)
            {
                return null;
            }

            long difference = 0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                difference += Math.Abs(
                    timeline.WaveformHeights[startCenter + offset] -
                    timeline.WaveformHeights[endCenter + offset]);
            }

            return difference / (double)(radius * 2 + 1);
        }

        private sealed class SafetyLoopCandidate
        {
            public int StartBeatIndex { get; set; }
            public int StartMs { get; set; }
            public int EndMs { get; set; }
            public double Score { get; set; }
        }

        private static IEnumerable<WorkflowMemoryCue> BuildCandidates(WorkflowPhraseTimeline timeline)
        {
            var phrases = timeline.Phrases
                .OrderBy(phrase => phrase.StartBeatIndex)
                .ThenBy(phrase => phrase.PhraseNumber)
                .ToList();
            for (var index = 0; index < phrases.Count;)
            {
                var first = phrases[index];
                if (!Abbreviations.TryGetValue(first.Group, out var abbreviation))
                {
                    index++;
                    continue;
                }

                var endIndex = index + 1;
                while (endIndex < phrases.Count && phrases[endIndex].Group == first.Group)
                    endIndex++;

                var blockEndBeat = phrases[endIndex - 1].EndBeatIndexExclusive;
                for (var distance = 32; blockEndBeat - distance > first.StartBeatIndex; distance += 32)
                {
                    var beatIndex = blockEndBeat - distance;
                    if (beatIndex < 0 || beatIndex >= timeline.Beats.Count)
                        continue;
                    yield return new WorkflowMemoryCue
                    {
                        Name = $"{abbreviation}-{distance}",
                        PositionMs = timeline.GetTimeMs(beatIndex),
                        BeatIndex = beatIndex,
                        DistanceBeats = distance
                    };
                }

                index = endIndex;
            }
        }

        private static int FindNearestBeat(WorkflowPhraseTimeline timeline, int positionMs)
        {
            return timeline.Beats
                .OrderBy(beat => Math.Abs((long)beat.TimeMs - positionMs))
                .ThenBy(beat => beat.Index)
                .First()
                .Index;
        }
    }
}
