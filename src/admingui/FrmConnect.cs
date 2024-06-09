using Microsoft.Win32;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace admingui
{
    public partial class frmConnect : Form
    {
        public frmConnect()
        {
            InitializeComponent();
        }

        private const string RegistryKeyPath = @"SOFTWARE\MakeFoxManager";

        private void FrmConnect_Load(object sender, EventArgs e)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key != null)
                {
                    cmbServerAddress.Text = key.GetValue("ServerAddress")?.ToString() ?? string.Empty;
                    txtUsername.Text = key.GetValue("Username")?.ToString() ?? string.Empty;
                    chkRememberPassword.Checked = key.GetValue("RememberPassword")?.ToString() == "1";

                    if (chkRememberPassword.Checked)
                    {
                        txtPassword.Text = key.GetValue("Password")?.ToString() ?? string.Empty;
                    }

                    key.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}");
            }
        }

        private void FrmConnect_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {

        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {

            btnConnect.Enabled = false;

            if (String.IsNullOrEmpty(cmbServerAddress.Text))
            {
                MessageBox.Show("Please enter server address");
                return;
            }

            if (String.IsNullOrEmpty(txtUsername.Text))
            {
                MessageBox.Show("Please enter username");
                return;
            }

            if (String.IsNullOrEmpty(txtPassword.Text))
            {
                MessageBox.Show("Please enter password");
                return;
            }

            try
            {
                await WebSocketManager.Instance.ConnectAsync(cmbServerAddress.Text);

                var loginSuccess = await WebSocketManager.Instance.LoginAsync(txtUsername.Text, txtPassword.Text);

                if (loginSuccess)
                {
                    // Normalize the username to match what the server returns
                    txtUsername.Text = WebSocketManager.Instance.Username; 

                    try
                    {
                        RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                        if (key != null)
                        {
                            key.SetValue("ServerAddress", cmbServerAddress.Text);
                            key.SetValue("Username", txtUsername.Text);
                            key.SetValue("RememberPassword", chkRememberPassword.Checked ? "1" : "0");

                            if (chkRememberPassword.Checked)
                            {
                                key.SetValue("Password", txtPassword.Text);
                            }
                            else
                            {
                                key.DeleteValue("Password", false);
                            }

                            key.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error saving settings: {ex.Message}");
                    }

                    MessageBox.Show("Login successful: " + WebSocketManager.Instance.Username, "Login Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                } else
                {
                    MessageBox.Show("Login failed (unknown error)", "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnConnect.Enabled = true;
            }
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void txtUsername_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
