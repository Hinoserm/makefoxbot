namespace admingui
{
    partial class frmConnect
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            cmbServerAddress = new ComboBox();
            label1 = new Label();
            label2 = new Label();
            txtUsername = new TextBox();
            label3 = new Label();
            txtPassword = new TextBox();
            chkRememberPassword = new CheckBox();
            btnConnect = new Button();
            btnCancel = new Button();
            SuspendLayout();
            // 
            // cmbServerAddress
            // 
            cmbServerAddress.FormattingEnabled = true;
            cmbServerAddress.Items.AddRange(new object[] { "http://makefox.bot/cs/ws", "http://localhost:5555/ws" });
            cmbServerAddress.Location = new Point(12, 33);
            cmbServerAddress.Name = "cmbServerAddress";
            cmbServerAddress.Size = new Size(312, 23);
            cmbServerAddress.TabIndex = 0;
            cmbServerAddress.SelectedIndexChanged += comboBox1_SelectedIndexChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(12, 15);
            label1.Name = "label1";
            label1.Size = new Size(87, 15);
            label1.TabIndex = 1;
            label1.Text = "Server Address:";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(9, 59);
            label2.Name = "label2";
            label2.Size = new Size(63, 15);
            label2.TabIndex = 2;
            label2.Text = "Username:";
            label2.Click += label2_Click;
            // 
            // txtUsername
            // 
            txtUsername.Location = new Point(12, 77);
            txtUsername.Name = "txtUsername";
            txtUsername.Size = new Size(160, 23);
            txtUsername.TabIndex = 3;
            txtUsername.TextChanged += txtUsername_TextChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(12, 103);
            label3.Name = "label3";
            label3.Size = new Size(60, 15);
            label3.TabIndex = 4;
            label3.Text = "Password:";
            label3.Click += label3_Click;
            // 
            // txtPassword
            // 
            txtPassword.Location = new Point(12, 121);
            txtPassword.Name = "txtPassword";
            txtPassword.Size = new Size(160, 23);
            txtPassword.TabIndex = 5;
            txtPassword.TextChanged += textBox2_TextChanged;
            // 
            // chkRememberPassword
            // 
            chkRememberPassword.AutoSize = true;
            chkRememberPassword.Checked = true;
            chkRememberPassword.CheckState = CheckState.Checked;
            chkRememberPassword.Location = new Point(12, 150);
            chkRememberPassword.Name = "chkRememberPassword";
            chkRememberPassword.Size = new Size(137, 19);
            chkRememberPassword.TabIndex = 6;
            chkRememberPassword.Text = "Remember Password";
            chkRememberPassword.UseVisualStyleBackColor = true;
            chkRememberPassword.CheckedChanged += checkBox1_CheckedChanged;
            // 
            // btnConnect
            // 
            btnConnect.Location = new Point(249, 176);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(75, 23);
            btnConnect.TabIndex = 7;
            btnConnect.Text = "Connect";
            btnConnect.UseVisualStyleBackColor = true;
            btnConnect.Click += btnConnect_Click;
            // 
            // btnCancel
            // 
            btnCancel.Location = new Point(168, 176);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(75, 23);
            btnCancel.TabIndex = 8;
            btnCancel.Text = "Cancel";
            btnCancel.UseVisualStyleBackColor = true;
            btnCancel.Click += btnCancel_Click;
            // 
            // frmConnect
            // 
            AcceptButton = btnConnect;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = btnCancel;
            ClientSize = new Size(336, 213);
            Controls.Add(btnCancel);
            Controls.Add(btnConnect);
            Controls.Add(chkRememberPassword);
            Controls.Add(txtPassword);
            Controls.Add(label3);
            Controls.Add(txtUsername);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(cmbServerAddress);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Name = "frmConnect";
            Text = "MakeFox Manager - Connect";
            Load += FrmConnect_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ComboBox cmbServerAddress;
        private Label label1;
        private Label label2;
        private TextBox txtUsername;
        private Label label3;
        private TextBox txtPassword;
        private CheckBox chkRememberPassword;
        private Button btnConnect;
        private Button btnCancel;
    }
}
