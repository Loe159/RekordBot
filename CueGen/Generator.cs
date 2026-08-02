using CueGen.Analysis;
using Ganss.IO;
using Newtonsoft.Json;
using NLog;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CueGen
{
    public record Status(int Total, int Count, Content Current);

    public class Generator
    {
        static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public Config Config { get; set; }
        public SQLiteConnectionString ConnectionString { get; set; }

        public Progress<Status> Progress { get; } = new Progress<Status>();

        private Dictionary<string, string> _keys;
        private Dictionary<string, string> KeysDictionary => _keys ??= GetKeys()
            .GroupBy(k => k.ScaleName)
            .ToDictionary(g => g.Key, g => g.First().ID);

        public Generator(Config config)
        {
            Config = config;
            ConnectionString = new SQLiteConnectionString(Config.DatabasePath,
                openFlags: SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite,
                storeDateTimeAsTicks: false,
                key: Config.UseSqlCipher ? "402fd482c38817c35ffa8ffb8c7d93143b749e7d315df7a81732a1ff43608497" : null,
                dateTimeStringFormat: "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz");
        }

        public IList<Cue> GetCues()
        {
            Log.Info("Getting cues from database {database}", ConnectionString.DatabasePath);

            using var db = new SQLiteConnection(ConnectionString);
            var cues = db.Table<Cue>().OrderBy(c => c.ID).ToList();

            Log.Info("{cues} cues read", cues.Count);

            return cues;
        }
        
        public IList<Artist> GetArtists()
        {
            Log.Info("Getting artists from database {database}", ConnectionString.DatabasePath);

            using var db = new SQLiteConnection(ConnectionString);
            var artists = db.Table<Artist>().OrderBy(c => c.ID).ToList();

            Log.Info("{artists} artists read", artists.Count);

            return artists;
        }

        public IList<Content> GetContents()
        {
            Log.Info("Getting contents from database {database}", ConnectionString.DatabasePath);

            using var db = new SQLiteConnection(ConnectionString);
            var contents = db.Table<Content>().OrderBy(c => c.ID).ToList();

            Log.Info("{count} contents read", contents.Count);

            var artists = Config.UpdateFromBeatport ? GetArtists() : new List<Artist>();
            var cues = GetCues();
            var contentCues = GetContentCues();
            var songMyTags = GetSongMyTags();

            foreach (var content in contents)
            {
                content.Artist = (artists.Where(c => c.ID == content.ArtistID).FirstOrDefault() ?? new Artist());
                content.Cues.AddRange(cues.Where(c => c.ContentID == content.ID));
                content.ContentCues.AddRange(contentCues.Where(c => c.ContentID == content.ID));
                content.MyTags.AddRange(songMyTags.Where(t => t.ContentID == content.ID));
            }

            return contents;
        }

        public IList<ContentCue> GetContentCues()
        {
            Log.Info("Getting contentCues from database {database}", ConnectionString.DatabasePath);

            using var db = new SQLiteConnection(ConnectionString);
            var contentCues = db.Table<ContentCue>().OrderBy(c => c.ID).ToList();

            Log.Info("{count} contentCues read", contentCues.Count);

            return contentCues;
        }

        public IList<MyTag> GetMyTags()
        {
            Log.Info("Getting MyTags from database {database}", ConnectionString.DatabasePath);

            using var db = new SQLiteConnection(ConnectionString);
            var myTags = db.Table<MyTag>().OrderBy(c => c.ID).ToList();

            Log.Info("{count} myTags read", myTags.Count);

            return myTags;
        }

        public IList<SongMyTag> GetSongMyTags()
        {
            Log.Info("Getting SongMyTags from database {database}", ConnectionString.DatabasePath);

            using var db = new SQLiteConnection(ConnectionString);
            var songMyTags = db.Table<SongMyTag>().OrderBy(c => c.created_at).ToList();

            Log.Info("{count} songMyTags read", songMyTags.Count);

            return songMyTags;
        }

        public IList<Key> GetKeys()
        {
            Log.Info("Getting keys from database {database}", ConnectionString.DatabasePath);

            using var db = new SQLiteConnection(ConnectionString);
            var keys = db.Table<Key>().OrderBy(c => c.ID).ToList();

            Log.Info("{count} keys read", keys.Count);

            return keys;
        }

        IList<MyTag> CreateMyTagEnergy(SQLiteConnection db, IList<MyTag> myTags)
        {
            var energyMyTag = myTags.FirstOrDefault(t => t.Name == "Energy" && t.ParentId == "root");

            if (energyMyTag == null && !Config.RemoveOnly)
            {
                var roots = myTags.Where(t => t.ParentId == "root");
                var maxRootId = roots.Max(t => long.Parse(t.ID));
                var maxRootSeq = roots.Max(t => t.Seq) ?? 0;
                var maxRootRbLocalUsn = roots.Max(t => t.rb_local_usn) ?? 0;

                energyMyTag = new MyTag
                {
                    ID = (maxRootId + 1).ToString(),
                    Seq = maxRootSeq + 1,
                    Name = "Energy",
                    Attribute = 1,
                    ParentId = "root",
                    UUID = Guid.NewGuid().ToString(),
                    rb_local_usn = maxRootRbLocalUsn + 9,
                    created_at = DateTime.UtcNow,
                    updated_at = DateTime.UtcNow
                };

                Log.Info("Inserting MyTag Energy");

                if (!Config.DryRun)
                    db.Insert(energyMyTag);
            }
            else if (Config.RemoveOnly)
            {
                var removeMyTags = myTags.Where(t => t.ParentId == energyMyTag.ID).ToList();
                var songMyTags = GetSongMyTags();

                Log.Info("Removing MyTag Energy");

                if (!Config.DryRun)
                {
                    foreach (var songMyTag in songMyTags.Where(t => removeMyTags.Exists(r => r.ID == t.MyTagID)))
                        db.Delete(songMyTag);
                    db.Table<MyTag>().Delete(t => t.ParentId == energyMyTag.ID);
                    db.Delete(energyMyTag);
                }

                return removeMyTags;
            }

            var energyMyTags = new List<MyTag>();
            var maxId = myTags.Max(t => long.Parse(t.ID));
            var maxRbLocalUsn = myTags.Max(t => t.rb_local_usn) ?? 0;

            foreach (var energy in Enumerable.Range(1, 8))
            {
                var myTag = myTags.FirstOrDefault(t => t.Name == energy.ToString() && t.ParentId == energyMyTag.ID);

                if (myTag == null)
                {
                    maxId++;
                    maxRbLocalUsn++;

                    myTag = new MyTag
                    {
                        ID = maxId.ToString(),
                        Seq = energy,
                        Name = energy.ToString(),
                        Attribute = 0,
                        ParentId = energyMyTag.ID,
                        UUID = Guid.NewGuid().ToString(),
                        rb_local_usn = maxRbLocalUsn,
                        created_at = DateTime.UtcNow,
                        updated_at = DateTime.UtcNow
                    };

                    Log.Info("Inserting MyTag Energy {energy}", energy);

                    if (!Config.DryRun)
                        db.Insert(myTag);
                }

                energyMyTags.Add(myTag);
            }

            return energyMyTags;
        }
        
        IList<MyTag> CreateMyTagGenre(SQLiteConnection db, IList<MyTag> myTags, String genre = "")
        {
            var genreMyTag = myTags.FirstOrDefault(t => t.Name == "Genre" && t.ParentId == "root");

            if (genreMyTag == null && !Config.RemoveOnly)
            {
                var roots = myTags.Where(t => t.ParentId == "root");
                var maxRootId = roots.Max(t => long.Parse(t.ID));
                var maxRootSeq = roots.Max(t => t.Seq) ?? 0;
                var maxRootRbLocalUsn = roots.Max(t => t.rb_local_usn) ?? 0;

                genreMyTag = new MyTag
                {
                    ID = (maxRootId + 1).ToString(),
                    Seq = maxRootSeq + 1,
                    Name = "Genre",
                    Attribute = 1,
                    ParentId = "root",
                    UUID = Guid.NewGuid().ToString(),
                    rb_local_usn = maxRootRbLocalUsn + 9,
                    created_at = DateTime.UtcNow,
                    updated_at = DateTime.UtcNow
                };

                Log.Info("Inserting Genre Energy");

                if (!Config.DryRun)
                    db.Insert(genreMyTag);
            }
            else if (Config.RemoveOnly)
            {
                var removeMyTags = myTags.Where(t => t.ParentId == genreMyTag.ID).ToList();
                var songMyTags = GetSongMyTags();

                Log.Info("Removing MyTag Energy");

                if (!Config.DryRun)
                {
                    foreach (var songMyTag in songMyTags.Where(t => removeMyTags.Exists(r => r.ID == t.MyTagID)))
                        db.Delete(songMyTag);
                    db.Table<MyTag>().Delete(t => t.ParentId == genreMyTag.ID);
                    db.Delete(genreMyTag);
                }

                return removeMyTags;
            }

            var genreMyTags = new List<MyTag>();
            var maxId = myTags.Max(t => long.Parse(t.ID));
            var maxRbLocalUsn = myTags.Max(t => t.rb_local_usn) ?? 0;
            var maxSeq = myTags.Where(t => t.ParentId == genreMyTag.ID).Max(t => t.Seq) ?? 0;

            if (genre.Length != 0)
            {
                var myTag = myTags.FirstOrDefault(t => t.Name == genre && t.ParentId == genreMyTag.ID);

                if (myTag == null)
                {
                    maxId++;
                    maxRbLocalUsn++;

                    myTag = new MyTag
                    {
                        ID = maxId.ToString(),
                        Seq = maxSeq + 1,
                        Name = genre,
                        Attribute = 0,
                        ParentId = genreMyTag.ID,
                        UUID = Guid.NewGuid().ToString(),
                        rb_local_usn = maxRbLocalUsn,
                        created_at = DateTime.UtcNow,
                        updated_at = DateTime.UtcNow
                    };

                    Log.Info("Inserting MyTag Genre {genre}", genre);

                    if (!Config.DryRun)
                        db.Insert(myTag);
                }
                
                genreMyTags.Add(myTag);
            }

            return genreMyTags;
        }

        ulong GetMaxId()
        {
            using var db = new SQLiteConnection(ConnectionString);
            var maxId = db.ExecuteScalar<long>("select max(cast(ID as INTEGER)) ID from djmdCue");
            return (ulong)maxId;
        }

        public bool Generate()
        {
            var contents = GetContents();
            var error = false;
            var maxId = GetMaxId() + 1;
            IList<MyTag> energyMyTags = new List<MyTag>();
            IList<MyTag> genreMyTags = new List<MyTag>();
            long maxMyTagUsn = 0L;
            using var db = new SQLiteConnection(ConnectionString);

            // Initialize stem separator if enabled
            StemSeparator stemSeparator = null;
            if (Config.SeparateStems)
            {
                if (string.IsNullOrEmpty(Config.StemsOutputDirectory))
                {
                    Log.Error("StemsOutputDirectory must be configured when SeparateStems is enabled");
                    return true;
                }

                stemSeparator = new StemSeparator(Config.StemsOutputDirectory, Config.DemucsCommand);
                Log.Info("Stem separation enabled. Output directory: {directory}", Config.StemsOutputDirectory);
            }

            if (Config.MyTagEnergy)
            {
                try
                {
                    var myTags = GetMyTags();
                    maxMyTagUsn = myTags.Max(t => t.rb_local_usn) ?? 0L;
                    db.RunInTransaction(() => energyMyTags = CreateMyTagEnergy(db, myTags));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error occurred during creation of Energy MyTag");
                    error = true;
                }
            }

            if (Config.MinCreatedDate > DateTime.MinValue)
                contents = contents.Where(c => c.created_at >= Config.MinCreatedDate).ToList();

            if (!string.IsNullOrEmpty(Config.FileGlob))
            {
                var glob = new Glob(Config.FileGlob, new GlobOptions { IgnoreCase = true });
                contents = contents.Where(c => glob.IsMatch(c.FolderPath)).ToList();
            }

            if (Config.UpdateFromSoundcharts)
            {
                Log.Warn("Soundcharts metadata updates are not connected to the generation flow and were skipped");
            }

            if (Config.UpdateFromBeatport &&
                (string.IsNullOrWhiteSpace(Config.BeatportUsername) || string.IsNullOrWhiteSpace(Config.BeatportPassword)))
            {
                Log.Error("Beatport credentials are required when Beatport metadata is enabled");
                return true;
            }

            using var beatportClient = Config.UpdateFromBeatport
                ? new BeatportClient(Config.BeatportUsername, Config.BeatportPassword)
                : null;

            var count = 0;

            foreach (var content in contents)
            {
                ((IProgress<Status>)Progress).Report(new Status(contents.Count, count, content));

                if (beatportClient != null)
                {
                    var isrc = content.ISRC;

                    var response = beatportClient.GetTracks(
                        new Dictionary<string, string>
                        {
                            { "isrc", isrc },
                        },
                        perPage: 1
                    );


                    var track = response.Results.FirstOrDefault();

                    // Try with Artists and Title
                    if (track == null)
                    {
                        response = beatportClient.GetTracks(
                            new Dictionary<string, string>
                            {
                                { "artist_name", content.Artist.Name.Split("/").First() },
                                { "name", content.Title.Split(" - ")[0] },
                            },
                            perPage: 1
                        );
                        track = response.Results.FirstOrDefault();
                    }
                    
                    try
                    {
                        Console.WriteLine(track.Artists.First().Name);
                        Console.WriteLine(track.Name);
                        Console.WriteLine(track.Genre.Name);
                        Console.WriteLine(track.Sub_Genre?.Name);
                        
                        
                        db.RunInTransaction(() => CreateSongMyTagGenre(content, track.Genre, maxMyTagUsn, db));

                        if (track.Sub_Genre != null)
                        {
                            db.RunInTransaction(() => CreateSongMyTagGenre(content, track.Sub_Genre, maxMyTagUsn, db));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error occurred during creation of Genre MyTag for {contentID} from {path}", content.ID, content.FolderPath);
                        error = true;
                    }
                    
                }

                // Perform stem separation if enabled
                if (stemSeparator != null && !Config.RemoveOnly)
                {
                    try
                    {
                        Log.Info("Starting stem separation for {contentID} at {path}", content.ID, content.FolderPath);
                        var success = stemSeparator.SeparateStems(content.FolderPath, Config.DemucsModel);

                        if (success)
                        {
                            var vocalsPath = stemSeparator.GetVocalsPath(content.FolderPath);
                            var instrumentalPath = stemSeparator.GetInstrumentalPath(content.FolderPath);
                            Log.Info("Stem separation successful. Vocals: {vocals}, Instrumental: {instrumental}",
                                     vocalsPath, instrumentalPath);


                            // Copy analysis data from parent to stems
                            Log.Info("Copying analysis data from parent to stems...");
                            var analysisMap = stemSeparator.CopyAnalysisToStems(db, content, Config);

                            if (analysisMap == null)
                            {
                                error = true;
                                continue;
                            }

                            // Create Content entries in database for stems
                            Log.Info("Updating database entries for stems...");
                            db.RunInTransaction(() => stemSeparator.UpdateStemContentEntries(db, content, content.FolderPath, analysisMap));
                        }
                        else
                        {
                            Log.Warn("Stem separation failed for {contentID}", content.ID);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error during stem separation for {contentID} from {path}", content.ID, content.FolderPath);
                        error = true;
                    }
                }

                if (Config.ColorEnergy)
                {
                    try
                    {
                        db.RunInTransaction(() => CreateColorEnergy(content, db));
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error occurred when setting energy color for {contentID} from {path}", content.ID, content.FolderPath);
                        error = true;
                    }
                }

                if (Config.MyTagEnergy)
                {
                    try
                    {
                        db.RunInTransaction(() => CreateSongMyTagEnergy(content, maxMyTagUsn, energyMyTags, db));
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error occurred during creation of Energy MyTag for {contentID} from {path}", content.ID, content.FolderPath);
                        error = true;
                    }
                }

                try
                {
                    db.RunInTransaction(() => CreateCuesForContent(ref maxId, db, content));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error occurred during creation of cues for {contentID} from {path}", content.ID, content.FolderPath);
                    error = true;
                }

                count++;
            }

            Log.Info($"Finished cue points creation {(error ? "with" : "without")} errors");

            return error;
        }

        private void CreateColorEnergy(Content content, SQLiteConnection db)
        {
            var tagFile = content.GetTag();
            var energy = tagFile.Energy?.EnergyLevel ?? 0;

            if (energy > 0 && energy <= 8)
            {
                var colorId = 9 - energy;
                Log.Info("Setting color for {contentId} to energy {energy} (color id {colorId})", content.ID, energy, colorId);
                content.ColorID = colorId.ToString();
                db.Update(content);
            }
            else
            {
                Log.Info("No energy level found for {contentId}", content.ID);
            }
        }

        private void CreateSongMyTagEnergy(Content content, long maxMyTagUsn, IList<MyTag> energyMyTags, SQLiteConnection db)
        {
            if (!energyMyTags.Any()) return;

            var tagFile = content.GetTag();
            var energy = tagFile.Energy?.EnergyLevel ?? 0;

            if (energy > 0 && energy <= 8)
            {
                Log.Info("Energy level for {contentId} is {energy}", content.ID, energy);
                var energyMyTag = energyMyTags[energy - 1];
                var songMyTag = content.MyTags.Find(t => t.MyTagID == energyMyTag.ID);
                if (songMyTag == null)
                {
                    maxMyTagUsn++;

                    songMyTag = new SongMyTag
                    {
                        ID = Guid.NewGuid().ToString(),
                        MyTagID = energyMyTag.ID,
                        ContentID = content.ID,
                        UUID = Guid.NewGuid().ToString(),
                        rb_local_usn = maxMyTagUsn,
                        created_at = DateTime.UtcNow,
                        updated_at = DateTime.UtcNow
                    };

                    Log.Info("Inserting Energy MyTag {energy} for {contentId}", energy, content.ID);

                    if (!Config.DryRun)
                        db.Insert(songMyTag);
                }
                else
                {
                    Log.Info("Energy MyTag for {contentId} already at {energy}", content.ID, energy);
                }

                foreach (var myTag in content.MyTags.Select(t => (Energy: energyMyTags.FirstOrDefault(e => e.ID == t.MyTagID), Tag: t))
                    .Where(t => t.Energy != null && t.Tag.MyTagID != energyMyTag.ID))
                {
                    Log.Info("Removing Energy MyTag {energy} for {contentId}", myTag.Energy.Seq, content.ID);
                    db.Delete(myTag.Tag);
                }
            }
            else
            {
                Log.Info("No energy level found for {contentId}", content.ID);
            }
        }
        
        private void CreateSongMyTagGenre(Content content, BeatportClient.BeatportGenre genre, long maxMyTagUsn, SQLiteConnection db)
        {
            var myTags = GetMyTags();
            MyTag genreMyTags = null;
            
            if (genre != null)
            {
                Log.Info("Genre for {contentId} is {genre}", content.ID, genre.Name);

                db.RunInTransaction(() => genreMyTags = CreateMyTagGenre(db, myTags, genre.Name).First());
                
                if (genreMyTags == null) return;

                var songMyTag = content.MyTags.Find(t => t.MyTagID == genreMyTags.ID);
                if (songMyTag == null)
                {
                    maxMyTagUsn++;

                    songMyTag = new SongMyTag
                    {
                        ID = Guid.NewGuid().ToString(),
                        MyTagID = genreMyTags.ID,
                        ContentID = content.ID,
                        UUID = Guid.NewGuid().ToString(),
                        rb_local_usn = maxMyTagUsn,
                        created_at = DateTime.UtcNow,
                        updated_at = DateTime.UtcNow
                    };

                    Log.Info("Inserting Genre MyTag {genre} for {contentId}", genre, content.ID);

                    if (!Config.DryRun)
                        db.Insert(songMyTag);
                }
                else
                {
                    Log.Info("Genre MyTag for {contentId} already at {genre}", content.ID, genre);
                }

                /*foreach (var myTag in content.MyTags.Select(t => (Genre: genreMyTags.(e => e.ID == t.MyTagID), Tag: t))
                    .Where(t => t.Genre != null && t.Tag.MyTagID != genreMyTags.ID))
                {
                    Log.Info("Removing Genre MyTag {genre} for {contentId}", myTag.Genre.Seq, content.ID);
                    db.Delete(myTag.Tag);
                }*/
            }
            else
            {
                Log.Info("No genre level found for {contentId}", content.ID);
            }
        }

        static readonly Dictionary<PhraseGroup, string> DefaultPhraseNames = new()
        {
            [PhraseGroup.Intro] = "Intro",
            [PhraseGroup.Verse] = "Verse",
            [PhraseGroup.Bridge] = "Bridge",
            [PhraseGroup.Chorus] = "Chorus",
            [PhraseGroup.Outro] = "Outro",
            [PhraseGroup.Up] = "Up",
            [PhraseGroup.Down] = "Down",
        };

        static readonly Dictionary<PhraseGroup, int> DefaultPhraseOrder = new()
        {
            [PhraseGroup.Intro] = 0,
            [PhraseGroup.Outro] = 1,
            [PhraseGroup.Verse] = 2,
            [PhraseGroup.Chorus] = 3,
            [PhraseGroup.Bridge] = 4,
            [PhraseGroup.Up] = 5,
            [PhraseGroup.Down] = 5,
        };

        private List<CuePoint> GetPhraseCuePoints(Content content)
        {
            var extAnlz = content.GetAnlz(AnalysisKind.Ext, Config);
            if (extAnlz == null || extAnlz.Sections == null) return new();
            var phraseTag = extAnlz.Sections.Select(s => s.Content).OfType<PhraseSection>().FirstOrDefault();
            var phrases = phraseTag?.Phrases;
            if (phrases == null || !phrases.Any()) return new();

            var beats = content.GetBeats(Config);
            if (!beats.Any()) return new();

            var phraseOrder = Config.PhraseOrder ?? DefaultPhraseOrder;
            var phraseNames = Config.PhraseNames ?? DefaultPhraseNames;
            var cues = new List<CuePoint>();

            int? lastOrder = null;
            for (int i = 0; i < phrases.Count; i++)
            {
                var phrase = phrases[i];
                if (!phraseOrder.TryGetValue(phrase.Kind.Group, out var currentOrder))
                    continue;

                // Grouping: consecutive phrases with the same ORDER are skipped
                if (lastOrder == currentOrder)
                    continue;

                // Find the end of this combined section to compute total length
                int j = i + 1;
                while (j < phrases.Count &&
                       phraseOrder.TryGetValue(phrases[j].Kind.Group, out var nextOrder) &&
                       nextOrder == currentOrder)
                {
                    j++;
                }

                var startBeat = phrase.Beat - 1;
                var endBeat = (j < phrases.Count) ? phrases[j].Beat - 1 : (phraseTag.EndBeat - 1);
                var lengthBeats = endBeat - startBeat;
                var lengthBars = lengthBeats / 4;

                if (lengthBars < Config.MinPhraseLength)
                    continue;

                var beatNum = startBeat;
                phraseNames.TryGetValue(phrase.Kind.Group, out var name);

                if (beatNum >= 0 && beatNum < beats.Count)
                {
                    var cue = new CuePoint
                    {
                        Name = name,
                        Time = beats[beatNum].Time,
                        Phrase = phrase
                    };
                    if (Config.PhraseHotCuesMemoryCues)
                    {
                        cue.Type = CueType.Hot;
                    }
                    cues.Add(cue);
                    lastOrder = currentOrder;
                }
            }

            return cues;
        }

        private List<CuePoint> GetInterPhraseCuePoints(Content content)
        {
            var extAnlz = content.GetAnlz(AnalysisKind.Ext, Config);
            if (extAnlz?.Sections == null) return new();

            var phraseTag = extAnlz.Sections
                .Select(s => s.Content)
                .OfType<PhraseSection>()
                .FirstOrDefault();

            if (phraseTag?.Phrases == null || !phraseTag.Phrases.Any())
                return new();

            var beats = content.GetBeats(Config);
            if (!beats.Any()) return new();

            var cues = new List<CuePoint>();
            var phrases = phraseTag.Phrases.OrderBy(p => p.Beat).ToList();
            var phraseOrder = Config.PhraseOrder ?? DefaultPhraseOrder;

            // Grouper les phrases consécutives identiques
            for (int i = 0; i < phrases.Count; i++)
            {
                var phrase = phrases[i];
                
                // Vérifier si la phrase est dans phraseOrder
                if (!phraseOrder.ContainsKey(phrase.Kind.Group))
                    continue;

                var startBeat = phrase.Beat - 1;
                
                // Trouver la fin de ce groupe de phrases (même type consécutif)
                int j = i + 1;
                while (j < phrases.Count && phrases[j].Kind.Group == phrase.Kind.Group)
                {
                    j++;
                }
                
                // endBeat est soit le début de la prochaine phrase différente, soit la fin du morceau
                var endBeat = (j < phrases.Count)
                    ? phrases[j].Beat - 1
                    : phraseTag.EndBeat - 1;

                // Sécurité
                startBeat = Math.Max(startBeat, 0);
                endBeat = Math.Min(endBeat, beats.Count - 1);

                // Partir de endBeat et revenir de 32 en 32 beats jusqu'à startBeat
                // On commence à endBeat - 32 pour laisser de l'espace avant la phrase suivante
                int firstMemoryCueBeat = endBeat - ((endBeat - startBeat) % 32);
                if (firstMemoryCueBeat == endBeat) 
                    firstMemoryCueBeat -= 32;
                
                for (int b = firstMemoryCueBeat; b > startBeat; b -= 32)
                {
                    if (b < 0 || b >= beats.Count) continue;

                    cues.Add(new CuePoint
                    {
                        Time = beats[b].Time,
                        Phrase = phrase,
                        Name = DefaultPhraseNames.TryGetValue(phrase.Kind.Group, out var n)
                            ? n
                            : phrase.Kind.Group.ToString(),
                        Type = CueType.Memory
                    });
                }
                
                // Sauter toutes les phrases du même groupe qu'on vient de traiter
                i = j - 1;
            }

            return cues;
        }

        
        private void CreateLoops(Cue cue, List<Cue> cues, int cueNum, List<CuePoint> cueCandidates, Content content)
        {
            if (Config.LoopIntroLength > 0 && cueNum == 1 && (!cues.Any() || cue.InMsec < cues.Min(c => c.InMsec)))
            {
                CreateLoop(cue, cueNum, content, Config.LoopIntroLength);
            }
            else if (Config.LoopOutroLength > 0 && cueNum == cueCandidates.Count && cues.Count > 0
                && cue.InMsec > cues.Max(c => c.InMsec))
            {
                CreateLoop(cue, cueNum, content, Config.LoopOutroLength);
            }
        }

        private void CreateLoop(Cue cue, int cueNum, Content content, int loopLen)
        {
            var beats = content.GetBeats(Config);

            if (beats.Any())
            {
                var startBeat = beats.Select((b, i) => (Index: i, Beat: b))
                    .OrderBy(b => Math.Abs(b.Beat.Time - (double)cue.InMsec)).First();
                var endBeatNum = Math.Min(beats.Count - 1, startBeat.Index + loopLen);
                var endBeat = beats[endBeatNum];
                var outTime = endBeat.Time;
                var outFrame = TimeToFrame(outTime);

                Log.Info("Setting cue point {cueNum} to active loop", cueNum);

                cue.OutMsec = (int)outTime;
                cue.OutFrame = outFrame;
                cue.BeatLoopSize = 0x10000 * loopLen + 1;
                cue.CueMicrosec = 0;

                if (Config.HotCues)
                {
                    cue.ActiveLoop = 1;
                    cue.Color = 255;
                }
                else
                {
                    cue.ActiveLoop = 0;
                    cue.Kind = 4;
                }
            }
        }

        private void CreateCuesForContent(ref ulong maxId, SQLiteConnection db, Content content)
        {
            List<CuePoint> cuePoints;

            if (Config.PhraseCues)
            {
                Log.Info("Getting cue points from phrase analysis for {contentID} with file at {path}", content.ID, content.FolderPath);
                cuePoints = GetPhraseCuePoints(content) ?? new();
                
                if (Config.PhraseHotCuesMemoryCues)
                {
                    var phraseMemoryCues = GetInterPhraseCuePoints(content);
                    cuePoints.AddRange(phraseMemoryCues);
                }
            }
            else
            {
                Log.Info("Reading cue points for {contentID} from tag of {path}", content.ID, content.FolderPath);
                var tagFile = content.GetTag();

                cuePoints = tagFile?.SeratoMarkers?.Cues?.Select(c => new CuePoint { Time = c.Time, Name = c.Name, Energy = c.Energy })?.ToList();

                if (cuePoints == null || !cuePoints.Any())
                    cuePoints = tagFile?.CuePoints?.Cues ?? new();
            }

            Log.Info("Found {count} cue points", cuePoints.Count);

            var cues = content.Cues;
            var contentCues = content.ContentCues;
            var cueNum = 1;
            var bpm = content.BPM ?? (120 * 100);

            if (content.BPM == null)
                Log.Info("BPM is unknown, assuming {bpm} BPM", bpm);

            if (!Config.Merge && !Config.RemoveOnly)
            {
                Log.Info("Removing all existing cue points for {contentID}", content.ID);

                cues.Clear();

                if (!Config.DryRun)
                    db.Table<Cue>().Delete(c => c.ContentID == content.ID);
            }
            else
            {
                Log.Info("Removing existing generated cue points for {contentID}", content.ID);

                cues.RemoveAll(c => c.UUID.StartsWith(UUIDPrefix));

                if (!Config.DryRun)
                    db.Table<Cue>().Delete(c => c.ContentID == content.ID
                                           && c.UUID.StartsWith(UUIDPrefix));
            }

            if (!Config.RemoveOnly)
            {
                var cueCandidates = new List<CuePoint>();
                
                if (Config.PhraseHotCuesMemoryCues)
                {
                    // Séparer les hotcues et memory cues
                    var hotCueCandidates = cuePoints.Where(c => c.Type == CueType.Hot).OrderBy(c => c.Time).ToList();
                    var memoryCueCandidates = cuePoints.Where(c => c.Type == CueType.Memory).OrderBy(c => c.Time).ToList();
                    
                    // Pour les hotcues: limité à 16
                    var maxHotCues = Math.Min(16, Config.MaxCues) - cues.Count(c => c.Kind > 0);
                    maxHotCues = Math.Max(0, maxHotCues);
                    
                    // Pour les memory cues: pas de limite stricte
                    var maxMemoryCues = Config.MaxCues - cues.Count(c => c.Kind == 0);
                    maxMemoryCues = Math.Max(0, maxMemoryCues);

                    Log.Info("Can create {hotcues} hot cues and {memorycues} memory cues", maxHotCues, maxMemoryCues);

                    // Ajouter d'abord les hotcues (limité à 16)
                    foreach (var cue in hotCueCandidates)
                {
                    if (cueCandidates.Count(c => c.Type == CueType.Hot) >= maxHotCues)
                        break;

                    if (Config.CueOffset != 0 && content.Length.HasValue)
                        OffsetCue(cue, bpm, content.Length.Value, Config.CueOffset);

                    if (Config.SnapToBar)
                        SnapToBar(content, cue);

                    var bars = Bars(cue.Time, bpm);
                    // Find close hot cues
                    var closeCues = cues.Where(c => Math.Abs(Bars(c.InMsec ?? 0, bpm) - bars) < Config.MinDistanceBars
                                               && c.Kind > 0).ToList();

                    Log.Info("Hot cue candidate #{num} is at {time}ms ({bars} bars)", cueNum, cue.Time, bars);
                    if ((int)cue.Energy > 0)
                        Log.Info("Energy is {energy}", (int)cue.Energy);
                    if (cue.Phrase != null)
                        Log.Info("Phrase is {phrase}", DefaultPhraseNames[cue.Phrase.Kind.Group]);

                    if (!closeCues.Any())
                    {
                        cueCandidates.Add(cue);
                    }
                    else
                    {
                        Log.Info("Ignoring cue point because there is an existing cue point within {bars} bars", Config.MinDistanceBars);
                        Log.Info("Close cues:");
                        foreach (var closeCue in closeCues)
                            Log.Info("ID {cueID} at {time}ms ({bars} bars)", closeCue.ID, closeCue.InMsec, Bars(closeCue.InMsec ?? 0, bpm));
                    }

                    cueNum++;
                }
                
                // Ensuite ajouter les memory cues
                foreach (var cue in memoryCueCandidates)
                {
                    if (cueCandidates.Count(c => c.Type == CueType.Memory) >= maxMemoryCues)
                        break;

                    if (Config.CueOffset != 0 && content.Length.HasValue)
                        OffsetCue(cue, bpm, content.Length.Value, Config.CueOffset);

                    if (Config.SnapToBar)
                        SnapToBar(content, cue);

                    var bars = Bars(cue.Time, bpm);
                    // Find close memory cues
                    var closeCues = cues.Where(c => Math.Abs(Bars(c.InMsec ?? 0, bpm) - bars) < Config.MinDistanceBars
                                               && c.Kind == 0).ToList();

                    Log.Info("Memory cue candidate #{num} is at {time}ms ({bars} bars)", cueNum, cue.Time, bars);
                    if ((int)cue.Energy > 0)
                        Log.Info("Energy is {energy}", (int)cue.Energy);
                    if (cue.Phrase != null)
                        Log.Info("Phrase is {phrase}", DefaultPhraseNames[cue.Phrase.Kind.Group]);

                    if (!closeCues.Any())
                    {
                        cueCandidates.Add(cue);
                    }
                    else
                    {
                        Log.Info("Ignoring cue point because there is an existing cue point within {bars} bars", Config.MinDistanceBars);
                        Log.Info("Close cues:");
                        foreach (var closeCue in closeCues)
                            Log.Info("ID {cueID} at {time}ms ({bars} bars)", closeCue.ID, closeCue.InMsec, Bars(closeCue.InMsec ?? 0, bpm));
                    }

                    cueNum++;
                }
                }
                else
                {
                    // Comportement original (sans séparation hotcues/memory cues)
                    var maxCues = Config.MaxCues - cues.Count(c => (c.Kind == 0 && !Config.HotCues) || (c.Kind > 0 && Config.HotCues));

                    if (Config.HotCues)
                        maxCues = Math.Min(8, maxCues);

                    Log.Info("Can create {cues} cue points", maxCues);

                    // iterate alternatingly between front and back
                    foreach (var cue in cuePoints.OrderBy(c => c.Time)
                        .Select((c, i) => (Cue: c, Index: i))
                        .OrderBy(c => Math.Min(c.Index, Math.Abs((cuePoints.Count - 1) - c.Index)))
                        .Select(c => c.Cue))
                    {
                        if (cueCandidates.Count >= maxCues)
                            break;

                        if (Config.CueOffset != 0 && content.Length.HasValue)
                            OffsetCue(cue, bpm, content.Length.Value, Config.CueOffset);

                        if (Config.SnapToBar)
                            SnapToBar(content, cue);

                        var bars = Bars(cue.Time, bpm);
                        // Find close cues of the same kind we are generating
                        var closeCues = cues.Where(c => Math.Abs(Bars(c.InMsec ?? 0, bpm) - bars) < Config.MinDistanceBars
                                                   && ((c.Kind == 0 && !Config.HotCues) || (c.Kind > 0 && Config.HotCues))).ToList();

                        Log.Info("Cue point candidate #{num} is at {time}ms ({bars} bars)", cueNum, cue.Time, bars);
                        if ((int)cue.Energy > 0)
                            Log.Info("Energy is {energy}", (int)cue.Energy);
                        if (cue.Phrase != null)
                            Log.Info("Phrase is {phrase}", DefaultPhraseNames[cue.Phrase.Kind.Group]);

                        if (!closeCues.Any())
                        {
                            cueCandidates.Add(cue);
                        }
                        else
                        {
                            Log.Info("Ignoring cue point because there is an existing cue point within {bars} bars", Config.MinDistanceBars);
                            Log.Info("Close cues:");
                            foreach (var closeCue in closeCues)
                                Log.Info("ID {cueID} at {time}ms ({bars} bars)", closeCue.ID, closeCue.InMsec, Bars(closeCue.InMsec ?? 0, bpm));
                        }

                        cueNum++;
                    }
                }

                cueNum = 1;

                foreach (var cue in cueCandidates.OrderBy(c => c.Time))
                {
                    var newCue = CreateCue(cue, cues, content, cueNum, maxId);
                    var bars = Bars(cue.Time, bpm);

                    CreateLoops(newCue, cues, cueNum, cueCandidates, content);

                    Log.Info("Created cue point {json}", JsonConvert.SerializeObject(newCue));
                    Log.Info("Inserting cue point #{num} with id {cueId} at {time}ms ({bars} bars)", cueNum, newCue.ID, cue.Time, bars);

                    cues.Add(newCue);

                    if (!Config.DryRun)
                        db.Insert(newCue);

                    maxId++;
                    cueNum++;
                }
            }

            var contentCue = contentCues.FirstOrDefault();

            if (contentCue != null)
            {
                Log.Info("Updating contentCue {cueID}", contentCue.ID);
                contentCue.SetCues(cues.Where(c => c.ContentID == content.ID));
                if (!Config.DryRun)
                    db.Update(contentCue);
            }
        }

        private void OffsetCue(CuePoint cue, int bpm, int length, int cueOffset)
        {
            var offsetMs = BeatsToMs(cueOffset, bpm);
            var time = cue.Time + offsetMs;

            Log.Info("Offsetting cue point from {time}ms to {offsetTime}ms", cue.Time, time);

            if (time >= 0.0 && time <= (length * 1000.0))
            {
                cue.Time = time;
            }
            else
            {
                Log.Info("Offset time is out of range");
            }
        }

        private void SnapToBar(Content content, CuePoint cue)
        {
            var beats = content.GetBeats(Config);
            if (!beats.Any()) return;
            var closestBar = beats.Where(b => b.BeatNumber == 1).OrderBy(b => Math.Abs(b.Time - cue.Time)).First();
            Log.Info("Snapping cue point from {time}ms to {snappedTime}ms", cue.Time, closestBar.Time);
            cue.Time = closestBar.Time;
        }

        const string UUIDPrefix = "e134b57e-5bc1-4554-";
        static readonly int[] ColorTableIndexes = { 49, 56, 60, 62, 1, 5, 9, 14, 18, 22, 26, 30, 32, 38, 42, 45 };
        static readonly List<int> DefaultColorIndexes = new() { 1, 4, 6, 9, 12, 13, 14, 15 };

        (int Color, int? ColorIndex) GetColor(CuePoint cue, int cueNum)
        {
            var color = -1;
            int? colorIndex = null;
            var val = cueNum - 1;

            if (Config.CueColorEnergy && cue.Energy > 0 && cue.Energy <= 8)
                val = cue.Energy - 1;
            else if (Config.CueColorPhrase && cue.Phrase != null)
            {
                if (!Config.Colors.Any())
                {
                    if (!Config.HotCues)
                        color = cue.Phrase.Kind.Color;
                    else
                        colorIndex = cue.Phrase.Kind.ColorIndex;

                    return (color, colorIndex);
                }

                val = (int)cue.Phrase.Kind.Group;
            }

            if (!Config.HotCues)
            {
                if (!Config.Colors.Any())
                    color = 7 - (val % 8);
                else
                    color = Math.Clamp(Config.Colors[val % Config.Colors.Count], 0, 7);
            }
            else
            {
                var colors = Config.Colors.Any() ? Config.Colors : DefaultColorIndexes;
                colorIndex = ColorTableIndexes[Math.Clamp(colors[val % colors.Count], 0, ColorTableIndexes.Length - 1)];
            }

            return (color, colorIndex);
        }

        int TimeToFrame(double time) => (int)((time * 150.0) / 1000.0);

        Cue CreateCue(CuePoint cue, IList<Cue> cues, Content content, int cueNum, ulong maxId)
        {
            var frame = TimeToFrame(cue.Time);
            var date = DateTime.UtcNow;
            var kind = 0;
            var maxIdHex = maxId.ToString("x12");
            var uuid = $"{UUIDPrefix}{maxIdHex[0..4]}-{maxIdHex[4..]}";
            (var color, var colorIndex) = GetColor(cue, cueNum);

            var isHot = cue.Type == CueType.Hot || (cue.Type == CueType.Memory && Config.HotCues);
            if (isHot)
            {
                var maxKind = cues.Select(c => c.Kind ?? 0).DefaultIfEmpty().Max();
                kind = maxKind + 1;
                if (kind == 4) kind++;
            }

            var newCue = new Cue
            {
                ID = maxId.ToString(),
                InMsec = (int)cue.Time,
                InFrame = frame,
                ContentID = content.ID,
                Kind = kind,
                ColorTableIndex = colorIndex,
                Color = color,
                ContentUUID = content.UUID,
                UUID = uuid,
                created_at = date,
                updated_at = date,
                rb_data_status = 0,
                rb_local_data_status = 0,
                rb_local_deleted = 0,
                rb_local_synced = 0
            };

            if (!string.IsNullOrEmpty(Config.Comment) && cue.Energy > 0)
                newCue.Comment = Config.Comment.Replace("#", cue.Energy.ToString());
            else if (!string.IsNullOrEmpty(cue.Name))
                newCue.Comment = cue.Name;

            return newCue;
        }

        int BeatsToMs(int beats, int bpm) => (int)Math.Round(beats * 60.0 * 1000.0 * 100.0 / bpm);
        int MsToBeats(double ms, int bpm) => (int)Math.Round(bpm * (ms / (60.0 * 1000.0)) / 100.0);
        int Bars(double ms, int bpm) => MsToBeats(ms, bpm) / 4 + 1;


        private static string ToCamelot(int key, int mode)
        {
            // key: 0=C, 1=C#/Db, 2=D, 3=D#/Eb, 4=E, 5=F, 6=F#/Gb, 7=G, 8=G#/Ab, 9=A, 10=A#/Bb, 11=B
            // mode: 1=major → B; 0=minor → A
            // Mapping derived from Camelot Wheel standard
            var major = new string[] {"8B","3B","10B","5B","12B","7B","2B","9B","4B","11B","6B","1B"};
            var minor = new string[] {"5A","12A","7A","2A","9A","4A","11A","6A","1A","8A","3A","10A"};
            var arr = mode == 1 ? major : minor; // default to minor if mode != 1? We'll treat any non-1 as minor per API (0=minor)
            if (key < 0 || key > 11) return null;
            return arr[key];
        }

        /*private string GetOrCreateGenre(SQLiteConnection db, string genreName)
        {
            if (string.IsNullOrWhiteSpace(genreName)) return null;

            var genre = db.Table<Genre>().FirstOrDefault(g => g.Name == genreName);
            if (genre != null) return genre.ID;

            var newId = Genre.GetNextId(db);
            genre = new Genre
            {
                ID = newId,
                Name = genreName,
                UUID = Guid.NewGuid().ToString(),
                created_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };

            Log.Info("Creating new genre {genreName} with ID {genreId}", genreName, newId);
            if (!Config.DryRun)
                db.Insert(genre);

            return newId;
        }

        private bool MapBeatportToContent(SQLiteConnection db, Content content, BeatportTrack track)
        {
            if (track == null) return false;
            bool changed = false;

            // Map Beatport Genre/Subgenre
            var genreName = track.Genres?.FirstOrDefault()?.Name;
            var subGenreName = track.SubGenres?.FirstOrDefault()?.Name;

            var finalGenreName = genreName;
            if (!string.IsNullOrEmpty(subGenreName))
            {
                finalGenreName = $"{genreName} / {subGenreName}";
            }

            if (!string.IsNullOrEmpty(finalGenreName))
            {
                var genreId = GetOrCreateGenre(db, finalGenreName);
                if (genreId != null && content.GenreID != genreId)
                {
                    content.GenreID = genreId;
                    changed = true;
                }
            }

            if (changed)
            {
                content.updated_at = DateTime.UtcNow;
            }

            return changed;
        }

        private bool MapSoundchartsToContent(Content content, SoundchartsSong song)
        {
            if (song == null) return false;
            bool changed = false;

            // Map Soundcharts Key/Mode -> Rekordbox Camelot KeyID 
            if (song.Audio?.Key.HasValue == true && song.Audio.Key.Value >= 0 && song.Audio.Key.Value <= 11)
            {
                var mode = song.Audio.Mode ?? 1; // default to major if missing per API? Mode: 1=major, 0=minor
                var camelot = ToCamelot(song.Audio.Key.Value, mode);
                if (!string.IsNullOrEmpty(camelot) && KeysDictionary.TryGetValue(camelot, out var keyId))
                {
                    content.KeyID = keyId;
                    changed = true;
                }
            }

            if (song.ReleaseDate.HasValue)
            {
                var year = song.ReleaseDate.Value.Year;
                if (!content.ReleaseYear.HasValue || content.ReleaseYear.Value == 0)
                {
                    content.ReleaseYear = year;
                    changed = true;
                }

                var rd = song.ReleaseDate.Value.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz");
                if (string.IsNullOrWhiteSpace(content.ReleaseDate))
                {
                    content.ReleaseDate = rd;
                    changed = true;
                }
            }

            if (changed)
            {
                content.updated_at = DateTime.UtcNow;
            }

            return changed;
        }

        private async Task UpdateMetadataForContentAsync(SQLiteConnection db, Content content, SoundchartsClient client)
        {
            try
            {
                if (content == null) return;
                var isrc = content.ISRC;
                if (string.IsNullOrWhiteSpace(isrc))
                {
                    Log.Info("Skipping Soundcharts fetch for {contentID}: no ISRC in database", content.ID);
                    return;
                }

                var song = await client.GetSongByIsrcAsync(isrc);
                if (song == null)
                {
                    Log.Warn("No Soundcharts data found for ISRC {isrc}", isrc);
                    return;
                }

                if (MapSoundchartsToContent(content, song))
                {
                    Log.Info("Updating content {contentID} with Soundcharts metadata (ISRC {isrc})", content.ID, isrc);
                    if (!Config.DryRun)
                        db.Update(content);
                }
                else
                {
                    Log.Info("No metadata changes required for {contentID}", content.ID);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update metadata from Soundcharts for {contentID}", content?.ID);
            }
        }

        private async Task UpdateMetadataFromBeatportAsync(SQLiteConnection db, Content content, BeatportClient client)
        {
            try
            {
                if (content == null || client.HasFailedAuthentication) return;
                var isrc = content.ISRC;
                if (string.IsNullOrWhiteSpace(isrc))
                {
                    Log.Info("Skipping Beatport fetch for {contentID}: no ISRC in database", content.ID);
                    return;
                }

                var track = await client.GetTrackByIsrcAsync(isrc);
                if (track == null)
                {
                    Log.Warn("No Beatport data found for ISRC {isrc}", isrc);
                    return;
                }

                if (MapBeatportToContent(db, content, track))
                {
                    Log.Info("Updating content {contentID} with Beatport metadata (ISRC {isrc})", content.ID, isrc);
                    if (!Config.DryRun)
                        db.Update(content);
                }
                else
                {
                    Log.Info("No Beatport metadata changes required for {contentID}", content.ID);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update metadata from Beatport for {contentID}", content?.ID);
            }
        }*/
    }
}
