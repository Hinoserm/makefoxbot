using Newtonsoft.Json.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;
using static System.Net.Mime.MediaTypeNames;

namespace makefoxsrv.commands
{
    internal class CmdModels
    {
        private static int editMessageID;

        public static async Task Run(FoxTelegram t, FoxUser user, TL.Message message)
        {
            await ShowModelFamilyList(t, user, message, null);
        }

        [BotCallable]
        public static async Task CBShowFamilies(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong origUserId)
        {
            if (!user.CheckAccessLevel(AccessLevel.ADMIN) && user.UID != origUserId)
            {
                await t.SendCallbackAnswer(query.query_id, 10, "❌ This is someone else's button.", alert: true);
                return;
            }

            await ShowModelFamilyList(t, user, null, new TL.Message { id = query.msg_id });
        }

        [BotCallable]
        public static async Task CBSelectFamily(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong origUserId, string familyName)
        {
            if (String.IsNullOrEmpty(familyName))
                throw new ArgumentException("Model name cannot be null or empty.", nameof(familyName));

            if (!user.CheckAccessLevel(AccessLevel.ADMIN) && user.UID != origUserId)
            {
                await t.SendCallbackAnswer(query.query_id, 10, "❌ This is someone else's button.", alert: true);
                return;
            }

            await ShowModelList(t, user, null, new TL.Message { id = query.msg_id }, familyName);
        }

        [BotCallable]
        public static async Task CBSelectModel(FoxTelegram t, FoxUser user, UpdateBotCallbackQuery query, ulong origUserId, string modelName)
        {
            if (String.IsNullOrEmpty(modelName))
                throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

            if (!user.CheckAccessLevel(AccessLevel.ADMIN) && user.UID != origUserId) {
                await t.SendCallbackAnswer(query.query_id, 10, "❌ This is someone else's button.", alert: true);
                return;
            }

            await t.SendCallbackAnswer(query.query_id, 0);

            var model = FoxModel.GetModelByName(modelName ?? FoxSettings.Get<string>("DefaultModel")!);

            if (model is null)
            {
                await t.EditMessageAsync(
                    text: "❌ Unknown model selected.",
                    id: query.msg_id
                );

                return;
            }
            else if (model.GetWorkersRunningModel().Count < 1)
            {
                await t.EditMessageAsync(
                    text: "❌ There are no workers currently available that can handle that model.  Please try again later.",
                    id: query.msg_id
                );

                return;
            }

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            settings.ModelName = model.Name;

            await settings.Save();

            StringBuilder message = new StringBuilder();

            message.AppendLine("✅ <b>Model selected:</b> " + settings.ModelName);

            if (model.Description is not null)
            {
                message.AppendLine();
                message.AppendLine("📝 <b>Description:</b> " + model.Description);
            }

            if (model.Notes is not null)
            {
                message.AppendLine();
                message.AppendLine("⚠️ <b>Important Notes:</b> " + model.Notes);
            }

            if (model.InfoUrl is not null)
            {
                message.AppendLine();
                message.AppendLine("🔗 <a href=\"" + model.InfoUrl + "\">More Information</a>");
            }

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if (model.IsPremium && !isPremium)
            {
                message.AppendLine();
                message.AppendLine("(🔒 This is a premium model and may require a membership to use)");
            }

            var msg = message.ToString();
            var entities = FoxTelegram.Client.HtmlToEntities(ref msg);

            await t.EditMessageAsync(
                    text: msg,
                    entities: entities,
                    disableWebPagePreview: true,
                    id: query.msg_id
                );
        }

