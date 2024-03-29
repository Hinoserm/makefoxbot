using MySqlConnector;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui;
using WTelegram;
using makefoxsrv;
using System.Linq.Expressions;
using System.Drawing;
using System.Diagnostics;
using System.Reflection.Metadata;

namespace makefoxsrv
{
    internal class FoxUI
    {
        private static Toplevel _top;

        private static Window? _win;
        private static FrameView? _leftPane;
        private static FrameView? _workerPane;
        private static FrameView? _userPane;
        private static Label? _userLabel;
        private static TextView? _logView;
        private static ScrollBarView? _logScrollBar;

        private static string _logBuffer = "";
        
        public static bool hasStopped { get; private set; } = false;

        public static void Init()
        {
            //Console.OutputEncoding = Encoding.UTF8;

            Application.Init();
        }

        public static void Start(CancellationTokenSource cts)
        {
            //Application.Init();

            _top = new()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            _win = new Window()
            {
                X = 0,
                Y = 1, // Leave one row for the top-level menu
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            //TabView tabView = new()
            //{
            //    X = 0,
            //    Y = 0,
            //    Width = Dim.Fill() - 42,
            //    Height = Dim.Fill(),
            //};

            _leftPane = new()
            {
                Title = "Log",
                X = 0,
                Y = 0,
                Width = Dim.Fill() - 42,
                Height = Dim.Fill(),
            };

            _workerPane = new()
            {
                Title = "Workers",
                X = Pos.Right(_leftPane),
                Y = 0,
                Width = Dim.Sized(42),
                Height = Dim.Sized(8)
            };

            _userPane = new()
            {
                Title = "Active Users",
                X = Pos.Right(_leftPane),
                Y = Pos.Bottom(_workerPane),
                Width = _workerPane.Width,
                Height = Dim.Fill()
            };

            _userLabel = new()
            {
                X = 0,
                Y = 0,
                AutoSize = false,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                TextAlignment = TextAlignment.Left,
                Visible = true
            };

            _logView = new()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = false, // Enable word wrap
                ColorScheme = new()
                {
                    Normal = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.Black, Terminal.Gui.Color.Gray)
                }
            };

            //tabView.AddTab(new TabView.Tab { Text = "Log", View = _logView }, true);
            _leftPane.Add(_logView);

            _logScrollBar = new(_logView, true, true)
            {
                Visible = true,
                AutoHideScrollBars = true,
                ShowScrollIndicator = true
            };

            _logScrollBar.ChangedPosition += () => {
                try
                {
                    _logView.TopRow = _logScrollBar.Position;
                    if (_logView.TopRow != _logScrollBar.Position)
                    {
                        _logScrollBar.Position = _logView.TopRow;
                    }
                    _logView.SetNeedsDisplay();
                }
                catch { }
            };

            _logScrollBar.OtherScrollBarView.ChangedPosition += () => {
                try
                {
                    _logView.LeftColumn = _logScrollBar.OtherScrollBarView.Position;
                    if (_logView.LeftColumn != _logScrollBar.OtherScrollBarView.Position)
                    {
                        _logScrollBar.OtherScrollBarView.Position = _logView.LeftColumn;
                    }
                    _logView.SetNeedsDisplay();
                }
                catch { }
            };

            _logView.DrawContent += (e) => {
                try
                {
                    _logScrollBar.Size = _logView.Lines - 1;
                    _logScrollBar.Position = _logView.TopRow;
                    _logScrollBar.OtherScrollBarView.Size = _logView.Maxlength;
                    _logScrollBar.OtherScrollBarView.Position = _logView.LeftColumn;
                    _logScrollBar.LayoutSubviews();
                    _logScrollBar.Refresh();
                }
                catch { }
            };

            var wordWrapMenuItem = new MenuItem("_WordWrap", "", () => { })
            {

                CheckType = MenuItemCheckStyle.Checked,
                Checked = _logView.WordWrap,
            };

            wordWrapMenuItem.Action = () =>
            {
                _logView.WordWrap = !_logView.WordWrap;
                _logView.SetNeedsDisplay();
                wordWrapMenuItem.Checked = _logView.WordWrap;
            };

            var menu = new MenuBar
            {
                Menus =
                [
                    new MenuBarItem("_View", new[] { wordWrapMenuItem })
                ]
            };

            Stopwatch stopwatch = new Stopwatch();

