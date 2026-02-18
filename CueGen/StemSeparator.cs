using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;
using SQLite;

namespace CueGen
{
    /// <summary>
    /// Handles stem separation using Demucs library
    /// </summary>
    public class StemSeparator
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly string _outputDirectory;
        private readonly string _demucsCommand;

        /// <summary>
        /// Initializes a new instance of the StemSeparator class
        /// </summary>
        /// <param name="outputDirectory">Base directory where separated stems will be stored</param>
        /// <param name="demucsCommand">Path to demucs executable or command (default: "python")</param>
        public StemSeparator(string outputDirectory, string demucsCommand = "python")
        {
            _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            _demucsCommand = demucsCommand;

            if (!Directory.Exists(_outputDirectory))
            {
                Directory.CreateDirectory(_outputDirectory);
                Log.Info("Created output directory: {directory}", _outputDirectory);
            }
        }

        /// <summary>
        /// Separates audio file into vocals and instrumental stems
        /// </summary>
        /// <param name="audioFilePath">Path to the audio file to process</param>
        /// <param name="model">Demucs model to use (default: htdemucs)</param>
        /// <returns>True if separation was successful, false otherwise</returns>
        public bool SeparateStems(string audioFilePath, string model = "htdemucs")
        {
            if (!File.Exists(audioFilePath))
            {
                Log.Error("Audio file not found: {path}", audioFilePath);
                return false;
            }

            var fileName = Path.GetFileNameWithoutExtension(audioFilePath);
            var fileDirectory = Path.GetDirectoryName(audioFilePath);

            // Skip files that are already stems (ending with _instrumental or _vocal)
            if (fileName.EndsWith("_instrumental", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("_vocal", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Skipping stem file: {file}", Path.GetFileName(audioFilePath));
                return false;
            }

            var vocalsFile = Path.Combine(fileDirectory, $"{fileName}_vocal.mp3");
            var instrumentalFile = Path.Combine(fileDirectory, $"{fileName}_instrumental.mp3");

            // Check if stems already exist
            if (File.Exists(vocalsFile) && File.Exists(instrumentalFile))
            {
                Log.Info("Stems already exist for {file}, skipping separation", Path.GetFileName(audioFilePath));
                Log.Info("Vocals file: {vocals}", vocalsFile);
                Log.Info("Instrumental file: {instrumental}", instrumentalFile);

                // Copy metadata to existing stems
                Log.Info("Copying metadata to existing stems...");
                CopyMetadataToStems(audioFilePath, vocalsFile, instrumentalFile);

                return true;
            }

            Log.Info("Starting stem separation for: {file}", Path.GetFileName(audioFilePath));
            Log.Info("This may take 1-3 minutes depending on file length...");

            try
            {
                // Use CUDA if available, otherwise CPU
                var device = "cuda"; // Demucs will fallback to CPU if CUDA is not available

                // Create a temporary directory for Demucs output
                var tempOutputDir = Path.Combine(Path.GetTempPath(), "demucs_temp_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempOutputDir);
                Log.Info("Created temporary output directory: {dir}", tempOutputDir);

                Log.Info("Running Demucs with model: {model}, device: {device}", model, device);
                var processInfo = new ProcessStartInfo
                {
                    FileName = _demucsCommand,
                    Arguments = $"-m demucs -n {model} --two-stems=vocals --mp3 -d {device} -o \"{tempOutputDir}\" \"{audioFilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processInfo };

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log.Debug("Demucs: {output}", e.Data);
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // Log progress indicators at Info level
                        if (e.Data.Contains('%') || e.Data.Contains('|'))
                        {
                            Log.Info("Demucs progress: {progress}", e.Data.Trim());
                        }
                        else if (!e.Data.Contains("seconds/s"))
                        {
                            // Only log actual errors, not progress messages
                            if (e.Data.Contains("Error") || e.Data.Contains("error") ||
                                e.Data.Contains("Exception") || e.Data.Contains("Traceback"))
                            {
                                Log.Error("Demucs error: {error}", e.Data);
                            }
                            else
                            {
                                Log.Info("Demucs: {output}", e.Data);
                            }
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Log.Info("Demucs process started, waiting for completion...");
                process.WaitForExit();
                Log.Info("Demucs process completed with exit code: {code}", process.ExitCode);

                if (process.ExitCode == 0)
                {
                    // Move files from Demucs output structure to desired location
                    var demucsOutputDir = Path.Combine(tempOutputDir, model, fileName);
                    var demucsVocalsFile = Path.Combine(demucsOutputDir, "vocals.mp3");
                    var demucsNoVocalsFile = Path.Combine(demucsOutputDir, "no_vocals.mp3");

                    Log.Info("Looking for output files in: {dir}", demucsOutputDir);

                    if (File.Exists(demucsVocalsFile) && File.Exists(demucsNoVocalsFile))
                    {
                        Log.Info("Found generated stems, moving to final location...");

                        // Move and rename files
                        if (File.Exists(vocalsFile))
                        {
                            Log.Info("Deleting existing vocals file: {file}", vocalsFile);
                            File.Delete(vocalsFile);
                        }
                        File.Move(demucsVocalsFile, vocalsFile);
                        Log.Info("Moved vocals to: {file}", vocalsFile);

                        if (File.Exists(instrumentalFile))
                        {
                            Log.Info("Deleting existing instrumental file: {file}", instrumentalFile);
                            File.Delete(instrumentalFile);
                        }
                        File.Move(demucsNoVocalsFile, instrumentalFile);
                        Log.Info("Moved instrumental to: {file}", instrumentalFile);

                        // Copy metadata from original file to stems
                        Log.Info("Copying metadata from original file to stems...");
                        CopyMetadataToStems(audioFilePath, vocalsFile, instrumentalFile);

                        // Clean up temporary directory
                        Log.Info("Cleaning up temporary directory...");
                        try
                        {
                            Directory.Delete(tempOutputDir, true);
                            Log.Info("Temporary directory deleted successfully");
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(ex, "Failed to delete temporary directory: {dir}", tempOutputDir);
                        }

                        Log.Info("Stem separation completed successfully for: {file}", audioFilePath);
                        Log.Info("Vocals file: {vocals}", vocalsFile);
                        Log.Info("Instrumental file: {instrumental}", instrumentalFile);
                        return true;
                    }
                    else
                    {
                        Log.Warn("Expected output files not found in: {dir}", demucsOutputDir);
                        return false;
                    }
                }
                else
                {
                    Log.Error("Stem separation failed with exit code: {code}", process.ExitCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during stem separation for: {file}", audioFilePath);
                return false;
            }
        }

        /// <summary>
        /// Copies metadata (tags, artwork, etc.) from original file to stem files
        /// </summary>
        /// <param name="sourceAudioPath">Path to the original audio file</param>
        /// <param name="vocalsPath">Path to the vocals stem file</param>
        /// <param name="instrumentalPath">Path to the instrumental stem file</param>
        private void CopyMetadataToStems(string sourceAudioPath, string vocalsPath, string instrumentalPath)
        {
            try
            {
                // Read metadata from source file
                using var sourceFile = TagLib.File.Create(sourceAudioPath);
                var sourceTag = sourceFile.Tag;

                // Copy to vocals file
                try
                {
                    using var vocalsFile = TagLib.File.Create(vocalsPath);
                    vocalsFile.Tag.Title = sourceTag.Title != null ? $"{sourceTag.Title} (Vocal)" : null;
                    vocalsFile.Tag.Performers = sourceTag.Performers;
                    vocalsFile.Tag.AlbumArtists = sourceTag.AlbumArtists;
                    vocalsFile.Tag.Album = sourceTag.Album;
                    vocalsFile.Tag.Year = sourceTag.Year;
                    vocalsFile.Tag.Genres = sourceTag.Genres;
                    vocalsFile.Tag.Comment = sourceTag.Comment;
                    vocalsFile.Tag.Composers = sourceTag.Composers;
                    vocalsFile.Tag.Conductor = sourceTag.Conductor;
                    vocalsFile.Tag.Copyright = sourceTag.Copyright;
                    vocalsFile.Tag.Disc = sourceTag.Disc;
                    vocalsFile.Tag.DiscCount = sourceTag.DiscCount;
                    vocalsFile.Tag.Track = sourceTag.Track;
                    vocalsFile.Tag.TrackCount = sourceTag.TrackCount;
                    vocalsFile.Tag.BeatsPerMinute = sourceTag.BeatsPerMinute;
                    vocalsFile.Tag.InitialKey = sourceTag.InitialKey;

                    // Copy artwork
                    if (sourceTag.Pictures != null && sourceTag.Pictures.Length > 0)
                    {
                        vocalsFile.Tag.Pictures = sourceTag.Pictures;
                    }

                    vocalsFile.Save();
                    Log.Info("Copied metadata to vocals file");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to copy metadata to vocals file: {path}", vocalsPath);
                }

                // Copy to instrumental file
                try
                {
                    using var instrumentalFile = TagLib.File.Create(instrumentalPath);
                    instrumentalFile.Tag.Title = sourceTag.Title != null ? $"{sourceTag.Title} (Instrumental)" : null;
                    instrumentalFile.Tag.Performers = sourceTag.Performers;
                    instrumentalFile.Tag.AlbumArtists = sourceTag.AlbumArtists;
                    instrumentalFile.Tag.Album = sourceTag.Album;
                    instrumentalFile.Tag.Year = sourceTag.Year;
                    instrumentalFile.Tag.Genres = sourceTag.Genres;
                    instrumentalFile.Tag.Comment = sourceTag.Comment;
                    instrumentalFile.Tag.Composers = sourceTag.Composers;
                    instrumentalFile.Tag.Conductor = sourceTag.Conductor;
                    instrumentalFile.Tag.Copyright = sourceTag.Copyright;
                    instrumentalFile.Tag.Disc = sourceTag.Disc;
                    instrumentalFile.Tag.DiscCount = sourceTag.DiscCount;
                    instrumentalFile.Tag.Track = sourceTag.Track;
                    instrumentalFile.Tag.TrackCount = sourceTag.TrackCount;
                    instrumentalFile.Tag.BeatsPerMinute = sourceTag.BeatsPerMinute;
                    // instrumentalFile.Tag.InitialKey = sourceTag.InitialKey;

                    // Copy artwork
                    if (sourceTag.Pictures != null && sourceTag.Pictures.Length > 0)
                    {
                        instrumentalFile.Tag.Pictures = sourceTag.Pictures;
                    }

                    instrumentalFile.Save();
                    Log.Info("Copied metadata to instrumental file");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to copy metadata to instrumental file: {path}", instrumentalPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read metadata from source file: {path}", sourceAudioPath);
            }
        }

        /// <summary>
        /// Copies metadata from source file to existing stems without regenerating them
        /// </summary>
        /// <param name="audioFilePath">Path to the original audio file</param>
        /// <param name="model">Demucs model used (default: htdemucs)</param>
        /// <returns>True if metadata was copied successfully, false otherwise</returns>
        public bool CopyMetadataToExistingStems(string audioFilePath)
        {
            if (!File.Exists(audioFilePath))
            {
                Log.Error("Audio file not found: {path}", audioFilePath);
                return false;
            }

            var vocalsPath = GetVocalsPath(audioFilePath);
            var instrumentalPath = GetInstrumentalPath(audioFilePath);

            if (vocalsPath == null || instrumentalPath == null)
            {
                Log.Warn("Stem files not found for {file}", audioFilePath);
                return false;
            }

            Log.Info("Copying metadata to existing stems for: {file}", Path.GetFileName(audioFilePath));
            CopyMetadataToStems(audioFilePath, vocalsPath, instrumentalPath);
            return true;
        }

        /// <summary>
        /// Gets the path to the vocals stem for a given audio file
        /// </summary>
        /// <param name="audioFilePath">Original audio file path</param>
        /// <returns>Path to vocals file if it exists, null otherwise</returns>
        public string GetVocalsPath(string audioFilePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(audioFilePath);
            var fileDirectory = Path.GetDirectoryName(audioFilePath);
            var vocalsPath = Path.Combine(fileDirectory, $"{fileName}_vocal.mp3").Replace('\\', '/');
            return File.Exists(vocalsPath) ? vocalsPath : null;
        }

        /// <summary>
        /// Gets the path to the instrumental stem for a given audio file
        /// </summary>
        /// <param name="audioFilePath">Original audio file path</param>
        /// <param name="model">Demucs model used (default: htdemucs)</param>
        /// <returns>Path to instrumental file if it exists, null otherwise</returns>
        public string GetInstrumentalPath(string audioFilePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(audioFilePath);
            var fileDirectory = Path.GetDirectoryName(audioFilePath);
            var instrumentalPath = Path.Combine(fileDirectory, $"{fileName}_instrumental.mp3").Replace('\\', '/');
            return File.Exists(instrumentalPath) ? instrumentalPath : null;
        }

        /// <summary>
        /// Copies analysis files (beat grid, cues, BPM, etc.) from parent audio file to stems
        /// </summary>
        /// <param name="parentAudioPath">Path to the original audio file</param>
        /// <param name="databasePath">Path to the Rekordbox database</param>
        /// <param name="parentAnalysisPath">Analysis path of the parent file (from Content.AnalysisDataPath)</param>
        /// <param name="model">Demucs model used (default: htdemucs)</param>
        /// <returns>Dictionary mapping stem paths to their analysis data paths (relative to share folder), or null if failed</returns>
        public Dictionary<string, (string Path, bool Copied)> CopyAnalysisToStems(SQLiteConnection db, Content parent, Config config)
        {
            var vocalsPath = GetVocalsPath(parent.FolderPath);
            var instrumentalPath = GetInstrumentalPath(parent.FolderPath);

            if (vocalsPath == null || instrumentalPath == null)
            {
                Log.Warn("Stem files not found for {file}", parent.AnalysisDataPath);
                return null;
            }

            try
            {
                var sharePath = Path.Join(Path.GetDirectoryName(config.DatabasePath), "share");
                var datPath = Path.Join(sharePath, parent.AnalysisDataPath);
                var extPath = datPath.Replace(".DAT", ".EXT", StringComparison.OrdinalIgnoreCase);

                if (!File.Exists(datPath))
                {
                    Log.Warn("Parent .DAT analysis file not found: {path}", datPath);
                    return null;
                }
                

                var result = new Dictionary<string, (string Path, bool Copied)>();

                // Copy analysis files to share folder for each stem
                var stems = new[] { vocalsPath, instrumentalPath };
                foreach (var stemPath in stems)
                {
                    var stemName = Path.GetFileNameWithoutExtension(stemPath);

                    // Generate analysis path relative to share folder (using same structure as parent)
                    var parentDir = Path.GetDirectoryName(parent.AnalysisDataPath);
                    var stemAnalysisDir = Path.Join(sharePath, parentDir);

                    // Use parent's directory structure with stem filename
                    var existingContent = db.Table<Content>().FirstOrDefault(c => c.FolderPath == stemPath);
                    
                    var stemAnalysisFileName = $"{stemName}.DAT";
                    var stemAnalysisPath = existingContent?.AnalysisDataPath ?? Path.Join( parentDir ?? "", stemAnalysisFileName);
                    var targetDatPath = Path.Join(sharePath, stemAnalysisPath);
                    var targetExtPath = targetDatPath.Replace(".DAT", ".EXT", StringComparison.OrdinalIgnoreCase);

                    // Copy .DAT and .EXT files, filtering out waveforms
                    try
                    {
                        var parentDatAnlz = parent.GetAnlz(AnalysisKind.Dat, config);
                        if (parentDatAnlz != null)
                        {
                            var stemDatAnlz = parentDatAnlz.Clone();
                            stemDatAnlz.FilterWaveforms();
                            var bytes = stemDatAnlz.Serialize();
                            Directory.CreateDirectory(Path.GetDirectoryName(targetDatPath));
                            File.WriteAllBytes(targetDatPath, bytes);
                            Log.Info("Copied and filtered analysis .DAT to {path}", targetDatPath);
                        }

                        var parentExtAnlz = parent.GetAnlz(AnalysisKind.Ext, config);
                        if (parentExtAnlz != null)
                        {
                            var stemExtAnlz = parentExtAnlz.Clone();
                            stemExtAnlz.FilterWaveforms();
                            var bytes = stemExtAnlz.Serialize();
                            Directory.CreateDirectory(Path.GetDirectoryName(targetExtPath));
                            File.WriteAllBytes(targetExtPath, bytes);
                            Log.Info("Copied and filtered analysis .EXT to {path}", targetExtPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex, "Could not copy and filter analysis files for stem: {path}", stemPath);
                    }

                    // Store the relative path (from share folder) - Always use forward slashes for Rekordbox DB
                    result[stemPath] = (stemAnalysisPath.Replace('\\', '/'), true);
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error copying analysis files for {file}", parent.AnalysisDataPath);
                return null;
            }
        }

        /// <summary>
        /// Copies cue points from parent content to stem content in the database
        /// </summary>
        /// <param name="db">SQLite database connection</param>
        /// <param name="parentContentId">Parent Content ID</param>
        /// <param name="stemContentId">Stem Content ID</param>
        /// <param name="stemContentUUID">Stem Content UUID</param>
        private List<Cue> CopyCuesToStem(SQLiteConnection db, string parentContentId, string stemContentId, string stemContentUUID)
        {
            var createdCues = new List<Cue>();
            try
            {
                // Delete existing cues for the stem to avoid duplicates
                db.Execute("DELETE FROM djmdCue WHERE ContentID = ?", stemContentId);

                // Get all cues from parent
                var parentCues = db.Table<Cue>().Where(c => c.ContentID == parentContentId).ToList();

                if (parentCues.Count == 0)
                {
                    Log.Debug("No cues found for parent content {contentId}", parentContentId);
                    return createdCues;
                }

                Log.Info("Copying {count} cue points from parent to stem...", parentCues.Count);

                // Get max ID for generating new IDs
                var maxId = db.Table<Cue>().Select(c => c.ID).ToList()
                    .Select(id => ulong.TryParse(id, out var val) ? val : 0UL)
                    .DefaultIfEmpty(0UL)
                    .Max() + 1;

                foreach (var parentCue in parentCues)
                {
                    // Create a copy of the cue for the stem
                    var stemCue = new Cue
                    {
                        ID = maxId.ToString(),
                        ContentID = stemContentId,
                        InMsec = parentCue.InMsec,
                        InFrame = parentCue.InFrame,
                        InMpegFrame = parentCue.InMpegFrame,
                        InMpegAbs = parentCue.InMpegAbs,
                        OutMsec = parentCue.OutMsec,
                        OutFrame = parentCue.OutFrame,
                        OutMpegFrame = parentCue.OutMpegFrame,
                        OutMpegAbs = parentCue.OutMpegAbs,
                        Kind = parentCue.Kind,
                        Color = parentCue.Color,
                        ColorTableIndex = parentCue.ColorTableIndex,
                        ActiveLoop = parentCue.ActiveLoop,
                        Comment = parentCue.Comment,
                        BeatLoopSize = parentCue.BeatLoopSize,
                        CueMicrosec = parentCue.CueMicrosec,
                        InPointSeekInfo = parentCue.InPointSeekInfo,
                        OutPointSeekInfo = parentCue.OutPointSeekInfo,
                        ContentUUID = stemContentUUID,
                        UUID = Guid.NewGuid().ToString(), // Use new UUID for uniqueness
                        created_at = DateTime.Now,
                        updated_at = DateTime.Now,
                        rb_local_deleted = parentCue.rb_local_deleted,
                        rb_local_synced = 0
                    };

                    db.Insert(stemCue);
                    createdCues.Add(stemCue);
                    maxId++;
                }

                Log.Info("Successfully copied {count} cues to stem", parentCues.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error copying cues from parent {parentId} to stem {stemId}", parentContentId, stemContentId);
            }
            return createdCues;
        }

        /// <summary>
        /// Copies content cue entries from parent to stem
        /// </summary>
        private void CopyContentCuesToStem(SQLiteConnection db, string stemContentId, List<Cue> stemCues)
        {
            try
            {
                // Delete existing entries
                db.Execute("DELETE FROM contentCue WHERE ContentID = ?", stemContentId);

                if (stemCues == null || stemCues.Count == 0) return;

                // Create new ContentCue entry
                var contentCue = new ContentCue
                {
                    ID = Guid.NewGuid().ToString(),
                    ContentID = stemContentId,
                    rb_cue_count = stemCues.Count,
                    rb_local_synced = 0,
                    rb_local_data_status = 1,
                    created_at = DateTime.Now,
                    updated_at = DateTime.Now
                };
                contentCue.SetCues(stemCues);

                db.Insert(contentCue);
                Log.Info("Copied ContentCue entry for stem {stemId}", stemContentId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error copying content cues to stem {stemId}", stemContentId);
            }
        }

        /// <summary>
        /// Copies MyTags from parent to stem
        /// </summary>
        private void CopyMyTagsToStem(SQLiteConnection db, string parentContentId, string stemContentId)
        {
            try
            {
                // Delete existing MyTags for the stem
                db.Execute("DELETE FROM djmdSongMyTag WHERE ContentID = ?", stemContentId);

                // Get parent MyTags
                var parentMyTags = db.Table<SongMyTag>().Where(t => t.ContentID == parentContentId).ToList();

                if (parentMyTags.Count == 0) return;

                // Get max ID
                var maxId = db.Table<SongMyTag>().Select(t => t.ID).ToList()
                    .Select(id => ulong.TryParse(id, out var val) ? val : 0UL)
                    .DefaultIfEmpty(0UL)
                    .Max() + 1;

                foreach (var parentTag in parentMyTags)
                {
                    var stemTag = new SongMyTag
                    {
                        ID = (maxId++).ToString(),
                        ContentID = stemContentId,
                        MyTagID = parentTag.MyTagID,
                        TrackNo = parentTag.TrackNo,
                        rb_local_synced = 0,
                        rb_local_data_status = 1,
                        created_at = DateTime.Now,
                        updated_at = DateTime.Now
                    };
                    db.Insert(stemTag);
                }
                Log.Info("Copied {count} MyTags to stem {stemId}", parentMyTags.Count, stemContentId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error copying MyTags to stem {stemId}", stemContentId);
            }
        }

        /// <summary>
        /// Creates Content entries in Rekordbox database for the separated stems
        /// </summary>
        /// <param name="db">SQLite database connection</param>
        /// <param name="parentContent">Parent Content object</param>
        /// <param name="parentAudioPath">Path to the original audio file</param>
        /// <param name="analysisPathMap">Dictionary mapping stem paths to their analysis data paths (from CopyAnalysisToStems)</param>
        /// <param name="model">Demucs model used (default: htdemucs)</param>
        /// <returns>True if entries were created successfully, false otherwise</returns>
        public bool UpdateStemContentEntries(SQLiteConnection db, Content parentContent, string parentAudioPath, Dictionary<string, (string Path, bool Copied)> analysisPathMap = null)
        {
            var vocalsPath = GetVocalsPath(parentAudioPath);
            var instrumentalPath = GetInstrumentalPath(parentAudioPath);

            if (vocalsPath == null || instrumentalPath == null)
            {
                Log.Warn("Stem files not found for {file}", parentAudioPath);
                return false;
            }

            try
            {
                var stems = new[]
                {
                    new { Path = vocalsPath, Title = $"{parentContent.Title} (Vocals)" },
                    new { Path = instrumentalPath, Title = $"{parentContent.Title} (Instrumental)" }
                };

                foreach (var stem in stems)
                {
                    // Get analysis path for this stem
                    string analysisPath = null;
                    bool analysisWasCopied = false;
                    if (analysisPathMap != null && analysisPathMap.TryGetValue(stem.Path, out var analysisInfo))
                    {
                        analysisPath = analysisInfo.Path;
                        analysisWasCopied = analysisInfo.Copied;
                    }

                    // Check if stem already exists in database
                    var existingContent = db.Table<Content>().Where(c => c.FolderPath == stem.Path).FirstOrDefault();
                    if (existingContent != null)
                    {
                        // If analysis was NOT copied (meaning it already existed), 
                        // and the stem is already in DB with an analysis path, 
                        // we skip updating the entry to avoid losing manual analysis info (waveform, etc.)
                        if (!analysisWasCopied && !string.IsNullOrEmpty(existingContent.AnalysisDataPath))
                        {
                            Log.Info("Stem {path} has existing analysis, skipping metadata/cue sync from parent.", stem.Path);
                            // continue;
                        }

                        Log.Info("Stem already exists in database: {path}, updating analysis data and metadata...", stem.Path);

                        // Update existing entry with parent's data
                        existingContent.Title = stem.Title;
                        existingContent.ArtistID = parentContent.ArtistID;
                        existingContent.AlbumID = parentContent.AlbumID;
                        existingContent.GenreID = parentContent.GenreID;
                        existingContent.BPM = parentContent.BPM;
                        existingContent.Length = parentContent.Length;
                        existingContent.BitRate = parentContent.BitRate;
                        existingContent.BitDepth = parentContent.BitDepth;
                        existingContent.Commnt = parentContent.Commnt;
                        existingContent.Rating = parentContent.Rating;
                        existingContent.KeyID = parentContent.KeyID;
                        existingContent.ColorID = parentContent.ColorID;
                        existingContent.RemixerID = parentContent.RemixerID;
                        existingContent.LabelID = parentContent.LabelID;
                        existingContent.ComposerID = parentContent.ComposerID;
                        existingContent.ReleaseYear = parentContent.ReleaseYear;
                        existingContent.ReleaseDate = parentContent.ReleaseDate;
                        existingContent.StockDate = parentContent.StockDate;
                        existingContent.OrgArtistID = parentContent.OrgArtistID;
                        existingContent.MasterDBID = parentContent.MasterDBID;
                        existingContent.MasterSongID = parentContent.MasterSongID;
                        existingContent.DiscNo = parentContent.DiscNo;
                        existingContent.Subtitle = parentContent.Subtitle;
                        existingContent.SampleRate = parentContent.SampleRate;
                        existingContent.AnalysisDataPath = analysisPath;
                        existingContent.Analysed = 1;
                        existingContent.AnalysisUpdated = parentContent.AnalysisUpdated;
                        existingContent.CueUpdated = parentContent.CueUpdated;
                        existingContent.TrackInfoUpdated = parentContent.TrackInfoUpdated;
                        existingContent.updated_at = DateTime.Now;
                        existingContent.rb_local_synced = 0;
                        existingContent.rb_local_data_status = 1;
                        existingContent.DeviceID = parentContent.DeviceID;
                        existingContent.SrcID = parentContent.SrcID;
                        existingContent.SrcTitle = parentContent.SrcTitle;
                        existingContent.SrcArtistName = parentContent.SrcArtistName;
                        existingContent.SrcAlbumName = parentContent.SrcAlbumName;
                        existingContent.SrcLength = parentContent.SrcLength;

                        db.Update(existingContent);
                        Log.Info("Updated Content entry for stem: {title}", stem.Title);

                        // Copy cues from parent to existing stem
                        var stemCues = CopyCuesToStem(db, parentContent.ID, existingContent.ID, existingContent.UUID);
                        CopyContentCuesToStem(db, existingContent.ID, stemCues);
                        CopyMyTagsToStem(db, parentContent.ID, existingContent.ID);

                        continue;
                    }
                    
                    Log.Info("No existing analysis entry for stem: {title} at {path}. Please analyze it in Rekordbox and try again", stem.Title, stem.Path);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating Content entries for stems of {file}", parentAudioPath);
                return false;
            }
        }

        /// <summary>
        /// Generates a unique Content ID based on file path
        /// </summary>
        private string GenerateContentId(string filePath)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(filePath));
            var id = BitConverter.ToUInt64(hash, 0);
            return id.ToString();
        }
    }
}
