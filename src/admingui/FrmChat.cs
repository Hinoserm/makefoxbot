using System;
using System.Drawing;
using System.Windows.Forms;

namespace admingui
{
    public partial class FrmChat : Form
    {
        public FrmChat()
        {
            InitializeComponent();
        }

        private void AddChatMessage(string message, string timestamp, string username, bool isSentByUser)
        {
            var chatMessage = new ChatMessageControl(message, timestamp, username, isSentByUser);
            chatPanel.AddChatMessage(chatMessage);
        }

        private async void FrmChat_Load(object sender, EventArgs e)
        {
            try
            {
                var chatList = await WebSocketManager.Instance.GetChatListAsync();

                foreach (var chat in chatList)
                {
                    var item = new ListViewItem(new[] { chat.ChatID.ToString(), chat.DisplayName });
                    item.Tag = chat; // Store the chat object in the Tag property
                    listView1.Items.Add(item);
                }

                chatPanel.ScrollToBottom();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void splitContainer1_Panel2_Paint(object sender, PaintEventArgs e)
        {
        }

        private void SendButton_Click(object sender, EventArgs e)
        {
            var message = messageInputBox.Text.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                //AddChatMessage(message, DateTime.Now.ToString("hh:mm tt"), true);
                messageInputBox.Clear();
                chatPanel.ScrollToBottom();
            }
        }

        private async void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
            {
                var selectedItem = listView1.SelectedItems[0];
                var chatID = int.Parse(selectedItem.SubItems[0].Text);

                try
                {
                    // Clear existing messages
                    chatPanel.SuspendLayout();
                    chatPanel.Controls.Clear();
                    chatPanel.VerticalScroll.Value = 0; // Reset scroll position
                    
                    // Fetch messages for the selected chat
                    var messages = await WebSocketManager.Instance.GetMessagesAsync(chatID);

                    // Add messages to the chat panel
                    foreach (var message in messages)
                    {
                        AddChatMessage(message.MessageText, message.Date.ToString("hh:mm tt"), message.Username, message.IsOutgoing);
                    }

                    // Scroll to the bottom
                    chatPanel.ResumeLayout();

                    chatPanel.ScrollToBottom();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }
    }
}
