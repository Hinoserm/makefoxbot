using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace makefoxsrv.commands
{
    internal class CmdAdminInfo
    {
        [BotCommand(cmd: "zonk")]
        public static async Task CmdZonk(FoxTelegram t, FoxUser user, TL.Message message, FoxUser targetUser, int? days = null, string? reasonStr = null)
        {
            return;
        }


        [BotCommand(cmd: "admin", sub: "info", adminOnly: true)]
        [BotCommand(cmd: "info", adminOnly: false)]
        public static async Task CmdInfo(FoxTelegram t, FoxUser user, TL.Message message, FoxUser? targetUser)
        {
            if (targetUser is null)
            {
                // Use the old command for now
                await FoxCommandHandlerOld.CmdInfo(t, message, user, null);

                return;
            } else if (!user.CheckAccessLevel(AccessLevel.ADMIN))
                throw new Exception("You must be an admin to view other users' info.");

            await HandleShowUserInfo(t, user, message, targetUser);
        }

        public static async Task HandleShowUserInfo(FoxTelegram t, FoxUser user, TL.Message message, FoxUser targetUser)
        {
            var msgStr = await FoxMessages.BuildUserInfoString(targetUser);

            var showProfileButton = true;

            while (true)
            {
                List<TL.KeyboardButtonRow> buttonRows = new List<TL.KeyboardButtonRow>();

                bool isBanned = (targetUser.GetAccessLevel() == AccessLevel.BANNED);

                var banButtonData = FoxCallbackHandler.BuildCallbackData(CmdAdminBan.cbBanUser, targetUser.UID);
                var unbanButtonData = FoxCallbackHandler.BuildCallbackData(CmdAdminBan.cbUnbanUser, targetUser.UID);

                buttonRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonBase[]
                    {
                        new TL.KeyboardButtonCallback { text = "🔒 Ban", data = banButtonData },
                        new TL.KeyboardButtonCallback { text = "🔓 Unban", data = unbanButtonData }
                    }
                });

                if (await targetUser.GetTotalPaid() > 0)
                {
                    var paymentsButtonData = FoxCallbackHandler.BuildCallbackData(FoxCmdAdminPayments.cbShowPayments, targetUser.UID);

                    buttonRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonBase[]
                        {
                            new TL.KeyboardButtonCallback { text = "💰 Show Payments", data = paymentsButtonData },
                        }
                    });
                }

                if (showProfileButton && targetUser.Telegram is not null)
                {
                    buttonRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonBase[]
                        {
                            new KeyboardButtonUrl() { text = "🔗 Image Viewer", url = $"{FoxMain.settings?.WebRootUrl}ui/images.php?uid={targetUser.UID}" },
                            new InputKeyboardButtonUserProfile() { text = "View Profile", user_id = targetUser.Telegram.User }
                        }
                    });
                }
                else
                {
                    buttonRows.Add(new TL.KeyboardButtonRow
                    {
                        buttons = new TL.KeyboardButtonBase[]
                        {
                            new KeyboardButtonUrl() { text = "🔗 Image Viewer", url = $"{FoxMain.settings?.WebRootUrl}ui/images.php?uid={targetUser.UID}" }
                        }
                    });
                }

                try
                {
                    await t.SendMessageAsync(
                        text: msgStr,
                        replyToMessage: message,
                        replyInlineMarkup: new TL.ReplyInlineMarkup { rows = buttonRows.ToArray() }
                    );
                }
                catch (Exception ex)
                {
                    // Probably hit an error about the profile button

                    FoxLog.LogException(ex);

                    if (!showProfileButton)
                        throw; // We already tried without the profile button, so give up.

                    showProfileButton = false;
                    continue;
                }

                break; // Exit the loop if successful
            }

        }

        [BotCommand(cmd: "admin", sub: "queue", adminOnly: true)]
        public static async Task HandleQueueStatus(FoxTelegram t, FoxUser user, Message message)
        {
            var queueStatus = FoxQueue.GenerateQueueStatusMessage();

            var statusMessage = $"📊 Queue Status:\n\n" +
                                $"{queueStatus}\n";

            var originalMsg = await t.SendMessageAsync(text: statusMessage, replyToMessage: message);

            _ = Task.Run(async () =>
            {
                DateTime startTime = DateTime.Now;
                while (DateTime.Now - startTime < TimeSpan.FromMinutes(60))
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(8));

                        string updatedStatus = $"📊 Queue Status:\n\n{FoxQueue.GenerateQueueStatusMessage()}\n";

                        await t.EditMessageAsync(originalMsg.ID, updatedStatus);
                    }
                    catch (Exception ex)
                    {
                        FoxLog.WriteLine($"Error updating queue status: {ex.Message}");
                        break; //Stop trying.
                    }
                }
            });
        }
    }
}
