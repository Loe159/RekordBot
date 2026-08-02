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
        private long nextTagId;
        private long nextTagUsn;
        private long nextRelationUsn;
        private long nextContentUsn;

        public RekordboxWorkflowRepository(SQLiteConnection database)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            tags = database.Table<MyTag>().ToList();
            relations = database.Table<SongMyTag>().ToList();
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
        }

        public IList<Content> GetContents()
        {
            return database.Table<Content>().ToList();
        }

        public IList<Artist> GetArtists()
        {
            return database.Table<Artist>().ToList();
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
    }
}