            stopwatch.Start();

            Application.MainLoop.AddIdle(() =>
            {
                Task.Delay(10).Wait();

                if (stopwatch.ElapsedMilliseconds >= 100)
                {
                    try
                    {
                        Update();
                    }
                    catch (Exception ex)
                    {
                        // Print the stack trace of the exception
                        FoxLog.WriteLine("UI Update Exception: " + ex.Message);
                        FoxLog.WriteLine("Stack Trace: " + ex.StackTrace);
                    }
                    stopwatch.Restart();
                }
                return true;
            });

            _logView.SetFocus();

            _userPane.Add(_userLabel);
            _win.Add(_leftPane, _workerPane, _userPane); 

            _top.Add(_win);
            _top.Add(menu);

            _top.KeyDown += (args) =>
            {
                if (args.KeyEvent.Key == (Key.CtrlMask | Key.C))
                {
                    Application.RequestStop();
                    args.Handled = true; // Mark the event as handled
                }
            };

           

            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await UserUpdate();
                        await Task.Delay(1000, cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        //Do nothing.
                    }
                    catch (Exception ex)
                    {
                        FoxLog.WriteLine("User Update Exception: " + ex.Message);
                        FoxLog.WriteLine("Stack Trace: " + ex.StackTrace);
                    }
                }
            });

            /* Application.Resized += (args) =>
            {
                _win?.SetNeedsDisplay();
            }; */

            try
            {
                if (!cts.IsCancellationRequested)
                    Application.Run(_top);

                FoxUI.hasStopped = true;
                Application.Shutdown();
            } catch (TaskCanceledException) {
                //Do nothing.
            }
            catch (Exception ex)
            {
                Application.Shutdown();

                FoxUI.hasStopped = true;

                FoxLog.WriteLine("User Update Exception: " + ex.Message);
                FoxLog.WriteLine("Stack Trace: " + ex.StackTrace);

                while (!cts.IsCancellationRequested) {
                    Task.Delay(1000).Wait();
                }
            }
        }

        private static ConcurrentDictionary<int, Label> workerNameLabels = new ConcurrentDictionary<int, Label>();
        private static ConcurrentDictionary<int, Label> workerStatusLabels = new ConcurrentDictionary<int, Label>();
        private static ConcurrentDictionary<int, ProgressBar> workerProgressBars = new ConcurrentDictionary<int, ProgressBar>();
        private static int labelI = 0;

        private static string userString = "";
        private static int userSize = 10;

        private static async Task UserUpdate()
        {

            if (userSize > 0)
            {

                using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

                await SQL.OpenAsync();

                MySqlCommand cmd = new MySqlCommand(@$"
                    SELECT u.username, q.uid, MAX(q.date_added) AS recent_date
                    FROM queue q
                    JOIN users u ON u.id = q.uid
                    GROUP BY q.uid, u.username
                    ORDER BY recent_date DESC
                    LIMIT {userSize - 2};
                    ", SQL);

                int rows = 0;

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (reader.HasRows)
                    {
                        string labelText = "";

                        while (reader.Read())
                        {
                            int uid = reader.GetInt32("uid");
                            string username = reader.IsDBNull(reader.GetOrdinal("username")) ? "" : reader.GetString("username");

                            DateTime lastActive = reader.GetDateTime("recent_date");
                            TimeSpan timeAgo = DateTime.Now - lastActive; // Assuming date_added is in UTC

                            labelText += $"{uid.ToString().PadLeft(5, ' ')} : {username.PadRight(24, ' ')} : {timeAgo.ToShortPrettyFormat()}\r\n";

                            rows++;
                        }

                        userString = labelText;
                    }
                }
            }
        }

        public static void Update()
        {

            // Define color schemes
            var redScheme = new ColorScheme
            {
                Normal = new Terminal.Gui.Attribute(Terminal.Gui.Color.BrightRed, Terminal.Gui.Color.Blue)
            };

            var greenScheme = new ColorScheme
            {
                //Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Blue),
                Normal = new Terminal.Gui.Attribute(Terminal.Gui.Color.BrightGreen, Terminal.Gui.Color.Blue)
            };

            var greyScheme = new ColorScheme
            {
                //Normal = Terminal.Gui.Attribute.Make(Terminal.Gui., Color.Blue),
                Normal = new Terminal.Gui.Attribute(Terminal.Gui.Color.Gray, Terminal.Gui.Color.Blue)
            };

            var manualResetEvent = new ManualResetEventSlim(false); // Reset event to control the flow

            //while (!cts.IsCancellationRequested)
            //{
                //manualResetEvent.Reset();

                //Application.MainLoop.Invoke(() =>
                //{
            if (_workerPane is not null && _workerPane.Visible == true)
            {
                foreach (var w in FoxWorker.GetAll())
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
                        nameLabel = new Label()
                        {
                            Text = "Worker",
                            AutoSize = false,
                            X = 0,
                            Y = labelI * 3,
                            Width = Dim.Fill(),
                            TextAlignment = TextAlignment.Left
                        };

                        statusLabel = new Label()
                        {
                            Text = "OFFLINE",
                            AutoSize = false,
                            X = 0,
                            Y = labelI * 3 + 1,
                            Width = Dim.Fill(),
                            TextAlignment = TextAlignment.Left,
                            Visible = true
                        };

                        progressBar = new ProgressBar()
                        {
                            X = 0,
                            Y = labelI * 3 + 2,
                            Width = Dim.Fill(),
                            Visible = true,
                            Fraction = 0.0f // Update this value to reflect progress
                        };

                        workerNameLabels.TryAdd(workerId, nameLabel);
                        workerStatusLabels.TryAdd(workerId, statusLabel);
                        workerProgressBars.TryAdd(workerId, progressBar);
                        _workerPane.Add(nameLabel);
                        _workerPane.Add(progressBar);
                        _workerPane.Add(statusLabel);
                        labelI++;
                    }
                    else
                    {
                        workerProgressBars.TryGetValue(workerId, out progressBar);
                        workerStatusLabels.TryGetValue(workerId, out statusLabel);
                    }

                    if (progressBar is not null && statusLabel is not null)
                    {
                        nameLabel.Text = $"Worker {workerId} - {workerName}";
                        if (!worker.online)
                        {
                            statusLabel.Text = "OFFLINE";
                            statusLabel.ColorScheme = redScheme;
                            statusLabel.Visible = true;
                            //progressBar.Visible = false;
                            progressBar.Fraction = 0;
                        }
                        else if (worker.PercentComplete is null)
                        {
                            statusLabel.Text = "IDLE";
                            statusLabel.ColorScheme = greyScheme;
                            statusLabel.Visible = true;
                            //progressBar.Visible = false;
                            progressBar.Fraction = 0;
                        }
                        else if (worker.PercentComplete is not null)
                        {
                            progressBar.Fraction = (float)(worker.PercentComplete / 100f);
                            progressBar.Visible = true;
                            if (worker.qitem is not null && worker.qitem.settings is not null)
                            {
                                statusLabel.Text = $"{worker.qitem.settings.width}x{worker.qitem.settings.height} {worker.qitem.settings.steps} {worker.qitem.settings.model}";
                                //statusLabel.Visible = false;
                            }
                            else
                            {
                                statusLabel.Text = "WORKING";
                                statusLabel.ColorScheme = greenScheme;
                            }
                            //statusLabel.Visible = false;
                        }

                        _workerPane.Height = Dim.Sized((FoxWorker.GetAll().Count * 3) + 2);
                    }
                }
            }

            if (_userPane is not null && _userLabel is not null && _userPane.Visible == true)
            {
                userSize = _userPane.Frame.Height;
                _userLabel.Text = userString;
            }
            else
            {
                userSize = 0;
            }

            if (_logBuffer.Length > 0 && _logView is not null && _logScrollBar is not null && _logView.Visible && _logBuffer.Length > 0)
            {
                const int maxTextLength = 10 * 1024 * 1024; // 10MB in characters

                var linesBeforeUpdate = _logView.Text.Split("\n");
                // Determine if the user has scrolled to the bottom
                //bool isUserAtBottom = _textView.TopRow + _textView.Frame.Height >= linesBeforeUpdate.Length;
                bool isUserAtBottom = _logView.TopRow + _logView.Frame.Height >= _logScrollBar.Size;
                int originalTopRow = _logView.TopRow;

                lock (_logBuffer)
                {
                    _logView.Text += _logBuffer;
                    _logBuffer = "";
                }

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
            }
        }

        public static void AppendLog(string value)
        {
            _logBuffer += value;

            if (hasStopped) //Also print to the console.
                Console.Write(value);
        }
    }
}
