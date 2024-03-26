using MySqlConnector;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui;
using static Unix.Terminal.Curses;

namespace makefoxsrv
{
    internal class FoxUI
    {
        private static Task? guiTask;
        private static FrameView? workerPane;
        private static FrameView? userPane;
        private static Label? userLabel;

        private static TextView? _logView;
        private static ScrollBarView? _logScrollBar;

        public static void Start(CancellationTokenSource cts)
        {
            Console.OutputEncoding = Encoding.UTF8;

            Application.Init();
            var Top = Application.Top;

            // Create the top-level window to hold everything.
            var win = new Terminal.Gui.Window()
            {
                Title = "MakeFoxSrv",
                X = 0,
                Y = 1, // Leave one row for the top-level menu
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            var clrBlackText = new ColorScheme
            {
                //Normal = Attribute.Make(Color.Black, Color.Gray),
            };

            // Create a horizontal split view
            var leftPane = new FrameView()
            {
                Title = "Log",
                X = 0,
                Y = 0,
                Width = Dim.Percent(70) - 1,
                Height = Dim.Fill(),
            };

            workerPane = new FrameView()
            {
                Title = "Workers",
                X = Pos.Right(leftPane),
                Y = 0,
                Width = Dim.Percent(30),
                Height = Dim.Percent(40)
            };

            userPane = new FrameView()
            {
                Title = "Active Users",
                X = Pos.Right(leftPane),
                Y = Pos.Bottom(workerPane),
                Width = workerPane.Width,
                Height = Dim.Fill()
            };

            userLabel = new Label()
            {
                X = 0,
                Y = 0,
                AutoSize = false,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                TextAlignment = TextAlignment.Left,
                Visible = true
            };

            userPane.Add(userLabel);

            // Add text view for logs
            var logView = new TextView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = false, // Enable word wrap
                ColorScheme = clrBlackText,
            };

            leftPane.Add(logView);

            // Create a vertical ScrollBarView
            var scrollBar = new ScrollBarView(logView, true, true)
            {
                Visible = true,
                AutoHideScrollBars = false,
                ShowScrollIndicator = true
            };

            scrollBar.ChangedPosition += () => {
                logView.TopRow = scrollBar.Position;
                if (logView.TopRow != scrollBar.Position)
                {
                    scrollBar.Position = logView.TopRow;
                }
                logView.SetNeedsDisplay();
            };

            scrollBar.OtherScrollBarView.ChangedPosition += () => {
                logView.LeftColumn = scrollBar.OtherScrollBarView.Position;
                if (logView.LeftColumn != scrollBar.OtherScrollBarView.Position)
                {
                    scrollBar.OtherScrollBarView.Position = logView.LeftColumn;
                }
                logView.SetNeedsDisplay();
            };

            logView.DrawContent += (e) => {
                scrollBar.Size = logView.Lines - 1;
                scrollBar.Position = logView.TopRow;
                scrollBar.OtherScrollBarView.Size = logView.Maxlength;
                scrollBar.OtherScrollBarView.Position = logView.LeftColumn;
                scrollBar.LayoutSubviews();
                scrollBar.Refresh();
            };

            _logView = logView;
            _logScrollBar = scrollBar;

            win.Add(leftPane, workerPane, userPane);

            var wordWrapMenuItem = new MenuItem("_WordWrap", "", () => { })
            {

                CheckType = MenuItemCheckStyle.Checked,
                Checked = logView.WordWrap,
            };

            wordWrapMenuItem.Action = () =>
            {
                logView.WordWrap = !logView.WordWrap;
                logView.SetNeedsDisplay();
                wordWrapMenuItem.Checked = logView.WordWrap;
            };

            var menu = new MenuBar
            {
                Menus =
                [
                    new MenuBarItem("_View", new[] { wordWrapMenuItem })
                ]
            };

            Top.Add(menu);

            logView.SetFocus();

            Application.Top.Add(win);

            guiTask = Task.Run(() =>
            {
                try
                {
                    Application.Run();
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine($"Exception in GUI task: {ex.Message}");
                    // Handle or log the exception as needed
                }
            });
        }

        private static ConcurrentDictionary<int, Label> workerNameLabels = new ConcurrentDictionary<int, Label>();
        private static ConcurrentDictionary<int, Label> workerStatusLabels = new ConcurrentDictionary<int, Label>();
        private static ConcurrentDictionary<int, ProgressBar> workerProgressBars = new ConcurrentDictionary<int, ProgressBar>();
        private static int labelI = 0;


