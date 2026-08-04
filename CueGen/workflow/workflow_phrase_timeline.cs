using CueGen.Analysis;
using System;
using System.Collections.Generic;

namespace CueGen.Workflow
{
    public sealed class WorkflowBeatPoint
    {
        public int Index { get; set; }
        public int TimeMs { get; set; }
        public int BeatNumber { get; set; }
    }

    public sealed class WorkflowPhraseSpan
    {
        public int PhraseNumber { get; set; }
        public PhraseGroup Group { get; set; }
        public int StartBeatIndex { get; set; }
        public int EndBeatIndexExclusive { get; set; }
    }

    public sealed class WorkflowPhraseTimeline
    {
        public const int WaveformPointsPerSecond = 150;

        public IList<WorkflowBeatPoint> Beats { get; set; } = new List<WorkflowBeatPoint>();
        public IList<WorkflowPhraseSpan> Phrases { get; set; } = new List<WorkflowPhraseSpan>();
        public IList<byte> WaveformHeights { get; set; } = new List<byte>();
        public IList<byte> VocalWaveformHeights { get; set; } = new List<byte>();

        public int GetTimeMs(int beatIndex)
        {
            if (beatIndex < 0 || beatIndex >= Beats.Count)
                throw new ArgumentOutOfRangeException(nameof(beatIndex));
            return Beats[beatIndex].TimeMs;
        }
    }
}
