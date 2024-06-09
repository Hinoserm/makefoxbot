using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace admingui
{
    public class ChatMessageControl : UserControl
    {
        private Label lblMessage;
        private Label lblTimestamp;
        private Label lblUsername;

        public ChatMessageControl(string message, string timestamp, string username, bool isSentByUser)
        {
            this.IsSentByUser = isSentByUser;
            this.lblMessage = new Label();
            this.lblTimestamp = new Label();
            this.lblUsername = new Label();

            this.InitializeComponent();

            this.Message = message;
            this.Timestamp = timestamp;
            this.Username = username;

            this.Resize += ChatMessageControl_Resize;
            AdjustSize();
        }

        private void InitializeComponent()
        {
            this.lblMessage.AutoSize = true;
            this.lblMessage.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblMessage.MaximumSize = new Size(380, 0); // Set maximum width to 380 pixels

            this.lblTimestamp.AutoSize = true;
            this.lblTimestamp.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblTimestamp.ForeColor = Color.Gray;

            this.lblUsername.AutoSize = true;
            this.lblUsername.Font = new Font("Segoe UI", 8F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblUsername.ForeColor = Color.DarkBlue;
            this.lblUsername.TextAlign = ContentAlignment.TopLeft;

            this.Controls.Add(this.lblMessage);
            this.Controls.Add(this.lblTimestamp);
            this.Controls.Add(this.lblUsername);

            this.Padding = new Padding(10);
            this.BackColor = Color.Transparent; // Set to transparent to draw custom background
        }

        private void ChatMessageControl_Resize(object sender, EventArgs e)
        {
            AdjustSize();
        }

        public bool IsSentByUser { get; set; }

        public string Message
        {
            get { return lblMessage.Text; }
            set
            {
                lblMessage.Text = value;
                AdjustSize();
            }
        }

        public string Timestamp
        {
            get { return lblTimestamp.Text; }
            set { lblTimestamp.Text = value; }
        }

        public string Username
        {
            get { return lblUsername.Text; }
            set { lblUsername.Text = value; }
        }

        private void AdjustSize()
        {
            using (Graphics g = CreateGraphics())
            {
                SizeF messageSize = TextRenderer.MeasureText(g, lblMessage.Text, lblMessage.Font, lblMessage.MaximumSize, TextFormatFlags.WordBreak);
                SizeF usernameSize = TextRenderer.MeasureText(g, lblUsername.Text, lblUsername.Font);
                SizeF timestampSize = TextRenderer.MeasureText(g, lblTimestamp.Text, lblTimestamp.Font);

                int maxWidth = (int)Math.Max(Math.Max(messageSize.Width, usernameSize.Width), timestampSize.Width);
                lblMessage.Size = messageSize.ToSize();
                lblUsername.Size = usernameSize.ToSize();
                lblTimestamp.Size = timestampSize.ToSize();

                lblUsername.Location = new Point(10, 10);
                lblMessage.Location = new Point(10, lblUsername.Bottom + 5);
                lblTimestamp.Location = new Point(10, lblMessage.Bottom + 5);

                int newWidth = maxWidth + Padding.Left + Padding.Right + 20; // Add extra padding for better spacing
                int newHeight = lblUsername.Height + lblMessage.Height + lblTimestamp.Height + Padding.Top + Padding.Bottom + 15;

                this.Size = new Size(newWidth, newHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            int cornerRadius = 15;

            using (GraphicsPath path = CreateRoundedRectanglePath(rect, cornerRadius))
            {
                using (Brush brush = new SolidBrush(IsSentByUser ? Color.LightBlue : Color.LightGray))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Color.Gray, 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }

        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int cornerRadius)
        {
            GraphicsPath path = new GraphicsPath();
            path.StartFigure();
            path.AddArc(rect.Left, rect.Top, cornerRadius, cornerRadius, 180, 90);
            path.AddArc(rect.Right - cornerRadius, rect.Top, cornerRadius, cornerRadius, 270, 90);
            path.AddArc(rect.Right - cornerRadius, rect.Bottom - cornerRadius, cornerRadius, cornerRadius, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - cornerRadius, cornerRadius, cornerRadius, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