        public static async Task Run(CancellationTokenSource cts)
        {

            // Define color schemes
            var redScheme = new ColorScheme
            {
                //Normal = Terminal.Gui.Attribute.Make(Color.BrightRed, Color.Blue),
            };

            var greenScheme = new ColorScheme
            {
                //Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Blue),
            };

            var greyScheme = new ColorScheme
            {
                //Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Blue),
            };

            var manualResetEvent = new ManualResetEventSlim(false); // Reset event to control the flow

            while (!cts.IsCancellationRequested)
            {
                manualResetEvent.Reset();

                Application.MainLoop.Invoke(() =>
                {
                    if (workerPane is not null && workerPane.Visible == true)
                    {
                        foreach (var w in FoxWorker.workers)
                        {
                            var worker = w.Value;
                            var workerId = w.Key;

                            var workerName = worker.name;

                            Label? nameLabel;
                            Label? statusLabel;
                            ProgressBar? progressBar;

                            if (!workerNameLabels.TryGetValue(workerId, out nameLabel))
                            {
                                // The index is missing, so add a new Label
                                nameLabel = new Label("Worker")
                                {
                                    AutoSize = false,
                                    X = 0,
                                    Y = labelI * 2,
                                    Width = Dim.Fill(),
                                    TextAlignment = TextAlignment.Left
                                };

                                statusLabel = new Label("OFFLINE")
                                {
                                    AutoSize = false,
                                    X = 0,
                                    Y = labelI * 2 + 1,
                                    Width = Dim.Fill(),
                                    TextAlignment = TextAlignment.Left,
                                    Visible = true
                                };

                                progressBar = new ProgressBar()
                                {
                                    X = 0,
                                    Y = labelI * 2 + 1,
                                    Width = Dim.Fill(),
                                    Visible = false,
                                    Fraction = 0.0f // Update this value to reflect progress
                                };

                                workerNameLabels.TryAdd(workerId, nameLabel);
                                workerStatusLabels.TryAdd(workerId, statusLabel);
                                workerProgressBars.TryAdd(workerId, progressBar);
                                workerPane.Add(nameLabel);
                                workerPane.Add(progressBar);
                                workerPane.Add(statusLabel);
                                labelI++;
                            }
                            else
                            {
                                workerProgressBars.TryGetValue(workerId, out progressBar);
                                workerStatusLabels.TryGetValue(workerId, out statusLabel);
                            }

                            nameLabel.Text = $"Worker {workerId} - {workerName}";
                            if (!worker.online)
                            {
                                statusLabel.Text = "OFFLINE";
                                statusLabel.ColorScheme = redScheme;
                                statusLabel.Visible = true;
                                progressBar.Visible = false;
                            }
                            else if (worker.PercentComplete is null)
                            {
                                statusLabel.Text = "IDLE";
                                statusLabel.ColorScheme = greyScheme;
                                statusLabel.Visible = true;
                                progressBar.Visible = false;
                            }
                            else if (worker.PercentComplete is not null)
                            {
                                progressBar.Fraction = (float)(worker.PercentComplete / 100f);
                                progressBar.Visible = true;
                                statusLabel.Text = "WORKING";
                                statusLabel.Visible = false;
                            }
                        }
                    }

                    manualResetEvent.Set();
                });

                manualResetEvent.Wait();
                manualResetEvent.Reset();

                using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

                await SQL.OpenAsync();

                int height = 1;
                Application.MainLoop.Invoke (() => {
                    height = userPane.Frame.Height;
                    manualResetEvent.Set();
                });

                manualResetEvent.Wait(); // Block until the reset event is signaled
                manualResetEvent.Reset();

                if (height > 0)
                {
                    MySqlCommand cmd = new MySqlCommand(@$"
                        SELECT u.username, q.uid, MAX(q.date_added) AS recent_date
                        FROM queue q
                        JOIN users u ON u.id = q.uid
                        GROUP BY q.uid, u.username
                        ORDER BY recent_date DESC
                        LIMIT {height - 2};
                        ", SQL);

                    int rows = 0;

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (reader.HasRows)
                        {
                            string labelText = "";

                            while (await reader.ReadAsync())
                            {
                                int uid = reader.GetInt32("uid");
                                string username = reader.IsDBNull(reader.GetOrdinal("username")) ? "" : reader.GetString("username");
                                DateTime lastActive = reader.GetDateTime("recent_date");
                                TimeSpan timeAgo = DateTime.Now - lastActive; // Assuming date_added is in UTC

                                labelText += $"{uid.ToString().PadLeft(4, ' ')} : {username.PadRight(17, ' ')} : {timeAgo.ToShortPrettyFormat()}\r\n";

                                rows++;
                            }

                            Application.MainLoop.Invoke(() => {
                                userLabel.Text = labelText;
                            });
                        }
                    }
                }
                //FoxLog.WriteLine($"run {rows}");

                //manualResetEvent.Set();

                //manualResetEvent.Wait(); // Block until the reset event is signaled
                //manualResetEvent.Dispose(); // Clean up the event

                

                await Task.Delay(200, cts.Token);
            }

            Application.RequestStop();

            if (guiTask is not null)
                await guiTask;
        }
        public static void AppendLog(string value)
        {
            var manualResetEvent = new ManualResetEventSlim(false); // Reset event to control the flow

            Application.MainLoop.Invoke(() =>
            {
                const int maxTextLength = 100 * 1024 * 1024; // 100MB in characters

                if (_logView is null || _logScrollBar is null)
                {
                    manualResetEvent.Set();
                    return;
                }

                var linesBeforeUpdate = _logView.Text.Split("\n");
                // Determine if the user has scrolled to the bottom
                //bool isUserAtBottom = _textView.TopRow + _textView.Frame.Height >= linesBeforeUpdate.Length;
                bool isUserAtBottom = _logView.TopRow + _logView.Frame.Height >= _logScrollBar.Size;
                int originalTopRow = _logView.TopRow;

                _logView.Text += value;

                // Trim the text if it exceeds the maximum length
                if (_logView.Text.Length > maxTextLength)
                {
                    int removeLength = _logView.Text.Length - maxTextLength;
                    _logView.Text = _logView.Text.Substring(removeLength);
                }


                if (isUserAtBottom)
                {
                    // Scroll to the bottom if the user was already there
                    //_textView.TopRow = Math.Max(0, lines.Length - _textView.Frame.Height);
                    _logView.CursorPosition = new Terminal.Gui.Point(_logView.Text.Length, _logView.Text.Count(c => c == '\n'));
                }
                else
                {
                    //Maintain user's scroll position.
                    _logView.TopRow = originalTopRow;
                }
                manualResetEvent.Set();
            });

            //manualResetEvent.Wait();
            manualResetEvent.Dispose();
        }
    }
}
