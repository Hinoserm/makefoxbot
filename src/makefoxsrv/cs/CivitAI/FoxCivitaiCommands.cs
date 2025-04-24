using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using WTelegram;
using TL;

namespace makefoxsrv
{
    internal class FoxCivitaiCommands
    {
        public static async Task AdminCmdDownloadRequests(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var pendingRequests = await FoxCivitaiRequests.FetchCivitaiItemsAsync(FoxCivitaiRequests.StatusFilter.Uninstalled);

            if (pendingRequests.Count == 0)
            {
                await t.SendMessageAsync(
                    text: "❌ No pending requests found.",
                    replyToMessage: message
                );
                return;
            }

            var groupedResults = FoxCivitaiRequests.GroupByType(pendingRequests);

            var sb = new StringBuilder();

            sb.AppendLine($"Attempting to download {pendingRequests.Count()}...");
            sb.AppendLine();

            var outMsg = await t.SendMessageAsync(
                text: sb.ToString(),
                replyToMessage: message
            );

            var semaphore = new SemaphoreSlim(3);
            var downloadTasks = new List<Task>();

            foreach (var (type, items) in groupedResults)
            {
                var requestTypeDir = type.ToString().ToLowerInvariant(); // lora, model, etc

                foreach (var item in items.ToList()) // Clone list to avoid modifying during iteration
                {
                    await semaphore.WaitAsync();

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
                                return;

                            if (string.IsNullOrWhiteSpace(item.DownloadUrl) || string.IsNullOrWhiteSpace(item.FileName))
                                throw new Exception("Invalid download URL or filename");

                            var subdirs = item.GetStoragePath()
                                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.ToLowerInvariant())
                                .ToArray();

                            var storagePath = Path.Combine(
                                new[] { "requests", requestTypeDir }
                                .Concat(subdirs)
                                .Append(item.FileName)
                                .ToArray()
                            );

                            FoxLog.WriteLine($"Downloading: {item.DownloadUrl} > {storagePath}");

                            await FoxCivitaiRequests.DownloadItemAsync(item, storagePath);

                            item.FilePath = storagePath;
                        }
                        catch (Exception ex)
                        {
                            FoxLog.LogException(ex);
                            sb.AppendLine($"Error downloading: {item.FileName}");

                            lock (groupedResults)
                            {
                                groupedResults[type] = groupedResults[type]
                                    .Where(i => i != item)
                                    .ToList();
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    downloadTasks.Add(task);
                }
            }

            await Task.WhenAll(downloadTasks);

            // Update the database with the new file paths
            foreach (var (type, items) in groupedResults)
            {
                await FoxCivitaiRequests.InsertCivitaiItemsAsync(items);
            }

            sb.AppendLine("Download complete.");
            
            await t.EditMessageAsync(
                id: outMsg.id,
                text: sb.ToString()
            );
        }
    }
}
