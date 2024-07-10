using System.Windows.Forms;

namespace admingui
{
    partial class FrmChat
    {
        private System.ComponentModel.IContainer components = null;
        private ImageList imageList1;
        private SplitContainer splitContainer1;
        private ListView listView1;
        private ColumnHeader userName;
        private ColumnHeader updateTime;
        private ColumnHeader chatId;
        private ChatContainerControl chatPanel;
        private TextBox messageInputBox;
        private Button sendButton;
        private UserSearchControl userSearchControl;
        private Button addChatButton;
        private Panel leftPanel;
        private ContextMenuStrip listViewContextMenu;
        private ToolStripMenuItem deleteChatMenuItem;
        private TabControl tabControl;
        private TabPage tabPageChat;
        private TabPage tabPageSettings;
        private TabPage tabPageImages;
        private ImageListView imageListView;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            ColumnHeader chatId;
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FrmChat));
            imageList1 = new ImageList(components);
            splitContainer1 = new SplitContainer();
            leftPanel = new Panel();
            userSearchControl = new UserSearchControl();
            addChatButton = new Button();
            listView1 = new ListView();
            userName = new ColumnHeader();
            updateTime = new ColumnHeader();
            imageListView = new ImageListView();
            listViewContextMenu = new ContextMenuStrip(components);
            deleteChatMenuItem = new ToolStripMenuItem();
            tabControl = new TabControl();
            tabPageChat = new TabPage();
            chatPanel = new ChatContainerControl();
            messageInputBox = new TextBox();
            sendButton = new Button();
            tabPageSettings = new TabPage();
            tabPageImages = new TabPage();
            backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            chatId = new ColumnHeader();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            leftPanel.SuspendLayout();
            listViewContextMenu.SuspendLayout();
            tabControl.SuspendLayout();
            tabPageChat.SuspendLayout();
            SuspendLayout();
            // 
            // chatId
            // 
            chatId.Text = "ID";
            chatId.TextAlign = HorizontalAlignment.Center;
            chatId.Width = 35;
            // 
            // imageList1
            // 
            imageList1.ColorDepth = ColorDepth.Depth32Bit;
            imageList1.ImageStream = (ImageListStreamer)resources.GetObject("imageList1.ImageStream");
            imageList1.TransparentColor = Color.Transparent;
            imageList1.Images.SetKeyName(0, "22218foxface_98828.ico");
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.FixedPanel = FixedPanel.Panel1;
            splitContainer1.IsSplitterFixed = true;
            splitContainer1.Location = new Point(0, 0);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(leftPanel);
            splitContainer1.Panel1MinSize = 260;
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(tabControl);
            splitContainer1.Panel2MinSize = 200;
            splitContainer1.Size = new Size(1112, 738);
            splitContainer1.SplitterDistance = 260;
            splitContainer1.TabIndex = 1;
            // 
            // leftPanel
            // 
            leftPanel.Controls.Add(userSearchControl);
            leftPanel.Controls.Add(addChatButton);
            leftPanel.Controls.Add(listView1);
            leftPanel.Dock = DockStyle.Fill;
            leftPanel.Location = new Point(0, 0);
            leftPanel.Name = "leftPanel";
            leftPanel.Size = new Size(260, 738);
            leftPanel.TabIndex = 0;
            // 
            // userSearchControl
            // 
            userSearchControl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            userSearchControl.Location = new Point(0, 0);
            userSearchControl.Name = "userSearchControl";
            userSearchControl.Size = new Size(210, 23);
            userSearchControl.TabIndex = 0;
            userSearchControl.UserSelected += UserSearchControl_UserSelected;
            // 
            // addChatButton
            // 
            addChatButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            addChatButton.Location = new Point(210, 0);
            addChatButton.Name = "addChatButton";
            addChatButton.Size = new Size(50, 23);
            addChatButton.TabIndex = 4;
            addChatButton.Text = "+";
            addChatButton.UseVisualStyleBackColor = true;
            addChatButton.Click += AddChatButton_Click;
            // 
            // listView1
            // 
            listView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            listView1.Columns.AddRange(new ColumnHeader[] { chatId, userName, updateTime });
            listView1.ContextMenuStrip = listViewContextMenu;
            listView1.FullRowSelect = true;
            listView1.Location = new Point(0, 30);
            listView1.MultiSelect = false;
            listView1.Name = "listView1";
            listView1.ShowGroups = false;
            listView1.Size = new Size(260, 708);
            listView1.Sorting = SortOrder.Ascending;
            listView1.TabIndex = 1;
            listView1.UseCompatibleStateImageBehavior = false;
            listView1.View = View.Details;
            listView1.SelectedIndexChanged += listView1_SelectedIndexChanged;
            // 
            // userName
            // 
            userName.Text = "User";
            userName.Width = 160;
            // 
            // updateTime
            // 
            updateTime.Text = "Time";
            // 
            // listViewContextMenu
            // 
            listViewContextMenu.Items.AddRange(new ToolStripItem[] { deleteChatMenuItem });
            listViewContextMenu.Name = "listViewContextMenu";
            listViewContextMenu.Size = new Size(136, 26);
            // 
            // deleteChatMenuItem
            // 
            deleteChatMenuItem.Name = "deleteChatMenuItem";
            deleteChatMenuItem.Size = new Size(135, 22);
            deleteChatMenuItem.Text = "Delete Chat";
            deleteChatMenuItem.Click += DeleteChatMenuItem_Click;
            // 
            // tabControl
            // 
            tabControl.Controls.Add(tabPageChat);
            tabControl.Controls.Add(tabPageSettings);
            tabControl.Controls.Add(tabPageImages);
            tabControl.Dock = DockStyle.Fill;
            tabControl.Location = new Point(0, 0);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(848, 738);
            tabControl.TabIndex = 3;
            tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
            // 
            // tabPageChat
            // 
            tabPageChat.Controls.Add(chatPanel);
            tabPageChat.Controls.Add(messageInputBox);
            tabPageChat.Controls.Add(sendButton);
            tabPageChat.Location = new Point(4, 24);
            tabPageChat.Name = "tabPageChat";
            tabPageChat.Padding = new Padding(3);
            tabPageChat.Size = new Size(840, 710);
            tabPageChat.TabIndex = 0;
            tabPageChat.Text = "Chat";
            tabPageChat.UseVisualStyleBackColor = true;
            // 
            // chatPanel
            // 
            chatPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            chatPanel.AutoScroll = true;
            chatPanel.Location = new Point(0, 0);
            chatPanel.Name = "chatPanel";
            chatPanel.Padding = new Padding(10, 10, 10, 25);
            chatPanel.Size = new Size(840, 650);
            chatPanel.TabIndex = 0;
            // 
            // messageInputBox
            // 
            messageInputBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            messageInputBox.Location = new Point(0, 650);
            messageInputBox.Multiline = true;
            messageInputBox.Name = "messageInputBox";
            messageInputBox.ScrollBars = ScrollBars.Vertical;
            messageInputBox.Size = new Size(740, 60);
            messageInputBox.TabIndex = 1;
            // 
            // sendButton
            // 
            sendButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            sendButton.Location = new Point(740, 650);
            sendButton.Name = "sendButton";
            sendButton.Size = new Size(100, 60);
            sendButton.TabIndex = 2;
            sendButton.Text = "Send";
            sendButton.UseVisualStyleBackColor = true;
            sendButton.Click += SendButton_Click;
            // 
            // tabPageSettings
            // 
            tabPageSettings.Location = new Point(4, 24);
            tabPageSettings.Name = "tabPageSettings";
            tabPageSettings.Padding = new Padding(3);
            tabPageSettings.Size = new Size(840, 710);
            tabPageSettings.TabIndex = 1;
            tabPageSettings.Text = "Settings";
            tabPageSettings.UseVisualStyleBackColor = true;
            // 
            // tabPageImages
            // 
            tabPageImages.Controls.Add(imageListView); // Add this line
            imageListView.Dock = DockStyle.Fill; // Add this line
            tabPageImages.Location = new Point(4, 24);
            tabPageImages.Name = "tabPageImages";
            tabPageImages.Padding = new Padding(3);
            tabPageImages.Size = new Size(840, 710);
            tabPageImages.TabIndex = 2;
            tabPageImages.Text = "Images";
            tabPageImages.UseVisualStyleBackColor = true;
            tabPageImages.Click += tabPageImages_Click;

            // 
            // backgroundWorker1
            // 
            backgroundWorker1.DoWork += backgroundWorker1_DoWork;
            // 
            // FrmChat
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1112, 738);
            Controls.Add(splitContainer1);
            Name = "FrmChat";
            Text = "FrmChat";
            FormClosing += FrmChat_FormClosing;
            Load += FrmChat_Load;
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            leftPanel.ResumeLayout(false);
            listViewContextMenu.ResumeLayout(false);
            tabControl.ResumeLayout(false);
            tabPageChat.ResumeLayout(false);
            tabPageChat.PerformLayout();
            ResumeLayout(false);
        }

        private System.ComponentModel.BackgroundWorker backgroundWorker1;
    }
}
