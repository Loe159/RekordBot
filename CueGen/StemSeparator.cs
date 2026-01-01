using System;
using System.Diagnostics;
using System.IO;
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

            // Skip files that are already stems (ending with _instrumental or _vocal)
            if (fileName.EndsWith("_instrumental", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("_vocal", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Skipping stem file: {file}", Path.GetFileName(audioFilePath));
                return false;
            }

            var vocalsFile = Path.Combine(_outputDirectory, $"{fileName}_vocal.mp3");
            var instrumentalFile = Path.Combine(_outputDirectory, $"{fileName}_instrumental.mp3");

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
                    instrumentalFile.Tag.InitialKey = sourceTag.InitialKey;

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
        public bool CopyMetadataToExistingStems(string audioFilePath, string model = "htdemucs")
        {
            if (!File.Exists(audioFilePath))
            {
                Log.Error("Audio file not found: {path}", audioFilePath);
                return false;
            }

            var vocalsPath = GetVocalsPath(audioFilePath, model);
            var instrumentalPath = GetInstrumentalPath(audioFilePath, model);

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
        /// <param name="model">Demucs model used (default: htdemucs)</param>
        /// <returns>Path to vocals file if it exists, null otherwise</returns>
        public string GetVocalsPath(string audioFilePath, string model = "htdemucs")
        {
            var fileName = Path.GetFileNameWithoutExtension(audioFilePath);
            var vocalsPath = Path.Combine(_outputDirectory, $"{fileName}_vocal.mp3");
            return File.Exists(vocalsPath) ? vocalsPath : null;
        }

        /// <summary>
        /// Gets the path to the instrumental stem for a given audio file
        /// </summary>
        /// <param name="audioFilePath">Original audio file path</param>
        /// <param name="model">Demucs model used (default: htdemucs)</param>
        /// <returns>Path to instrumental file if it exists, null otherwise</returns>
        public string GetInstrumentalPath(string audioFilePath, string model = "htdemucs")
        {
            var fileName = Path.GetFileNameWithoutExtension(audioFilePath);
            var instrumentalPath = Path.Combine(_outputDirectory, $"{fileName}_instrumental.mp3");
            return File.Exists(instrumentalPath) ? instrumentalPath : null;
        }

        /// <summary>
        /// Copies analysis files (beat grid, cues, BPM, etc.) from parent audio file to stems
        /// </summary>
        /// <param name="parentAudioPath">Path to the original audio file</param>
        /// <param name="databasePath">Path to the Rekordbox database</param>
        /// <param name="parentAnalysisPath">Analysis path of the parent file (from Content.AnalysisDataPath)</param>
        /// <param name="model">Demucs model used (default: htdemucs)</param>
        /// <returns>True if copy was successful, false otherwise</returns>
        public bool CopyAnalysisToStems(string parentAudioPath, string databasePath, string parentAnalysisPath, string model = "htdemucs")
        {
            if (string.IsNullOrEmpty(parentAnalysisPath))
            {
                Log.Warn("No parent analysis path provided for {file}", parentAudioPath);
                return false;
            }

            var vocalsPath = GetVocalsPath(parentAudioPath, model);
            var instrumentalPath = GetInstrumentalPath(parentAudioPath, model);

            if (vocalsPath == null || instrumentalPath == null)
            {
                Log.Warn("Stem files not found for {file}", parentAudioPath);
                return false;
            }

            try
            {
                var sharePath = Path.Join(Path.GetDirectoryName(databasePath), "share");
                var datPath = Path.Join(sharePath, parentAnalysisPath);
                var extPath = datPath.Replace(".DAT", ".EXT", StringComparison.OrdinalIgnoreCase);

                if (!File.Exists(datPath))
                {
                    Log.Warn("Parent .DAT analysis file not found: {path}", datPath);
                    return false;
                }

                // Copy analysis files next to each stem
                var stems = new[] { vocalsPath, instrumentalPath };
                foreach (var stemPath in stems)
                {
                    var stemDir = Path.GetDirectoryName(stemPath);
                    var stemName = Path.GetFileNameWithoutExtension(stemPath);

                    var targetDatPath = Path.Combine(stemDir, stemName + ".DAT");
                    var targetExtPath = Path.Combine(stemDir, stemName + ".EXT");

                    // Copy .DAT file
                    File.Copy(datPath, targetDatPath, overwrite: true);
                    Log.Info("Copied analysis .DAT to {path}", targetDatPath);

                    // Copy .EXT file if it exists
                    if (File.Exists(extPath))
                    {
                        File.Copy(extPath, targetExtPath, overwrite: true);
                        Log.Info("Copied analysis .EXT to {path}", targetExtPath);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error copying analysis files for {file}", parentAudioPath);
                return false;
            }
        }

        /// <summary>
        /// Creates Content entries in Rekordbox database for the separated stems
        /// </summary>
        /// <param name="db">SQLite database connection</param>
        /// <param name="parentContent">Parent Content object</param>
        /// <param name="parentAudioPath">Path to the original audio file</param>
        /// <param name="model">Demucs model used (default: htdemucs)</param>
        /// <returns>True if entries were created successfully, false otherwise</returns>
        public bool CreateStemContentEntries(SQLiteConnection db, Content parentContent, string parentAudioPath, string model = "htdemucs")
        {
            var vocalsPath = GetVocalsPath(parentAudioPath, model);
            var instrumentalPath = GetInstrumentalPath(parentAudioPath, model);

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
                    // Check if stem already exists in database
                    var existingContent = db.Table<Content>().Where(c => c.FolderPath == stem.Path).FirstOrDefault();
                    if (existingContent != null)
                    {
                        Log.Info("Stem already exists in database: {path}", stem.Path);
                        continue;
                    }

                    // Create new Content entry for stem
                    var stemContent = new Content
                    {
                        ID = GenerateContentId(stem.Path),
                        FolderPath = stem.Path,
                        FileNameL = Path.GetFileName(stem.Path),
                        FileNameS = Path.GetFileName(stem.Path),
                        Title = stem.Title,
                        ArtistID = parentContent.ArtistID,
                        AlbumID = parentContent.AlbumID,
                        GenreID = parentContent.GenreID,
                        BPM = parentContent.BPM,
                        Length = parentContent.Length,
                        BitRate = parentContent.BitRate,
                        BitDepth = parentContent.BitDepth,
                        FileType = 1, // MP3 file type
                        KeyID = parentContent.KeyID,
                        ColorID = parentContent.ColorID,
                        RemixerID = parentContent.RemixerID,
                        LabelID = parentContent.LabelID,
                        ComposerID = parentContent.ComposerID,
                        SampleRate = parentContent.SampleRate,
                        FileSize = (int?)new FileInfo(stem.Path).Length,
                        Analysed = 0,
                        created_at = DateTime.Now,
                        updated_at = DateTime.Now,
                        rb_file_id = Guid.NewGuid().ToString(),
                        DeviceID = parentContent.DeviceID,
                        rb_LocalFolderPath = stem.Path,
                        SrcID = parentContent.SrcID,
                        SrcTitle = parentContent.SrcTitle,
                        SrcArtistName = parentContent.SrcArtistName,
                        SrcAlbumName = parentContent.SrcAlbumName,
                        SrcLength = parentContent.SrcLength
                    };

                    db.Insert(stemContent);
                    Log.Info("Created Content entry for stem: {title} at {path}", stem.Title, stem.Path);
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
