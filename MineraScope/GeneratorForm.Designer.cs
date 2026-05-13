namespace MineraScope
{
    partial class GeneratorForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            buttonRunSpectrumGeneration = new Button();
            groupBoxMineralInfo = new GroupBox();
            panelMineralName = new Panel();
            labelMineralName = new Label();
            textBoxMineralName = new TextBox();
            panelMemo = new Panel();
            labelMemo = new Label();
            textBoxMemo = new TextBox();
            groupBoxSampling = new GroupBox();
            textBoxConstraints = new TextBox();
            labelConstraints = new Label();
            labelCompositionList = new Label();
            textBoxCompositionList = new TextBox();
            groupBoxEndmembers = new GroupBox();
            panelEndmembers = new Panel();
            flowLayoutPanelEndmembers = new FlowLayoutPanel();
            EndmemberControl1 = new EndmemberControl();
            EndmemberControl2 = new EndmemberControl();
            buttonEndmemberDelete = new Button();
            buttonEndmemberAdd = new Button();
            labelEndmemberFormula = new Label();
            labelEndmemberName = new Label();
            buttonAddMineral = new Button();
            buttonUpdateMineral = new Button();
            checkedListBoxMinerals = new CheckedListBox();
            groupBoxAdvancedSettings = new GroupBox();
            groupBoxModelSettings = new GroupBox();
            flowLayoutPanelModelSettings = new FlowLayoutPanel();
            numericBoxEpochs = new Crystallography.Controls.NumericBox();
            numericBoxBatchSize = new Crystallography.Controls.NumericBox();
            numericBoxEarlyStopping = new Crystallography.Controls.NumericBox();
            numericBoxValidationSplit = new Crystallography.Controls.NumericBox();
            groupBoxSpectrumSettings = new GroupBox();
            flowLayoutPanelSpectrumSettings = new FlowLayoutPanel();
            numericBoxSpectraPerMineral = new Crystallography.Controls.NumericBox();
            numericBoxResolution = new Crystallography.Controls.NumericBox();
            numericBoxParallel = new Crystallography.Controls.NumericBox();
            buttonCalibration = new Button();
            panelModelLog = new Panel();
            labelModelLog = new Label();
            textBoxModelLog = new TextBox();
            buttonAllSelect = new Button();
            buttonModelTrain = new Button();
            numericBoxProbeCurrent = new Crystallography.Controls.NumericBox();
            numericBoxLiveTime = new Crystallography.Controls.NumericBox();
            buttonDelete = new Button();
            groupBoxModelCreation = new GroupBox();
            tableLayoutPanelMain = new TableLayoutPanel();
            groupBoxEDXSettings = new GroupBox();
            flowLayoutPanelEDXSettings = new FlowLayoutPanel();
            panelDetectorName = new Panel();
            labelDetectorName = new Label();
            textBoxDetectorName = new TextBox();
            numericBoxBeamEnergy = new Crystallography.Controls.NumericBox();
            numericBoxCarbonThickness = new Crystallography.Controls.NumericBox();
            panelCommandBar = new Panel();
            buttonCancelSpectrumGeneration = new Button();
            panelModelName = new Panel();
            labelModelName = new Label();
            textBoxModelName = new TextBox();
            checkBoxAdvanced = new CheckBox();
            panelBottomDrawer = new Panel();
            groupBoxMineral = new GroupBox();
            flowLayoutPanelMineralActions = new FlowLayoutPanel();
            buttonAllDelete = new Button();
            buttonReset = new Button();
            statusStrip1 = new StatusStrip();
            toolStripProgressBar1 = new ToolStripProgressBar();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            groupBoxMineralInfo.SuspendLayout();
            panelMineralName.SuspendLayout();
            panelMemo.SuspendLayout();
            groupBoxSampling.SuspendLayout();
            groupBoxEndmembers.SuspendLayout();
            panelEndmembers.SuspendLayout();
            flowLayoutPanelEndmembers.SuspendLayout();
            groupBoxAdvancedSettings.SuspendLayout();
            groupBoxModelSettings.SuspendLayout();
            flowLayoutPanelModelSettings.SuspendLayout();
            groupBoxSpectrumSettings.SuspendLayout();
            flowLayoutPanelSpectrumSettings.SuspendLayout();
            panelModelLog.SuspendLayout();
            groupBoxModelCreation.SuspendLayout();
            tableLayoutPanelMain.SuspendLayout();
            groupBoxEDXSettings.SuspendLayout();
            flowLayoutPanelEDXSettings.SuspendLayout();
            panelDetectorName.SuspendLayout();
            panelCommandBar.SuspendLayout();
            panelModelName.SuspendLayout();
            panelBottomDrawer.SuspendLayout();
            groupBoxMineral.SuspendLayout();
            flowLayoutPanelMineralActions.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // buttonRunSpectrumGeneration
            // 
            buttonRunSpectrumGeneration.AutoSize = true;
            buttonRunSpectrumGeneration.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonRunSpectrumGeneration.Location = new Point(781, 2);
            buttonRunSpectrumGeneration.Margin = new Padding(3, 2, 3, 2);
            buttonRunSpectrumGeneration.Name = "buttonRunSpectrumGeneration";
            buttonRunSpectrumGeneration.Size = new Size(41, 25);
            buttonRunSpectrumGeneration.TabIndex = 0;
            buttonRunSpectrumGeneration.Text = "実行";
            buttonRunSpectrumGeneration.UseVisualStyleBackColor = true;
            buttonRunSpectrumGeneration.Click += buttonRunSpectrumGeneration_Click;
            // 
            // groupBoxMineralInfo
            // 
            groupBoxMineralInfo.Controls.Add(panelMineralName);
            groupBoxMineralInfo.Controls.Add(panelMemo);
            groupBoxMineralInfo.Controls.Add(groupBoxSampling);
            groupBoxMineralInfo.Controls.Add(groupBoxEndmembers);
            groupBoxMineralInfo.Location = new Point(354, 50);
            groupBoxMineralInfo.Name = "groupBoxMineralInfo";
            groupBoxMineralInfo.Size = new Size(589, 223);
            groupBoxMineralInfo.TabIndex = 37;
            groupBoxMineralInfo.TabStop = false;
            groupBoxMineralInfo.Text = "詳細情報";
            // 
            // panelMineralName
            // 
            panelMineralName.AutoSize = true;
            panelMineralName.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelMineralName.Controls.Add(labelMineralName);
            panelMineralName.Controls.Add(textBoxMineralName);
            panelMineralName.Location = new Point(15, 18);
            panelMineralName.Name = "panelMineralName";
            panelMineralName.Size = new Size(240, 27);
            panelMineralName.TabIndex = 105;
            // 
            // labelMineralName
            // 
            labelMineralName.AutoSize = true;
            labelMineralName.Location = new Point(0, 8);
            labelMineralName.Name = "labelMineralName";
            labelMineralName.Size = new Size(107, 15);
            labelMineralName.TabIndex = 92;
            labelMineralName.Text = "鉱物/鉱物グループ名";
            // 
            // textBoxMineralName
            // 
            textBoxMineralName.Location = new Point(113, 1);
            textBoxMineralName.Name = "textBoxMineralName";
            textBoxMineralName.Size = new Size(124, 23);
            textBoxMineralName.TabIndex = 91;
            // 
            // panelMemo
            // 
            panelMemo.AutoSize = true;
            panelMemo.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelMemo.Controls.Add(labelMemo);
            panelMemo.Controls.Add(textBoxMemo);
            panelMemo.Location = new Point(318, 18);
            panelMemo.Name = "panelMemo";
            panelMemo.Size = new Size(169, 27);
            panelMemo.TabIndex = 104;
            // 
            // labelMemo
            // 
            labelMemo.AutoSize = true;
            labelMemo.Location = new Point(0, 5);
            labelMemo.Name = "labelMemo";
            labelMemo.Size = new Size(40, 15);
            labelMemo.TabIndex = 93;
            labelMemo.Text = "memo";
            // 
            // textBoxMemo
            // 
            textBoxMemo.Location = new Point(46, 1);
            textBoxMemo.Name = "textBoxMemo";
            textBoxMemo.Size = new Size(120, 23);
            textBoxMemo.TabIndex = 94;
            // 
            // groupBoxSampling
            // 
            groupBoxSampling.Controls.Add(textBoxConstraints);
            groupBoxSampling.Controls.Add(labelConstraints);
            groupBoxSampling.Controls.Add(labelCompositionList);
            groupBoxSampling.Controls.Add(textBoxCompositionList);
            groupBoxSampling.Location = new Point(318, 50);
            groupBoxSampling.Name = "groupBoxSampling";
            groupBoxSampling.Size = new Size(266, 167);
            groupBoxSampling.TabIndex = 103;
            groupBoxSampling.TabStop = false;
            groupBoxSampling.Text = "サンプリング条件";
            // 
            // textBoxConstraints
            // 
            textBoxConstraints.Location = new Point(18, 37);
            textBoxConstraints.Multiline = true;
            textBoxConstraints.Name = "textBoxConstraints";
            textBoxConstraints.ScrollBars = ScrollBars.Both;
            textBoxConstraints.Size = new Size(230, 44);
            textBoxConstraints.TabIndex = 93;
            // 
            // labelConstraints
            // 
            labelConstraints.AutoSize = true;
            labelConstraints.Location = new Point(107, 19);
            labelConstraints.Name = "labelConstraints";
            labelConstraints.Size = new Size(43, 15);
            labelConstraints.TabIndex = 94;
            labelConstraints.Text = "条件式";
            // 
            // labelCompositionList
            // 
            labelCompositionList.AutoSize = true;
            labelCompositionList.Location = new Point(85, 84);
            labelCompositionList.Name = "labelCompositionList";
            labelCompositionList.Size = new Size(80, 15);
            labelCompositionList.TabIndex = 96;
            labelCompositionList.Text = "化学組成リスト";
            // 
            // textBoxCompositionList
            // 
            textBoxCompositionList.Location = new Point(18, 102);
            textBoxCompositionList.Multiline = true;
            textBoxCompositionList.Name = "textBoxCompositionList";
            textBoxCompositionList.ScrollBars = ScrollBars.Both;
            textBoxCompositionList.Size = new Size(230, 59);
            textBoxCompositionList.TabIndex = 95;
            // 
            // groupBoxEndmembers
            // 
            groupBoxEndmembers.Controls.Add(panelEndmembers);
            groupBoxEndmembers.Location = new Point(5, 50);
            groupBoxEndmembers.Margin = new Padding(1);
            groupBoxEndmembers.Name = "groupBoxEndmembers";
            groupBoxEndmembers.Padding = new Padding(1);
            groupBoxEndmembers.Size = new Size(301, 167);
            groupBoxEndmembers.TabIndex = 90;
            groupBoxEndmembers.TabStop = false;
            groupBoxEndmembers.Text = "端成分";
            // 
            // panelEndmembers
            // 
            panelEndmembers.AutoSize = true;
            panelEndmembers.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelEndmembers.Controls.Add(flowLayoutPanelEndmembers);
            panelEndmembers.Controls.Add(buttonEndmemberDelete);
            panelEndmembers.Controls.Add(buttonEndmemberAdd);
            panelEndmembers.Controls.Add(labelEndmemberFormula);
            panelEndmembers.Controls.Add(labelEndmemberName);
            panelEndmembers.Location = new Point(4, 20);
            panelEndmembers.Name = "panelEndmembers";
            panelEndmembers.Size = new Size(291, 127);
            panelEndmembers.TabIndex = 106;
            // 
            // flowLayoutPanelEndmembers
            // 
            flowLayoutPanelEndmembers.AutoScroll = true;
            flowLayoutPanelEndmembers.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanelEndmembers.Controls.Add(EndmemberControl1);
            flowLayoutPanelEndmembers.Controls.Add(EndmemberControl2);
            flowLayoutPanelEndmembers.FlowDirection = FlowDirection.TopDown;
            flowLayoutPanelEndmembers.Location = new Point(3, 17);
            flowLayoutPanelEndmembers.Name = "flowLayoutPanelEndmembers";
            flowLayoutPanelEndmembers.Size = new Size(282, 87);
            flowLayoutPanelEndmembers.TabIndex = 106;
            flowLayoutPanelEndmembers.WrapContents = false;
            // 
            // EndmemberControl1
            // 
            EndmemberControl1.EndmemberFormula = "  ";
            EndmemberControl1.EndmemberName = "  ";
            EndmemberControl1.Location = new Point(3, 3);
            EndmemberControl1.Name = "EndmemberControl1";
            EndmemberControl1.Size = new Size(260, 23);
            EndmemberControl1.TabIndex = 87;
            // 
            // EndmemberControl2
            // 
            EndmemberControl2.EndmemberFormula = "  ";
            EndmemberControl2.EndmemberName = "  ";
            EndmemberControl2.Location = new Point(3, 32);
            EndmemberControl2.Name = "EndmemberControl2";
            EndmemberControl2.Size = new Size(260, 23);
            EndmemberControl2.TabIndex = 88;
            // 
            // buttonEndmemberDelete
            // 
            buttonEndmemberDelete.AutoSize = true;
            buttonEndmemberDelete.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonEndmemberDelete.Font = new Font("Yu Gothic UI", 8F);
            buttonEndmemberDelete.Location = new Point(249, 101);
            buttonEndmemberDelete.Name = "buttonEndmemberDelete";
            buttonEndmemberDelete.Size = new Size(39, 23);
            buttonEndmemberDelete.TabIndex = 98;
            buttonEndmemberDelete.Text = "削除";
            buttonEndmemberDelete.UseVisualStyleBackColor = true;
            buttonEndmemberDelete.Click += buttonEndmemberDelete_Click;
            // 
            // buttonEndmemberAdd
            // 
            buttonEndmemberAdd.AutoSize = true;
            buttonEndmemberAdd.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonEndmemberAdd.Font = new Font("Yu Gothic UI", 8F);
            buttonEndmemberAdd.Location = new Point(204, 101);
            buttonEndmemberAdd.Name = "buttonEndmemberAdd";
            buttonEndmemberAdd.Size = new Size(39, 23);
            buttonEndmemberAdd.TabIndex = 100;
            buttonEndmemberAdd.Text = "追加";
            buttonEndmemberAdd.UseVisualStyleBackColor = true;
            buttonEndmemberAdd.Click += buttonEndmemberAdd_Click;
            // 
            // labelEndmemberFormula
            // 
            labelEndmemberFormula.AutoSize = true;
            labelEndmemberFormula.Location = new Point(176, 0);
            labelEndmemberFormula.Margin = new Padding(60, 0, 0, 0);
            labelEndmemberFormula.Name = "labelEndmemberFormula";
            labelEndmemberFormula.Size = new Size(55, 15);
            labelEndmemberFormula.TabIndex = 76;
            labelEndmemberFormula.Text = "化学組成";
            // 
            // labelEndmemberName
            // 
            labelEndmemberName.AutoSize = true;
            labelEndmemberName.Location = new Point(41, 0);
            labelEndmemberName.Margin = new Padding(60, 0, 0, 0);
            labelEndmemberName.Name = "labelEndmemberName";
            labelEndmemberName.Size = new Size(43, 15);
            labelEndmemberName.TabIndex = 75;
            labelEndmemberName.Text = "鉱物名";
            // 
            // buttonAddMineral
            // 
            buttonAddMineral.AutoSize = true;
            buttonAddMineral.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonAddMineral.Location = new Point(3, 3);
            buttonAddMineral.Name = "buttonAddMineral";
            buttonAddMineral.Size = new Size(41, 25);
            buttonAddMineral.TabIndex = 96;
            buttonAddMineral.Text = "追加";
            buttonAddMineral.UseVisualStyleBackColor = true;
            buttonAddMineral.Click += buttonAddMineral_Click;
            // 
            // buttonUpdateMineral
            // 
            buttonUpdateMineral.AutoSize = true;
            buttonUpdateMineral.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonUpdateMineral.Location = new Point(151, 3);
            buttonUpdateMineral.Name = "buttonUpdateMineral";
            buttonUpdateMineral.Size = new Size(41, 25);
            buttonUpdateMineral.TabIndex = 99;
            buttonUpdateMineral.Text = "更新";
            buttonUpdateMineral.UseVisualStyleBackColor = true;
            buttonUpdateMineral.Click += buttonUpdateMineral_Click;
            // 
            // checkedListBoxMinerals
            // 
            checkedListBoxMinerals.FormattingEnabled = true;
            checkedListBoxMinerals.HorizontalScrollbar = true;
            checkedListBoxMinerals.Location = new Point(3, 48);
            checkedListBoxMinerals.MultiColumn = true;
            checkedListBoxMinerals.Name = "checkedListBoxMinerals";
            checkedListBoxMinerals.ScrollAlwaysVisible = true;
            checkedListBoxMinerals.Size = new Size(344, 220);
            checkedListBoxMinerals.TabIndex = 86;
            checkedListBoxMinerals.ItemCheck += checkedListBoxMinerals_ItemCheck;
            checkedListBoxMinerals.SelectedIndexChanged += checkedListBoxMinerals_SelectedIndexChanged;
            // 
            // groupBoxAdvancedSettings
            // 
            groupBoxAdvancedSettings.Controls.Add(groupBoxModelSettings);
            groupBoxAdvancedSettings.Controls.Add(groupBoxSpectrumSettings);
            groupBoxAdvancedSettings.Location = new Point(0, 3);
            groupBoxAdvancedSettings.Name = "groupBoxAdvancedSettings";
            groupBoxAdvancedSettings.Size = new Size(946, 113);
            groupBoxAdvancedSettings.TabIndex = 43;
            groupBoxAdvancedSettings.TabStop = false;
            groupBoxAdvancedSettings.Text = "詳細設定";
            // 
            // groupBoxModelSettings
            // 
            groupBoxModelSettings.Controls.Add(flowLayoutPanelModelSettings);
            groupBoxModelSettings.Location = new Point(465, 22);
            groupBoxModelSettings.Name = "groupBoxModelSettings";
            groupBoxModelSettings.Size = new Size(484, 76);
            groupBoxModelSettings.TabIndex = 109;
            groupBoxModelSettings.TabStop = false;
            groupBoxModelSettings.Text = "モデル訓練詳細";
            // 
            // flowLayoutPanelModelSettings
            // 
            flowLayoutPanelModelSettings.AutoSize = true;
            flowLayoutPanelModelSettings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanelModelSettings.Controls.Add(numericBoxEpochs);
            flowLayoutPanelModelSettings.Controls.Add(numericBoxBatchSize);
            flowLayoutPanelModelSettings.Controls.Add(numericBoxEarlyStopping);
            flowLayoutPanelModelSettings.Controls.Add(numericBoxValidationSplit);
            flowLayoutPanelModelSettings.Location = new Point(0, 26);
            flowLayoutPanelModelSettings.Name = "flowLayoutPanelModelSettings";
            flowLayoutPanelModelSettings.Size = new Size(469, 26);
            flowLayoutPanelModelSettings.TabIndex = 108;
            // 
            // numericBoxEpochs
            // 
            numericBoxEpochs.BackColor = Color.Transparent;
            numericBoxEpochs.DecimalPlaces = 0;
            numericBoxEpochs.Font = new Font("Yu Gothic UI", 9F);
            numericBoxEpochs.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxEpochs.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxEpochs.HeaderText = "エポック数";
            numericBoxEpochs.Location = new Point(0, 0);
            numericBoxEpochs.Margin = new Padding(0);
            numericBoxEpochs.Maximum = 100000D;
            numericBoxEpochs.MaximumSize = new Size(1000, 28);
            numericBoxEpochs.Minimum = 1D;
            numericBoxEpochs.MinimumSize = new Size(1, 18);
            numericBoxEpochs.Name = "numericBoxEpochs";
            numericBoxEpochs.RadianValue = 8.7266462599716466D;
            numericBoxEpochs.ShowUpDown = true;
            numericBoxEpochs.Size = new Size(102, 26);
            numericBoxEpochs.SmartIncrement = true;
            numericBoxEpochs.TabIndex = 104;
            numericBoxEpochs.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxEpochs.Value = 500D;
            // 
            // numericBoxBatchSize
            // 
            numericBoxBatchSize.BackColor = Color.Transparent;
            numericBoxBatchSize.DecimalPlaces = 0;
            numericBoxBatchSize.Font = new Font("Yu Gothic UI", 9F);
            numericBoxBatchSize.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxBatchSize.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxBatchSize.HeaderText = "バッチサイズ";
            numericBoxBatchSize.Location = new Point(102, 0);
            numericBoxBatchSize.Margin = new Padding(0);
            numericBoxBatchSize.MaximumSize = new Size(1000, 28);
            numericBoxBatchSize.Minimum = 1D;
            numericBoxBatchSize.MinimumSize = new Size(1, 18);
            numericBoxBatchSize.Name = "numericBoxBatchSize";
            numericBoxBatchSize.RadianValue = 0.27925268031909273D;
            numericBoxBatchSize.ShowUpDown = true;
            numericBoxBatchSize.Size = new Size(104, 26);
            numericBoxBatchSize.SmartIncrement = true;
            numericBoxBatchSize.TabIndex = 105;
            numericBoxBatchSize.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxBatchSize.Value = 16D;
            // 
            // numericBoxEarlyStopping
            // 
            numericBoxEarlyStopping.BackColor = Color.Transparent;
            numericBoxEarlyStopping.DecimalPlaces = 0;
            numericBoxEarlyStopping.Font = new Font("Yu Gothic UI", 9F);
            numericBoxEarlyStopping.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxEarlyStopping.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxEarlyStopping.HeaderText = "待機回数";
            numericBoxEarlyStopping.Location = new Point(206, 0);
            numericBoxEarlyStopping.Margin = new Padding(0);
            numericBoxEarlyStopping.MaximumSize = new Size(1000, 28);
            numericBoxEarlyStopping.Minimum = 0D;
            numericBoxEarlyStopping.MinimumSize = new Size(1, 18);
            numericBoxEarlyStopping.Name = "numericBoxEarlyStopping";
            numericBoxEarlyStopping.RadianValue = 0.17453292519943295D;
            numericBoxEarlyStopping.ShowUpDown = true;
            numericBoxEarlyStopping.Size = new Size(102, 26);
            numericBoxEarlyStopping.SmartIncrement = true;
            numericBoxEarlyStopping.TabIndex = 106;
            numericBoxEarlyStopping.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxEarlyStopping.Value = 10D;
            // 
            // numericBoxValidationSplit
            // 
            numericBoxValidationSplit.BackColor = Color.Transparent;
            numericBoxValidationSplit.DecimalPlaces = 0;
            numericBoxValidationSplit.Font = new Font("Yu Gothic UI", 9F);
            numericBoxValidationSplit.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxValidationSplit.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxValidationSplit.HeaderText = "テストデータの割合(%)";
            numericBoxValidationSplit.Location = new Point(308, 0);
            numericBoxValidationSplit.Margin = new Padding(0);
            numericBoxValidationSplit.Maximum = 100000D;
            numericBoxValidationSplit.MaximumSize = new Size(1000, 28);
            numericBoxValidationSplit.Minimum = 1D;
            numericBoxValidationSplit.MinimumSize = new Size(1, 18);
            numericBoxValidationSplit.Name = "numericBoxValidationSplit";
            numericBoxValidationSplit.RadianValue = 0.3490658503988659D;
            numericBoxValidationSplit.ShowUpDown = true;
            numericBoxValidationSplit.Size = new Size(161, 26);
            numericBoxValidationSplit.SmartIncrement = true;
            numericBoxValidationSplit.TabIndex = 93;
            numericBoxValidationSplit.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxValidationSplit.Value = 20D;
            // 
            // groupBoxSpectrumSettings
            // 
            groupBoxSpectrumSettings.Controls.Add(flowLayoutPanelSpectrumSettings);
            groupBoxSpectrumSettings.Location = new Point(3, 22);
            groupBoxSpectrumSettings.Name = "groupBoxSpectrumSettings";
            groupBoxSpectrumSettings.Size = new Size(459, 76);
            groupBoxSpectrumSettings.TabIndex = 108;
            groupBoxSpectrumSettings.TabStop = false;
            groupBoxSpectrumSettings.Text = "EDXスペクトル生成詳細";
            // 
            // flowLayoutPanelSpectrumSettings
            // 
            flowLayoutPanelSpectrumSettings.AutoSize = true;
            flowLayoutPanelSpectrumSettings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanelSpectrumSettings.Controls.Add(numericBoxSpectraPerMineral);
            flowLayoutPanelSpectrumSettings.Controls.Add(numericBoxResolution);
            flowLayoutPanelSpectrumSettings.Controls.Add(numericBoxParallel);
            flowLayoutPanelSpectrumSettings.Location = new Point(6, 26);
            flowLayoutPanelSpectrumSettings.Name = "flowLayoutPanelSpectrumSettings";
            flowLayoutPanelSpectrumSettings.Size = new Size(386, 28);
            flowLayoutPanelSpectrumSettings.TabIndex = 115;
            // 
            // numericBoxSpectraPerMineral
            // 
            numericBoxSpectraPerMineral.BackColor = Color.Transparent;
            numericBoxSpectraPerMineral.DecimalPlaces = 0;
            numericBoxSpectraPerMineral.Font = new Font("Yu Gothic UI", 9F);
            numericBoxSpectraPerMineral.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxSpectraPerMineral.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxSpectraPerMineral.HeaderText = "学習スペクトル数/鉱物";
            numericBoxSpectraPerMineral.Location = new Point(0, 0);
            numericBoxSpectraPerMineral.Margin = new Padding(0);
            numericBoxSpectraPerMineral.MaximumSize = new Size(1000, 28);
            numericBoxSpectraPerMineral.Minimum = 0D;
            numericBoxSpectraPerMineral.MinimumSize = new Size(1, 18);
            numericBoxSpectraPerMineral.Name = "numericBoxSpectraPerMineral";
            numericBoxSpectraPerMineral.RadianValue = 17.453292519943293D;
            numericBoxSpectraPerMineral.ShowUpDown = true;
            numericBoxSpectraPerMineral.Size = new Size(170, 26);
            numericBoxSpectraPerMineral.SmartIncrement = true;
            numericBoxSpectraPerMineral.TabIndex = 106;
            numericBoxSpectraPerMineral.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxSpectraPerMineral.Value = 1000D;
            // 
            // numericBoxResolution
            // 
            numericBoxResolution.BackColor = Color.Transparent;
            numericBoxResolution.DecimalPlaces = 0;
            numericBoxResolution.Font = new Font("Yu Gothic UI", 9F);
            numericBoxResolution.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxResolution.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxResolution.HeaderText = "化学組成分解能";
            numericBoxResolution.Location = new Point(170, 0);
            numericBoxResolution.Margin = new Padding(0);
            numericBoxResolution.MaximumSize = new Size(1000, 28);
            numericBoxResolution.Minimum = 0D;
            numericBoxResolution.MinimumSize = new Size(1, 18);
            numericBoxResolution.Name = "numericBoxResolution";
            numericBoxResolution.RadianValue = 0.17453292519943295D;
            numericBoxResolution.ShowUpDown = true;
            numericBoxResolution.Size = new Size(134, 26);
            numericBoxResolution.SmartIncrement = true;
            numericBoxResolution.TabIndex = 106;
            numericBoxResolution.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxResolution.Value = 10D;
            // 
            // numericBoxParallel
            // 
            numericBoxParallel.BackColor = Color.Transparent;
            numericBoxParallel.DecimalPlaces = 0;
            numericBoxParallel.Font = new Font("Yu Gothic UI", 9F);
            numericBoxParallel.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxParallel.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxParallel.HeaderText = "並列数";
            numericBoxParallel.Location = new Point(304, 0);
            numericBoxParallel.Margin = new Padding(0);
            numericBoxParallel.Maximum = 100000D;
            numericBoxParallel.MaximumSize = new Size(1000, 28);
            numericBoxParallel.Minimum = 1D;
            numericBoxParallel.MinimumSize = new Size(1, 18);
            numericBoxParallel.Name = "numericBoxParallel";
            numericBoxParallel.RadianValue = 0.017453292519943295D;
            numericBoxParallel.ShowUpDown = true;
            numericBoxParallel.Size = new Size(82, 28);
            numericBoxParallel.SmartIncrement = true;
            numericBoxParallel.TabIndex = 114;
            numericBoxParallel.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxParallel.Value = 1D;
            // 
            // buttonCalibration
            // 
            buttonCalibration.AutoSize = true;
            buttonCalibration.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonCalibration.Location = new Point(742, 3);
            buttonCalibration.Name = "buttonCalibration";
            buttonCalibration.Size = new Size(115, 25);
            buttonCalibration.TabIndex = 104;
            buttonCalibration.Text = "EDXキャリブレーション";
            buttonCalibration.UseVisualStyleBackColor = true;
            buttonCalibration.Click += buttonCalibration_Click;
            // 
            // panelModelLog
            // 
            panelModelLog.Controls.Add(labelModelLog);
            panelModelLog.Controls.Add(textBoxModelLog);
            panelModelLog.Location = new Point(228, 556);
            panelModelLog.Name = "panelModelLog";
            panelModelLog.Size = new Size(444, 142);
            panelModelLog.TabIndex = 107;
            // 
            // labelModelLog
            // 
            labelModelLog.AutoSize = true;
            labelModelLog.Location = new Point(189, 18);
            labelModelLog.Margin = new Padding(2, 0, 2, 0);
            labelModelLog.Name = "labelModelLog";
            labelModelLog.Size = new Size(49, 15);
            labelModelLog.TabIndex = 21;
            labelModelLog.Text = "訓練ログ";
            // 
            // textBoxModelLog
            // 
            textBoxModelLog.Location = new Point(3, 36);
            textBoxModelLog.Multiline = true;
            textBoxModelLog.Name = "textBoxModelLog";
            textBoxModelLog.ScrollBars = ScrollBars.Both;
            textBoxModelLog.Size = new Size(438, 95);
            textBoxModelLog.TabIndex = 0;
            // 
            // buttonAllSelect
            // 
            buttonAllSelect.Font = new Font("Yu Gothic UI", 8F);
            buttonAllSelect.Location = new Point(254, 3);
            buttonAllSelect.Name = "buttonAllSelect";
            buttonAllSelect.Size = new Size(85, 23);
            buttonAllSelect.TabIndex = 103;
            buttonAllSelect.Text = "全選択/解除";
            buttonAllSelect.UseVisualStyleBackColor = true;
            buttonAllSelect.Click += buttonToggleAllMinerals_Click;
            // 
            // buttonModelTrain
            // 
            buttonModelTrain.AutoSize = true;
            buttonModelTrain.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonModelTrain.Location = new Point(828, 2);
            buttonModelTrain.Name = "buttonModelTrain";
            buttonModelTrain.Size = new Size(69, 25);
            buttonModelTrain.TabIndex = 0;
            buttonModelTrain.Text = "モデル作成";
            buttonModelTrain.UseVisualStyleBackColor = true;
            buttonModelTrain.Click += buttonModelTrain_Click;
            // 
            // numericBoxProbeCurrent
            // 
            numericBoxProbeCurrent.BackColor = Color.Transparent;
            numericBoxProbeCurrent.DecimalPlaces = 2;
            numericBoxProbeCurrent.Font = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.HeaderText = "照射電流(nA)";
            numericBoxProbeCurrent.Location = new Point(128, 0);
            numericBoxProbeCurrent.Margin = new Padding(0);
            numericBoxProbeCurrent.MaximumSize = new Size(1000, 28);
            numericBoxProbeCurrent.MinimumSize = new Size(1, 18);
            numericBoxProbeCurrent.Name = "numericBoxProbeCurrent";
            numericBoxProbeCurrent.RadianValue = 0.0087266462599716477D;
            numericBoxProbeCurrent.ShowUpDown = true;
            numericBoxProbeCurrent.Size = new Size(138, 26);
            numericBoxProbeCurrent.SmartIncrement = true;
            numericBoxProbeCurrent.TabIndex = 104;
            numericBoxProbeCurrent.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.Value = 0.5D;
            // 
            // numericBoxLiveTime
            // 
            numericBoxLiveTime.BackColor = Color.Transparent;
            numericBoxLiveTime.DecimalPlaces = 1;
            numericBoxLiveTime.Font = new Font("Yu Gothic UI", 9F);
            numericBoxLiveTime.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxLiveTime.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxLiveTime.HeaderText = "測定時間(秒)";
            numericBoxLiveTime.Location = new Point(404, 0);
            numericBoxLiveTime.Margin = new Padding(0);
            numericBoxLiveTime.MaximumSize = new Size(1000, 28);
            numericBoxLiveTime.Minimum = 0D;
            numericBoxLiveTime.MinimumSize = new Size(1, 18);
            numericBoxLiveTime.Name = "numericBoxLiveTime";
            numericBoxLiveTime.RadianValue = 2.0943951023931953D;
            numericBoxLiveTime.ShowUpDown = true;
            numericBoxLiveTime.Size = new Size(136, 26);
            numericBoxLiveTime.SmartIncrement = true;
            numericBoxLiveTime.TabIndex = 105;
            numericBoxLiveTime.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxLiveTime.Value = 120D;
            // 
            // buttonDelete
            // 
            buttonDelete.AutoSize = true;
            buttonDelete.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonDelete.Font = new Font("Yu Gothic UI", 8F);
            buttonDelete.Location = new Point(50, 3);
            buttonDelete.Name = "buttonDelete";
            buttonDelete.Size = new Size(39, 23);
            buttonDelete.TabIndex = 97;
            buttonDelete.Text = "削除";
            buttonDelete.UseVisualStyleBackColor = true;
            buttonDelete.Click += buttonDeleteMineral_Click;
            // 
            // groupBoxModelCreation
            // 
            groupBoxModelCreation.Controls.Add(panelModelLog);
            groupBoxModelCreation.Controls.Add(tableLayoutPanelMain);
            groupBoxModelCreation.Dock = DockStyle.Fill;
            groupBoxModelCreation.Location = new Point(3, 3);
            groupBoxModelCreation.Name = "groupBoxModelCreation";
            groupBoxModelCreation.Size = new Size(990, 721);
            groupBoxModelCreation.TabIndex = 88;
            groupBoxModelCreation.TabStop = false;
            groupBoxModelCreation.Text = "モデル作成";
            // 
            // tableLayoutPanelMain
            // 
            tableLayoutPanelMain.ColumnCount = 1;
            tableLayoutPanelMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanelMain.Controls.Add(groupBoxEDXSettings, 0, 0);
            tableLayoutPanelMain.Controls.Add(panelCommandBar, 0, 2);
            tableLayoutPanelMain.Controls.Add(panelBottomDrawer, 0, 3);
            tableLayoutPanelMain.Controls.Add(groupBoxMineral, 0, 1);
            tableLayoutPanelMain.Location = new Point(15, 22);
            tableLayoutPanelMain.Name = "tableLayoutPanelMain";
            tableLayoutPanelMain.RowCount = 4;
            tableLayoutPanelMain.RowStyles.Add(new RowStyle());
            tableLayoutPanelMain.RowStyles.Add(new RowStyle());
            tableLayoutPanelMain.RowStyles.Add(new RowStyle());
            tableLayoutPanelMain.RowStyles.Add(new RowStyle());
            tableLayoutPanelMain.Size = new Size(955, 528);
            tableLayoutPanelMain.TabIndex = 104;
            // 
            // groupBoxEDXSettings
            // 
            groupBoxEDXSettings.Controls.Add(flowLayoutPanelEDXSettings);
            groupBoxEDXSettings.Location = new Point(3, 3);
            groupBoxEDXSettings.Name = "groupBoxEDXSettings";
            groupBoxEDXSettings.Size = new Size(949, 52);
            groupBoxEDXSettings.TabIndex = 99;
            groupBoxEDXSettings.TabStop = false;
            groupBoxEDXSettings.Text = "SEM-EDX条件";
            // 
            // flowLayoutPanelEDXSettings
            // 
            flowLayoutPanelEDXSettings.Controls.Add(panelDetectorName);
            flowLayoutPanelEDXSettings.Controls.Add(numericBoxProbeCurrent);
            flowLayoutPanelEDXSettings.Controls.Add(numericBoxBeamEnergy);
            flowLayoutPanelEDXSettings.Controls.Add(numericBoxLiveTime);
            flowLayoutPanelEDXSettings.Controls.Add(numericBoxCarbonThickness);
            flowLayoutPanelEDXSettings.Controls.Add(buttonCalibration);
            flowLayoutPanelEDXSettings.Dock = DockStyle.Fill;
            flowLayoutPanelEDXSettings.Location = new Point(3, 19);
            flowLayoutPanelEDXSettings.Name = "flowLayoutPanelEDXSettings";
            flowLayoutPanelEDXSettings.Size = new Size(943, 30);
            flowLayoutPanelEDXSettings.TabIndex = 108;
            // 
            // panelDetectorName
            // 
            panelDetectorName.AutoSize = true;
            panelDetectorName.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelDetectorName.Controls.Add(labelDetectorName);
            panelDetectorName.Controls.Add(textBoxDetectorName);
            panelDetectorName.Location = new Point(3, 3);
            panelDetectorName.Name = "panelDetectorName";
            panelDetectorName.Size = new Size(122, 22);
            panelDetectorName.TabIndex = 104;
            // 
            // labelDetectorName
            // 
            labelDetectorName.Location = new Point(0, 0);
            labelDetectorName.Name = "labelDetectorName";
            labelDetectorName.Size = new Size(60, 15);
            labelDetectorName.TabIndex = 8;
            labelDetectorName.Text = "検出器名";
            // 
            // textBoxDetectorName
            // 
            textBoxDetectorName.Location = new Point(69, -3);
            textBoxDetectorName.Margin = new Padding(3, 2, 3, 2);
            textBoxDetectorName.Name = "textBoxDetectorName";
            textBoxDetectorName.Size = new Size(50, 23);
            textBoxDetectorName.TabIndex = 3;
            textBoxDetectorName.Text = "test";
            // 
            // numericBoxBeamEnergy
            // 
            numericBoxBeamEnergy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            numericBoxBeamEnergy.AutoValidate = AutoValidate.EnableAllowFocusChange;
            numericBoxBeamEnergy.BackColor = Color.Transparent;
            numericBoxBeamEnergy.DecimalPlaces = 2;
            numericBoxBeamEnergy.Font = new Font("Yu Gothic UI", 9F);
            numericBoxBeamEnergy.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxBeamEnergy.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxBeamEnergy.HeaderText = "加速電圧(kV)";
            numericBoxBeamEnergy.Location = new Point(266, 0);
            numericBoxBeamEnergy.Margin = new Padding(0);
            numericBoxBeamEnergy.MaximumSize = new Size(1000, 28);
            numericBoxBeamEnergy.Minimum = 0D;
            numericBoxBeamEnergy.MinimumSize = new Size(1, 18);
            numericBoxBeamEnergy.Name = "numericBoxBeamEnergy";
            numericBoxBeamEnergy.RadianValue = 0.3490658503988659D;
            numericBoxBeamEnergy.ShowUpDown = true;
            numericBoxBeamEnergy.Size = new Size(138, 26);
            numericBoxBeamEnergy.SmartIncrement = true;
            numericBoxBeamEnergy.TabIndex = 104;
            numericBoxBeamEnergy.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxBeamEnergy.Value = 20D;
            // 
            // numericBoxCarbonThickness
            // 
            numericBoxCarbonThickness.BackColor = Color.Transparent;
            numericBoxCarbonThickness.DecimalPlaces = 3;
            numericBoxCarbonThickness.Font = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.HeaderText = "カーボン蒸着の厚さ(nm)";
            numericBoxCarbonThickness.Location = new Point(540, 0);
            numericBoxCarbonThickness.Margin = new Padding(0);
            numericBoxCarbonThickness.MaximumSize = new Size(1000, 28);
            numericBoxCarbonThickness.Minimum = 0D;
            numericBoxCarbonThickness.MinimumSize = new Size(1, 18);
            numericBoxCarbonThickness.Name = "numericBoxCarbonThickness";
            numericBoxCarbonThickness.RadianValue = 0.00034906585039886593D;
            numericBoxCarbonThickness.ShowUpDown = true;
            numericBoxCarbonThickness.Size = new Size(199, 26);
            numericBoxCarbonThickness.SmartIncrement = true;
            numericBoxCarbonThickness.TabIndex = 104;
            numericBoxCarbonThickness.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.Value = 0.02D;
            // 
            // panelCommandBar
            // 
            panelCommandBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelCommandBar.Controls.Add(buttonCancelSpectrumGeneration);
            panelCommandBar.Controls.Add(panelModelName);
            panelCommandBar.Controls.Add(checkBoxAdvanced);
            panelCommandBar.Controls.Add(buttonModelTrain);
            panelCommandBar.Controls.Add(buttonRunSpectrumGeneration);
            panelCommandBar.Location = new Point(3, 349);
            panelCommandBar.Name = "panelCommandBar";
            panelCommandBar.Size = new Size(949, 32);
            panelCommandBar.TabIndex = 108;
            //
            // buttonCancelSpectrumGeneration
            //
            buttonCancelSpectrumGeneration.AutoSize = true;
            buttonCancelSpectrumGeneration.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonCancelSpectrumGeneration.Location = new Point(902, 3);
            buttonCancelSpectrumGeneration.Name = "buttonCancelSpectrumGeneration";
            buttonCancelSpectrumGeneration.Size = new Size(41, 25);
            buttonCancelSpectrumGeneration.TabIndex = 106;
            buttonCancelSpectrumGeneration.Text = "中止";
            buttonCancelSpectrumGeneration.UseVisualStyleBackColor = true;
            buttonCancelSpectrumGeneration.Click += buttonCancelSpectrumGeneration_Click;
            //
            // panelModelName
            // 
            panelModelName.AutoSize = true;
            panelModelName.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelModelName.Controls.Add(labelModelName);
            panelModelName.Controls.Add(textBoxModelName);
            panelModelName.Location = new Point(566, 3);
            panelModelName.Name = "panelModelName";
            panelModelName.Size = new Size(176, 27);
            panelModelName.TabIndex = 105;
            // 
            // labelModelName
            // 
            labelModelName.AutoSize = true;
            labelModelName.Location = new Point(0, 5);
            labelModelName.Name = "labelModelName";
            labelModelName.Size = new Size(47, 15);
            labelModelName.TabIndex = 93;
            labelModelName.Text = "モデル名";
            // 
            // textBoxModelName
            // 
            textBoxModelName.Location = new Point(53, 1);
            textBoxModelName.Name = "textBoxModelName";
            textBoxModelName.Size = new Size(120, 23);
            textBoxModelName.TabIndex = 94;
            // 
            // checkBoxAdvanced
            // 
            checkBoxAdvanced.AutoSize = true;
            checkBoxAdvanced.Location = new Point(3, 6);
            checkBoxAdvanced.Name = "checkBoxAdvanced";
            checkBoxAdvanced.Size = new Size(107, 19);
            checkBoxAdvanced.TabIndex = 101;
            checkBoxAdvanced.Text = "詳細設定を表示";
            checkBoxAdvanced.UseVisualStyleBackColor = true;
            checkBoxAdvanced.CheckedChanged += checkBoxAdvanced_CheckedChanged;
            // 
            // panelBottomDrawer
            // 
            panelBottomDrawer.Controls.Add(groupBoxAdvancedSettings);
            panelBottomDrawer.Location = new Point(3, 387);
            panelBottomDrawer.Name = "panelBottomDrawer";
            panelBottomDrawer.Size = new Size(949, 129);
            panelBottomDrawer.TabIndex = 109;
            // 
            // groupBoxMineral
            // 
            groupBoxMineral.Controls.Add(groupBoxMineralInfo);
            groupBoxMineral.Controls.Add(checkedListBoxMinerals);
            groupBoxMineral.Controls.Add(flowLayoutPanelMineralActions);
            groupBoxMineral.Location = new Point(3, 61);
            groupBoxMineral.Name = "groupBoxMineral";
            groupBoxMineral.Size = new Size(949, 282);
            groupBoxMineral.TabIndex = 98;
            groupBoxMineral.TabStop = false;
            groupBoxMineral.Text = "計算対象";
            // 
            // flowLayoutPanelMineralActions
            // 
            flowLayoutPanelMineralActions.AutoSize = true;
            flowLayoutPanelMineralActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanelMineralActions.Controls.Add(buttonAddMineral);
            flowLayoutPanelMineralActions.Controls.Add(buttonDelete);
            flowLayoutPanelMineralActions.Controls.Add(buttonAllDelete);
            flowLayoutPanelMineralActions.Controls.Add(buttonUpdateMineral);
            flowLayoutPanelMineralActions.Controls.Add(buttonReset);
            flowLayoutPanelMineralActions.Controls.Add(buttonAllSelect);
            flowLayoutPanelMineralActions.Dock = DockStyle.Top;
            flowLayoutPanelMineralActions.Location = new Point(3, 19);
            flowLayoutPanelMineralActions.Name = "flowLayoutPanelMineralActions";
            flowLayoutPanelMineralActions.Size = new Size(943, 31);
            flowLayoutPanelMineralActions.TabIndex = 103;
            // 
            // buttonAllDelete
            // 
            buttonAllDelete.AutoSize = true;
            buttonAllDelete.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonAllDelete.Font = new Font("Yu Gothic UI", 8F);
            buttonAllDelete.Location = new Point(95, 3);
            buttonAllDelete.Name = "buttonAllDelete";
            buttonAllDelete.Size = new Size(50, 23);
            buttonAllDelete.TabIndex = 98;
            buttonAllDelete.Text = "全削除";
            buttonAllDelete.UseVisualStyleBackColor = true;
            buttonAllDelete.Click += buttonDeleteAllMinerals_Click;
            // 
            // buttonReset
            // 
            buttonReset.AutoSize = true;
            buttonReset.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonReset.Font = new Font("Yu Gothic UI", 8F);
            buttonReset.Location = new Point(198, 3);
            buttonReset.Name = "buttonReset";
            buttonReset.Size = new Size(50, 23);
            buttonReset.TabIndex = 99;
            buttonReset.Text = "初期化";
            buttonReset.UseVisualStyleBackColor = true;
            buttonReset.Click += buttonResetMinerals_Click;
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripProgressBar1, toolStripStatusLabel1 });
            statusStrip1.Location = new Point(3, 724);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(990, 22);
            statusStrip1.TabIndex = 89;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripProgressBar1
            // 
            toolStripProgressBar1.Name = "toolStripProgressBar1";
            toolStripProgressBar1.Size = new Size(100, 16);
            // 
            // toolStripStatusLabel1
            // 
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(118, 17);
            toolStripStatusLabel1.Text = "toolStripStatusLabel1";
            // 
            // GeneratorForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(996, 749);
            Controls.Add(groupBoxModelCreation);
            Controls.Add(statusStrip1);
            Margin = new Padding(3, 2, 3, 2);
            Name = "GeneratorForm";
            Padding = new Padding(3);
            FormClosing += GeneratorForm_FormClosing;
            groupBoxMineralInfo.ResumeLayout(false);
            groupBoxMineralInfo.PerformLayout();
            panelMineralName.ResumeLayout(false);
            panelMineralName.PerformLayout();
            panelMemo.ResumeLayout(false);
            panelMemo.PerformLayout();
            groupBoxSampling.ResumeLayout(false);
            groupBoxSampling.PerformLayout();
            groupBoxEndmembers.ResumeLayout(false);
            groupBoxEndmembers.PerformLayout();
            panelEndmembers.ResumeLayout(false);
            panelEndmembers.PerformLayout();
            flowLayoutPanelEndmembers.ResumeLayout(false);
            groupBoxAdvancedSettings.ResumeLayout(false);
            groupBoxModelSettings.ResumeLayout(false);
            groupBoxModelSettings.PerformLayout();
            flowLayoutPanelModelSettings.ResumeLayout(false);
            groupBoxSpectrumSettings.ResumeLayout(false);
            groupBoxSpectrumSettings.PerformLayout();
            flowLayoutPanelSpectrumSettings.ResumeLayout(false);
            panelModelLog.ResumeLayout(false);
            panelModelLog.PerformLayout();
            groupBoxModelCreation.ResumeLayout(false);
            tableLayoutPanelMain.ResumeLayout(false);
            groupBoxEDXSettings.ResumeLayout(false);
            flowLayoutPanelEDXSettings.ResumeLayout(false);
            flowLayoutPanelEDXSettings.PerformLayout();
            panelDetectorName.ResumeLayout(false);
            panelDetectorName.PerformLayout();
            panelCommandBar.ResumeLayout(false);
            panelCommandBar.PerformLayout();
            panelModelName.ResumeLayout(false);
            panelModelName.PerformLayout();
            panelBottomDrawer.ResumeLayout(false);
            groupBoxMineral.ResumeLayout(false);
            groupBoxMineral.PerformLayout();
            flowLayoutPanelMineralActions.ResumeLayout(false);
            flowLayoutPanelMineralActions.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Button buttonRunSpectrumGeneration;
        private GroupBox groupBoxMineralInfo;
        private Label labelEndmemberName;
        private Label labelEndmemberFormula;
        private EndmemberControl EndmemberControl2;
        private EndmemberControl EndmemberControl1;
        private GroupBox groupBoxEndmembers;
        private CheckedListBox checkedListBoxMinerals;
        private TextBox textBoxConstraints;
        private Label labelConstraints;
        private GroupBox groupBoxModelCreation;
        private Label labelCompositionList;
        private TextBox textBoxCompositionList;
        private Button buttonAddMineral;
        private Button buttonDelete;
        private GroupBox groupBoxMineral;
        private Label labelMineralName;
        private TextBox textBoxMineralName;
        private Label labelDetectorName;
        private TextBox textBoxDetectorName;
        private GroupBox groupBoxAdvancedSettings;
        private TextBox textBoxModelLog;
        private Label labelModelLog;
        private Button buttonModelTrain;
        private Button buttonEndmemberAdd;
        private Button buttonEndmemberDelete;
        private Button buttonUpdateMineral;
        private Button buttonAllDelete;
        private TextBox textBoxMemo;
        private Label labelMemo;
        private Button buttonReset;
        private Button buttonAllSelect;
        private FlowLayoutPanel flowLayoutPanelMineralActions;
        private FlowLayoutPanel flowLayoutPanelEndmembers;
        private Panel panelEndmembers;
        private GroupBox groupBoxEDXSettings;
        private GroupBox groupBoxSampling;
        private Crystallography.Controls.NumericBox numericBoxEpochs;
        private Crystallography.Controls.NumericBox numericBoxBatchSize;
        private Crystallography.Controls.NumericBox numericBoxEarlyStopping;
        private Crystallography.Controls.NumericBox numericBoxProbeCurrent;
        private Crystallography.Controls.NumericBox numericBoxLiveTime;
        private Crystallography.Controls.NumericBox numericBoxBeamEnergy;
        private Crystallography.Controls.NumericBox numericBoxCarbonThickness;
        private StatusStrip statusStrip1;
        private ToolStripProgressBar toolStripProgressBar1;
        private ToolStripStatusLabel toolStripStatusLabel1;
        private Crystallography.Controls.NumericBox numericBoxValidationSplit;
        private Crystallography.Controls.NumericBox numericBoxParallel;
        private Crystallography.Controls.NumericBox numericBoxResolution;
        private Crystallography.Controls.NumericBox numericBoxSpectraPerMineral;
        private Button buttonCalibration;
        private TableLayoutPanel tableLayoutPanelMain;
        private Panel panelCommandBar;
        private Panel panelBottomDrawer;
        private CheckBox checkBoxAdvanced;
        private Panel panelModelLog;
        private GroupBox groupBoxSpectrumSettings;
        private GroupBox groupBoxModelSettings;
        private Panel panelMemo;
        private Panel panelMineralName;
        private Panel panelDetectorName;
        private Panel panelModelName;
        private Label labelModelName;
        private TextBox textBoxModelName;
        private FlowLayoutPanel flowLayoutPanelSpectrumSettings;
        private FlowLayoutPanel flowLayoutPanelModelSettings;
        private FlowLayoutPanel flowLayoutPanelEDXSettings;
        private Button buttonCancelSpectrumGeneration;
    }
}

