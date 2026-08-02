using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CueGen.Workflow
{
    public sealed class WorkflowTrackResolver
    {
        private static readonly StringComparison PathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public Content Resolve(
            WorkflowTrackIdentity identity,
            IEnumerable<Content> contents,
            IEnumerable<Artist> artists,
            string databasePath)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))
                ?? throw new InvalidOperationException("The database directory could not be resolved");
            var requestedPath = NormalizePath(identity.Path, databaseDirectory);
            if (!File.Exists(requestedPath))
                throw new FileNotFoundException("The verified track file does not exist", requestedPath);

            var candidates = contents
                .Where(content => string.Equals(
                    NormalizePath(content.FolderPath, databaseDirectory),
                    requestedPath,
                    PathComparison))
                .ToList();

            if (candidates.Count == 0)
                throw new InvalidOperationException($"No Rekordbox content matches path '{identity.Path}'");
            if (candidates.Count > 1)
                throw new InvalidOperationException($"Multiple Rekordbox contents match path '{identity.Path}'");

            var content = candidates[0];
            if (!string.IsNullOrWhiteSpace(identity.Isrc) &&
                !string.Equals(NormalizeIsrc(identity.Isrc), NormalizeIsrc(content.ISRC), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The resolved track ISRC does not match the import document");
            }

            if (!string.IsNullOrWhiteSpace(identity.Title) &&
                !string.Equals(NormalizeText(identity.Title), NormalizeText(content.Title), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The resolved track title does not match the import document");
            }

            if (!string.IsNullOrWhiteSpace(identity.Artist))
            {
                var artistMatches = artists.Where(artist => artist.ID == content.ArtistID).ToList();
                if (artistMatches.Count != 1 ||
                    !string.Equals(
                        NormalizeText(identity.Artist),
                        NormalizeText(artistMatches[0].Name),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The resolved track artist does not match the import document");
                }
            }

            return content;
        }

        private static string NormalizePath(string path, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var combined = Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);
            return Path.GetFullPath(combined)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string NormalizeIsrc(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static string NormalizeText(string value)
        {
            return (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        }
    }
}
