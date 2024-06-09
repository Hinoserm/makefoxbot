using System;
using System.Windows.Forms;

namespace admingui
{
    internal static class Program
    {
        private static NotifyIcon? trayIcon;

        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // Show the login form and wait for successful login
            using (var loginForm = new frmConnect())
            {
                if (loginForm.ShowDialog() != DialogResult.OK)
                {
                    return; // Exit if login is not successful or the user cancels
                }
            }

            // Create the NotifyIcon and ContextMenu
            trayIcon = new NotifyIcon();
            ContextMenuStrip trayMenu = new ContextMenuStrip();

            trayMenu.Items.Add("Chat", null, OnChatClick);
            trayMenu.Items.Add("Status", null, OnStatusClick);
            trayMenu.Items.Add("Exit", null, OnExitClick);

            trayIcon.Text = "Admin GUI";
            trayIcon.Icon = Resources.trayicon; // Use the icon from resources
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;

            Application.ApplicationExit += OnApplicationExit;

            // Run the application without a main form
            Application.Run();
        }

        private static void OnChatClick(object sender, EventArgs e)
        {
            // Show the Chat form
            using (var chatForm = new FrmChat())
            {
                chatForm.ShowDialog();
            }
        }

        private static void OnStatusClick(object sender, EventArgs e)
        {
            // Show the Status form
            //using (var statusForm = new frmStatus())
            //{
            //    statusForm.ShowDialog();
            //}
        }

        private static void OnExitClick(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private static void OnApplicationExit(object sender, EventArgs e)
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
        }
    }
}