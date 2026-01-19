using System;
using Eto.Forms;
using Eto.Drawing;
using L1MapViewer.Localization;

namespace L1MapViewer.Forms
{
    /// <summary>
    /// 批次替換 Tile 對話框 - 使用 Eto.Forms 原生佈局
    /// </summary>
    public class BatchReplaceTileDialog : Dialog
    {
        #region 公開屬性

        /// <summary>
        /// 選擇的圖層 (1, 2, 或 4)
        /// </summary>
        public int SelectedLayer
        {
            get
            {
                if (_rbLayer1.Checked) return 1;
                if (_rbLayer2.Checked) return 2;
                if (_rbLayer4.Checked) return 4;
                return 1;
            }
        }

        /// <summary>
        /// 來源 TileId
        /// </summary>
        public int SourceTileId => (int)_nudSrcTileId.Value;

        /// <summary>
        /// 來源 IndexId
        /// </summary>
        public int SourceIndexId => (int)_nudSrcIndexId.Value;

        /// <summary>
        /// 是否比對 IndexId
        /// </summary>
        public bool MatchIndexId => _chkMatchIndexId.Checked ?? false;

        /// <summary>
        /// 目標 TileId
        /// </summary>
        public int TargetTileId => (int)_nudDstTileId.Value;

        /// <summary>
        /// 目標 IndexId
        /// </summary>
        public int TargetIndexId => (int)_nudDstIndexId.Value;

        /// <summary>
        /// 是否替換 IndexId
        /// </summary>
        public bool ReplaceIndexId => _chkReplaceIndexId.Checked ?? false;

        /// <summary>
        /// 預覽結果文字
        /// </summary>
        public string PreviewResult
        {
            get => _lblResult.Text;
            set => _lblResult.Text = value;
        }

        #endregion

        #region 事件

        /// <summary>
        /// 點擊預覽按鈕時觸發
        /// </summary>
        public event EventHandler PreviewClicked;

        /// <summary>
        /// 點擊執行按鈕時觸發
        /// </summary>
        public event EventHandler ExecuteClicked;

        #endregion

        #region 私有欄位

        private GroupBox _grpLayer;
        private GroupBox _grpSource;
        private GroupBox _grpTarget;

        private RadioButton _rbLayer1;
        private RadioButton _rbLayer2;
        private RadioButton _rbLayer4;

        private NumericStepper _nudSrcTileId;
        private NumericStepper _nudSrcIndexId;
        private CheckBox _chkMatchIndexId;

        private NumericStepper _nudDstTileId;
        private NumericStepper _nudDstIndexId;
        private CheckBox _chkReplaceIndexId;

        private Button _btnPreview;
        private Button _btnExecute;
        private Button _btnCancel;
        private Label _lblResult;

        private Label _lblSrcTileId;
        private Label _lblSrcIndexId;
        private Label _lblDstTileId;
        private Label _lblDstIndexId;

        #endregion

        public BatchReplaceTileDialog()
        {
            InitializeComponents();
            UpdateLocalization();
            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Application.Instance.Invoke(() => UpdateLocalization());
        }

        protected override void OnUnLoad(EventArgs e)
        {
            base.OnUnLoad(e);
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
        }

        private void InitializeComponents()
        {
            Title = "批次替換 TileId";
            MinimumSize = new Size(420, 380);
            Resizable = false;
            Padding = new Padding(15);

            // === 圖層選擇 ===
            _rbLayer1 = new RadioButton { Text = "Layer1 (地板)", Checked = true };
            _rbLayer2 = new RadioButton(_rbLayer1) { Text = "Layer2 (索引)" };
            _rbLayer4 = new RadioButton(_rbLayer1) { Text = "Layer4 (物件)" };

            var layerLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 20,
                Items = { _rbLayer1, _rbLayer2, _rbLayer4 }
            };

            _grpLayer = new GroupBox
            {
                Text = "選擇圖層",
                Padding = new Padding(10),
                Content = layerLayout
            };

            // === 來源設定 ===
            _lblSrcTileId = new Label { Text = "TileId:", VerticalAlignment = VerticalAlignment.Center };
            _nudSrcTileId = new NumericStepper { MinValue = 0, MaxValue = 65535, Value = 0, Width = 100 };

            _lblSrcIndexId = new Label { Text = "IndexId:", VerticalAlignment = VerticalAlignment.Center };
            _nudSrcIndexId = new NumericStepper { MinValue = 0, MaxValue = 255, Value = 0, Width = 100 };

            _chkMatchIndexId = new CheckBox { Text = "比對 IndexId", Checked = true };
            _chkMatchIndexId.CheckedChanged += (s, e) =>
            {
                _nudSrcIndexId.Enabled = _chkMatchIndexId.Checked ?? false;
            };

