using System;
using System.Drawing;
using System.Windows.Forms;
using L1MapViewer.Helper;

namespace L1FlyMapViewer
{
    public class MonsterSearchDialog : Form
    {
        private TextBox txtSearch = null!;
        private ListBox lstMonsters = null!;
        private Button btnConfirm = null!;
        private Button btnCancel = null!;

        public int SelectedMonsterId { get; private set; }
        public string SelectedMonsterName { get; private set; } = string.Empty;

        public MonsterSearchDialog()
        {
            InitializeComponent();
            LoadMonsters();
        }

        private void InitializeComponent()
        {
            this.Text = "搜尋怪物";
            this.Size = new Size(400, 500);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            // 搜尋框
            Label lblSearch = new Label
            {
                Text = "搜尋:",
                Location = new Point(10, 15),
                Size = new Size(50, 20)
            };

            txtSearch = new TextBox
            {
                Location = new Point(60, 12),
                Size = new Size(310, 23)
            };
            txtSearch.TextChanged += TxtSearch_TextChanged;

            // 怪物列表
            lstMonsters = new ListBox
            {
                Location = new Point(10, 45),
                Size = new Size(360, 360)
            };
            lstMonsters.DoubleClick += (s, e) => ConfirmSelection();

            // 按鈕
            btnConfirm = new Button
            {
                Text = "確定",
                Location = new Point(215, 420),
                Size = new Size(75, 30),
                DialogResult = DialogResult.OK
            };
            btnConfirm.Click += (s, e) => ConfirmSelection();

            btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(295, 420),
                Size = new Size(75, 30),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[] { lblSearch, txtSearch, lstMonsters, btnConfirm, btnCancel });
        }

        private void LoadMonsters(string searchText = "")
        {
            lstMonsters.Items.Clear();

            if (!DatabaseHelper.IsConnected)
                return;

            try
            {
                string query = "SELECT npcid, name FROM npc WHERE name IS NOT NULL AND name != ''";
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    query += $" AND (name LIKE '%{searchText}%' OR npcid LIKE '%{searchText}%')";
                }
                query += " ORDER BY name LIMIT 1000";

                using (var reader = DatabaseHelper.ExecuteQuery(query))
                {
                    while (reader.Read())
                    {
                        int npcId = Convert.ToInt32(reader["npcid"]);
                        string name = reader["name"].ToString();
                        lstMonsters.Items.Add(new MonsterItem { Id = npcId, Name = name });
                    }
                }
            }
            catch
            {
                // 忽略錯誤
            }
        }

        private void TxtSearch_TextChanged(object? sender, EventArgs e)
        {
            LoadMonsters(txtSearch.Text);
        }

        private void ConfirmSelection()
        {
            if (lstMonsters.SelectedItem is MonsterItem item)
            {
                SelectedMonsterId = item.Id;
                SelectedMonsterName = item.Name;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private class MonsterItem
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;

            public override string ToString()
            {
                return $"[{Id}] {Name}";
            }
        }
    }
}
