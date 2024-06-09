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
            listView1 = new ListView();
            userName = new ColumnHeader();
            updateTime = new ColumnHeader();
            chatId = new ColumnHeader();
            chatPanel = new ChatContainerControl();
            messageInputBox = new TextBox();
            sendButton = new Button();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
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
            splitContainer1.Panel1.Controls.Add(listView1);
            splitContainer1.Panel1MinSize = 260;
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(chatPanel);
            splitContainer1.Panel2.Controls.Add(messageInputBox);
            splitContainer1.Panel2.Controls.Add(sendButton);
            splitContainer1.Panel2MinSize = 200;
            splitContainer1.Size = new Size(1112, 738);
            splitContainer1.SplitterDistance = 260;
            splitContainer1.TabIndex = 1;
            // 
            // listView1
            // 
            listView1.Columns.AddRange(new ColumnHeader[] { chatId, userName, updateTime });
            listView1.Dock = DockStyle.Fill;
            listView1.FullRowSelect = true;
            listView1.Location = new Point(0, 0);
            listView1.MultiSelect = false;
            listView1.Name = "listView1";
            listView1.ShowGroups = false;
            listView1.Size = new Size(260, 738);
            listView1.Sorting = SortOrder.Ascending;
            listView1.TabIndex = 1;
            listView1.UseCompatibleStateImageBehavior = false;
            this.listView1.SelectedIndexChanged += new System.EventHandler(this.listView1_SelectedIndexChanged);
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
            // chatPanel
            // 
            chatPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            chatPanel.Location = new Point(0, 0);
            chatPanel.Name = "chatPanel";
            chatPanel.Size = new Size(848, 658); // Reduce height to increase space for messageInputBox and sendButton
            chatPanel.TabIndex = 0;
            // 
            // messageInputBox
            // 
            messageInputBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            messageInputBox.Location = new Point(0, 668); // Adjust position
            messageInputBox.Multiline = true;
            messageInputBox.ScrollBars = ScrollBars.Vertical; // Enable vertical scrollbars
            messageInputBox.Name = "messageInputBox";
            messageInputBox.Size = new Size(748, 60); // Adjust height
            messageInputBox.TabIndex = 1;
            // 
            // sendButton
            // 
            sendButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            sendButton.Location = new Point(748, 668); // Adjust position
            sendButton.Name = "sendButton";
            sendButton.Size = new Size(100, 60); // Adjust height
            sendButton.TabIndex = 2;
            sendButton.Text = "Send";
            sendButton.UseVisualStyleBackColor = true;
            sendButton.Click += SendButton_Click;
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
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            ResumeLayout(false);
        }
    }
}
