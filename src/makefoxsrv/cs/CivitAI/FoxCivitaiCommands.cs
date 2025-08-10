using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using WTelegram;
using TL;
using PayPalCheckoutSdk.Orders;

namespace makefoxsrv
{
    internal class FoxCivitaiCommands
    {
        public static async Task AdminCmdDownloadRequests(FoxTelegram t, Message message, FoxUser user, String? argument)
        {
            var startTime = DateTime.Now;

            // This should eventually be changed to RequestStatus.Approved, once the approval process is implemented
            var pendingRequests = await FoxCivitaiRequests.FetchAllRequestsAsync(FoxCivitaiRequests.RequestStatus.Pending);

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

            var downloadCounts = new Dictionary<FoxUser, Dictionary<string, int>>();

            var downloadTasks = new List<Task>();

            foreach (var (type, items) in groupedResults)
            {
                var downloadItems = FoxCivitaiRequests.PrepareDownloadList(items);
                var requestType = type.ToString().ToLowerInvariant(); // lora, model, etc

                foreach (var downloadItem in downloadItems)
                {
                    await semaphore.WaitAsync();

                    var task = Task.Run(async () =>
                    {

                        var infoItem = downloadItem.Request.InfoItem;
                        var file = downloadItem.File;
                        var request = downloadItem.Request;

                        try
                        {
                            var baseModel = infoItem.BaseModel?.Trim().ToLowerInvariant();

                            var basePath = "other";

                            if (baseModel is not null)
                            {
                                basePath = baseModel switch
                                {
                                    "sd 1.5" => "sd",
                                    "sd 2.1 768" => "sd21",
                                    "flux" => "flux",
                                    "noobai" => "sdxl/nai",
                                    "pony" => "sdxl/pony",
                                    "illustrious" => "sdxl/illustrious",
                                    "sdxl lightning" => "sdxl/lightning",
                                    "sdxl 1.0" => "sdxl/other",
                                    "sdxl" => "sdxl/other",
                                    _ when baseModel.StartsWith("sdxl ") => "sdxl/other",
                                    _ => "other"
                                };
                            }

                            var subdirs = basePath
                                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.ToLowerInvariant())
                                .ToArray();

                            var storagePath = Path.Combine(
                                new[] { "..", "data", "requests", requestType }
                                .Concat(subdirs)
                                .Append(downloadItem.FileName) // Use the renamed file name
                                .ToArray()
                            );

                            FoxLog.WriteLine($"Downloading: {file.DownloadUrl} > {storagePath}");

                            await file.DownloadAsync(storagePath);

                            var now = DateTime.Now;

                            // Until we have a proper approval process, we will just set the status to Approved after download
                            request.DateApproved = now;
                            request.ApprovedBy = user;

                            request.DateInstalled = now;
                            request.InstalledBy = user;

                            await request.SaveAsync();

                            if (!downloadCounts.TryGetValue(request.RequestedBy, out var userCounts))
                            {
                                userCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                downloadCounts[request.RequestedBy] = userCounts;
                            }

                            if (userCounts.ContainsKey(requestType))
                                userCounts[requestType]++;
                            else
                                userCounts[requestType] = 1;
                        }
                        catch (Exception ex)
                        {
                            FoxLog.LogException(ex);
                            sb.AppendLine($"Error downloading: {downloadItem.FileName}");

                            await Task.Delay(15000); // Wait before moving on to prevent triggering flood protection
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

            sb.AppendLine("Download complete.");

            await t.EditMessageAsync(
                id: outMsg.id,
                text: sb.ToString()
            );
        }
    }
}
