using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;
namespace makefoxsrv.commands
{
    internal class CmdAdminBan
    {
        [BotCommand(cmd: "admin", sub: "ban", adminOnly: true)]
        [BotCommand(cmd: "ban", adminOnly: true)]
        public static async Task CmdBan(FoxTelegram t, FoxUser user, Message message, FoxUser targetUser, string? banReason)
        {
            await HandleBanAsync(t, user, message, targetUser, banReason);
        }

        [BotCommand(cmd: "admin", sub: "unban", adminOnly: true)]
        public static async Task CmdUnban(FoxTelegram t, FoxUser user, Message message, FoxUser targetUser, string? msgNote)
        {
            await HandleUnbanAsync(t, user, message, targetUser, msgNote);
        }

        [BotCallable(adminOnly: true)]
        public static async Task cbBanUser(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            var targetUser = await FoxUser.GetByUID(userId);

            if (targetUser is null)
                throw new Exception($"Cannot find user #{userId}");

            await HandleBanAsync(t, user, new TL.Message() { id = query.msg_id }, targetUser);

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        [BotCallable(adminOnly: true)]
        public static async Task cbUnbanUser(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong userId)
        {
            var targetUser = await FoxUser.GetByUID(userId);
            if (targetUser is null)
                throw new Exception($"Cannot find user #{userId}");
            await HandleUnbanAsync(t, user, new TL.Message() { id = query.msg_id }, targetUser);

            await t.SendCallbackAnswer(query.query_id, 0);
        }

        private static async Task HandleUnbanAsync(FoxTelegram t, FoxUser user, Message message, FoxUser targetUser, string? reasonMsg = null)
        {
            if (targetUser.GetAccessLevel() != AccessLevel.BANNED)
                throw new Exception("User is not banned.");

            await targetUser.UnBan(reasonMessage: reasonMsg);

            await t.SendMessageAsync(
                text: $"✅ User {targetUser.UID} unbanned.",
                replyToMessageId: message.ID
            );
        }

        private static async Task HandleBanAsync(FoxTelegram t, FoxUser user, Message message, FoxUser banUser, string? reasonMsg = null)
        {
            if (banUser.CheckAccessLevel(AccessLevel.PREMIUM) || banUser.CheckAccessLevel(AccessLevel.ADMIN))
                throw new Exception("You can't ban an admin or premium user!");

            if (banUser.GetAccessLevel() == AccessLevel.BANNED)
                throw new Exception("User is already banned.");

            await banUser.Ban(reasonMessage: reasonMsg);

            await t.SendMessageAsync(
                text: $"✅ User {banUser.UID} banned.",
                replyToMessage: message
            );
        }

        [BotCommand(cmd: "admin", sub: "resetterms", adminOnly: true)]
        [BotCommand(cmd: "admin", sub: "resettos", adminOnly: true)]
        public static async Task HandleResetTerms(FoxTelegram t, FoxUser user, Message message, FoxUser targetUser)
        {
            await targetUser.SetTermsAccepted(false);

            await t.SendMessageAsync(
                text: $"✅ User {targetUser.UID} must now re-agree to the terms on their next command.",
                replyToMessageId: message.ID
            );
        }
    }
}
