using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CueGen
{
    public static class RekordboxSafety
    {
        public static string ValidateDatabase(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("A Rekordbox database path is required", nameof(databasePath));

            var fullPath = Path.GetFullPath(databasePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Rekordbox database not found", fullPath);

            if (new FileInfo(fullPath).Length == 0)
                throw new InvalidDataException("Rekordbox database is empty");

            return fullPath;
        }

        public static bool IsRekordboxRunning()
        {
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName("rekordbox");
                return processes.Length > 0;
            }
            finally
            {
                if (processes != null)
                {
                    foreach (var process in processes)
                        process.Dispose();
                }
            }
        }

        public static string CreateVerifiedBackup(string databasePath)
        {
            var sourcePath = ValidateDatabase(databasePath);
            var directory = Path.GetDirectoryName(sourcePath)
                ?? throw new InvalidOperationException("The database directory could not be resolved");
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var extension = Path.GetExtension(sourcePath);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-fff");
            var backupPath = Path.Combine(directory, $"{fileName}.backup.{timestamp}{extension}");
            var suffix = 1;

            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(directory, $"{fileName}.backup.{timestamp}.{suffix}{extension}");
                suffix++;
            }

            File.Copy(sourcePath, backupPath, overwrite: false);

            if (!FilesMatch(sourcePath, backupPath))
            {
                File.Delete(backupPath);
                throw new IOException("The Rekordbox database backup could not be verified");
            }

            return backupPath;
        }

        public static bool FilesMatch(string firstPath, string secondPath)
        {
            var first = new FileInfo(firstPath);
            var second = new FileInfo(secondPath);
            if (!first.Exists || !second.Exists || first.Length != second.Length)
                return false;

            using var algorithm = SHA256.Create();
            using var firstStream = File.Open(first.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var firstHash = algorithm.ComputeHash(firstStream);
            using var secondStream = File.Open(second.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var secondHash = algorithm.ComputeHash(secondStream);
            return firstHash.SequenceEqual(secondHash);
        }
    }
}
