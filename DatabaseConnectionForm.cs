using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MySql.Data.MySqlClient;
using L1MapViewer.Helper;
using L1MapViewer.Localization;

namespace L1FlyMapViewer
{
    public class DatabaseConnection
    {
        public string Name { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Port { get; set; } = "3306";
        public string Database { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public override string ToString()
        {
            return Name;
        }

        public string GetConnectionString()
        {
            return $"Server={Server};Port={Port};Database={Database};Uid={Username};Pwd={Password};";
        }
    }

    public partial class DatabaseConnectionForm : Form
    {
        private ListBox listConnections = null!;
        private Button btnNew = null!;
        private Button btnEdit = null!;
        private Button btnDelete = null!;
        private Button btnConnect = null!;
        private Button btnClose = null!;
        private GroupBox grpConnectionInfo = null!;
        private TextBox txtName = null!;
        private TextBox txtServer = null!;
        private TextBox txtPort = null!;
        private TextBox txtDatabase = null!;
        private TextBox txtUsername = null!;
        private TextBox txtPassword = null!;
        private Button btnTestConnection = null!;
        private Button btnSave = null!;
        private Button btnCancel = null!;
        private Label lblStatus = null!;

        // Labels for localization
        private Label lblConnections = null!;
        private Label lblName = null!;
        private Label lblServer = null!;
        private Label lblPort = null!;
        private Label lblDatabase = null!;
        private Label lblUsername = null!;
        private Label lblPassword = null!;

        private List<DatabaseConnection> connections = new List<DatabaseConnection>();
        private DatabaseConnection? selectedConnection;
        private bool isEditing = false;

        public DatabaseConnectionForm()
        {
            InitializeComponent();
            LoadConnections();
            UpdateUI();
            UpdateLocalization();
            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateLocalization()));
            }
            else
            {
                UpdateLocalization();
            }
        }

        private void InitializeComponent()
        {
            this.Text = "資料庫連線管理";
            this.Size = new Size(700, 450);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            // 左側連線列表
            lblConnections = new Label
            {
                Text = "已儲存的連線:",
                Location = new Point(10, 10),
                Size = new Size(200, 20)
            };

            listConnections = new ListBox
            {
                Location = new Point(10, 35),
                Size = new Size(200, 300)
            };
            listConnections.SelectedIndexChanged += ListConnections_SelectedIndexChanged;

            btnNew = new Button
            {
                Text = "新增",
                Location = new Point(10, 345),
                Size = new Size(60, 25)
            };
            btnNew.Click += BtnNew_Click;

            btnEdit = new Button
            {
                Text = "編輯",
                Location = new Point(75, 345),
                Size = new Size(60, 25)
            };
            btnEdit.Click += BtnEdit_Click;

            btnDelete = new Button
            {
                Text = "刪除",
                Location = new Point(140, 345),
                Size = new Size(60, 25)
            };
            btnDelete.Click += BtnDelete_Click;

            btnConnect = new Button
            {
                Text = "連線",
                Location = new Point(10, 375),
                Size = new Size(90, 30),
                Font = new Font("微軟正黑體", 10, FontStyle.Bold)
            };
            btnConnect.Click += BtnConnect_Click;

            btnClose = new Button
            {
                Text = "關閉",
                Location = new Point(110, 375),
                Size = new Size(90, 30)
            };
            btnClose.Click += (s, e) => this.Close();

            // 右側連線資訊
            grpConnectionInfo = new GroupBox
            {
                Text = "連線資訊",
                Location = new Point(220, 10),
                Size = new Size(460, 395)
            };

            int labelX = 15;
            int textBoxX = 110;
            int startY = 25;
            int spacing = 35;
            int width = 320;

            // Name
            lblName = new Label
            {
                Text = "連線名稱:",
                Location = new Point(labelX, startY),
                Size = new Size(90, 20)
            };
            txtName = new TextBox
            {
                Location = new Point(textBoxX, startY),
                Size = new Size(width, 20)
            };

            // Server
            lblServer = new Label
            {
                Text = "伺服器位址:",
                Location = new Point(labelX, startY + spacing),
                Size = new Size(90, 20)
            };
            txtServer = new TextBox
            {
                Location = new Point(textBoxX, startY + spacing),
                Size = new Size(width, 20)
            };

            // Port
            lblPort = new Label
            {
                Text = "埠號:",
                Location = new Point(labelX, startY + spacing * 2),
                Size = new Size(90, 20)
            };
            txtPort = new TextBox
            {
                Location = new Point(textBoxX, startY + spacing * 2),
                Size = new Size(width, 20),
                Text = "3306"
            };

            // Database
            lblDatabase = new Label
            {
                Text = "資料庫名稱:",
                Location = new Point(labelX, startY + spacing * 3),
                Size = new Size(90, 20)
            };
            txtDatabase = new TextBox
            {
                Location = new Point(textBoxX, startY + spacing * 3),
                Size = new Size(width, 20)
            };

            // Username
            lblUsername = new Label
            {
                Text = "使用者名稱:",
                Location = new Point(labelX, startY + spacing * 4),
                Size = new Size(90, 20)
            };
            txtUsername = new TextBox
            {
                Location = new Point(textBoxX, startY + spacing * 4),
                Size = new Size(width, 20)
            };

            // Password
            lblPassword = new Label
            {
                Text = "密碼:",
                Location = new Point(labelX, startY + spacing * 5),
                Size = new Size(90, 20)
            };
            txtPassword = new TextBox
            {
                Location = new Point(textBoxX, startY + spacing * 5),
                Size = new Size(width, 20),
                PasswordChar = '*'
            };

            // Status label
            lblStatus = new Label
            {
                Location = new Point(labelX, startY + spacing * 6),
                Size = new Size(420, 30),
                ForeColor = Color.Blue,
                Text = ""
            };

            // Test Connection Button
            btnTestConnection = new Button
            {
                Text = "測試連線",
                Location = new Point(110, startY + spacing * 7),
                Size = new Size(90, 30)
            };
            btnTestConnection.Click += BtnTestConnection_Click;

            // Save Button
            btnSave = new Button
            {
                Text = "儲存",
                Location = new Point(210, startY + spacing * 7),
                Size = new Size(90, 30)
            };
            btnSave.Click += BtnSave_Click;

            // Cancel Button
            btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(310, startY + spacing * 7),
                Size = new Size(90, 30)
            };
            btnCancel.Click += BtnCancel_Click;

