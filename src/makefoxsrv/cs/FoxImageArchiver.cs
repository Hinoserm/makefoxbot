using MySqlConnector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public class FoxImageArchiver
    {
        private readonly string _liveRoot;
        private readonly string _archiveRoot;
        private static readonly Regex _hashRegex = new Regex("^[A-Fa-f0-9]{40}\\.[a-zA-Z0-9]+$", RegexOptions.Compiled);

        public FoxImageArchiver(string liveRoot, string archiveRoot)
        {
            _liveRoot = liveRoot ?? throw new ArgumentNullException(nameof(liveRoot));
            _archiveRoot = archiveRoot ?? throw new ArgumentNullException(nameof(archiveRoot));
        }

        /// <summary>
        /// Archive all files in one hour-directory.
        /// </summary>
        private async Task ArchiveDirectoryAsync(string relativeHourPath)
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

            try
            {
                Directory.CreateDirectory(targetDir);

                // Copy files
                foreach (var src in validFiles)
                {
                    string dest = Path.Combine(targetDir, Path.GetFileName(src));
                    File.Copy(src, dest, overwrite: true);
                }

                // Update DB in batches
                using (var conn = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await conn.OpenAsync();
                    using var tx = await conn.BeginTransactionAsync();

                    const int batchSize = 500; // tune as needed
                    for (int i = 0; i < validFiles.Count; i += batchSize)
                    {
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

                        await cmd.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                }

                // Success: delete originals
                Directory.Delete(sourceDir, recursive: true);
                FoxLog.WriteLine($"Archive complete: {sourceDir} -> {targetDir} ({validFiles.Count} files)", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Archive failed for {sourceDir}");
                // Rollback archive dir
                if (Directory.Exists(targetDir))
                {
                    try { Directory.Delete(targetDir, recursive: true); }
                    catch { /* ignore cleanup errors */ }
                }
            }
        }

        /// <summary>
        /// Archives all directories older than the cutoff date.
        /// </summary>
        public async Task ArchiveOlderThanAsync(DateTime cutoff, int maxHours = int.MaxValue)
        {
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

            int processed = 0;
            foreach (var dir in allHourDirs)
            {
                string relative = Path.GetRelativePath(_liveRoot, dir);
                await ArchiveDirectoryAsync(relative);

                processed++;
                if (processed >= maxHours)
                    break;
            }
        }
    }
}
