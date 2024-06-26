using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;

namespace admingui
{
    public partial class FrmChat : Form
    {
        private Icon unreadMessageIcon = SystemIcons.Information;

        public FrmChat()
        {
            InitializeComponent();
        }

        private void AddChatMessage(string message, DateTime timestamp, string username, bool isSentByUser)
        {
            var formattedTimestamp = timestamp.ToString("MMM d yyyy hh:mm tt zzz"); // Jan 1 1998 12:21 AM CDT
            var chatMessage = new ChatMessageControl(message, formattedTimestamp, username, isSentByUser);
            chatPanel.AddChatMessage(chatMessage);
        }

        private async void FrmChat_Load(object sender, EventArgs e)
        {
            try
            {
                await LoadChatList();
                chatPanel.ScrollToBottom();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }

            WebSocketManager.Instance.NewMessageReceived += OnNewMessageReceived;
        }

        private async Task LoadChatList()
        {
            try
            {
                listView1.Items.Clear();
                var chatList = await WebSocketManager.Instance.GetChatListAsync();
                foreach (var chat in chatList)
                {
                    var item = new ListViewItem(new[] { chat.ChatID.ToString(), chat.DisplayName });
                    item.Tag = chat;
                    listView1.Items.Add(item);
                }

                // Keep existing sorting and behavior
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void FrmChat_FormClosing(object sender, FormClosingEventArgs e)
        {
            WebSocketManager.Instance.NewMessageReceived -= OnNewMessageReceived;
        }

        private async void SendButton_Click(object sender, EventArgs e)
        {
            var message = messageInputBox.Text.Trim();
            if (!string.IsNullOrEmpty(message) && listView1.SelectedItems.Count > 0)
            {
                var selectedItem = listView1.SelectedItems[0];
                var chatID = int.Parse(selectedItem.SubItems[0].Text);

                try
                {
                    await WebSocketManager.Instance.SendMessageAsync(chatID, message);
                    messageInputBox.Clear();
                    chatPanel.ScrollToBottom();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

        private async void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
            {
                var selectedItem = listView1.SelectedItems[0];
                var chatID = int.Parse(selectedItem.SubItems[0].Text);

                // Clear highlight
                selectedItem.BackColor = Color.White;

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
                        AddChatMessage(message.MessageText, message.Date, message.Username, message.IsOutgoing);
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

        private async void DeleteChatMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
            {
                var selectedItem = listView1.SelectedItems[0];
                var chatID = int.Parse(selectedItem.SubItems[0].Text);

                try
                {
                    var success = await WebSocketManager.Instance.DeleteChatAsync(chatID);
                    if (success)
                    {
                        // Remove the chat from the list view
                        listView1.Items.Remove(selectedItem);

                        // Optionally clear the chat messages panel if the deleted chat was selected
                        chatPanel.Controls.Clear();
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete the chat.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

        private async void AddChatButton_Click(object sender, EventArgs e)
        {
            var selectedUser = userSearchControl.Text?.Trim();

            if (!string.IsNullOrEmpty(selectedUser))
            {
                try
                {
                    // Create a new chat with the selected user
                    var newChatID = await WebSocketManager.Instance.CreateNewChatAsync(selectedUser);

                    // Reload chat list and preserve the selected chat
                    await ReloadChatList(newChatID);

                    userSearchControl.Clear();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("Type a username into the box please.");
            }
        }

        private async Task ReloadChatList(int selectedChatID)
        {
            try
            {
                listView1.Items.Clear();
                var chatList = await WebSocketManager.Instance.GetChatListAsync();

                foreach (var chat in chatList)
                {
                    var item = new ListViewItem(new[] { chat.ChatID.ToString(), chat.DisplayName });
                    item.Tag = chat;
                    listView1.Items.Add(item);

                    if (chat.ChatID == selectedChatID)
                    {
                        item.Selected = true;
                    }
                }

                if (listView1.SelectedItems.Count > 0)
                {
                    listView1.EnsureVisible(listView1.SelectedItems[0].Index);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void OnNewMessageReceived(object sender, WebSocketManager.MessageEventArgs e)
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => OnNewMessageReceived(sender, e)));
                    return;
                }

                foreach (ListViewItem item in listView1.Items)
                {
                    if (int.Parse(item.SubItems[0].Text) == e.ChatID)
                    {
                        if (e.ChatID != GetCurrentChatID())
                        {
                            item.BackColor = Color.LightBlue;
                            HighlightChatTab();
                        }
                        else
                        {
                            AddChatMessage(e.Message.MessageText, e.Message.Date, e.Message.Username, e.Message.IsOutgoing);
                            chatPanel.ScrollToBottom();
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\r\n{ex.StackTrace}");
            }
        }

        private void HighlightChatTab()
        {
            if (tabControl.SelectedTab != tabPageChat)
            {
                tabPageChat.ImageIndex = 0;
                tabControl.ImageList = new ImageList();
                tabControl.ImageList.Images.Add(unreadMessageIcon);
                tabPageChat.ImageKey = "UnreadMessage";
            }
        }

        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl.SelectedTab == tabPageChat)
            {
                tabPageChat.ImageKey = null;
            }
        }

        private int GetCurrentChatID()
        {
            if (listView1.SelectedItems.Count > 0)
            {
                var selectedItem = listView1.SelectedItems[0];
                return int.Parse(selectedItem.SubItems[0].Text);
            }
            return -1;
        }

        private void UserSearchControl_UserSelected(object sender, string selectedUser)
        {
            MessageBox.Show($"Selected user: {selectedUser}");
        }
    }

    public class ChatListViewItemComparer : System.Collections.IComparer
    {
        public int Compare(object x, object y)
        {
            return int.Parse(((ListViewItem)x).SubItems[0].Text).CompareTo(int.Parse(((ListViewItem)y).SubItems[0].Text));
        }
    }
}
