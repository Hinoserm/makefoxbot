using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static WebSocketManager;

namespace admingui
{
    public class UserSearchControl : UserControl
    {
        private TextBox searchTextBox;
        private ToolStripDropDown suggestionDropDown;
        private string selectedUserText;

        public event EventHandler<string> UserSelected;

        public UserSearchControl()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.searchTextBox = new TextBox
            {
                Dock = DockStyle.Top
            };
            this.searchTextBox.TextChanged += SearchTextBox_TextChanged;
            this.searchTextBox.TextChanged += (s, e) => selectedUserText = searchTextBox.Text;
            this.searchTextBox.LostFocus += SearchTextBox_LostFocus;
            this.Controls.Add(searchTextBox);

            this.suggestionDropDown = new ToolStripDropDown
            {
                AutoClose = false
            };
            this.suggestionDropDown.LostFocus += SuggestionDropDown_LostFocus;
        }

        private async void SearchTextBox_TextChanged(object sender, EventArgs e)
        {
            var query = searchTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                try
                {
                    var suggestions = await WebSocketManager.Instance.GetAutocompleteSuggestionsAsync(query);
                    if (suggestions.Count > 0)
                    {
                        ShowSuggestions(suggestions);
                    }
                    else
                    {
                        suggestionDropDown.Close();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
            else
            {
                suggestionDropDown.Close();
            }
        }

        private void ShowSuggestions(List<Suggestion> suggestions)
        {
            suggestionDropDown.Items.Clear();
            int maxSuggestions = 5; // Limit to top 5 suggestions

            // Calculate the maximum width of display text
            int maxDisplayTextWidth = 0;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                foreach (var suggestion in suggestions)
                {
                    var displayTextSize = g.MeasureString(suggestion.Display, this.Font);
                    maxDisplayTextWidth = Math.Max(maxDisplayTextWidth, (int)displayTextSize.Width);
                }
            }

            for (int i = 0; i < Math.Min(suggestions.Count, maxSuggestions); i++)
            {
                var item = new CustomToolStripMenuItem(suggestions[i].Display, suggestions[i].Paste, maxDisplayTextWidth)
                {
                    Tag = suggestions[i].Paste
                };
                item.Click += (s, e) =>
                {
                    searchTextBox.TextChanged -= SearchTextBox_TextChanged; // Temporarily disable the event
                    searchTextBox.Text = item.Tag.ToString();
                    selectedUserText = item.Tag.ToString(); // Set the selected user text
                    searchTextBox.SelectionStart = searchTextBox.Text.Length; // Move cursor to the end
                    searchTextBox.TextChanged += SearchTextBox_TextChanged; // Re-enable the event
                    suggestionDropDown.Close();
                };
                suggestionDropDown.Items.Add(item);
            }

            var point = searchTextBox.PointToScreen(new Point(0, searchTextBox.Height));
            suggestionDropDown.Show(point);
        }

        private void SearchTextBox_LostFocus(object sender, EventArgs e)
        {
            if (!suggestionDropDown.ContainsFocus)
            {
                suggestionDropDown.Close();
            }
        }

        private void SuggestionDropDown_LostFocus(object sender, EventArgs e)
        {
            suggestionDropDown.Close();
        }

        public void Clear()
        {
            searchTextBox.Clear();
            selectedUserText = string.Empty;
            suggestionDropDown.Close();
        }

        public override string Text
        {
            get => selectedUserText;
            set
            {
                searchTextBox.Text = value;
                selectedUserText = value;
            }
        }
    }

    public class CustomToolStripMenuItem : ToolStripMenuItem
    {
        private string pasteText;
        private int maxDisplayTextWidth;

        public CustomToolStripMenuItem(string displayText, string pasteText, int maxDisplayTextWidth) : base(displayText)
        {
            this.pasteText = pasteText;
            this.maxDisplayTextWidth = maxDisplayTextWidth;
            this.AutoSize = false; // Disable auto-size to manage custom size
            this.Padding = new Padding(5); // Add padding to avoid text overlap
            this.Width = CalculateWidth(displayText, pasteText);
            this.Height = CalculateHeight(displayText, pasteText);
            this.BackColor = Color.White; // Set background to white
        }

        private int CalculateWidth(string displayText, string pasteText)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                var pasteTextSize = g.MeasureString(pasteText, this.Font);
                return maxDisplayTextWidth + (int)pasteTextSize.Width + this.Padding.Horizontal + 20; // Add extra padding for safety
            }
        }

        private int CalculateHeight(string displayText, string pasteText)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                var displayTextSize = g.MeasureString(displayText, this.Font);
                var pasteTextSize = g.MeasureString(pasteText, this.Font);
                return (int)Math.Max(displayTextSize.Height, pasteTextSize.Height) + this.Padding.Vertical; // Ensure height accommodates both texts
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Clear the item to avoid overlap
            e.Graphics.Clear(this.BackColor);

            // Measure the size of the display text
            var displayTextSize = e.Graphics.MeasureString(this.Text, this.Font);

            // Draw the display text
            e.Graphics.DrawString(this.Text, this.Font, Brushes.Black, new PointF(this.Padding.Left, (this.Height - displayTextSize.Height) / 2));

            // Measure the size of the paste text
            var pasteTextSize = e.Graphics.MeasureString(pasteText, this.Font);

            // Draw the paste text in grey color
            using (Brush brush = new SolidBrush(Color.Gray))
            {
                e.Graphics.DrawString(pasteText, this.Font, brush, new PointF(maxDisplayTextWidth + this.Padding.Left + 10, (this.Height - pasteTextSize.Height) / 2));
            }
        }
    }
}
