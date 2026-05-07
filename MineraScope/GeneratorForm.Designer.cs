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
            buttonSpectrumGenerationRun = new Button();
            groupBoxMineralInfo = new GroupBox();
            panelMineralName = new Panel();
            labelMineralName = new Label();
            textBoxMineralName = new TextBox();
            panelMemo = new Panel();
            labelMemo = new Label();
            textBoxMemo = new TextBox();
            groupBox2 = new GroupBox();
            textBoxEndmembers_Constraints = new TextBox();
            labelEndmembers_Constraints = new Label();
            labelEndmembers_CompositionLists = new Label();
            textBoxEndmembers_CompositionLists = new TextBox();
            groupBoxEndmembers = new GroupBox();
            panelEndmembers = new Panel();
            buttonEndmember_Delete = new Button();
            buttonEndmember_Add = new Button();
            labelEndmember_Formula = new Label();
            labelEndmember_Name = new Label();
            flowLayoutPanel3 = new FlowLayoutPanel();
            EndmemberControl1 = new EndmemberControl();
            EndmemberControl2 = new EndmemberControl();
            buttonAddList = new Button();
            buttonUpdateList = new Button();
            checkedListBoxMineral = new CheckedListBox();
            checkedListBoxTrainMinerals = new CheckedListBox();
            groupBoxTrainModel = new GroupBox();
            groupBox4 = new GroupBox();
            numericBoxModel_Epochs = new Crystallography.Controls.NumericBox();
            numericBoxValidationSplit = new Crystallography.Controls.NumericBox();
            numericBoxModel_EarlyStopping = new Crystallography.Controls.NumericBox();
            numericBoxModel_BatchSize = new Crystallography.Controls.NumericBox();
            groupBox3 = new GroupBox();
            numericBoxParallel = new Crystallography.Controls.NumericBox();
            numericBoxCount = new Crystallography.Controls.NumericBox();
            numericBoxTarget = new Crystallography.Controls.NumericBox();
            numericBoxResolution = new Crystallography.Controls.NumericBox();
            buttonCalibration = new Button();
            panelModelLog = new Panel();
            labelModelLog = new Label();
            textBoxModelLog = new TextBox();
            buttonAllSelect = new Button();
            buttonModelTrain = new Button();
            numericBoxProbeCurrent = new Crystallography.Controls.NumericBox();
            numericBoxLiveTime = new Crystallography.Controls.NumericBox();
            buttonDelete = new Button();
            groupBoxModelGeneration = new GroupBox();
            tableLayoutPanelMain = new TableLayoutPanel();
            groupBoxEDXsetting = new GroupBox();
            panel1 = new Panel();
            labelDetectorName = new Label();
            textBoxDetectorName = new TextBox();
            numericBoxCarbonThickness = new Crystallography.Controls.NumericBox();
            groupBox5 = new GroupBox();
            label2 = new Label();
            numericBoxBeamEnergy = new Crystallography.Controls.NumericBox();
            panelCommandBar = new Panel();
            panelModelName = new Panel();
            label1 = new Label();
            textBoxModelName = new TextBox();
            checkBoxAdvanced = new CheckBox();
            panelBottomDrawer = new Panel();
            groupBoxMineral = new GroupBox();
            flowLayoutPanel2 = new FlowLayoutPanel();
            buttonAllDelete = new Button();
            buttonReset = new Button();
            statusStrip1 = new StatusStrip();
            toolStripProgressBar1 = new ToolStripProgressBar();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            groupBoxMineralInfo.SuspendLayout();
            panelMineralName.SuspendLayout();
            panelMemo.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBoxEndmembers.SuspendLayout();
            panelEndmembers.SuspendLayout();
            flowLayoutPanel3.SuspendLayout();
            groupBoxTrainModel.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox3.SuspendLayout();
            panelModelLog.SuspendLayout();
            groupBoxModelGeneration.SuspendLayout();
            tableLayoutPanelMain.SuspendLayout();
            groupBoxEDXsetting.SuspendLayout();
            panel1.SuspendLayout();
            groupBox5.SuspendLayout();
            panelCommandBar.SuspendLayout();
            panelModelName.SuspendLayout();
            panelBottomDrawer.SuspendLayout();
            groupBoxMineral.SuspendLayout();
            flowLayoutPanel2.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            //
            // buttonSpectrumGenerationRun
            //
            buttonSpectrumGenerationRun.AutoSize = true;
            buttonSpectrumGenerationRun.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonSpectrumGenerationRun.Location = new Point(800, 3);
            buttonSpectrumGenerationRun.Margin = new Padding(3, 2, 3, 2);
            buttonSpectrumGenerationRun.Name = "buttonSpectrumGenerationRun";
            buttonSpectrumGenerationRun.Size = new Size(41, 25);
            buttonSpectrumGenerationRun.TabIndex = 0;
            buttonSpectrumGenerationRun.Text = "実行";
            buttonSpectrumGenerationRun.UseVisualStyleBackColor = true;
            buttonSpectrumGenerationRun.Click += buttonSpectrumGenerationRun_Click;
            //
            // groupBoxMineralInfo
            //
            groupBoxMineralInfo.Controls.Add(panelMineralName);
            groupBoxMineralInfo.Controls.Add(panelMemo);
            groupBoxMineralInfo.Controls.Add(groupBox2);
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
            // groupBox2
            //
            groupBox2.Controls.Add(textBoxEndmembers_Constraints);
            groupBox2.Controls.Add(labelEndmembers_Constraints);
            groupBox2.Controls.Add(labelEndmembers_CompositionLists);
            groupBox2.Controls.Add(textBoxEndmembers_CompositionLists);
            groupBox2.Location = new Point(318, 50);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(266, 167);
            groupBox2.TabIndex = 103;
            groupBox2.TabStop = false;
            groupBox2.Text = "サンプリング条件";
            //
            // textBoxEndmembers_Constraints
            //
            textBoxEndmembers_Constraints.Location = new Point(18, 37);
            textBoxEndmembers_Constraints.Multiline = true;
            textBoxEndmembers_Constraints.Name = "textBoxEndmembers_Constraints";
            textBoxEndmembers_Constraints.ScrollBars = ScrollBars.Both;
            textBoxEndmembers_Constraints.Size = new Size(230, 44);
            textBoxEndmembers_Constraints.TabIndex = 93;
            //
            // labelEndmembers_Constraints
            //
            labelEndmembers_Constraints.AutoSize = true;
            labelEndmembers_Constraints.Location = new Point(107, 19);
            labelEndmembers_Constraints.Name = "labelEndmembers_Constraints";
            labelEndmembers_Constraints.Size = new Size(43, 15);
            labelEndmembers_Constraints.TabIndex = 94;
            labelEndmembers_Constraints.Text = "条件式";
            //
            // labelEndmembers_CompositionLists
            //
            labelEndmembers_CompositionLists.AutoSize = true;
            labelEndmembers_CompositionLists.Location = new Point(85, 84);
            labelEndmembers_CompositionLists.Name = "labelEndmembers_CompositionLists";
            labelEndmembers_CompositionLists.Size = new Size(80, 15);
            labelEndmembers_CompositionLists.TabIndex = 96;
            labelEndmembers_CompositionLists.Text = "化学組成リスト";
            //
            // textBoxEndmembers_CompositionLists
            //
            textBoxEndmembers_CompositionLists.Location = new Point(18, 102);
            textBoxEndmembers_CompositionLists.Multiline = true;
            textBoxEndmembers_CompositionLists.Name = "textBoxEndmembers_CompositionLists";
            textBoxEndmembers_CompositionLists.ScrollBars = ScrollBars.Both;
            textBoxEndmembers_CompositionLists.Size = new Size(230, 59);
            textBoxEndmembers_CompositionLists.TabIndex = 95;
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
            panelEndmembers.Controls.Add(buttonEndmember_Delete);
            panelEndmembers.Controls.Add(buttonEndmember_Add);
            panelEndmembers.Controls.Add(labelEndmember_Formula);
            panelEndmembers.Controls.Add(labelEndmember_Name);
            panelEndmembers.Controls.Add(flowLayoutPanel3);
            panelEndmembers.Location = new Point(4, 20);
            panelEndmembers.Name = "panelEndmembers";
            panelEndmembers.Size = new Size(291, 127);
            panelEndmembers.TabIndex = 106;
            //
            // buttonEndmember_Delete
            //
            buttonEndmember_Delete.AutoSize = true;
            buttonEndmember_Delete.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonEndmember_Delete.Font = new Font("Yu Gothic UI", 8F);
            buttonEndmember_Delete.Location = new Point(249, 101);
            buttonEndmember_Delete.Name = "buttonEndmember_Delete";
            buttonEndmember_Delete.Size = new Size(39, 23);
            buttonEndmember_Delete.TabIndex = 98;
            buttonEndmember_Delete.Text = "削除";
            buttonEndmember_Delete.UseVisualStyleBackColor = true;
            buttonEndmember_Delete.Click += buttonRemoveEndmemberControl_Click;
            //
            // buttonEndmember_Add
            //
            buttonEndmember_Add.AutoSize = true;
            buttonEndmember_Add.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonEndmember_Add.Font = new Font("Yu Gothic UI", 8F);
            buttonEndmember_Add.Location = new Point(204, 101);
            buttonEndmember_Add.Name = "buttonEndmember_Add";
            buttonEndmember_Add.Size = new Size(39, 23);
            buttonEndmember_Add.TabIndex = 100;
            buttonEndmember_Add.Text = "追加";
            buttonEndmember_Add.UseVisualStyleBackColor = true;
            buttonEndmember_Add.Click += buttonAddEndmemberControl_Click;
            //
            // labelEndmember_Formula
            //
            labelEndmember_Formula.AutoSize = true;
            labelEndmember_Formula.Location = new Point(176, 0);
            labelEndmember_Formula.Margin = new Padding(60, 0, 0, 0);
            labelEndmember_Formula.Name = "labelEndmember_Formula";
            labelEndmember_Formula.Size = new Size(55, 15);
            labelEndmember_Formula.TabIndex = 76;
            labelEndmember_Formula.Text = "化学組成";
            //
            // labelEndmember_Name
            //
            labelEndmember_Name.AutoSize = true;
            labelEndmember_Name.Location = new Point(41, 0);
            labelEndmember_Name.Margin = new Padding(60, 0, 0, 0);
            labelEndmember_Name.Name = "labelEndmember_Name";
            labelEndmember_Name.Size = new Size(43, 15);
            labelEndmember_Name.TabIndex = 75;
            labelEndmember_Name.Text = "鉱物名";
            //
            // flowLayoutPanel3
            //
            flowLayoutPanel3.AutoScroll = true;
            flowLayoutPanel3.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanel3.Controls.Add(EndmemberControl1);
            flowLayoutPanel3.Controls.Add(EndmemberControl2);
            flowLayoutPanel3.FlowDirection = FlowDirection.TopDown;
            flowLayoutPanel3.Location = new Point(3, 17);
            flowLayoutPanel3.Name = "flowLayoutPanel3";
            flowLayoutPanel3.Size = new Size(282, 87);
            flowLayoutPanel3.TabIndex = 103;
            flowLayoutPanel3.WrapContents = false;
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
            // buttonAddList
            //
            buttonAddList.AutoSize = true;
            buttonAddList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonAddList.Location = new Point(3, 3);
            buttonAddList.Name = "buttonAddList";
            buttonAddList.Size = new Size(41, 25);
            buttonAddList.TabIndex = 96;
            buttonAddList.Text = "追加";
            buttonAddList.UseVisualStyleBackColor = true;
            buttonAddList.Click += buttonAddList_Click;
            //
            // buttonUpdateList
            //
            buttonUpdateList.AutoSize = true;
            buttonUpdateList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonUpdateList.Location = new Point(151, 3);
            buttonUpdateList.Name = "buttonUpdateList";
            buttonUpdateList.Size = new Size(41, 25);
            buttonUpdateList.TabIndex = 99;
            buttonUpdateList.Text = "更新";
            buttonUpdateList.UseVisualStyleBackColor = true;
            buttonUpdateList.Click += buttonUpdate_Click;
            //
            // checkedListBoxMineral
            //
            checkedListBoxMineral.FormattingEnabled = true;
            checkedListBoxMineral.HorizontalScrollbar = true;
            checkedListBoxMineral.Location = new Point(3, 48);
            checkedListBoxMineral.MultiColumn = true;
            checkedListBoxMineral.Name = "checkedListBoxMineral";
            checkedListBoxMineral.ScrollAlwaysVisible = true;
            checkedListBoxMineral.Size = new Size(344, 220);
            checkedListBoxMineral.TabIndex = 86;
            checkedListBoxMineral.ItemCheck += checkedListBoxMineral_ItemCheck;
            checkedListBoxMineral.SelectedIndexChanged += checkedListBoxMineral_SelectedIndexChanged;
            //
            // checkedListBoxTrainMinerals
            //
            checkedListBoxTrainMinerals.FormattingEnabled = true;
            checkedListBoxTrainMinerals.HorizontalScrollbar = true;
            checkedListBoxTrainMinerals.Location = new Point(152, 69);
            checkedListBoxTrainMinerals.MultiColumn = true;
            checkedListBoxTrainMinerals.Name = "checkedListBoxTrainMinerals";
            checkedListBoxTrainMinerals.ScrollAlwaysVisible = true;
            checkedListBoxTrainMinerals.Size = new Size(186, 40);
            checkedListBoxTrainMinerals.TabIndex = 101;
            //
            // groupBoxTrainModel
            //
            groupBoxTrainModel.Controls.Add(groupBox4);
            groupBoxTrainModel.Controls.Add(groupBox3);
            groupBoxTrainModel.Location = new Point(0, 3);
            groupBoxTrainModel.Name = "groupBoxTrainModel";
            groupBoxTrainModel.Size = new Size(946, 113);
            groupBoxTrainModel.TabIndex = 43;
            groupBoxTrainModel.TabStop = false;
            groupBoxTrainModel.Text = "詳細設定";
            //
            // groupBox4
            //
            groupBox4.Controls.Add(numericBoxModel_Epochs);
            groupBox4.Controls.Add(numericBoxValidationSplit);
            groupBox4.Controls.Add(numericBoxModel_EarlyStopping);
            groupBox4.Controls.Add(numericBoxModel_BatchSize);
            groupBox4.Location = new Point(465, 22);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(484, 76);
            groupBox4.TabIndex = 109;
            groupBox4.TabStop = false;
            groupBox4.Text = "モデル訓練詳細";
            //
            // numericBoxModel_Epochs
            //
            numericBoxModel_Epochs.BackColor = Color.Transparent;
            numericBoxModel_Epochs.DecimalPlaces = 0;
            numericBoxModel_Epochs.Font = new Font("Yu Gothic UI", 9F);
            numericBoxModel_Epochs.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_Epochs.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_Epochs.HeaderText = "エポック数";
            numericBoxModel_Epochs.Location = new Point(9, 26);
            numericBoxModel_Epochs.Margin = new Padding(0);
            numericBoxModel_Epochs.Maximum = 100000D;
            numericBoxModel_Epochs.MaximumSize = new Size(1000, 28);
            numericBoxModel_Epochs.Minimum = 1D;
            numericBoxModel_Epochs.MinimumSize = new Size(1, 18);
            numericBoxModel_Epochs.Name = "numericBoxModel_Epochs";
            numericBoxModel_Epochs.RadianValue = 8.7266462599716466D;
            numericBoxModel_Epochs.ShowUpDown = true;
            numericBoxModel_Epochs.Size = new Size(102, 26);
            numericBoxModel_Epochs.SmartIncrement = true;
            numericBoxModel_Epochs.TabIndex = 104;
            numericBoxModel_Epochs.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_Epochs.Value = 500D;
            //
            // numericBoxValidationSplit
            //
            numericBoxValidationSplit.BackColor = Color.Transparent;
            numericBoxValidationSplit.DecimalPlaces = 0;
            numericBoxValidationSplit.Font = new Font("Yu Gothic UI", 9F);
            numericBoxValidationSplit.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxValidationSplit.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxValidationSplit.HeaderText = "テストデータの割合(%)";
            numericBoxValidationSplit.Location = new Point(215, 26);
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
            // numericBoxModel_EarlyStopping
            //
            numericBoxModel_EarlyStopping.BackColor = Color.Transparent;
            numericBoxModel_EarlyStopping.DecimalPlaces = 0;
            numericBoxModel_EarlyStopping.Font = new Font("Yu Gothic UI", 9F);
            numericBoxModel_EarlyStopping.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_EarlyStopping.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_EarlyStopping.HeaderText = "待機回数";
            numericBoxModel_EarlyStopping.Location = new Point(376, 26);
            numericBoxModel_EarlyStopping.Margin = new Padding(0);
            numericBoxModel_EarlyStopping.MaximumSize = new Size(1000, 28);
            numericBoxModel_EarlyStopping.Minimum = 0D;
            numericBoxModel_EarlyStopping.MinimumSize = new Size(1, 18);
            numericBoxModel_EarlyStopping.Name = "numericBoxModel_EarlyStopping";
            numericBoxModel_EarlyStopping.RadianValue = 0.17453292519943295D;
            numericBoxModel_EarlyStopping.ShowUpDown = true;
            numericBoxModel_EarlyStopping.Size = new Size(102, 26);
            numericBoxModel_EarlyStopping.SmartIncrement = true;
            numericBoxModel_EarlyStopping.TabIndex = 106;
            numericBoxModel_EarlyStopping.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_EarlyStopping.Value = 10D;
            //
            // numericBoxModel_BatchSize
            //
            numericBoxModel_BatchSize.BackColor = Color.Transparent;
            numericBoxModel_BatchSize.DecimalPlaces = 0;
            numericBoxModel_BatchSize.Font = new Font("Yu Gothic UI", 9F);
            numericBoxModel_BatchSize.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_BatchSize.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_BatchSize.HeaderText = "バッチサイズ";
            numericBoxModel_BatchSize.Location = new Point(111, 26);
            numericBoxModel_BatchSize.Margin = new Padding(0);
            numericBoxModel_BatchSize.MaximumSize = new Size(1000, 28);
            numericBoxModel_BatchSize.Minimum = 1D;
            numericBoxModel_BatchSize.MinimumSize = new Size(1, 18);
            numericBoxModel_BatchSize.Name = "numericBoxModel_BatchSize";
            numericBoxModel_BatchSize.RadianValue = 0.27925268031909273D;
            numericBoxModel_BatchSize.ShowUpDown = true;
            numericBoxModel_BatchSize.Size = new Size(104, 26);
            numericBoxModel_BatchSize.SmartIncrement = true;
            numericBoxModel_BatchSize.TabIndex = 105;
            numericBoxModel_BatchSize.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_BatchSize.Value = 16D;
            //
            // groupBox3
            //
            groupBox3.Controls.Add(numericBoxParallel);
            groupBox3.Controls.Add(numericBoxCount);
            groupBox3.Controls.Add(numericBoxTarget);
            groupBox3.Controls.Add(numericBoxResolution);
            groupBox3.Location = new Point(3, 22);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(459, 76);
            groupBox3.TabIndex = 108;
            groupBox3.TabStop = false;
            groupBox3.Text = "EDXスペクトル生成詳細";
            //
            // numericBoxParallel
            //
            numericBoxParallel.BackColor = Color.Transparent;
            numericBoxParallel.DecimalPlaces = 0;
            numericBoxParallel.Font = new Font("Yu Gothic UI", 9F);
            numericBoxParallel.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxParallel.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxParallel.HeaderText = "並列数";
            numericBoxParallel.Location = new Point(354, 26);
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
            // numericBoxCount
            //
            numericBoxCount.BackColor = Color.Transparent;
            numericBoxCount.DecimalPlaces = 0;
            numericBoxCount.Font = new Font("Yu Gothic UI", 9F);
            numericBoxCount.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxCount.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxCount.HeaderText = "回数";
            numericBoxCount.Location = new Point(277, 26);
            numericBoxCount.Margin = new Padding(0);
            numericBoxCount.Maximum = 100000D;
            numericBoxCount.MaximumSize = new Size(1000, 28);
            numericBoxCount.Minimum = 1D;
            numericBoxCount.MinimumSize = new Size(1, 18);
            numericBoxCount.Name = "numericBoxCount";
            numericBoxCount.RadianValue = 0.017453292519943295D;
            numericBoxCount.ShowUpDown = true;
            numericBoxCount.Size = new Size(80, 26);
            numericBoxCount.SmartIncrement = true;
            numericBoxCount.TabIndex = 115;
            numericBoxCount.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxCount.Value = 1D;
            //
            // numericBoxTarget
            //
            numericBoxTarget.BackColor = Color.Transparent;
            numericBoxTarget.DecimalPlaces = 0;
            numericBoxTarget.Font = new Font("Yu Gothic UI", 9F);
            numericBoxTarget.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxTarget.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxTarget.HeaderText = "シミュレーション数";
            numericBoxTarget.Location = new Point(4, 26);
            numericBoxTarget.Margin = new Padding(0);
            numericBoxTarget.MaximumSize = new Size(1000, 28);
            numericBoxTarget.Minimum = 0D;
            numericBoxTarget.MinimumSize = new Size(1, 18);
            numericBoxTarget.Name = "numericBoxTarget";
            numericBoxTarget.RadianValue = 17.453292519943293D;
            numericBoxTarget.ShowUpDown = true;
            numericBoxTarget.Size = new Size(139, 26);
            numericBoxTarget.SmartIncrement = true;
            numericBoxTarget.TabIndex = 106;
            numericBoxTarget.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxTarget.Value = 1000D;
            //
            // numericBoxResolution
            //
            numericBoxResolution.BackColor = Color.Transparent;
            numericBoxResolution.DecimalPlaces = 0;
            numericBoxResolution.Font = new Font("Yu Gothic UI", 9F);
            numericBoxResolution.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxResolution.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxResolution.HeaderText = "化学組成分解能";
            numericBoxResolution.Location = new Point(143, 26);
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
            // buttonCalibration
            //
            buttonCalibration.AutoSize = true;
            buttonCalibration.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonCalibration.Location = new Point(828, 21);
            buttonCalibration.Name = "buttonCalibration";
            buttonCalibration.Size = new Size(115, 25);
            buttonCalibration.TabIndex = 104;
            buttonCalibration.Text = "EDXキャリブレーション";
            buttonCalibration.UseVisualStyleBackColor = true;
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
            buttonAllSelect.Location = new Point(61, 80);
            buttonAllSelect.Name = "buttonAllSelect";
            buttonAllSelect.Size = new Size(85, 23);
            buttonAllSelect.TabIndex = 103;
            buttonAllSelect.Text = "全選択/解除";
            buttonAllSelect.UseVisualStyleBackColor = true;
            buttonAllSelect.Click += buttonAllSelect_Click;
            //
            // buttonModelTrain
            //
            buttonModelTrain.AutoSize = true;
            buttonModelTrain.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonModelTrain.Location = new Point(852, 4);
            buttonModelTrain.Name = "buttonModelTrain";
            buttonModelTrain.Size = new Size(69, 25);
            buttonModelTrain.TabIndex = 0;
            buttonModelTrain.Text = "モデル作成";
            buttonModelTrain.UseVisualStyleBackColor = true;
            buttonModelTrain.Click += buttonModel_Train_Click;
            //
            // numericBoxProbeCurrent
            //
            numericBoxProbeCurrent.BackColor = Color.Transparent;
            numericBoxProbeCurrent.DecimalPlaces = 2;
            numericBoxProbeCurrent.Font = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.HeaderText = "照射電流(nA)";
            numericBoxProbeCurrent.Location = new Point(682, 20);
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
            numericBoxLiveTime.Location = new Point(308, 20);
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
            buttonDelete.Click += buttonMineral_Delete_Click;
            //
            // groupBoxModelGeneration
            //
            groupBoxModelGeneration.Controls.Add(panelModelLog);
            groupBoxModelGeneration.Controls.Add(tableLayoutPanelMain);
            groupBoxModelGeneration.Dock = DockStyle.Fill;
            groupBoxModelGeneration.Location = new Point(3, 3);
            groupBoxModelGeneration.Name = "groupBoxModelGeneration";
            groupBoxModelGeneration.Size = new Size(990, 721);
            groupBoxModelGeneration.TabIndex = 88;
            groupBoxModelGeneration.TabStop = false;
            groupBoxModelGeneration.Text = "モデル作成";
            //
            // tableLayoutPanelMain
            //
            tableLayoutPanelMain.ColumnCount = 1;
            tableLayoutPanelMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanelMain.Controls.Add(groupBoxEDXsetting, 0, 0);
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
            // groupBoxEDXsetting
            //
            groupBoxEDXsetting.Controls.Add(panel1);
            groupBoxEDXsetting.Controls.Add(numericBoxCarbonThickness);
            groupBoxEDXsetting.Controls.Add(numericBoxLiveTime);
            groupBoxEDXsetting.Controls.Add(buttonCalibration);
            groupBoxEDXsetting.Controls.Add(numericBoxProbeCurrent);
            groupBoxEDXsetting.Controls.Add(groupBox5);
            groupBoxEDXsetting.Controls.Add(numericBoxBeamEnergy);
            groupBoxEDXsetting.Location = new Point(3, 3);
            groupBoxEDXsetting.Name = "groupBoxEDXsetting";
            groupBoxEDXsetting.Size = new Size(949, 52);
            groupBoxEDXsetting.TabIndex = 99;
            groupBoxEDXsetting.TabStop = false;
            groupBoxEDXsetting.Text = "SEM-EDX条件";
            //
            // panel1
            //
            panel1.AutoSize = true;
            panel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel1.Controls.Add(labelDetectorName);
            panel1.Controls.Add(textBoxDetectorName);
            panel1.Location = new Point(7, 19);
            panel1.Name = "panel1";
            panel1.Size = new Size(119, 27);
            panel1.TabIndex = 104;
            //
            // labelDetectorName
            //
            labelDetectorName.Location = new Point(0, 7);
            labelDetectorName.Name = "labelDetectorName";
            labelDetectorName.Size = new Size(60, 15);
            labelDetectorName.TabIndex = 8;
            labelDetectorName.Text = "検出器名";
            //
            // textBoxDetectorName
            //
            textBoxDetectorName.Location = new Point(66, 2);
            textBoxDetectorName.Margin = new Padding(3, 2, 3, 2);
            textBoxDetectorName.Name = "textBoxDetectorName";
            textBoxDetectorName.Size = new Size(50, 23);
            textBoxDetectorName.TabIndex = 3;
            textBoxDetectorName.Text = "test";
            //
            // numericBoxCarbonThickness
            //
            numericBoxCarbonThickness.BackColor = Color.Transparent;
            numericBoxCarbonThickness.DecimalPlaces = 3;
            numericBoxCarbonThickness.Font = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.HeaderText = "カーボン蒸着の厚さ(nm)";
            numericBoxCarbonThickness.Location = new Point(472, 20);
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
            // groupBox5
            //
            groupBox5.Controls.Add(label2);
            groupBox5.Location = new Point(7, 20);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(0, 0);
            groupBox5.TabIndex = 41;
            groupBox5.TabStop = false;
            groupBox5.Text = "化学組成条件設定";
            //
            // label2
            //
            label2.AutoSize = true;
            label2.Location = new Point(27, 69);
            label2.Name = "label2";
            label2.Size = new Size(31, 15);
            label2.TabIndex = 42;
            label2.Text = "成分";
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
            numericBoxBeamEnergy.Location = new Point(132, 20);
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
            // panelCommandBar
            //
            panelCommandBar.AutoSize = true;
            panelCommandBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelCommandBar.Controls.Add(panelModelName);
            panelCommandBar.Controls.Add(checkBoxAdvanced);
            panelCommandBar.Controls.Add(buttonModelTrain);
            panelCommandBar.Controls.Add(buttonSpectrumGenerationRun);
            panelCommandBar.Location = new Point(3, 349);
            panelCommandBar.Name = "panelCommandBar";
            panelCommandBar.Size = new Size(924, 32);
            panelCommandBar.TabIndex = 108;
            //
            // panelModelName
            //
            panelModelName.AutoSize = true;
            panelModelName.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelModelName.Controls.Add(label1);
            panelModelName.Controls.Add(textBoxModelName);
            panelModelName.Location = new Point(600, 2);
            panelModelName.Name = "panelModelName";
            panelModelName.Size = new Size(176, 27);
            panelModelName.TabIndex = 105;
            //
            // label1
            //
            label1.AutoSize = true;
            label1.Location = new Point(0, 5);
            label1.Name = "label1";
            label1.Size = new Size(47, 15);
            label1.TabIndex = 93;
            label1.Text = "モデル名";
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
            //
            // panelBottomDrawer
            //
            panelBottomDrawer.Controls.Add(groupBoxTrainModel);
            panelBottomDrawer.Location = new Point(3, 387);
            panelBottomDrawer.Name = "panelBottomDrawer";
            panelBottomDrawer.Size = new Size(949, 129);
            panelBottomDrawer.TabIndex = 109;
            //
            // groupBoxMineral
            //
            groupBoxMineral.Controls.Add(checkedListBoxTrainMinerals);
            groupBoxMineral.Controls.Add(buttonAllSelect);
            groupBoxMineral.Controls.Add(groupBoxMineralInfo);
            groupBoxMineral.Controls.Add(checkedListBoxMineral);
            groupBoxMineral.Controls.Add(flowLayoutPanel2);
            groupBoxMineral.Location = new Point(3, 61);
            groupBoxMineral.Name = "groupBoxMineral";
            groupBoxMineral.Size = new Size(949, 282);
            groupBoxMineral.TabIndex = 98;
            groupBoxMineral.TabStop = false;
            groupBoxMineral.Text = "計算対象";
            //
            // flowLayoutPanel2
            //
            flowLayoutPanel2.AutoSize = true;
            flowLayoutPanel2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanel2.Controls.Add(buttonAddList);
            flowLayoutPanel2.Controls.Add(buttonDelete);
            flowLayoutPanel2.Controls.Add(buttonAllDelete);
            flowLayoutPanel2.Controls.Add(buttonUpdateList);
            flowLayoutPanel2.Controls.Add(buttonReset);
            flowLayoutPanel2.Dock = DockStyle.Top;
            flowLayoutPanel2.Location = new Point(3, 19);
            flowLayoutPanel2.Name = "flowLayoutPanel2";
            flowLayoutPanel2.Size = new Size(943, 31);
            flowLayoutPanel2.TabIndex = 103;
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
            buttonAllDelete.Click += buttonMineral_AllDelete_Click;
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
            buttonReset.Click += buttonMineral_Reset_Click;
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
            Controls.Add(groupBoxModelGeneration);
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
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBoxEndmembers.ResumeLayout(false);
            groupBoxEndmembers.PerformLayout();
            panelEndmembers.ResumeLayout(false);
            panelEndmembers.PerformLayout();
            flowLayoutPanel3.ResumeLayout(false);
            groupBoxTrainModel.ResumeLayout(false);
            groupBox4.ResumeLayout(false);
            groupBox3.ResumeLayout(false);
            panelModelLog.ResumeLayout(false);
            panelModelLog.PerformLayout();
            groupBoxModelGeneration.ResumeLayout(false);
            tableLayoutPanelMain.ResumeLayout(false);
            tableLayoutPanelMain.PerformLayout();
            groupBoxEDXsetting.ResumeLayout(false);
            groupBoxEDXsetting.PerformLayout();
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            panelCommandBar.ResumeLayout(false);
            panelCommandBar.PerformLayout();
            panelModelName.ResumeLayout(false);
            panelModelName.PerformLayout();
            panelBottomDrawer.ResumeLayout(false);
            groupBoxMineral.ResumeLayout(false);
            groupBoxMineral.PerformLayout();
            flowLayoutPanel2.ResumeLayout(false);
            flowLayoutPanel2.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Button buttonSpectrumGenerationRun;
        private TextBox textBoxDTSALog;
        private GroupBox groupBoxMineralInfo;
        private Label labelEndmember_Name;
        private Label labelEndmember_Formula;
        private EndmemberControl EndmemberControl2;
        private EndmemberControl EndmemberControl1;
        private Label labelProgress;
        private GroupBox groupBoxEndmembers;
        private CheckedListBox checkedListBoxMineral;
        private TextBox textBoxEndmembers_Constraints;
        private Label labelEndmembers_Constraints;
        private GroupBox groupBoxModelGeneration;
        private GroupBox groupBox5;
        private Label label2;
        private Label labelEndmembers_CompositionLists;
        private TextBox textBoxEndmembers_CompositionLists;
        private Button buttonAddList;
        private Button buttonDelete;
        private GroupBox groupBoxMineral;
        private Label labelMineralName;
        private TextBox textBoxMineralName;
        private Label labelDetectorName;
        private TextBox textBoxDetectorName;
        private GroupBox groupBoxTrainModel;
        private TextBox textBoxModelLog;
        private Button buttonModel_Save;
        private Label labelModelLog;
        private Button buttonModelTrain;
        private Button buttonEndmember_Add;
        private Button buttonEndmember_Delete;
        private Button buttonUpdateList;
        private Button buttonAllDelete;
        private TextBox textBoxMemo;
        private Label labelMemo;
        private Button buttonReset;
        private CheckedListBox checkedListBoxTrainMinerals;
        private Button buttonAllSelect;
        private ProgressBar progressBar1;
        private FlowLayoutPanel flowLayoutPanel2;
        private FlowLayoutPanel flowLayoutPanel3;
        private Panel panelEndmembers;
        private GroupBox groupBoxEDXsetting;
        private GroupBox groupBox2;
        private Crystallography.Controls.NumericBox numericBoxModel_Epochs;
        private Crystallography.Controls.NumericBox numericBoxModel_BatchSize;
        private Crystallography.Controls.NumericBox numericBoxModel_EarlyStopping;
        private Crystallography.Controls.NumericBox numericBoxProbeCurrent;
        private Crystallography.Controls.NumericBox numericBoxLiveTime;
        private Crystallography.Controls.NumericBox numericBoxBeamEnergy;
        private Crystallography.Controls.NumericBox numericBoxCarbonThickness;
        private StatusStrip statusStrip1;
        private ToolStripProgressBar toolStripProgressBar1;
        private ToolStripStatusLabel toolStripStatusLabel1;
        private Crystallography.Controls.NumericBox numericBoxValidationSplit;
        private Crystallography.Controls.NumericBox numericBoxCount;
        private Crystallography.Controls.NumericBox numericBoxParallel;
        private Crystallography.Controls.NumericBox numericBoxResolution;
        private Crystallography.Controls.NumericBox numericBoxTarget;
        private Button buttonCalibration;
        private TableLayoutPanel tableLayoutPanelMain;
        private Panel panelCommandBar;
        private Panel panelBottomDrawer;
        private CheckBox checkBoxAdvanced;
        private Panel panelModelLog;
        private GroupBox groupBox3;
        private GroupBox groupBox4;
        private Panel panelMemo;
        private Panel panelMineralName;
        private Panel panel1;
        private Panel panelModelName;
        private Label label1;
        private TextBox textBoxModelName;
        private System.ComponentModel.BackgroundWorker backgroundWorker1;
    }
}

