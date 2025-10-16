using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv.llm.functions
{
    internal class TextResponse
    {
        [LLMFunction("Sends a text response to the user.")]
        public static async Task SendResponse(
            FoxTelegram t,
            FoxUser user,
            [LLMParam("Text you wish to send to the user.")] string Message
            )

        {
            if (!string.IsNullOrEmpty(Message?.Trim()))
            {
                //FoxLog.WriteLine($"LLM Output: {assistantReply}");

                var convResponseId = await FoxLLMConversation.InsertConversationMessageAsync(user, FoxLLMConversation.ChatRole.Assistant, Message, null);

                TL.Message? lastResponseMessage = null;

                // Telegram caps messages at 4096 chars; split safely on paragraph or word boundaries
                const int maxLen = 3900; // leave buffer for markup/UTF8
                int start = 0;

                while (start < Message.Length)
                {
                    int len = Math.Min(maxLen, Message.Length - start);
                    int split = start + len;

                    // try to split at newline or space before the limit
                    if (split < Message.Length)
                    {
                        int newline = Message.LastIndexOf('\n', split, len);
                        int space = Message.LastIndexOf(' ', split, len);
                        if (newline > start + 200) split = newline;
                        else if (space > start + 200) split = space;
                    }

                    var chunk = Message[start..split].Trim();
                    if (chunk.Length > 0)
                    {
                        bool isLast = (split >= Message.Length);

                        lastResponseMessage = await t.SendMessageAsync(chunk);

                        await Task.Delay(500); // try to protect against floods
                    }

                    start = split;
                }
            }
        }

        [LLMFunction("Sends a brief text message to the admin team. Returns true once the message has been sent. Use SPARINGLY; only when explicitly requested by the user.")]
        public static async Task<bool> SendAdminMessage(
            FoxTelegram t,
            FoxUser user,
            [LLMParam("Text you wish to send.")] string Message
            )

        {
            if (!string.IsNullOrEmpty(Message?.Trim()))
            {
                await FoxContentFilter.SendModerationNotification($"Message from LLM for user {user.UID}:\r\n{Message}");

                await Task.Delay(2000);
                return true;
            }

            return false;
        }
    }
}