            var srcRow1 = new TableLayout
            {
                Spacing = new Size(10, 5),
                Rows =
                {
                    new TableRow(
                        _lblSrcTileId,
                        new TableCell(_nudSrcTileId, false),
                        _lblSrcIndexId,
                        new TableCell(_nudSrcIndexId, false),
                        null  // 彈性空間
                    )
                }
            };

            var srcLayout = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Items = { srcRow1, _chkMatchIndexId }
            };

            _grpSource = new GroupBox
            {
                Text = "來源",
                Padding = new Padding(10),
                Content = srcLayout
            };

            // === 目標設定 ===
            _lblDstTileId = new Label { Text = "TileId:", VerticalAlignment = VerticalAlignment.Center };
            _nudDstTileId = new NumericStepper { MinValue = 0, MaxValue = 65535, Value = 0, Width = 100 };

            _lblDstIndexId = new Label { Text = "IndexId:", VerticalAlignment = VerticalAlignment.Center };
            _nudDstIndexId = new NumericStepper { MinValue = 0, MaxValue = 255, Value = 0, Width = 100 };

            _chkReplaceIndexId = new CheckBox { Text = "替換 IndexId", Checked = true };
            _chkReplaceIndexId.CheckedChanged += (s, e) =>
            {
                _nudDstIndexId.Enabled = _chkReplaceIndexId.Checked ?? false;
            };

            var dstRow1 = new TableLayout
            {
                Spacing = new Size(10, 5),
                Rows =
                {
                    new TableRow(
                        _lblDstTileId,
                        new TableCell(_nudDstTileId, false),
                        _lblDstIndexId,
                        new TableCell(_nudDstIndexId, false),
                        null  // 彈性空間
                    )
                }
            };

            var dstLayout = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Items = { dstRow1, _chkReplaceIndexId }
            };

            _grpTarget = new GroupBox
            {
                Text = "替換為",
                Padding = new Padding(10),
                Content = dstLayout
            };

            // === 按鈕列 ===
            _btnPreview = new Button { Text = "預覽", Width = 90 };
            _btnPreview.Click += (s, e) => PreviewClicked?.Invoke(this, EventArgs.Empty);

            _btnExecute = new Button { Text = "執行替換", Width = 90 };
            _btnExecute.Click += (s, e) => ExecuteClicked?.Invoke(this, EventArgs.Empty);

            _btnCancel = new Button { Text = "取消", Width = 90 };
            _btnCancel.Click += (s, e) => Close();

            var buttonLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 15,
                HorizontalContentAlignment = Eto.Forms.HorizontalAlignment.Center,
                Items = { _btnPreview, _btnExecute, _btnCancel }
            };

            // === 結果標籤 ===
            _lblResult = new Label
            {
                Text = "",
                TextColor = Color.FromArgb(0, 100, 180)
            };

            // === 主佈局 ===
            Content = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 12,
                HorizontalContentAlignment = Eto.Forms.HorizontalAlignment.Stretch,
                Items =
                {
                    _grpLayer,
                    _grpSource,
                    _grpTarget,
                    buttonLayout,
                    _lblResult
                }
            };

            // 設定取消按鈕
            AbortButton = _btnCancel;
        }

        private void UpdateLocalization()
        {
            Title = LocalizationManager.L("Form_BatchReplaceTile_Title");
            _grpLayer.Text = LocalizationManager.L("BatchReplaceTile_SelectLayer");
            _rbLayer1.Text = LocalizationManager.L("BatchReplaceTile_Layer1");
            _rbLayer2.Text = LocalizationManager.L("BatchReplaceTile_Layer2");
            _rbLayer4.Text = LocalizationManager.L("BatchReplaceTile_Layer4");
            _grpSource.Text = LocalizationManager.L("BatchReplaceTile_Source");
            _grpTarget.Text = LocalizationManager.L("BatchReplaceTile_Target");
            _chkMatchIndexId.Text = LocalizationManager.L("BatchReplaceTile_MatchIndexId");
            _chkReplaceIndexId.Text = LocalizationManager.L("BatchReplaceTile_ReplaceIndexId");
            _btnPreview.Text = LocalizationManager.L("Button_Preview");
            _btnExecute.Text = LocalizationManager.L("BatchReplaceTile_Execute");
            _btnCancel.Text = LocalizationManager.L("Button_Cancel");
        }

        /// <summary>
        /// 取得圖層名稱
        /// </summary>
        public string GetLayerName()
        {
            return SelectedLayer switch
            {
                1 => "Layer1",
                2 => "Layer2",
                4 => "Layer4",
                _ => "Layer1"
            };
        }
    }
}
