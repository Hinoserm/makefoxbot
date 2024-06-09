using System;
using System.Drawing;
using System.Windows.Forms;

namespace admingui
{
    public class ChatContainerControl : Panel
    {
        private bool autoScrollEnabled = true;

        public ChatContainerControl()
        {
            this.AutoScroll = true;
            this.DoubleBuffered = true;
            this.Padding = new Padding(10, 10, 10, 25); // Reduce padding, especially at the bottom
            this.Resize += (s, e) => PositionChatMessages();
            this.Scroll += ChatContainerControl_Scroll;
        }

        public void AddChatMessage(ChatMessageControl messageControl)
        {
            this.Controls.Add(messageControl);
            PositionChatMessages();
            if (autoScrollEnabled)
            {
                ScrollToBottom();
            }
        }

        private void ChatContainerControl_Scroll(object sender, ScrollEventArgs e)
        {
            // If the user scrolls manually, disable auto-scrolling
            if (e.ScrollOrientation == ScrollOrientation.VerticalScroll)
            {
                if (e.NewValue != this.VerticalScroll.Maximum)
                {
                    autoScrollEnabled = false;
                }
                else
                {
                    autoScrollEnabled = true;
                }
            }
        }

        public void ScrollToBottom()
        {
            this.VerticalScroll.Value = this.VerticalScroll.Maximum;
            this.PerformLayout();
        }

        private void PositionChatMessages()
        {
            int yOffset = Padding.Top;
            int padding = 10;

            foreach (Control control in this.Controls)
            {
                ChatMessageControl chatMessage = control as ChatMessageControl;
                if (chatMessage != null)
                {
                    chatMessage.MaximumSize = new Size(400, 0); // Set maximum width to 400 pixels
                    int xOffset = chatMessage.IsSentByUser
                        ? this.ClientSize.Width - chatMessage.Width - padding - this.Padding.Right
                        : padding + this.Padding.Left;

                    chatMessage.Location = new Point(xOffset, yOffset);
                    yOffset += chatMessage.Height + padding;
                }
            }
        }
    }
}
