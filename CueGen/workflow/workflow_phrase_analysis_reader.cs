using CueGen.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CueGen.Workflow
{
    public sealed class WorkflowPhraseAnalysisReader
    {
        public WorkflowPhraseTimeline Read(Content content, Config config)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var ext = content.GetAnlz(AnalysisKind.Ext, config);
            var phraseSection = ext?.Sections?
                .Select(section => section.Content)
                .OfType<PhraseSection>()
                .SingleOrDefault();
            if (phraseSection?.Phrases == null || phraseSection.Phrases.Count == 0)
                throw new InvalidOperationException("The track has no readable Rekordbox phrase analysis");

            var sourceBeats = content.GetBeats(config);
            if (sourceBeats == null || sourceBeats.Count == 0)
                throw new InvalidOperationException("The track has no readable Rekordbox beatgrid");

            var beats = sourceBeats.Select((beat, index) => new WorkflowBeatPoint
            {
                Index = index,
                TimeMs = checked((int)beat.Time),
                BeatNumber = beat.BeatNumber
            }).ToList();
            for (var index = 1; index < beats.Count; index++)
            {
                if (beats[index].TimeMs <= beats[index - 1].TimeMs)
                    throw new InvalidOperationException("The Rekordbox beatgrid is not strictly monotone");
            }

            var ordered = phraseSection.Phrases
                .OrderBy(phrase => phrase.Beat)
                .ThenBy(phrase => phrase.PhraseNumber)
                .ToList();
            var phrases = new List<WorkflowPhraseSpan>();
            for (var index = 0; index < ordered.Count; index++)
            {
                var phrase = ordered[index];
                if (phrase.Kind == null || phrase.Beat == 0)
                    throw new InvalidOperationException("The Rekordbox phrase analysis contains an invalid phrase");

                var startBeatIndex = phrase.Beat - 1;
                var endBeatIndex = index + 1 < ordered.Count
                    ? ordered[index + 1].Beat - 1
                    : Math.Min((int)phraseSection.EndBeat, beats.Count);
                if (startBeatIndex < 0 || startBeatIndex >= beats.Count ||
                    endBeatIndex <= startBeatIndex || endBeatIndex > beats.Count)
                {
                    throw new InvalidOperationException(
                        $"Phrase {phrase.PhraseNumber} is outside the Rekordbox beatgrid");
                }

                phrases.Add(new WorkflowPhraseSpan
                {
                    PhraseNumber = phrase.PhraseNumber,
                    Group = phrase.Kind.Group,
                    StartBeatIndex = startBeatIndex,
                    EndBeatIndexExclusive = endBeatIndex
                });
            }

            return new WorkflowPhraseTimeline
            {
                Beats = beats,
                Phrases = phrases,
                WaveformHeights = ReadWaveformHeights(ext)
            };
        }

        public IList<byte> ReadWaveformHeights(Content content, Config config)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            return ReadWaveformHeights(content.GetAnlz(AnalysisKind.Ext, config));
        }

        private static IList<byte> ReadWaveformHeights(Anlz analysis)
        {
            var waveformSection = analysis?.Sections?
                .Select(section => section.Content)
                .OfType<WaveformDetailSection>()
                .FirstOrDefault();
            if (waveformSection?.Data == null || waveformSection.LenEntryBytes != 1)
                return new List<byte>();

            var entryCount = Math.Min((long)waveformSection.LenEntries, waveformSection.Data.LongLength);
            return waveformSection.Data
                .Take(checked((int)entryCount))
                .Select(value => (byte)(value & 0x0f))
                .ToList();
        }
    }
}
