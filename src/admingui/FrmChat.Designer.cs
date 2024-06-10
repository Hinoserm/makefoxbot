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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FrmChat));
            imageList1 = new ImageList(components);
            splitContainer1 = new SplitContainer();
            leftPanel = new Panel();
            listView1 = new ListView();
            userName = new ColumnHeader();
            updateTime = new ColumnHeader();
            chatId = new ColumnHeader();
            tabControl = new TabControl();
            tabPageChat = new TabPage();
            tabPageSettings = new TabPage();
            tabPageImages = new TabPage();
            chatPanel = new ChatContainerControl();
            messageInputBox = new TextBox();
            sendButton = new Button();
            userSearchControl = new UserSearchControl();
            addChatButton = new Button();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            leftPanel.SuspendLayout();
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
            userSearchControl.UserSelected += new EventHandler<string>(UserSearchControl_UserSelected);
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
            addChatButton.Click += new EventHandler(AddChatButton_Click);
            // 
            // listView1
            // 
            listView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            listView1.Columns.AddRange(new ColumnHeader[] { chatId, userName, updateTime });
            listView1.FullRowSelect = true;
            listView1.Location = new Point(0, 30);
            listView1.MultiSelect = false;
            listView1.Name = "listView1";
            listView1.ShowGroups = false;
            listView1.Size = new Size(260, 708);
            listView1.Sorting = SortOrder.Ascending;
            listView1.TabIndex = 1;
            listView1.UseCompatibleStateImageBehavior = false;
            listView1.SelectedIndexChanged += new EventHandler(this.listView1_SelectedIndexChanged);
            listView1.View = View.Details;
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
            tabControl.SelectedIndexChanged += new EventHandler(TabControl_SelectedIndexChanged);
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
            tabPageImages.Location = new Point(4, 24);
            tabPageImages.Name = "tabPageImages";
            tabPageImages.Padding = new Padding(3);
            tabPageImages.Size = new Size(840, 710);
            tabPageImages.TabIndex = 2;
            tabPageImages.Text = "Images";
            tabPageImages.UseVisualStyleBackColor = true;
            // 
            // chatPanel
            // 
            chatPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            chatPanel.Location = new Point(0, 0);
            chatPanel.Name = "chatPanel";
            chatPanel.Size = new Size(840, 650);
            chatPanel.TabIndex = 0;
            // 
            // messageInputBox
            // 
            messageInputBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            messageInputBox.Location = new Point(0, 650);
            messageInputBox.Multiline = true;
            messageInputBox.ScrollBars = ScrollBars.Vertical;
            messageInputBox.Name = "messageInputBox";
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
            // listViewContextMenu
            // 
            listViewContextMenu = new ContextMenuStrip();
            deleteChatMenuItem = new ToolStripMenuItem("Delete Chat");
            deleteChatMenuItem.Click += new EventHandler(DeleteChatMenuItem_Click);
            listViewContextMenu.Items.AddRange(new ToolStripItem[] { deleteChatMenuItem });
            listView1.ContextMenuStrip = listViewContextMenu;

            // 
            // FrmChat
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1112, 738);
            Controls.Add(splitContainer1);
            Name = "FrmChat";
            Text = "FrmChat";
            Load += FrmChat_Load;
            FormClosing += FrmChat_FormClosing;
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel1.PerformLayout();
            splitContainer1.Panel2.ResumeLayout(false);
            splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            leftPanel.ResumeLayout(false);
            leftPanel.PerformLayout();
            tabControl.ResumeLayout(false);
            tabPageChat.ResumeLayout(false);
            tabPageChat.PerformLayout();
            ResumeLayout(false);
        }
    }
}
