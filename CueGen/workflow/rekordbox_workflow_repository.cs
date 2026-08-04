using SQLite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CueGen.Workflow
{
    public sealed class RekordboxWorkflowRepository
    {
        private readonly SQLiteConnection database;
        private readonly IList<MyTag> tags;
        private readonly IList<SongMyTag> relations;
        private readonly IList<Cue> cues;
        private readonly IList<ContentCue> contentCues;
        private IList<Playlist> playlists;
        private IList<SongPlaylist> playlistRelations;
        private long nextTagId;
        private long nextTagUsn;
        private long nextRelationUsn;
        private long nextContentUsn;
        private ulong nextCueId;
        private long nextCueUsn;
        private long nextContentCueUsn;
        private long nextPlaylistId;
        private long nextPlaylistUsn;
        private long nextPlaylistRelationUsn;

        private const string WorkflowPlaylistUuidPrefix = "51facbf8-0bb1-4f78-";

        public RekordboxWorkflowRepository(SQLiteConnection database)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            tags = database.Table<MyTag>().ToList();
            relations = database.Table<SongMyTag>().ToList();
            cues = database.Table<Cue>().ToList();
            contentCues = database.Table<ContentCue>().ToList();
            nextTagId = tags
                .Select(tag => long.TryParse(tag.ID, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0L)
                .DefaultIfEmpty(0L)
                .Max();
            nextTagUsn = tags.Select(tag => tag.rb_local_usn ?? 0L).DefaultIfEmpty(0L).Max();
            nextRelationUsn = relations.Select(relation => relation.rb_local_usn ?? 0L).DefaultIfEmpty(0L).Max();
            nextContentUsn = database.Table<Content>()
                .Select(content => content.rb_local_usn ?? 0L)
                .DefaultIfEmpty(0L)
                .Max();
            nextCueId = cues
                .Select(cue => ulong.TryParse(cue.ID, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0UL)
                .DefaultIfEmpty(0UL)
                .Max();
            nextCueUsn = cues.Select(cue => cue.rb_local_usn ?? 0L).DefaultIfEmpty(0L).Max();
            nextContentCueUsn = contentCues
                .Select(contentCue => contentCue.rb_local_usn ?? 0L)
                .DefaultIfEmpty(0L)
                .Max();
        }

        public IList<Content> GetContents()
        {
            return database.Table<Content>().ToList();
        }

        public IList<Artist> GetArtists()
        {
            return database.Table<Artist>().ToList();
        }

        public void ValidatePlaylistPreflight(IList<string> desiredPaths)
        {
            if (desiredPaths == null)
                return;

            EnsurePlaylistState();
            foreach (var path in desiredPaths)
            {
                var segments = new[] { WorkflowPlaylistPlan.RootName }
                    .Concat(WorkflowPlaylistPlan.Split(path));
                var parentId = "root";
                var nodes = segments.ToList();
                for (var index = 0; index < nodes.Count; index++)
                {
                    var segment = nodes[index];
                    var matches = playlists
                        .Where(playlist => playlist.ParentID == parentId && playlist.Name == segment)
                        .ToList();
                    if (matches.Count > 1)
                        throw new InvalidOperationException($"Multiple playlists named '{segment}' exist under '{parentId}'");
                    if (matches.Count == 0)
                        break;

                    var existing = matches[0];
                    var isLeaf = index == nodes.Count - 1;
                    var expectedAttribute = isLeaf ? 0 : 1;
                    if (existing.Attribute != expectedAttribute || !IsManagedPlaylist(existing))
                    {
                        throw new InvalidOperationException(
                            $"Playlist path '{path}' collides with an unmanaged Rekordbox item");
                    }

                    parentId = existing.ID;
                }
            }
        }

        public IList<string> GetManagedPlaylistPaths(string contentId)
        {
            EnsurePlaylistState();
            var playlistIds = new HashSet<string>(
                playlistRelations
                    .Where(relation => relation.ContentID == contentId)
                    .Select(relation => relation.PlaylistID),
                StringComparer.Ordinal);
            return playlists
                .Where(playlist => playlistIds.Contains(playlist.ID) && playlist.Attribute == 0)
                .Select(GetManagedPlaylistPath)
                .Where(path => path != null)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        public void SyncPlaylists(string contentId, IList<string> desiredPaths)
        {
            if (desiredPaths == null)
                return;

            EnsurePlaylistState();
            var desired = desiredPaths
                .Select(EnsurePlaylistPath)
                .ToDictionary(playlist => playlist.ID, playlist => playlist, StringComparer.Ordinal);
            var managedPlaylistIds = new HashSet<string>(
                playlists
                    .Where(playlist => playlist.Attribute == 0 && GetManagedPlaylistPath(playlist) != null)
                    .Select(playlist => playlist.ID),
                StringComparer.Ordinal);
            var current = playlistRelations
                .Where(relation => relation.ContentID == contentId && managedPlaylistIds.Contains(relation.PlaylistID))
                .ToList();
            var affectedPlaylists = new HashSet<string>(StringComparer.Ordinal);

            foreach (var group in current.GroupBy(relation => relation.PlaylistID, StringComparer.Ordinal))
            {
                var keep = desired.ContainsKey(group.Key);
                var keptOne = false;
                foreach (var relation in group.OrderBy(relation => relation.TrackNo ?? int.MaxValue))
                {
                    if (keep && !keptOne)
                    {
                        keptOne = true;
                        continue;
                    }

                    database.Delete(relation);
                    playlistRelations.Remove(relation);
                    affectedPlaylists.Add(relation.PlaylistID);
                }
            }

            var currentIds = new HashSet<string>(
                playlistRelations
                    .Where(relation => relation.ContentID == contentId && managedPlaylistIds.Contains(relation.PlaylistID))
                    .Select(relation => relation.PlaylistID),
                StringComparer.Ordinal);
            foreach (var playlist in desired.Values.Where(playlist => !currentIds.Contains(playlist.ID)))
            {
                var now = DateTime.UtcNow;
                var relation = new SongPlaylist
                {
                    ID = Guid.NewGuid().ToString(),
                    UUID = Guid.NewGuid().ToString(),
                    PlaylistID = playlist.ID,
                    ContentID = contentId,
                    TrackNo = playlistRelations
                        .Where(item => item.PlaylistID == playlist.ID)
                        .Select(item => item.TrackNo ?? 0)
                        .DefaultIfEmpty(0)
                        .Max() + 1,
                    rb_local_usn = ++nextPlaylistRelationUsn,
                    created_at = now,
                    updated_at = now
                };
                database.Insert(relation);
                playlistRelations.Add(relation);
            }

            foreach (var playlistId in affectedPlaylists)
                RenumberPlaylist(playlistId);
        }

        public IList<WorkflowHotCueState> GetManagedHotCueStates(
            string contentId,
            WorkflowTaxonomy taxonomy)
        {
            return cues
                .Where(cue => cue.ContentID == contentId && IsManagedCue(cue))
                .Select(cue => (Cue: cue, Slot: KindToSlot(cue.Kind)))
                .Where(item => item.Slot != null && taxonomy.HotCues.ContainsKey(item.Slot))
                .Select(item =>
                {
                    var mapping = taxonomy.HotCues[item.Slot];
                    return new WorkflowHotCueState
                    {
                        Slot = item.Slot,
                        Name = item.Cue.Comment,
                        Color = mapping.Color,
                        ColorTableIndex = item.Cue.ColorTableIndex,
                        PositionMs = item.Cue.InMsec ?? 0,
                        LoopBeats = GetLoopBeats(item.Cue)
                    };
                })
                .OrderBy(cue => cue.Slot, StringComparer.Ordinal)
                .ToList();
        }

        public IList<WorkflowMemoryCueState> GetWorkflowMemoryCueStates(string contentId)
        {
            return cues
                .Where(cue => cue.ContentID == contentId && cue.Kind == 0)
                .Where(cue => IsManagedCue(cue) ||
                    string.Equals(cue.Comment, WorkflowMemoryCueRuleEngine.ManualVocalName, StringComparison.Ordinal))
                .Select(ToMemoryCueState)
                .OrderBy(cue => cue.PositionMs)
                .ThenBy(cue => cue.Name, StringComparer.Ordinal)
                .ToList();
        }

        public IList<WorkflowMemoryCueState> GetAllMemoryCueStates(string contentId)
        {
            return cues
                .Where(cue => cue.ContentID == contentId && cue.Kind == 0)
                .Select(ToMemoryCueState)
                .OrderBy(cue => cue.PositionMs)
                .ThenBy(cue => cue.Name, StringComparer.Ordinal)
                .ToList();
        }

        public void ValidateHotCuePreflight(Content content, IList<WorkflowHotCue> desiredCues)
        {
            if (desiredCues == null)
                return;

            foreach (var desired in desiredCues)
            {
                var kind = SlotToKind(desired.Slot);
                if (cues.Any(cue => cue.ContentID == content.ID && cue.Kind == kind && !IsManagedCue(cue)))
                {
                    throw new InvalidOperationException(
                        $"Manual Hot Cue {desired.Slot} already occupies the requested slot; no cue was changed");
                }

                if (content.Length.HasValue && desired.PositionMs > content.Length.Value * 1000)
                    throw new InvalidOperationException($"Hot Cue {desired.Slot} is outside the track duration");

                if (desired.LoopBeats.HasValue)
                    GetLoopEnd(content, desired);
            }
        }

        public bool IsContentCueConsistent(string contentId)
        {
            var rows = contentCues.Where(row => row.ContentID == contentId).ToList();
            if (rows.Count != 1)
                return false;

            var allCues = GetOrderedCues(contentId);
            var expected = new ContentCue();
            expected.SetCues(allCues);
            return rows[0].rb_cue_count == allCues.Count &&
                string.Equals(rows[0].Cues, expected.Cues, StringComparison.Ordinal);
        }

        public bool SyncHotCues(
            Content content,
            IList<WorkflowHotCue> desiredCues,
            WorkflowTaxonomy taxonomy)
        {
            if (desiredCues == null)
                return false;

            ValidateHotCuePreflight(content, desiredCues);
            var desiredByKind = desiredCues.ToDictionary(cue => SlotToKind(cue.Slot));
            var managed = cues
                .Where(cue => cue.ContentID == content.ID && IsManagedCue(cue) && KindToSlot(cue.Kind) != null)
                .OrderBy(cue => ParseCueId(cue.ID))
                .ToList();
            var changed = false;

            foreach (var group in managed.GroupBy(cue => cue.Kind ?? 0))
            {
                if (!desiredByKind.TryGetValue(group.Key, out var desired))
                {
                    foreach (var cue in group)
                        changed |= DeleteCue(cue);
                    continue;
                }

                var keep = group.First();
                foreach (var duplicate in group.Skip(1))
                    changed |= DeleteCue(duplicate);
                changed |= UpdateCue(keep, content, desired, taxonomy.HotCues[desired.Slot]);
                desiredByKind.Remove(group.Key);
            }

            foreach (var desired in desiredByKind.Values.OrderBy(cue => cue.Slot, StringComparer.Ordinal))
            {
                var cue = CreateCue(content, desired, taxonomy.HotCues[desired.Slot]);
                database.Insert(cue);
                cues.Add(cue);
                changed = true;
            }

            if (changed || !IsContentCueConsistent(content.ID))
            {
                SyncContentCue(content);
                changed = true;
            }
            return changed;
        }

        public void ValidateMemoryCuePreflight(
            Content content,
            IList<WorkflowMemoryCue> desiredCues)
        {
            if (desiredCues == null)
                return;

            var existingVocal = cues.Count(cue =>
                cue.ContentID == content.ID &&
                cue.Kind == 0 &&
                string.Equals(cue.Comment, WorkflowMemoryCueRuleEngine.ManualVocalName, StringComparison.Ordinal));
            if (existingVocal > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple Memory Cues named '{WorkflowMemoryCueRuleEngine.ManualVocalName}' exist");
            }

            var unmanagedCount = cues.Count(cue =>
                cue.ContentID == content.ID && cue.Kind == 0 && !IsManagedCue(cue));
            var unmanagedVocal = cues.Any(cue =>
                cue.ContentID == content.ID &&
                cue.Kind == 0 &&
                !IsManagedCue(cue) &&
                string.Equals(cue.Comment, WorkflowMemoryCueRuleEngine.ManualVocalName, StringComparison.Ordinal));
            var finalCount = unmanagedCount + desiredCues.Count - (unmanagedVocal ? 1 : 0);
            if (finalCount > WorkflowMemoryCueRuleEngine.MaximumMemoryCues)
            {
                throw new InvalidOperationException(
                    $"The generated result would exceed {WorkflowMemoryCueRuleEngine.MaximumMemoryCues} Memory Cues");
            }

            foreach (var desired in desiredCues)
            {
                if (desired.PositionMs < 0 ||
                    content.Length.HasValue && desired.PositionMs > content.Length.Value * 1000)
                {
                    throw new InvalidOperationException($"Memory Cue '{desired.Name}' is outside the track duration");
                }
                if (desired.LoopEndMs.HasValue &&
                    content.Length.HasValue &&
                    desired.LoopEndMs.Value > content.Length.Value * 1000)
                {
                    throw new InvalidOperationException($"Memory Cue '{desired.Name}' loop extends beyond the track duration");
                }
                if (desired.LoopBeats.HasValue && !desired.LoopEndMs.HasValue)
                    throw new InvalidOperationException($"Memory Cue '{desired.Name}' loop has no end position");
            }
        }

        public bool SyncMemoryCues(Content content, IList<WorkflowMemoryCue> desiredCues)
        {
            if (desiredCues == null)
                return false;

            ValidateMemoryCuePreflight(content, desiredCues);
            var changed = false;
            var desiredVocal = desiredCues.Single(cue =>
                string.Equals(cue.Name, WorkflowMemoryCueRuleEngine.ManualVocalName, StringComparison.Ordinal));
            var existingVocal = cues.SingleOrDefault(cue =>
                cue.ContentID == content.ID &&
                cue.Kind == 0 &&
                string.Equals(cue.Comment, WorkflowMemoryCueRuleEngine.ManualVocalName, StringComparison.Ordinal));
            if (existingVocal == null)
            {
                var vocal = CreateMemoryCue(content, desiredVocal);
                database.Insert(vocal);
                cues.Add(vocal);
                existingVocal = vocal;
                changed = true;
            }

            var availableManaged = cues
                .Where(cue => cue.ContentID == content.ID && cue.Kind == 0 && IsManagedCue(cue))
                .Where(cue => cue != existingVocal)
                .OrderBy(cue => ParseCueId(cue.ID))
                .ToList();
            foreach (var desired in desiredCues
                .Where(cue => !string.Equals(cue.Name, WorkflowMemoryCueRuleEngine.ManualVocalName, StringComparison.Ordinal))
                .OrderBy(cue => cue.PositionMs)
                .ThenBy(cue => cue.Name, StringComparer.Ordinal))
            {
                var matching = availableManaged.FirstOrDefault(cue => MemoryCueMatches(cue, desired));
                if (matching != null)
                {
                    availableManaged.Remove(matching);
                    continue;
                }

                var created = CreateMemoryCue(content, desired);
                database.Insert(created);
                cues.Add(created);
                changed = true;
            }

            foreach (var obsolete in availableManaged)
                changed |= DeleteCue(obsolete);

            if (changed || !IsContentCueConsistent(content.ID))
            {
                SyncContentCue(content);
                changed = true;
            }
            return changed;
        }

        public IList<string> GetAssignedTagNames(string contentId, string categoryName)
        {
            var root = FindSingleTag(categoryName, "root", required: false);
            if (root == null)
                return new List<string>();

            var childNames = tags
                .Where(tag => tag.ParentId == root.ID)
                .ToDictionary(tag => tag.ID, tag => tag.Name);
            return relations
                .Where(relation => relation.ContentID == contentId && childNames.ContainsKey(relation.MyTagID))
                .Select(relation => childNames[relation.MyTagID])
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }

        public void UpdateMetadata(Content content, string colorId, int? energy)
        {
            var changed = false;
            if (colorId != null && !string.Equals(content.ColorID, colorId, StringComparison.Ordinal))
            {
                content.ColorID = colorId;
                changed = true;
            }

            if (energy.HasValue && content.Rating != energy)
            {
                content.Rating = energy;
                changed = true;
            }

            if (!changed)
                return;

            content.updated_at = DateTime.UtcNow;
            content.rb_local_usn = ++nextContentUsn;
            database.Update(content);
        }

        public void SyncCategory(string contentId, string categoryName, IEnumerable<string> desiredNames)
        {
            var desired = new HashSet<string>(desiredNames, StringComparer.Ordinal);
            var root = EnsureTag(categoryName, "root", attribute: 1);
            var desiredTags = desired
                .Select(name => EnsureTag(name, root.ID, attribute: 0))
                .ToDictionary(tag => tag.ID, tag => tag, StringComparer.Ordinal);
            var categoryTagIds = new HashSet<string>(
                tags.Where(tag => tag.ParentId == root.ID).Select(tag => tag.ID),
                StringComparer.Ordinal);
            var current = relations
                .Where(relation => relation.ContentID == contentId && categoryTagIds.Contains(relation.MyTagID))
                .ToList();

            foreach (var group in current.GroupBy(relation => relation.MyTagID, StringComparer.Ordinal))
            {
                var keep = desiredTags.ContainsKey(group.Key);
                var keptOne = false;
                foreach (var relation in group)
                {
                    if (keep && !keptOne)
                    {
                        keptOne = true;
                        continue;
                    }

                    database.Delete(relation);
                    relations.Remove(relation);
                }
            }

            var currentIds = new HashSet<string>(
                relations
                    .Where(relation => relation.ContentID == contentId && categoryTagIds.Contains(relation.MyTagID))
                    .Select(relation => relation.MyTagID),
                StringComparer.Ordinal);
            foreach (var tag in desiredTags.Values.Where(tag => !currentIds.Contains(tag.ID)))
            {
                var now = DateTime.UtcNow;
                var relation = new SongMyTag
                {
                    ID = Guid.NewGuid().ToString(),
                    UUID = Guid.NewGuid().ToString(),
                    MyTagID = tag.ID,
                    ContentID = contentId,
                    rb_local_usn = ++nextRelationUsn,
                    created_at = now,
                    updated_at = now
                };
                database.Insert(relation);
                relations.Add(relation);
            }
        }

        private MyTag EnsureTag(string name, string parentId, int attribute)
        {
            var existing = FindSingleTag(name, parentId, required: false);
            if (existing != null)
                return existing;

            var siblings = tags.Where(tag => tag.ParentId == parentId).ToList();
            var now = DateTime.UtcNow;
            var tag = new MyTag
            {
                ID = (++nextTagId).ToString(CultureInfo.InvariantCulture),
                Seq = siblings.Select(sibling => sibling.Seq ?? 0).DefaultIfEmpty(0).Max() + 1,
                Name = name,
                Attribute = attribute,
                ParentId = parentId,
                UUID = Guid.NewGuid().ToString(),
                rb_local_usn = ++nextTagUsn,
                created_at = now,
                updated_at = now
            };
            database.Insert(tag);
            tags.Add(tag);
            return tag;
        }

        private void EnsurePlaylistState()
        {
            if (playlists != null)
                return;

            foreach (var tableName in new[] { "djmdPlaylist", "djmdSongPlaylist" })
            {
                var count = database.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?",
                    tableName);
                if (count != 1)
                {
                    throw new InvalidOperationException(
                        $"Rekordbox playlist table '{tableName}' is unavailable; no playlist was changed");
                }
            }

            playlists = database.Table<Playlist>().ToList();
            playlistRelations = database.Table<SongPlaylist>().ToList();
            nextPlaylistId = playlists
                .Select(playlist => long.TryParse(
                    playlist.ID,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var id) ? id : 0L)
                .DefaultIfEmpty(0L)
                .Max();
            nextPlaylistUsn = playlists
                .Select(playlist => playlist.rb_local_usn ?? 0L)
                .DefaultIfEmpty(0L)
                .Max();
            nextPlaylistRelationUsn = playlistRelations
                .Select(relation => relation.rb_local_usn ?? 0L)
                .DefaultIfEmpty(0L)
                .Max();
        }

        private Playlist EnsurePlaylistPath(string path)
        {
            var segments = new[] { WorkflowPlaylistPlan.RootName }
                .Concat(WorkflowPlaylistPlan.Split(path))
                .ToList();
            var parentId = "root";
            Playlist current = null;
            for (var index = 0; index < segments.Count; index++)
            {
                var attribute = index == segments.Count - 1 ? 0 : 1;
                current = EnsurePlaylist(segments[index], parentId, attribute, path);
                parentId = current.ID;
            }

            return current;
        }

        private Playlist EnsurePlaylist(string name, string parentId, int attribute, string requestedPath)
        {
            var matches = playlists
                .Where(playlist => playlist.ParentID == parentId && playlist.Name == name)
                .ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException($"Multiple playlists named '{name}' exist under '{parentId}'");
            if (matches.Count == 1)
            {
                var existing = matches[0];
                if (existing.Attribute != attribute || !IsManagedPlaylist(existing))
                {
                    throw new InvalidOperationException(
                        $"Playlist path '{requestedPath}' collides with an unmanaged Rekordbox item");
                }

                return existing;
            }

            if (nextPlaylistId == long.MaxValue)
                throw new InvalidOperationException("No Rekordbox playlist ID is available");

            nextPlaylistId++;
            var now = DateTime.UtcNow;
            var playlist = new Playlist
            {
                ID = nextPlaylistId.ToString(CultureInfo.InvariantCulture),
                Seq = playlists
                    .Where(item => item.ParentID == parentId)
                    .Select(item => item.Seq ?? 0)
                    .DefaultIfEmpty(0)
                    .Max() + 1,
                Name = name,
                Attribute = attribute,
                ParentID = parentId,
                UUID = CreateManagedPlaylistUuid(nextPlaylistId),
                rb_local_usn = ++nextPlaylistUsn,
                created_at = now,
                updated_at = now
            };
            database.Insert(playlist);
            playlists.Add(playlist);
            return playlist;
        }

        private string GetManagedPlaylistPath(Playlist playlist)
        {
            if (!IsManagedPlaylist(playlist))
                return null;

            var byId = playlists.ToDictionary(item => item.ID, StringComparer.Ordinal);
            var segments = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = playlist;
            while (current != null)
            {
                if (!seen.Add(current.ID))
                    throw new InvalidOperationException("A cycle exists in the managed playlist hierarchy");
                if (!IsManagedPlaylist(current))
                    return null;

                segments.Add(current.Name);
                if (current.ParentID == "root")
                    break;
                if (!byId.TryGetValue(current.ParentID, out current))
                    return null;
            }

            segments.Reverse();
            if (segments.Count != 3 || segments[0] != WorkflowPlaylistPlan.RootName)
                return null;
            return string.Join("/", segments.Skip(1));
        }

        private void RenumberPlaylist(string playlistId)
        {
            var now = DateTime.UtcNow;
            var ordered = playlistRelations
                .Where(relation => relation.PlaylistID == playlistId)
                .OrderBy(relation => relation.TrackNo ?? int.MaxValue)
                .ThenBy(relation => relation.ID, StringComparer.Ordinal)
                .ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                var trackNo = index + 1;
                if (ordered[index].TrackNo == trackNo)
                    continue;

                ordered[index].TrackNo = trackNo;
                ordered[index].rb_local_usn = ++nextPlaylistRelationUsn;
                ordered[index].updated_at = now;
                database.Update(ordered[index]);
            }
        }

        private static bool IsManagedPlaylist(Playlist playlist)
        {
            return playlist.UUID != null &&
                playlist.UUID.StartsWith(WorkflowPlaylistUuidPrefix, StringComparison.Ordinal);
        }

        private static string CreateManagedPlaylistUuid(long id)
        {
            var idHex = id.ToString("x16", CultureInfo.InvariantCulture);
            return $"{WorkflowPlaylistUuidPrefix}{idHex.Substring(0, 4)}-{idHex.Substring(4)}";
        }

        private MyTag FindSingleTag(string name, string parentId, bool required)
        {
            var matches = tags
                .Where(tag => tag.ParentId == parentId && string.Equals(tag.Name, name, StringComparison.Ordinal))
                .ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException($"Multiple My Tags named '{name}' exist under '{parentId}'");
            if (required && matches.Count == 0)
                throw new InvalidOperationException($"My Tag '{name}' does not exist under '{parentId}'");
            return matches.SingleOrDefault();
        }

        private static WorkflowMemoryCueState ToMemoryCueState(Cue cue)
        {
            return new WorkflowMemoryCueState
            {
                Name = cue.Comment,
                PositionMs = cue.InMsec ?? 0,
                LoopBeats = GetLoopBeats(cue),
                LoopEndMs = cue.OutMsec.HasValue && cue.OutMsec.Value >= 0 ? cue.OutMsec : null,
                Managed = IsManagedCue(cue)
            };
        }

        private Cue CreateMemoryCue(Content content, WorkflowMemoryCue desired)
        {
            var id = ++nextCueId;
            var now = DateTime.UtcNow;
            var cue = new Cue
            {
                ID = id.ToString(CultureInfo.InvariantCulture),
                ContentID = content.ID,
                InMsec = desired.PositionMs,
                InFrame = Generator.TimeToFrame(desired.PositionMs),
                Kind = 0,
                Color = -1,
                Comment = desired.Name,
                ContentUUID = content.UUID,
                UUID = Generator.CreateManagedCueUuid(id),
                rb_local_usn = ++nextCueUsn,
                created_at = now,
                updated_at = now
            };
            SetMemoryLoop(cue, desired);
            return cue;
        }

        private static bool MemoryCueMatches(Cue cue, WorkflowMemoryCue desired)
        {
            var expectedOut = desired.LoopBeats.HasValue ? desired.LoopEndMs : -1;
            var expectedOutFrame = desired.LoopBeats.HasValue
                ? Generator.TimeToFrame(desired.LoopEndMs.Value)
                : 0;
            var expectedLoopSize = desired.LoopBeats.HasValue
                ? 0x10000 * desired.LoopBeats.Value + 1
                : (int?)null;
            var expectedActiveLoop = desired.LoopBeats.HasValue ? 1 : (int?)null;
            var expectedColor = desired.LoopBeats.HasValue ? 255 : -1;
            return cue.InMsec == desired.PositionMs &&
                cue.InFrame == Generator.TimeToFrame(desired.PositionMs) &&
                cue.OutMsec == expectedOut &&
                cue.OutFrame == expectedOutFrame &&
                cue.Kind == 0 &&
                cue.Color == expectedColor &&
                cue.ActiveLoop == expectedActiveLoop &&
                cue.BeatLoopSize == expectedLoopSize &&
                cue.Comment == desired.Name;
        }

        private static void SetMemoryLoop(Cue cue, WorkflowMemoryCue desired)
        {
            cue.OutMsec = -1;
            cue.OutFrame = 0;
            cue.ActiveLoop = null;
            cue.BeatLoopSize = null;
            cue.CueMicrosec = null;
            cue.Color = -1;
            if (!desired.LoopBeats.HasValue)
                return;
            if (!desired.LoopEndMs.HasValue)
                throw new InvalidOperationException($"Memory Cue '{desired.Name}' loop has no end position");

            cue.OutMsec = desired.LoopEndMs.Value;
            cue.OutFrame = Generator.TimeToFrame(desired.LoopEndMs.Value);
            cue.ActiveLoop = 1;
            cue.BeatLoopSize = 0x10000 * desired.LoopBeats.Value + 1;
            cue.CueMicrosec = 0;
            cue.Color = 255;
        }

        private Cue CreateCue(
            Content content,
            WorkflowHotCue desired,
            WorkflowHotCueMapping mapping)
        {
            var id = ++nextCueId;
            var now = DateTime.UtcNow;
            var cue = new Cue
            {
                ID = id.ToString(CultureInfo.InvariantCulture),
                ContentID = content.ID,
                InMsec = desired.PositionMs.Value,
                InFrame = Generator.TimeToFrame(desired.PositionMs.Value),
                Kind = SlotToKind(desired.Slot),
                Color = -1,
                ColorTableIndex = mapping.ColorTableIndex,
                Comment = desired.Name,
                ContentUUID = content.UUID,
                UUID = Generator.CreateManagedCueUuid(id),
                rb_local_usn = ++nextCueUsn,
                created_at = now,
                updated_at = now
            };
            SetLoop(cue, content, desired);
            return cue;
        }

        private bool UpdateCue(
            Cue cue,
            Content content,
            WorkflowHotCue desired,
            WorkflowHotCueMapping mapping)
        {
            var expectedOut = desired.LoopBeats.HasValue ? GetLoopEnd(content, desired) : -1;
            var expectedOutFrame = desired.LoopBeats.HasValue ? Generator.TimeToFrame(expectedOut) : 0;
            var expectedLoopSize = desired.LoopBeats.HasValue ? 0x10000 * desired.LoopBeats.Value + 1 : (int?)null;
            var expectedActiveLoop = desired.LoopBeats.HasValue ? 1 : (int?)null;
            var expectedColor = desired.LoopBeats.HasValue ? 255 : -1;
            if (cue.InMsec == desired.PositionMs &&
                cue.InFrame == Generator.TimeToFrame(desired.PositionMs.Value) &&
                cue.OutMsec == expectedOut &&
                cue.OutFrame == expectedOutFrame &&
                cue.Kind == SlotToKind(desired.Slot) &&
                cue.Color == expectedColor &&
                cue.ColorTableIndex == mapping.ColorTableIndex &&
                cue.ActiveLoop == expectedActiveLoop &&
                cue.BeatLoopSize == expectedLoopSize &&
                cue.Comment == desired.Name &&
                cue.ContentUUID == content.UUID)
            {
                return false;
            }

            cue.InMsec = desired.PositionMs.Value;
            cue.InFrame = Generator.TimeToFrame(desired.PositionMs.Value);
            cue.Kind = SlotToKind(desired.Slot);
            cue.ColorTableIndex = mapping.ColorTableIndex;
            cue.Comment = desired.Name;
            cue.ContentUUID = content.UUID;
            SetLoop(cue, content, desired);
            cue.rb_local_usn = ++nextCueUsn;
            cue.updated_at = DateTime.UtcNow;
            database.Update(cue);
            return true;
        }

        private void SetLoop(Cue cue, Content content, WorkflowHotCue desired)
        {
            cue.OutMsec = -1;
            cue.OutFrame = 0;
            cue.ActiveLoop = null;
            cue.BeatLoopSize = null;
            cue.CueMicrosec = null;
            cue.Color = -1;
            if (!desired.LoopBeats.HasValue)
                return;

            var outMsec = GetLoopEnd(content, desired);
            cue.OutMsec = outMsec;
            cue.OutFrame = Generator.TimeToFrame(outMsec);
            cue.ActiveLoop = 1;
            cue.BeatLoopSize = 0x10000 * desired.LoopBeats.Value + 1;
            cue.CueMicrosec = 0;
            cue.Color = 255;
        }

        private static int GetLoopEnd(Content content, WorkflowHotCue desired)
        {
            if (!desired.LoopEndMs.HasValue && (!content.BPM.HasValue || content.BPM.Value <= 0))
                throw new InvalidOperationException($"Hot Cue {desired.Slot} loop requires a valid track BPM");

            var outMsec = desired.LoopEndMs ??
                desired.PositionMs.Value + Generator.BeatsToMs(desired.LoopBeats.Value, content.BPM.Value);
            if (content.Length.HasValue && outMsec > content.Length.Value * 1000)
                throw new InvalidOperationException($"Hot Cue {desired.Slot} loop extends beyond the track duration");
            return outMsec;
        }

        private bool DeleteCue(Cue cue)
        {
            database.Delete(cue);
            cues.Remove(cue);
            return true;
        }

        private void SyncContentCue(Content content)
        {
            var rows = contentCues.Where(row => row.ContentID == content.ID).ToList();
            if (rows.Count > 1)
                throw new InvalidOperationException($"Multiple contentCue rows exist for content '{content.ID}'");

            var allCues = GetOrderedCues(content.ID);
            var now = DateTime.UtcNow;
            var contentCue = rows.SingleOrDefault();
            if (contentCue == null)
            {
                contentCue = new ContentCue
                {
                    ID = content.UUID,
                    ContentID = content.ID,
                    UUID = Guid.NewGuid().ToString(),
                    created_at = now
                };
                contentCue.SetCues(allCues);
                contentCue.rb_cue_count = allCues.Count;
                contentCue.rb_local_usn = ++nextContentCueUsn;
                contentCue.updated_at = now;
                database.Insert(contentCue);
                contentCues.Add(contentCue);
                return;
            }

            contentCue.SetCues(allCues);
            contentCue.rb_cue_count = allCues.Count;
            contentCue.rb_local_usn = ++nextContentCueUsn;
            contentCue.updated_at = now;
            database.Update(contentCue);
        }

        private IList<Cue> GetOrderedCues(string contentId)
        {
            return cues
                .Where(cue => cue.ContentID == contentId)
                .OrderBy(cue => cue.Kind ?? 0)
                .ThenBy(cue => cue.InMsec ?? 0)
                .ThenBy(cue => ParseCueId(cue.ID))
                .ToList();
        }

        private static bool IsManagedCue(Cue cue)
        {
            return cue.UUID != null && cue.UUID.StartsWith(Generator.UUIDPrefix, StringComparison.Ordinal);
        }

        private static int SlotToKind(string slot)
        {
            var index = slot[0] - 'A' + 1;
            return index >= 4 ? index + 1 : index;
        }

        private static string KindToSlot(int? kind)
        {
            if (!kind.HasValue || kind.Value < 1 || kind.Value > 9 || kind.Value == 4)
                return null;
            var index = kind.Value > 4 ? kind.Value - 1 : kind.Value;
            return ((char)('A' + index - 1)).ToString();
        }

        private static int? GetLoopBeats(Cue cue)
        {
            if (!cue.BeatLoopSize.HasValue || cue.BeatLoopSize.Value <= 1)
                return null;
            return (cue.BeatLoopSize.Value - 1) / 0x10000;
        }

        private static ulong ParseCueId(string id)
        {
            return ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : ulong.MaxValue;
        }
    }
}
