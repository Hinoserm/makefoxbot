using MySqlConnector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public static class FoxImageArchiver
    {
        private static readonly string _liveRoot = "../data/images";
        private static readonly string _archiveRoot = "../data/archive/images";
        private static readonly Regex _hashRegex = new Regex("^[A-Fa-f0-9]{40}\\.[a-zA-Z0-9]+$", RegexOptions.Compiled);


        private static FoxTelegram? _telegram = null;
        private static TL.Message? _telegramMessage = null;

        private static int _directoryCount = 0;
        private static int _currentDirectoryIndex = 0;

        private static DateTime _lastStatusUpdate = DateTime.MinValue;

        // Semaphore to ensure only one archiver runs at a time
        private static readonly SemaphoreSlim _archiverSemaphore = new SemaphoreSlim(1, 1);


        /// <summary>
        /// Recursively removes empty directories under the live root.
        /// Traverses depth-first so it clears children before parents.
        /// Stops at _liveRoot itself (never deletes it).
        /// </summary>
        public static void CleanupEmptyTree()
        {
            CleanupEmptyTreeRecursive(new DirectoryInfo(_liveRoot));
        }

        private static void CleanupEmptyTreeRecursive(DirectoryInfo dir)
        {
            foreach (var sub in dir.GetDirectories())
            {
                CleanupEmptyTreeRecursive(sub);

                // After cleaning children, check this subdir
                if (sub.Exists && sub.GetFileSystemInfos().Length == 0)
                {
                    try
                    {
                        sub.Delete();
                        FoxLog.WriteLine($"Deleted empty directory: {sub.FullName}");
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex, $"Failed to delete {sub.FullName}");
                    }
                }
            }
        }

        /// <summary>
        /// Archive all files in one hour-directory.
        /// </summary>
        private static async Task ArchiveDirectoryAsync(string relativeHourPath)
        {
            string sourceDir = Path.Combine(_liveRoot, relativeHourPath);
            string targetDir = Path.Combine(_archiveRoot, relativeHourPath);

            if (!Directory.Exists(sourceDir))
                return;

            var files = Directory.GetFiles(sourceDir);
            var validFiles = files
                .Where(f => _hashRegex.IsMatch(Path.GetFileName(f)))
                .ToList();

            if (validFiles.Count == 0)
                return;

            var cancellationToken = FoxMain.CancellationToken;

            if (cancellationToken.IsCancellationRequested)
            {
                // Let the user know.
                if (_telegram is not null && _telegramMessage is not null)
                {
                    try
                    {
                        var msgStr = $"Archive cancelled. Processed {_currentDirectoryIndex} of {_directoryCount} directories.";
                        await _telegram.EditMessageAsync(_telegramMessage.ID, msgStr);
                    }
                    catch
                    { /* ignore telegram errors */ }                   
                }

                FoxLog.WriteLine("Archiver cancelled.");

                throw new OperationCanceledException();
            }

            if (_telegram is not null && _telegramMessage is not null)
            {
                if ((DateTime.UtcNow - _lastStatusUpdate).TotalSeconds >= 2)
                {
                    var msgStr = $"Archiving directory {_currentDirectoryIndex + 1} of {_directoryCount}\r\n\r\n{relativeHourPath}\r\n\r\n({validFiles.Count} files)";
                    await _telegram.EditMessageAsync(_telegramMessage.ID, msgStr);
                    _lastStatusUpdate = DateTime.UtcNow;
                }
            }

            try
            {
                Directory.CreateDirectory(targetDir);

                // Copy files
                foreach (var src in validFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException();

                    string dest = Path.Combine(targetDir, Path.GetFileName(src));
                    File.Copy(src, dest, overwrite: true);
                }

                // Update DB in batches
                using (var conn = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await conn.OpenAsync(cancellationToken);
                    using var tx = await conn.BeginTransactionAsync(cancellationToken);

                    const int batchSize = 500; // tune as needed
                    for (int i = 0; i < validFiles.Count; i += batchSize)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException();

                        var batch = validFiles.Skip(i).Take(batchSize).ToList();

                        // build IN clause with parameters
                        var paramNames = new List<string>();
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;

                        int pIndex = 0;
                        foreach (var src in batch)
                        {
                            string hash = Path.GetFileNameWithoutExtension(src);
                            string pname = $"@hash{pIndex++}";
                            cmd.Parameters.AddWithValue(pname, hash);
                            paramNames.Add(pname);
                        }

                        cmd.CommandText = $@"
                            UPDATE images
                            SET image_file = CONCAT('archive/', image_file)
                            WHERE sha1hash IN ({string.Join(",", paramNames)})
                              AND image_file NOT LIKE 'archive/%';";

                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await tx.CommitAsync(cancellationToken);
                }

                // Success: delete originals
                Directory.Delete(sourceDir, recursive: true);
                FoxLog.WriteLine($"Archive complete: {sourceDir} -> {targetDir} ({validFiles.Count} files)", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Archive failed for {sourceDir}");
                // Rollback archive dir
                // actually don't, because we might have partial data
                //if (Directory.Exists(targetDir))
                //{
                //    try {
                //        Directory.Delete(targetDir, recursive: true);
                //    }
                //    catch { /* ignore cleanup errors */ }
                //}
                throw;
            }
        }

        /// <summary>
        /// Archives all directories older than the cutoff date.
        /// </summary>
        public static async Task ArchiveOlderThanAsync(DateTime cutoff, FoxTelegram? telegram = null, TL.Message? replyToMessage = null, int maxHours = int.MaxValue)
        {
            if (!await _archiverSemaphore.WaitAsync(0))
                throw new InvalidOperationException("Archiver is already running.");

            try
            {
                _telegram = telegram;

                if (_telegram is not null)
                {
                    _telegramMessage = await _telegram.SendMessageAsync("Starting image archive...", replyToMessage: replyToMessage);
                }

                var allHourDirs = Directory.GetDirectories(_liveRoot, "*", SearchOption.AllDirectories)
                    .Where(d =>
                    {
                        // Example: /.../images/output/2025/august/10/05
                        // parts: [images, output, 2025, august, 10, 05]
                        var relative = Path.GetRelativePath(_liveRoot, d);
                        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length < 5)
                            return false; // need at least input/output + YYYY/MMM/DD/HH

                        if (!int.TryParse(parts[^4], out int year))
                            return false;

                        var monthName = char.ToUpper(parts[^3][0]) + parts[^3].Substring(1).ToLower();
                        if (!DateTime.TryParse($"{monthName} 1, {year}",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out DateTime month)
                        )
                            return false;

                        if (!int.TryParse(parts[^2], out int day))
                            return false;

                        if (!int.TryParse(parts[^1], out int hour))
                            return false;

                        try
                        {
                            var dt = new DateTime(year, month.Month, day, hour, 0, 0);
                            return dt < cutoff;
                        }
                        catch { return false; }
                    })
                    .OrderBy(d => d) // oldest first
                    .ToList();

                _directoryCount = allHourDirs.Count;

                int processed = 0;
                foreach (var dir in allHourDirs)
                {
                    string relative = Path.GetRelativePath(_liveRoot, dir);
                    await ArchiveDirectoryAsync(relative);

                    _currentDirectoryIndex++;

                    processed++;
                    if (processed >= maxHours)
                        break;
                }

                if (_telegram is not null && _telegramMessage is not null)
                {
                    try
                    {
                        var msgStr = $"Cleaning directory tree...";
                        await _telegram.EditMessageAsync(_telegramMessage.ID, msgStr);
                    }
                    catch
                    { /* ignore telegram errors */ }
                }

                CleanupEmptyTree();

                if (_telegram is not null && _telegramMessage is not null)
                {
                    try
                    {
                        var msgStr = $"Archive complete. Processed {processed} directories.";
                        await _telegram.EditMessageAsync(_telegramMessage.ID, msgStr);
                    }
                    catch
                    { /* ignore telegram errors */ }
                }
            }
            finally
            {
                _archiverSemaphore.Release();
                _telegram = null;
                _telegramMessage = null;
                _directoryCount = 0;
                _currentDirectoryIndex = 0;
                _lastStatusUpdate = DateTime.MinValue;
            }
        }
    }
}