        private static async Task ShowModelFamilyList(FoxTelegram t, FoxUser user, TL.Message? replyToMessage, TL.Message? editMessage)
        {
            List<TL.KeyboardButtonRow> keyboardRows = new List<TL.KeyboardButtonRow>();

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            var models = FoxModel.GetAvailableModels();

            var categories = models
                .GroupBy(m => m.Category ?? "Other")
                .OrderBy(g => g.Key == "Other")   // put "Other" at the bottom
                .ThenBy(g => g.Key)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToList();

            if (categories.Count == 0)
            {
                throw new Exception("No models available.");
            }

            var selectedModel = FoxModel.GetModelByName(settings.ModelName);

            string selectedCategory = selectedModel?.Category ?? "Other";


            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                new TL.KeyboardButtonCallback { text = "Default", data = System.Text.Encoding.UTF8.GetBytes("/model default") }
                }
            });

            // Build buttons with counts
            foreach (var category in categories)
            {
                var buttonData = FoxCallbackHandler.BuildCallbackData(CBSelectFamily, user.UID, category.Name);

                keyboardRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonCallback[]
                    {
                        new TL.KeyboardButtonCallback
                        {
                            text = (selectedCategory == category.Name ? "✅ " : "") + $"{category.Name} ({category.Count})",
                            data = System.Text.Encoding.UTF8.GetBytes(buttonData)
                        }
                    }
                });
            }

            // Show the cancel button
            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                        new TL.KeyboardButtonCallback { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/model cancel") }
                }
            });

            var inlineKeyboard = new TL.ReplyInlineMarkup { rows = keyboardRows.ToArray() };

            var msgText = "Select a category:\r\n\r\n✅ = Currently Using";

            if (editMessage is null)
            {
                await t.SendMessageAsync(
                    text: msgText,
                    replyInlineMarkup: inlineKeyboard,
                    replyToMessage: replyToMessage
                );
            }
            else
            {
                await t.EditMessageAsync(
                    id: editMessage.ID,
                    text: msgText,
                    replyInlineMarkup: inlineKeyboard
                );
            }
        }

        private static async Task ShowModelList(FoxTelegram t, FoxUser user, TL.Message? replyToMessage, TL.Message? editMessage, string? modelFamily = null)
        {
            List<TL.KeyboardButtonRow> keyboardRows = new List<TL.KeyboardButtonRow>();

            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            var models = FoxModel.GetAvailableModels();

            if (!string.IsNullOrEmpty(modelFamily))
            {
                models = modelFamily == "Other"
                    ? models.FindAll(m => m.Category == null)
                    : models.FindAll(m => m.Category == modelFamily);
            }

            if (models.Count == 0)
            {
                throw new Exception("No models available.");
            }

            keyboardRows.Add(new TL.KeyboardButtonRow
            {
                buttons = new TL.KeyboardButtonCallback[]
                {
                new TL.KeyboardButtonCallback { text = "Default", data = System.Text.Encoding.UTF8.GetBytes("/model default") }
                }
            });

            // Sort the models dictionary by key (model name) alphabetically
            foreach (var model in models.OrderBy(m => m.Name))
            {
                string modelName = model.Name;
                int workerCount = model.GetWorkersRunningModel().Count;

                var buttonLabel = (modelName == settings.ModelName ? "✅ " : "") + (model.IsPremium ? "⭐" : "") + $"{modelName} ({workerCount})";
                var buttonData = FoxCallbackHandler.BuildCallbackData(CBSelectModel, user.UID, modelName);

                keyboardRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonCallback[]
                    {
                    new TL.KeyboardButtonCallback { text = buttonLabel, data = System.Text.Encoding.UTF8.GetBytes(buttonData) }
                    }
                });
            }

            if (modelFamily is not null)
            {
                var buttonData = FoxCallbackHandler.BuildCallbackData(CBShowFamilies, user.UID);

                keyboardRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonCallback[]
                    {
                        new TL.KeyboardButtonCallback { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/model cancel") },
                        new TL.KeyboardButtonCallback { text = "⬅️ Back", data = System.Text.Encoding.UTF8.GetBytes(buttonData) }
                    }
                });
            }
            else
            {
                // Show just the cancel button
                keyboardRows.Add(new TL.KeyboardButtonRow
                {
                    buttons = new TL.KeyboardButtonCallback[]
                    {
                        new TL.KeyboardButtonCallback { text = "❌ Cancel", data = System.Text.Encoding.UTF8.GetBytes("/model cancel") }
                    }
                });
            }


            var inlineKeyboard = new TL.ReplyInlineMarkup { rows = keyboardRows.ToArray() };

            var msgText = "Select a model:\r\n\r\n⭐ = Premium\r\n✅ = Currently Using\r\n(#) = Available Workers";

            if (editMessage is null)
            {
                await t.SendMessageAsync(
                    text: msgText,
                    replyInlineMarkup: inlineKeyboard,
                    replyToMessage: replyToMessage
                );
            }
            else
            {
                await t.EditMessageAsync(
                    id: editMessage.ID,
                    text: msgText,
                    replyInlineMarkup: inlineKeyboard
                );
            }
        }
    }
}