            grpConnectionInfo.Controls.AddRange(new Control[]
            {
                lblName, txtName,
                lblServer, txtServer,
                lblPort, txtPort,
                lblDatabase, txtDatabase,
                lblUsername, txtUsername,
                lblPassword, txtPassword,
                lblStatus,
                btnTestConnection, btnSave, btnCancel
            });

            // 加入主控制項
            this.Controls.AddRange(new Control[]
            {
                lblConnections, listConnections,
                btnNew, btnEdit, btnDelete,
                btnConnect, btnClose,
                grpConnectionInfo
            });
        }

        private void LoadConnections()
        {
            connections = DatabaseHelper.LoadMultipleConnectionSettings();
            listConnections.Items.Clear();
            foreach (var conn in connections)
            {
                listConnections.Items.Add(conn);
            }
        }

        private void ListConnections_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (!isEditing && listConnections.SelectedIndex >= 0)
            {
                selectedConnection = connections[listConnections.SelectedIndex];
                DisplayConnection(selectedConnection);
                SetEditMode(false);
            }
        }

        private void DisplayConnection(DatabaseConnection conn)
        {
            txtName.Text = conn.Name;
            txtServer.Text = conn.Server;
            txtPort.Text = conn.Port;
            txtDatabase.Text = conn.Database;
            txtUsername.Text = conn.Username;
            txtPassword.Text = conn.Password;
            lblStatus.Text = "";
        }

        private void ClearInputs()
        {
            txtName.Text = "";
            txtServer.Text = "";
            txtPort.Text = "3306";
            txtDatabase.Text = "";
            txtUsername.Text = "";
            txtPassword.Text = "";
            lblStatus.Text = "";
        }

        private void BtnNew_Click(object? sender, EventArgs e)
        {
            selectedConnection = null;
            ClearInputs();
            SetEditMode(true);
        }

        private void BtnEdit_Click(object? sender, EventArgs e)
        {
            if (listConnections.SelectedIndex < 0)
            {
                MessageBox.Show("請先選擇一個連線", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetEditMode(true);
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            if (listConnections.SelectedIndex < 0)
            {
                MessageBox.Show("請先選擇一個連線", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show("確定要刪除此連線設定嗎？", "確認刪除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                connections.RemoveAt(listConnections.SelectedIndex);
                DatabaseHelper.SaveMultipleConnectionSettings(connections);
                LoadConnections();
                ClearInputs();
                SetEditMode(false);
            }
        }

        private void BtnTestConnection_Click(object? sender, EventArgs e)
        {
            if (!ValidateInputs())
                return;

            try
            {
                lblStatus.ForeColor = Color.Blue;
                lblStatus.Text = "正在測試連線...";
                Application.DoEvents();

                bool success = DatabaseHelper.TestConnection(
                    txtServer.Text.Trim(),
                    txtPort.Text.Trim(),
                    txtDatabase.Text.Trim(),
                    txtUsername.Text.Trim(),
                    txtPassword.Text
                );

                if (success)
                {
                    lblStatus.ForeColor = Color.Green;
                    lblStatus.Text = "連線成功！";
                }
                else
                {
                    lblStatus.ForeColor = Color.Red;
                    lblStatus.Text = "連線失敗";
                }
            }
            catch (Exception ex)
            {
                lblStatus.ForeColor = Color.Red;
                lblStatus.Text = "連線失敗: " + ex.Message;
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (!ValidateInputs())
                return;

            // 建立或更新連線物件
            DatabaseConnection conn = selectedConnection ?? new DatabaseConnection();
            conn.Name = txtName.Text.Trim();
            conn.Server = txtServer.Text.Trim();
            conn.Port = txtPort.Text.Trim();
            conn.Database = txtDatabase.Text.Trim();
            conn.Username = txtUsername.Text.Trim();
            conn.Password = txtPassword.Text;

            // 檢查名稱是否重複
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i] != selectedConnection && connections[i].Name == conn.Name)
                {
                    MessageBox.Show("連線名稱已存在，請使用其他名稱", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // 新增或更新
            if (selectedConnection == null)
            {
                connections.Add(conn);
            }

            // 儲存
            DatabaseHelper.SaveMultipleConnectionSettings(connections);
            LoadConnections();
            SetEditMode(false);

            MessageBox.Show("儲存成功", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            if (listConnections.SelectedIndex >= 0)
            {
                DisplayConnection(connections[listConnections.SelectedIndex]);
            }
            else
            {
                ClearInputs();
            }
            SetEditMode(false);
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (listConnections.SelectedIndex < 0)
            {
                MessageBox.Show("請先選擇一個連線", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DatabaseConnection conn = connections[listConnections.SelectedIndex];

            try
            {
                DatabaseHelper.Connect(conn.Server, conn.Port, conn.Database, conn.Username, conn.Password);
                // 保存最後使用的連線名稱
                DatabaseHelper.SaveLastUsedConnection(conn.Name);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"連線失敗: {ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("請輸入連線名稱", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtServer.Text))
            {
                MessageBox.Show("請輸入伺服器位址", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtPort.Text))
            {
                MessageBox.Show("請輸入埠號", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtDatabase.Text))
            {
                MessageBox.Show("請輸入資料庫名稱", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                MessageBox.Show("請輸入使用者名稱", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void SetEditMode(bool editing)
        {
            isEditing = editing;
            txtName.ReadOnly = !editing;
            txtServer.ReadOnly = !editing;
            txtPort.ReadOnly = !editing;
            txtDatabase.ReadOnly = !editing;
            txtUsername.ReadOnly = !editing;
            txtPassword.ReadOnly = !editing;

            btnTestConnection.Visible = editing;
            btnSave.Visible = editing;
            btnCancel.Visible = editing;

            listConnections.Enabled = !editing;
            btnNew.Enabled = !editing;
            btnEdit.Enabled = !editing;
            btnDelete.Enabled = !editing;
            btnConnect.Enabled = !editing;
        }

        private void UpdateUI()
        {
            SetEditMode(false);
        }

        private void UpdateLocalization()
        {
            // Form title
            this.Text = LocalizationManager.L("Form_DatabaseConnection_Title");

            // Labels
            lblConnections.Text = LocalizationManager.L("Label_SavedConnections");
            grpConnectionInfo.Text = LocalizationManager.L("Group_ConnectionInfo");
            lblName.Text = LocalizationManager.L("Label_ConnectionName");
            lblServer.Text = LocalizationManager.L("Label_ServerAddress");
            lblPort.Text = LocalizationManager.L("Label_Port");
            lblDatabase.Text = LocalizationManager.L("Label_Database");
            lblUsername.Text = LocalizationManager.L("Label_Username");
            lblPassword.Text = LocalizationManager.L("Label_Password");

            // Buttons
            btnNew.Text = LocalizationManager.L("Button_New");
            btnEdit.Text = LocalizationManager.L("Button_Edit");
            btnDelete.Text = LocalizationManager.L("Button_Delete");
            btnConnect.Text = LocalizationManager.L("Button_Connect");
            btnClose.Text = LocalizationManager.L("Button_Close");
            btnTestConnection.Text = LocalizationManager.L("Button_TestConnection");
            btnSave.Text = LocalizationManager.L("Button_Save");
            btnCancel.Text = LocalizationManager.L("Button_Cancel");
        }
    }
}
