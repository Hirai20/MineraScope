namespace MineraScope
{
    // 260416Codex: WinForms Designer 側の partial class 名も GeneratorForm に揃えます。
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
            numericUpDownEndmembers_Resolution = new NumericUpDown();
            labelEndmembers_Resolution = new Label();
            groupBoxMineralInfo = new GroupBox();
            groupBox2 = new GroupBox();
            textBoxEndmembers_Constraints = new TextBox();
            labelEndmembers_Constraints = new Label();
            labelEndmembers_CompositionLists = new Label();
            numericUpDownMineral_Target = new NumericUpDown();
            textBoxEndmembers_CompositionLists = new TextBox();
            labelMineral_Target = new Label();
            textBoxMineral_Memo = new TextBox();
            label15 = new Label();
            labelMineralInfo_Name = new Label();
            textBoxMineral_Name = new TextBox();
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
            labelCompositionCount = new Label();
            textBoxCompositionCount = new TextBox();
            checkedListBoxMineral = new CheckedListBox();
            groupBoxComposition = new GroupBox();
            labelCompositionName = new Label();
            ModelTrain = new GroupBox();
            groupBox4 = new GroupBox();
            numericBoxModel_EarlyStopping = new Crystallography.Controls.NumericBox();
            numericBoxModel_BatchSize = new Crystallography.Controls.NumericBox();
            numericBoxModel_Epochs = new Crystallography.Controls.NumericBox();
            buttonAllSelect = new Button();
            checkedListBoxTrainMinerals = new CheckedListBox();
            textBoxModel_Evaluation = new TextBox();
            buttonModel_Train = new Button();
            label5 = new Label();
            groupBoxModel_LearningData = new GroupBox();
            numericUpDownModel_ValidationSplit = new NumericUpDown();
            label13 = new Label();
            numericBoxProbeCurrent = new Crystallography.Controls.NumericBox();
            numericBoxLiveTime = new Crystallography.Controls.NumericBox();
            buttonModel_SaveFolder = new Button();
            label7 = new Label();
            textBoxModelPath = new TextBox();
            labelPathTeacher = new Label();
            buttonModel_Teacher = new Button();
            textBoxPathTeacher = new TextBox();
            buttonDelete = new Button();
            SpectrumGeneration = new GroupBox();
            groupBox1 = new GroupBox();
            numericBoxCarbonThickness = new Crystallography.Controls.NumericBox();
            groupBox5 = new GroupBox();
            label2 = new Label();
            numericBoxBeamEnergy = new Crystallography.Controls.NumericBox();
            labelDetectorName = new Label();
            textBoxDetectorName = new TextBox();
            groupBoxMineral = new GroupBox();
            flowLayoutPanel4 = new FlowLayoutPanel();
            flowLayoutPanel2 = new FlowLayoutPanel();
            buttonAllDelete = new Button();
            buttonReset = new Button();
            checkBox1 = new CheckBox();
            tabControl2 = new TabControl();
            tabPage5 = new TabPage();
            flowLayoutPanel1 = new FlowLayoutPanel();
            panel2 = new Panel();
            panel4 = new Panel();
            panelPathEDX = new Panel();
            labelPathEDX = new Label();
            textBoxPathEDX = new TextBox();
            buttonPathDTSA = new Button();
            panelPathDTSA = new Panel();
            buttonPathEDX = new Button();
            textBoxPathDTSA = new TextBox();
            labelPathDTSA = new Label();
            tabPage7 = new TabPage();
            numericUpDownExecution_Count = new NumericUpDown();
            labelExecution_Count = new Label();
            numericUpDownExecution_Parallel = new NumericUpDown();
            labelExecution_Parallel = new Label();
            ((System.ComponentModel.ISupportInitialize)numericUpDownEndmembers_Resolution).BeginInit();
            groupBoxMineralInfo.SuspendLayout();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDownMineral_Target).BeginInit();
            groupBoxEndmembers.SuspendLayout();
            panelEndmembers.SuspendLayout();
            flowLayoutPanel3.SuspendLayout();
            groupBoxComposition.SuspendLayout();
            ModelTrain.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBoxModel_LearningData.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDownModel_ValidationSplit).BeginInit();
            SpectrumGeneration.SuspendLayout();
            groupBox1.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBoxMineral.SuspendLayout();
            flowLayoutPanel4.SuspendLayout();
            flowLayoutPanel2.SuspendLayout();
            tabControl2.SuspendLayout();
            tabPage5.SuspendLayout();
            flowLayoutPanel1.SuspendLayout();
            panel2.SuspendLayout();
            panel4.SuspendLayout();
            panelPathEDX.SuspendLayout();
            panelPathDTSA.SuspendLayout();
            tabPage7.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDownExecution_Count).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownExecution_Parallel).BeginInit();
            SuspendLayout();
            // 
            // buttonSpectrumGenerationRun
            // 
            buttonSpectrumGenerationRun.Location = new Point(558, 666);
            buttonSpectrumGenerationRun.Margin = new Padding(3, 2, 3, 2);
            buttonSpectrumGenerationRun.Name = "buttonSpectrumGenerationRun";
            buttonSpectrumGenerationRun.Size = new Size(77, 24);
            buttonSpectrumGenerationRun.TabIndex = 0;
            buttonSpectrumGenerationRun.Text = "実行";
            buttonSpectrumGenerationRun.UseVisualStyleBackColor = true;
            buttonSpectrumGenerationRun.Click += buttonSpectrumGenerationRun_Click;
            // 
            // numericUpDownEndmembers_Resolution
            // 
            numericUpDownEndmembers_Resolution.Location = new Point(102, 69);
            numericUpDownEndmembers_Resolution.Name = "numericUpDownEndmembers_Resolution";
            numericUpDownEndmembers_Resolution.Size = new Size(42, 23);
            numericUpDownEndmembers_Resolution.TabIndex = 19;
            numericUpDownEndmembers_Resolution.Value = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDownEndmembers_Resolution.ValueChanged += numericUpDownSolidSolution_Resolution_ValueChanged;
            numericUpDownEndmembers_Resolution.Enter += numericUpDownSolidSolution_Resolution_ValueChanged;
            // 
            // labelEndmembers_Resolution
            // 
            labelEndmembers_Resolution.AutoSize = true;
            labelEndmembers_Resolution.Location = new Point(9, 71);
            labelEndmembers_Resolution.Margin = new Padding(2, 0, 2, 0);
            labelEndmembers_Resolution.Name = "labelEndmembers_Resolution";
            labelEndmembers_Resolution.Size = new Size(91, 15);
            labelEndmembers_Resolution.TabIndex = 20;
            labelEndmembers_Resolution.Text = "化学組成分解能";
            // 
            // groupBoxMineralInfo
            // 
            groupBoxMineralInfo.Controls.Add(groupBox2);
            groupBoxMineralInfo.Controls.Add(textBoxMineral_Memo);
            groupBoxMineralInfo.Controls.Add(label15);
            groupBoxMineralInfo.Controls.Add(labelMineralInfo_Name);
            groupBoxMineralInfo.Controls.Add(textBoxMineral_Name);
            groupBoxMineralInfo.Controls.Add(groupBoxEndmembers);
            groupBoxMineralInfo.Dock = DockStyle.Bottom;
            groupBoxMineralInfo.Location = new Point(3, 175);
            groupBoxMineralInfo.Name = "groupBoxMineralInfo";
            groupBoxMineralInfo.Size = new Size(634, 242);
            groupBoxMineralInfo.TabIndex = 37;
            groupBoxMineralInfo.TabStop = false;
            groupBoxMineralInfo.Text = "詳細情報";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(textBoxEndmembers_Constraints);
            groupBox2.Controls.Add(labelEndmembers_Resolution);
            groupBox2.Controls.Add(labelEndmembers_Constraints);
            groupBox2.Controls.Add(numericUpDownEndmembers_Resolution);
            groupBox2.Controls.Add(labelEndmembers_CompositionLists);
            groupBox2.Controls.Add(numericUpDownMineral_Target);
            groupBox2.Controls.Add(textBoxEndmembers_CompositionLists);
            groupBox2.Controls.Add(labelMineral_Target);
            groupBox2.Location = new Point(310, 50);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(317, 181);
            groupBox2.TabIndex = 103;
            groupBox2.TabStop = false;
            groupBox2.Text = "サンプリング条件";
            // 
            // textBoxEndmembers_Constraints
            // 
            textBoxEndmembers_Constraints.Location = new Point(65, 17);
            textBoxEndmembers_Constraints.Multiline = true;
            textBoxEndmembers_Constraints.Name = "textBoxEndmembers_Constraints";
            textBoxEndmembers_Constraints.ScrollBars = ScrollBars.Both;
            textBoxEndmembers_Constraints.Size = new Size(230, 41);
            textBoxEndmembers_Constraints.TabIndex = 93;
            // 
            // labelEndmembers_Constraints
            // 
            labelEndmembers_Constraints.AutoSize = true;
            labelEndmembers_Constraints.Location = new Point(15, 20);
            labelEndmembers_Constraints.Name = "labelEndmembers_Constraints";
            labelEndmembers_Constraints.Size = new Size(43, 15);
            labelEndmembers_Constraints.TabIndex = 94;
            labelEndmembers_Constraints.Text = "条件式";
            // 
            // labelEndmembers_CompositionLists
            // 
            labelEndmembers_CompositionLists.AutoSize = true;
            labelEndmembers_CompositionLists.Location = new Point(6, 103);
            labelEndmembers_CompositionLists.Name = "labelEndmembers_CompositionLists";
            labelEndmembers_CompositionLists.Size = new Size(80, 15);
            labelEndmembers_CompositionLists.TabIndex = 96;
            labelEndmembers_CompositionLists.Text = "化学組成リスト";
            // 
            // numericUpDownMineral_Target
            // 
            numericUpDownMineral_Target.Location = new Point(256, 71);
            numericUpDownMineral_Target.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            numericUpDownMineral_Target.Name = "numericUpDownMineral_Target";
            numericUpDownMineral_Target.Size = new Size(50, 23);
            numericUpDownMineral_Target.TabIndex = 101;
            numericUpDownMineral_Target.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            numericUpDownMineral_Target.ValueChanged += numericUpDownMineral_Target_ValueChanged;
            // 
            // textBoxEndmembers_CompositionLists
            // 
            textBoxEndmembers_CompositionLists.Location = new Point(17, 122);
            textBoxEndmembers_CompositionLists.Multiline = true;
            textBoxEndmembers_CompositionLists.Name = "textBoxEndmembers_CompositionLists";
            textBoxEndmembers_CompositionLists.ScrollBars = ScrollBars.Both;
            textBoxEndmembers_CompositionLists.Size = new Size(288, 51);
            textBoxEndmembers_CompositionLists.TabIndex = 95;
            // 
            // labelMineral_Target
            // 
            labelMineral_Target.AutoSize = true;
            labelMineral_Target.Location = new Point(164, 71);
            labelMineral_Target.Margin = new Padding(2, 0, 2, 0);
            labelMineral_Target.Name = "labelMineral_Target";
            labelMineral_Target.Size = new Size(87, 15);
            labelMineral_Target.TabIndex = 102;
            labelMineral_Target.Text = "シミュレーション数";
            // 
            // textBoxMineral_Memo
            // 
            textBoxMineral_Memo.Location = new Point(299, 22);
            textBoxMineral_Memo.Name = "textBoxMineral_Memo";
            textBoxMineral_Memo.Size = new Size(120, 23);
            textBoxMineral_Memo.TabIndex = 94;
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(253, 24);
            label15.Name = "label15";
            label15.Size = new Size(40, 15);
            label15.TabIndex = 93;
            label15.Text = "memo";
            // 
            // labelMineralInfo_Name
            // 
            labelMineralInfo_Name.AutoSize = true;
            labelMineralInfo_Name.Location = new Point(9, 24);
            labelMineralInfo_Name.Name = "labelMineralInfo_Name";
            labelMineralInfo_Name.Size = new Size(107, 15);
            labelMineralInfo_Name.TabIndex = 92;
            labelMineralInfo_Name.Text = "鉱物/鉱物グループ名";
            // 
            // textBoxMineral_Name
            // 
            textBoxMineral_Name.Location = new Point(122, 21);
            textBoxMineral_Name.Name = "textBoxMineral_Name";
            textBoxMineral_Name.Size = new Size(120, 23);
            textBoxMineral_Name.TabIndex = 91;
            // 
            // groupBoxEndmembers
            // 
            groupBoxEndmembers.Controls.Add(panelEndmembers);
            groupBoxEndmembers.Location = new Point(5, 50);
            groupBoxEndmembers.Margin = new Padding(1);
            groupBoxEndmembers.Name = "groupBoxEndmembers";
            groupBoxEndmembers.Padding = new Padding(1);
            groupBoxEndmembers.Size = new Size(301, 150);
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
            buttonAddList.Location = new Point(445, 3);
            buttonAddList.Name = "buttonAddList";
            buttonAddList.Size = new Size(75, 25);
            buttonAddList.TabIndex = 96;
            buttonAddList.Text = "リストに追加";
            buttonAddList.UseVisualStyleBackColor = true;
            buttonAddList.Click += buttonAddList_Click;
            // 
            // buttonUpdateList
            // 
            buttonUpdateList.Location = new Point(526, 3);
            buttonUpdateList.Name = "buttonUpdateList";
            buttonUpdateList.Size = new Size(105, 24);
            buttonUpdateList.TabIndex = 99;
            buttonUpdateList.Text = "リストを更新";
            buttonUpdateList.UseVisualStyleBackColor = true;
            buttonUpdateList.Click += buttonUpdate_Click;
            // 
            // labelCompositionCount
            // 
            labelCompositionCount.AutoSize = true;
            labelCompositionCount.Location = new Point(199, 675);
            labelCompositionCount.Margin = new Padding(2, 0, 2, 0);
            labelCompositionCount.Name = "labelCompositionCount";
            labelCompositionCount.Size = new Size(99, 15);
            labelCompositionCount.TabIndex = 91;
            labelCompositionCount.Text = "総シミュレーション数";
            // 
            // textBoxCompositionCount
            // 
            textBoxCompositionCount.BackColor = Color.White;
            textBoxCompositionCount.Location = new Point(327, 654);
            textBoxCompositionCount.Margin = new Padding(3, 2, 3, 2);
            textBoxCompositionCount.Multiline = true;
            textBoxCompositionCount.Name = "textBoxCompositionCount";
            textBoxCompositionCount.ReadOnly = true;
            textBoxCompositionCount.ScrollBars = ScrollBars.Both;
            textBoxCompositionCount.Size = new Size(147, 59);
            textBoxCompositionCount.TabIndex = 92;
            // 
            // checkedListBoxMineral
            // 
            checkedListBoxMineral.Dock = DockStyle.Fill;
            checkedListBoxMineral.FormattingEnabled = true;
            checkedListBoxMineral.HorizontalScrollbar = true;
            checkedListBoxMineral.Location = new Point(3, 48);
            checkedListBoxMineral.MultiColumn = true;
            checkedListBoxMineral.Name = "checkedListBoxMineral";
            checkedListBoxMineral.ScrollAlwaysVisible = true;
            checkedListBoxMineral.Size = new Size(634, 96);
            checkedListBoxMineral.TabIndex = 86;
            checkedListBoxMineral.ItemCheck += checkedListBoxMineral_ItemCheck;
            checkedListBoxMineral.SelectedIndexChanged += checkedListBoxMineral_SelectedIndexChanged;
            // 
            // groupBoxComposition
            // 
            groupBoxComposition.Controls.Add(labelCompositionName);
            groupBoxComposition.Location = new Point(10, 44);
            groupBoxComposition.Name = "groupBoxComposition";
            groupBoxComposition.Size = new Size(0, 0);
            groupBoxComposition.TabIndex = 41;
            groupBoxComposition.TabStop = false;
            groupBoxComposition.Text = "化学組成条件設定";
            // 
            // labelCompositionName
            // 
            labelCompositionName.AutoSize = true;
            labelCompositionName.Location = new Point(27, 69);
            labelCompositionName.Name = "labelCompositionName";
            labelCompositionName.Size = new Size(31, 15);
            labelCompositionName.TabIndex = 42;
            labelCompositionName.Text = "成分";
            // 
            // ModelTrain
            // 
            ModelTrain.Controls.Add(groupBox4);
            ModelTrain.Controls.Add(groupBoxModel_LearningData);
            ModelTrain.Controls.Add(groupBoxComposition);
            ModelTrain.Location = new Point(664, 27);
            ModelTrain.Name = "ModelTrain";
            ModelTrain.Size = new Size(540, 666);
            ModelTrain.TabIndex = 87;
            ModelTrain.TabStop = false;
            ModelTrain.Text = "モデル作成";
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(numericBoxModel_EarlyStopping);
            groupBox4.Controls.Add(numericBoxModel_BatchSize);
            groupBox4.Controls.Add(numericBoxModel_Epochs);
            groupBox4.Controls.Add(buttonAllSelect);
            groupBox4.Controls.Add(checkedListBoxTrainMinerals);
            groupBox4.Controls.Add(textBoxModel_Evaluation);
            groupBox4.Controls.Add(buttonModel_Train);
            groupBox4.Controls.Add(label5);
            groupBox4.Location = new Point(13, 136);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(520, 524);
            groupBox4.TabIndex = 43;
            groupBox4.TabStop = false;
            groupBox4.Text = "モデル訓練";
            // 
            // numericBoxModel_EarlyStopping
            // 
            numericBoxModel_EarlyStopping.BackColor = Color.Transparent;
            numericBoxModel_EarlyStopping.DecimalPlaces = 0;
            numericBoxModel_EarlyStopping.Font = new Font("Yu Gothic UI", 9F);
            numericBoxModel_EarlyStopping.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_EarlyStopping.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_EarlyStopping.HeaderText = "待機回数";
            numericBoxModel_EarlyStopping.Location = new Point(48, 170);
            numericBoxModel_EarlyStopping.Margin = new Padding(0);
            numericBoxModel_EarlyStopping.MaximumSize = new Size(1000, 28);
            numericBoxModel_EarlyStopping.Minimum = 0D;
            numericBoxModel_EarlyStopping.MinimumSize = new Size(1, 18);
            numericBoxModel_EarlyStopping.Name = "numericBoxModel_EarlyStopping";
            numericBoxModel_EarlyStopping.RadianValue = 0.17453292519943295D;
            numericBoxModel_EarlyStopping.ShowUpDown = true;
            numericBoxModel_EarlyStopping.Size = new Size(144, 26);
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
            numericBoxModel_BatchSize.Location = new Point(229, 121);
            numericBoxModel_BatchSize.Margin = new Padding(0);
            numericBoxModel_BatchSize.MaximumSize = new Size(1000, 28);
            numericBoxModel_BatchSize.Minimum = 1D;
            numericBoxModel_BatchSize.MinimumSize = new Size(1, 18);
            numericBoxModel_BatchSize.Name = "numericBoxModel_BatchSize";
            numericBoxModel_BatchSize.RadianValue = 0.27925268031909273D;
            numericBoxModel_BatchSize.ShowUpDown = true;
            numericBoxModel_BatchSize.Size = new Size(144, 26);
            numericBoxModel_BatchSize.SmartIncrement = true;
            numericBoxModel_BatchSize.TabIndex = 105;
            numericBoxModel_BatchSize.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_BatchSize.Value = 16D;
            // 
            // numericBoxModel_Epochs
            // 
            numericBoxModel_Epochs.BackColor = Color.Transparent;
            numericBoxModel_Epochs.DecimalPlaces = 0;
            numericBoxModel_Epochs.Font = new Font("Yu Gothic UI", 9F);
            numericBoxModel_Epochs.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_Epochs.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_Epochs.HeaderText = "エポック数";
            numericBoxModel_Epochs.Location = new Point(48, 121);
            numericBoxModel_Epochs.Margin = new Padding(0);
            numericBoxModel_Epochs.Maximum = 100000D;
            numericBoxModel_Epochs.MaximumSize = new Size(1000, 28);
            numericBoxModel_Epochs.Minimum = 1D;
            numericBoxModel_Epochs.MinimumSize = new Size(1, 18);
            numericBoxModel_Epochs.Name = "numericBoxModel_Epochs";
            numericBoxModel_Epochs.RadianValue = 8.7266462599716466D;
            numericBoxModel_Epochs.ShowUpDown = true;
            numericBoxModel_Epochs.Size = new Size(144, 26);
            numericBoxModel_Epochs.SmartIncrement = true;
            numericBoxModel_Epochs.TabIndex = 104;
            numericBoxModel_Epochs.TextFont = new Font("Yu Gothic UI", 9F);
            numericBoxModel_Epochs.Value = 500D;
            // 
            // buttonAllSelect
            // 
            buttonAllSelect.Font = new Font("Yu Gothic UI", 8F);
            buttonAllSelect.Location = new Point(422, 118);
            buttonAllSelect.Name = "buttonAllSelect";
            buttonAllSelect.Size = new Size(85, 23);
            buttonAllSelect.TabIndex = 103;
            buttonAllSelect.Text = "全選択/解除";
            buttonAllSelect.UseVisualStyleBackColor = true;
            buttonAllSelect.Click += buttonAllSelect_Click;
            // 
            // checkedListBoxTrainMinerals
            // 
            checkedListBoxTrainMinerals.FormattingEnabled = true;
            checkedListBoxTrainMinerals.HorizontalScrollbar = true;
            checkedListBoxTrainMinerals.Location = new Point(19, 22);
            checkedListBoxTrainMinerals.MultiColumn = true;
            checkedListBoxTrainMinerals.Name = "checkedListBoxTrainMinerals";
            checkedListBoxTrainMinerals.ScrollAlwaysVisible = true;
            checkedListBoxTrainMinerals.Size = new Size(488, 94);
            checkedListBoxTrainMinerals.TabIndex = 101;
            // 
            // textBoxModel_Evaluation
            // 
            textBoxModel_Evaluation.Location = new Point(27, 247);
            textBoxModel_Evaluation.Multiline = true;
            textBoxModel_Evaluation.Name = "textBoxModel_Evaluation";
            textBoxModel_Evaluation.ScrollBars = ScrollBars.Both;
            textBoxModel_Evaluation.Size = new Size(480, 221);
            textBoxModel_Evaluation.TabIndex = 0;
            // 
            // buttonModel_Train
            // 
            buttonModel_Train.Location = new Point(439, 478);
            buttonModel_Train.Name = "buttonModel_Train";
            buttonModel_Train.Size = new Size(68, 25);
            buttonModel_Train.TabIndex = 0;
            buttonModel_Train.Text = "訓練開始";
            buttonModel_Train.UseVisualStyleBackColor = true;
            buttonModel_Train.Click += buttonModel_Train_Click;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(216, 219);
            label5.Margin = new Padding(2, 0, 2, 0);
            label5.Name = "label5";
            label5.Size = new Size(49, 15);
            label5.TabIndex = 21;
            label5.Text = "訓練ログ";
            // 
            // groupBoxModel_LearningData
            // 
            groupBoxModel_LearningData.Controls.Add(numericUpDownModel_ValidationSplit);
            groupBoxModel_LearningData.Controls.Add(label13);
            groupBoxModel_LearningData.Location = new Point(13, 31);
            groupBoxModel_LearningData.Name = "groupBoxModel_LearningData";
            groupBoxModel_LearningData.Size = new Size(510, 97);
            groupBoxModel_LearningData.TabIndex = 42;
            groupBoxModel_LearningData.TabStop = false;
            groupBoxModel_LearningData.Text = "学習データ設定";
            // 
            // numericUpDownModel_ValidationSplit
            // 
            numericUpDownModel_ValidationSplit.Location = new Point(161, 68);
            numericUpDownModel_ValidationSplit.Name = "numericUpDownModel_ValidationSplit";
            numericUpDownModel_ValidationSplit.Size = new Size(42, 23);
            numericUpDownModel_ValidationSplit.TabIndex = 93;
            numericUpDownModel_ValidationSplit.Value = new decimal(new int[] { 20, 0, 0, 0 });
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(19, 74);
            label13.Margin = new Padding(2, 0, 2, 0);
            label13.Name = "label13";
            label13.Size = new Size(111, 15);
            label13.TabIndex = 30;
            label13.Text = "テストデータの割合(%)";
            // 
            // numericBoxProbeCurrent
            // 
            numericBoxProbeCurrent.BackColor = Color.Transparent;
            numericBoxProbeCurrent.DecimalPlaces = 2;
            numericBoxProbeCurrent.Font = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxProbeCurrent.HeaderText = "照射電流(nA)";
            numericBoxProbeCurrent.Location = new Point(216, 40);
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
            numericBoxLiveTime.Location = new Point(370, 14);
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
            // buttonModel_SaveFolder
            // 
            buttonModel_SaveFolder.AutoSize = true;
            buttonModel_SaveFolder.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonModel_SaveFolder.Location = new Point(275, 5);
            buttonModel_SaveFolder.Name = "buttonModel_SaveFolder";
            buttonModel_SaveFolder.Size = new Size(26, 25);
            buttonModel_SaveFolder.TabIndex = 1;
            buttonModel_SaveFolder.Text = "...";
            buttonModel_SaveFolder.UseVisualStyleBackColor = true;
            buttonModel_SaveFolder.Click += buttonFolderBrowserDialog_Click;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(12, 10);
            label7.Name = "label7";
            label7.Size = new Size(81, 15);
            label7.TabIndex = 24;
            label7.Text = "モデルの保存先";
            // 
            // textBoxModelPath
            // 
            textBoxModelPath.Location = new Point(115, 10);
            textBoxModelPath.Name = "textBoxModelPath";
            textBoxModelPath.Size = new Size(155, 23);
            textBoxModelPath.TabIndex = 23;
            // 
            // labelPathTeacher
            // 
            labelPathTeacher.AutoSize = true;
            labelPathTeacher.Location = new Point(23, 10);
            labelPathTeacher.Name = "labelPathTeacher";
            labelPathTeacher.Size = new Size(57, 15);
            labelPathTeacher.TabIndex = 2;
            labelPathTeacher.Text = "教師データ";
            // 
            // buttonModel_Teacher
            // 
            buttonModel_Teacher.AutoSize = true;
            buttonModel_Teacher.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonModel_Teacher.Location = new Point(275, 5);
            buttonModel_Teacher.Name = "buttonModel_Teacher";
            buttonModel_Teacher.Size = new Size(26, 25);
            buttonModel_Teacher.TabIndex = 1;
            buttonModel_Teacher.Text = "...";
            buttonModel_Teacher.UseVisualStyleBackColor = true;
            buttonModel_Teacher.Click += buttonModel_Teacher_Click;
            // 
            // textBoxPathTeacher
            // 
            textBoxPathTeacher.Location = new Point(115, 5);
            textBoxPathTeacher.Name = "textBoxPathTeacher";
            textBoxPathTeacher.Size = new Size(155, 23);
            textBoxPathTeacher.TabIndex = 1;
            // 
            // buttonDelete
            // 
            buttonDelete.AutoSize = true;
            buttonDelete.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonDelete.Font = new Font("Yu Gothic UI", 8F);
            buttonDelete.Location = new Point(536, 3);
            buttonDelete.Name = "buttonDelete";
            buttonDelete.Size = new Size(39, 23);
            buttonDelete.TabIndex = 97;
            buttonDelete.Text = "削除";
            buttonDelete.UseVisualStyleBackColor = true;
            buttonDelete.Click += buttonMineral_Delete_Click;
            // 
            // SpectrumGeneration
            // 
            SpectrumGeneration.Controls.Add(groupBox1);
            SpectrumGeneration.Controls.Add(groupBoxMineral);
            SpectrumGeneration.Controls.Add(tabControl2);
            SpectrumGeneration.Controls.Add(buttonSpectrumGenerationRun);
            SpectrumGeneration.Controls.Add(labelCompositionCount);
            SpectrumGeneration.Controls.Add(textBoxCompositionCount);
            SpectrumGeneration.Dock = DockStyle.Left;
            SpectrumGeneration.Location = new Point(3, 3);
            SpectrumGeneration.Name = "SpectrumGeneration";
            SpectrumGeneration.Size = new Size(661, 734);
            SpectrumGeneration.TabIndex = 88;
            SpectrumGeneration.TabStop = false;
            SpectrumGeneration.Text = "EDXスペクトル生成";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(numericBoxCarbonThickness);
            groupBox1.Controls.Add(numericBoxLiveTime);
            groupBox1.Controls.Add(numericBoxProbeCurrent);
            groupBox1.Controls.Add(groupBox5);
            groupBox1.Controls.Add(numericBoxBeamEnergy);
            groupBox1.Controls.Add(labelDetectorName);
            groupBox1.Controls.Add(textBoxDetectorName);
            groupBox1.Location = new Point(5, 156);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(640, 74);
            groupBox1.TabIndex = 99;
            groupBox1.TabStop = false;
            groupBox1.Text = "SEM-EDX条件";
            // 
            // numericBoxCarbonThickness
            // 
            numericBoxCarbonThickness.BackColor = Color.Transparent;
            numericBoxCarbonThickness.DecimalPlaces = 3;
            numericBoxCarbonThickness.Font = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.FooterFont = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.HeaderFont = new Font("Yu Gothic UI", 9F);
            numericBoxCarbonThickness.HeaderText = "カーボン蒸着の厚さ(nm)";
            numericBoxCarbonThickness.Location = new Point(9, 39);
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
            numericBoxBeamEnergy.Location = new Point(216, 14);
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
            // labelDetectorName
            // 
            labelDetectorName.Location = new Point(9, 20);
            labelDetectorName.Name = "labelDetectorName";
            labelDetectorName.Size = new Size(60, 15);
            labelDetectorName.TabIndex = 8;
            labelDetectorName.Text = "検出器名";
            // 
            // textBoxDetectorName
            // 
            textBoxDetectorName.Location = new Point(135, 12);
            textBoxDetectorName.Margin = new Padding(3, 2, 3, 2);
            textBoxDetectorName.Name = "textBoxDetectorName";
            textBoxDetectorName.Size = new Size(50, 23);
            textBoxDetectorName.TabIndex = 3;
            textBoxDetectorName.Text = "test";
            // 
            // groupBoxMineral
            // 
            groupBoxMineral.Controls.Add(checkedListBoxMineral);
            groupBoxMineral.Controls.Add(flowLayoutPanel4);
            groupBoxMineral.Controls.Add(flowLayoutPanel2);
            groupBoxMineral.Controls.Add(groupBoxMineralInfo);
            groupBoxMineral.Location = new Point(5, 232);
            groupBoxMineral.Name = "groupBoxMineral";
            groupBoxMineral.Size = new Size(640, 420);
            groupBoxMineral.TabIndex = 98;
            groupBoxMineral.TabStop = false;
            groupBoxMineral.Text = "計算対象";
            // 
            // flowLayoutPanel4
            // 
            flowLayoutPanel4.AutoSize = true;
            flowLayoutPanel4.Controls.Add(buttonUpdateList);
            flowLayoutPanel4.Controls.Add(buttonAddList);
            flowLayoutPanel4.Dock = DockStyle.Bottom;
            flowLayoutPanel4.FlowDirection = FlowDirection.RightToLeft;
            flowLayoutPanel4.Location = new Point(3, 144);
            flowLayoutPanel4.Name = "flowLayoutPanel4";
            flowLayoutPanel4.Size = new Size(634, 31);
            flowLayoutPanel4.TabIndex = 105;
            // 
            // flowLayoutPanel2
            // 
            flowLayoutPanel2.AutoSize = true;
            flowLayoutPanel2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanel2.Controls.Add(buttonAllDelete);
            flowLayoutPanel2.Controls.Add(buttonDelete);
            flowLayoutPanel2.Controls.Add(buttonReset);
            flowLayoutPanel2.Controls.Add(checkBox1);
            flowLayoutPanel2.Dock = DockStyle.Top;
            flowLayoutPanel2.FlowDirection = FlowDirection.RightToLeft;
            flowLayoutPanel2.Location = new Point(3, 19);
            flowLayoutPanel2.Name = "flowLayoutPanel2";
            flowLayoutPanel2.Size = new Size(634, 29);
            flowLayoutPanel2.TabIndex = 103;
            // 
            // buttonAllDelete
            // 
            buttonAllDelete.AutoSize = true;
            buttonAllDelete.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonAllDelete.Font = new Font("Yu Gothic UI", 8F);
            buttonAllDelete.Location = new Point(581, 3);
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
            buttonReset.Location = new Point(480, 3);
            buttonReset.Name = "buttonReset";
            buttonReset.Size = new Size(50, 23);
            buttonReset.TabIndex = 99;
            buttonReset.Text = "初期化";
            buttonReset.UseVisualStyleBackColor = true;
            buttonReset.Click += buttonMineral_Reset_Click;
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(367, 3);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(107, 19);
            checkBox1.TabIndex = 100;
            checkBox1.Text = "詳細情報を表示";
            checkBox1.UseVisualStyleBackColor = true;
            // 
            // tabControl2
            // 
            tabControl2.Controls.Add(tabPage5);
            tabControl2.Controls.Add(tabPage7);
            tabControl2.Location = new Point(3, 24);
            tabControl2.Name = "tabControl2";
            tabControl2.SelectedIndex = 0;
            tabControl2.Size = new Size(649, 131);
            tabControl2.TabIndex = 95;
            // 
            // tabPage5
            // 
            tabPage5.Controls.Add(flowLayoutPanel1);
            tabPage5.Location = new Point(4, 24);
            tabPage5.Name = "tabPage5";
            tabPage5.Padding = new Padding(3);
            tabPage5.Size = new Size(641, 103);
            tabPage5.TabIndex = 0;
            tabPage5.Text = "ファイル設定";
            tabPage5.UseVisualStyleBackColor = true;
            // 
            // flowLayoutPanel1
            // 
            flowLayoutPanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanel1.Controls.Add(panel2);
            flowLayoutPanel1.Controls.Add(panel4);
            flowLayoutPanel1.Controls.Add(panelPathEDX);
            flowLayoutPanel1.Controls.Add(panelPathDTSA);
            flowLayoutPanel1.Dock = DockStyle.Top;
            flowLayoutPanel1.FlowDirection = FlowDirection.TopDown;
            flowLayoutPanel1.Location = new Point(3, 3);
            flowLayoutPanel1.Name = "flowLayoutPanel1";
            flowLayoutPanel1.Size = new Size(635, 94);
            flowLayoutPanel1.TabIndex = 20;
            // 
            // panel2
            // 
            panel2.AutoSize = true;
            panel2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel2.Controls.Add(label7);
            panel2.Controls.Add(textBoxModelPath);
            panel2.Controls.Add(buttonModel_SaveFolder);
            panel2.Location = new Point(3, 3);
            panel2.Name = "panel2";
            panel2.Size = new Size(304, 36);
            panel2.TabIndex = 19;
            // 
            // panel4
            // 
            panel4.AutoSize = true;
            panel4.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel4.Controls.Add(buttonModel_Teacher);
            panel4.Controls.Add(textBoxPathTeacher);
            panel4.Controls.Add(labelPathTeacher);
            panel4.Location = new Point(3, 45);
            panel4.Name = "panel4";
            panel4.Size = new Size(304, 33);
            panel4.TabIndex = 18;
            // 
            // panelPathEDX
            // 
            panelPathEDX.AutoSize = true;
            panelPathEDX.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelPathEDX.Controls.Add(labelPathEDX);
            panelPathEDX.Controls.Add(textBoxPathEDX);
            panelPathEDX.Controls.Add(buttonPathDTSA);
            panelPathEDX.Location = new Point(313, 3);
            panelPathEDX.Name = "panelPathEDX";
            panelPathEDX.Size = new Size(304, 33);
            panelPathEDX.TabIndex = 17;
            // 
            // labelPathEDX
            // 
            labelPathEDX.AutoSize = true;
            labelPathEDX.Location = new Point(5, 10);
            labelPathEDX.Name = "labelPathEDX";
            labelPathEDX.Size = new Size(110, 15);
            labelPathEDX.TabIndex = 15;
            labelPathEDX.Text = "EDXスペクトル出力先";
            // 
            // textBoxPathEDX
            // 
            textBoxPathEDX.Location = new Point(115, 5);
            textBoxPathEDX.Name = "textBoxPathEDX";
            textBoxPathEDX.Size = new Size(155, 23);
            textBoxPathEDX.TabIndex = 0;
            // 
            // buttonPathDTSA
            // 
            buttonPathDTSA.AutoSize = true;
            buttonPathDTSA.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonPathDTSA.Location = new Point(275, 5);
            buttonPathDTSA.Name = "buttonPathDTSA";
            buttonPathDTSA.Size = new Size(26, 25);
            buttonPathDTSA.TabIndex = 1;
            buttonPathDTSA.Text = "...";
            buttonPathDTSA.UseVisualStyleBackColor = true;
            buttonPathDTSA.Click += buttonFolderBrowserDialog_Click;
            // 
            // panelPathDTSA
            // 
            panelPathDTSA.AutoSize = true;
            panelPathDTSA.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelPathDTSA.Controls.Add(buttonPathEDX);
            panelPathDTSA.Controls.Add(textBoxPathDTSA);
            panelPathDTSA.Controls.Add(labelPathDTSA);
            panelPathDTSA.Location = new Point(313, 42);
            panelPathDTSA.Name = "panelPathDTSA";
            panelPathDTSA.Size = new Size(304, 33);
            panelPathDTSA.TabIndex = 16;
            // 
            // buttonPathEDX
            // 
            buttonPathEDX.AutoSize = true;
            buttonPathEDX.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonPathEDX.Location = new Point(275, 5);
            buttonPathEDX.Name = "buttonPathEDX";
            buttonPathEDX.Size = new Size(26, 25);
            buttonPathEDX.TabIndex = 1;
            buttonPathEDX.Text = "...";
            buttonPathEDX.UseVisualStyleBackColor = true;
            buttonPathEDX.Click += buttonFolderBrowserDialog_Click;
            // 
            // textBoxPathDTSA
            // 
            textBoxPathDTSA.Location = new Point(115, 5);
            textBoxPathDTSA.Name = "textBoxPathDTSA";
            textBoxPathDTSA.Size = new Size(155, 23);
            textBoxPathDTSA.TabIndex = 0;
            textBoxPathDTSA.Text = "C:\\\\Users\\\\mineral\\\\AppData\\\\Local\\\\NIST\\\\NIST DTSA-II Oberon 2026-01-07";
            // 
            // labelPathDTSA
            // 
            labelPathDTSA.AutoSize = true;
            labelPathDTSA.Location = new Point(5, 10);
            labelPathDTSA.Name = "labelPathDTSA";
            labelPathDTSA.Size = new Size(105, 15);
            labelPathDTSA.TabIndex = 15;
            labelPathDTSA.Text = "DTSA-Ⅱファイルパス";
            // 
            // tabPage7
            // 
            tabPage7.Controls.Add(numericUpDownExecution_Count);
            tabPage7.Controls.Add(labelExecution_Count);
            tabPage7.Controls.Add(numericUpDownExecution_Parallel);
            tabPage7.Controls.Add(labelExecution_Parallel);
            tabPage7.Location = new Point(4, 24);
            tabPage7.Name = "tabPage7";
            tabPage7.Padding = new Padding(3);
            tabPage7.Size = new Size(641, 103);
            tabPage7.TabIndex = 2;
            tabPage7.Text = "シミュレーション実行設定";
            tabPage7.UseVisualStyleBackColor = true;
            // 
            // numericUpDownExecution_Count
            // 
            numericUpDownExecution_Count.Location = new Point(100, 10);
            numericUpDownExecution_Count.Margin = new Padding(3, 2, 3, 2);
            numericUpDownExecution_Count.Name = "numericUpDownExecution_Count";
            numericUpDownExecution_Count.Size = new Size(40, 23);
            numericUpDownExecution_Count.TabIndex = 18;
            numericUpDownExecution_Count.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // labelExecution_Count
            // 
            labelExecution_Count.AutoEllipsis = true;
            labelExecution_Count.Location = new Point(20, 10);
            labelExecution_Count.Name = "labelExecution_Count";
            labelExecution_Count.Size = new Size(35, 15);
            labelExecution_Count.TabIndex = 17;
            labelExecution_Count.Text = "回数";
            // 
            // numericUpDownExecution_Parallel
            // 
            numericUpDownExecution_Parallel.Location = new Point(100, 39);
            numericUpDownExecution_Parallel.Margin = new Padding(3, 2, 3, 2);
            numericUpDownExecution_Parallel.Name = "numericUpDownExecution_Parallel";
            numericUpDownExecution_Parallel.Size = new Size(40, 23);
            numericUpDownExecution_Parallel.TabIndex = 34;
            numericUpDownExecution_Parallel.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // labelExecution_Parallel
            // 
            labelExecution_Parallel.Location = new Point(20, 40);
            labelExecution_Parallel.Name = "labelExecution_Parallel";
            labelExecution_Parallel.Size = new Size(66, 15);
            labelExecution_Parallel.TabIndex = 35;
            labelExecution_Parallel.Text = "並列数";
            // 
            // GeneratorForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1197, 740);
            Controls.Add(ModelTrain);
            Controls.Add(SpectrumGeneration);
            Margin = new Padding(3, 2, 3, 2);
            Name = "GeneratorForm";
            Padding = new Padding(3);
            ((System.ComponentModel.ISupportInitialize)numericUpDownEndmembers_Resolution).EndInit();
            groupBoxMineralInfo.ResumeLayout(false);
            groupBoxMineralInfo.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDownMineral_Target).EndInit();
            groupBoxEndmembers.ResumeLayout(false);
            groupBoxEndmembers.PerformLayout();
            panelEndmembers.ResumeLayout(false);
            panelEndmembers.PerformLayout();
            flowLayoutPanel3.ResumeLayout(false);
            groupBoxComposition.ResumeLayout(false);
            groupBoxComposition.PerformLayout();
            ModelTrain.ResumeLayout(false);
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            groupBoxModel_LearningData.ResumeLayout(false);
            groupBoxModel_LearningData.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDownModel_ValidationSplit).EndInit();
            SpectrumGeneration.ResumeLayout(false);
            SpectrumGeneration.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            groupBoxMineral.ResumeLayout(false);
            groupBoxMineral.PerformLayout();
            flowLayoutPanel4.ResumeLayout(false);
            flowLayoutPanel4.PerformLayout();
            flowLayoutPanel2.ResumeLayout(false);
            flowLayoutPanel2.PerformLayout();
            tabControl2.ResumeLayout(false);
            tabPage5.ResumeLayout(false);
            flowLayoutPanel1.ResumeLayout(false);
            flowLayoutPanel1.PerformLayout();
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            panel4.ResumeLayout(false);
            panel4.PerformLayout();
            panelPathEDX.ResumeLayout(false);
            panelPathEDX.PerformLayout();
            panelPathDTSA.ResumeLayout(false);
            panelPathDTSA.PerformLayout();
            tabPage7.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)numericUpDownExecution_Count).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownExecution_Parallel).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private Button buttonSpectrumGenerationRun;
        private TextBox textBoxDTSALog;
        private NumericUpDown numericUpDownEndmembers_Resolution;
        private Label labelEndmembers_Resolution;
        private GroupBox groupBoxMineralInfo;
        private GroupBox groupBoxComposition;
        private Label labelCompositionName;
        private Label labelEndmember_Name;
        private Label labelEndmember_Formula;
        private EndmemberControl EndmemberControl2;
        private EndmemberControl EndmemberControl1;
        private Label labelProgress;
        private GroupBox groupBoxEndmembers;
        private CheckedListBox checkedListBoxMineral;
        private Label labelCompositionCount;
        private TextBox textBoxCompositionCount;
        private TextBox textBoxEndmembers_Constraints;
        private Label labelEndmembers_Constraints;
        private GroupBox ModelTrain;
        private GroupBox SpectrumGeneration;
        private GroupBox groupBox5;
        private Label label2;
        private Label labelEndmembers_CompositionLists;
        private TextBox textBoxEndmembers_CompositionLists;
        private Button buttonAddList;
        private Button buttonDelete;
        private GroupBox groupBoxMineral;
        private Label labelMineralInfo_Name;
        private TextBox textBoxMineral_Name;
        private Label labelDetectorName;
        private TextBox textBoxDetectorName;
        private GroupBox groupBoxModel_LearningData;
        private GroupBox groupBox4;
        private Label labelPathTeacher;
        private TextBox textBoxPathTeacher;
        private TextBox textBoxModel_Evaluation;
        private Button buttonModel_Save;
        private Label label5;
        private Button buttonModel_Train;
        private TextBox textBoxModelPath;
        private Label label7;
        private Label label13;
        // 260416Codex: モデル訓練設定は NumericBox 群に置き換えます。
        private NumericUpDown numericUpDownModel_ValidationSplit;
        private Button buttonEndmember_Add;
        private Button buttonEndmember_Delete;
        private Button buttonUpdateList;
        private Button buttonAllDelete;
        private TextBox textBoxMineral_Memo;
        private Label label15;
        private Button buttonModel_SaveFolder;
        private Button buttonModel_Teacher;
        private Button buttonReset;
        private Label labelMineral_Target;
        private NumericUpDown numericUpDownMineral_Target;
        private CheckedListBox checkedListBoxTrainMinerals;
        private Button buttonAllSelect;
        private ProgressBar progressBar1;
        private FlowLayoutPanel flowLayoutPanel2;
        private FlowLayoutPanel flowLayoutPanel3;
        private Panel panelEndmembers;
        private GroupBox groupBox1;
        private CheckBox checkBox1;
        private GroupBox groupBox2;
        private FlowLayoutPanel flowLayoutPanel4;
        private Crystallography.Controls.NumericBox numericBoxModel_Epochs;
        private Crystallography.Controls.NumericBox numericBoxModel_BatchSize;
        private Crystallography.Controls.NumericBox numericBoxModel_EarlyStopping;
        private TabControl tabControl2;
        private TabPage tabPage5;
        private Button buttonPathDTSA;
        private Button buttonPathEDX;
        private TextBox textBoxPathDTSA;
        private TextBox textBoxPathEDX;
        private Label labelPathDTSA;
        private Label labelPathEDX;
        private TabPage tabPage7;
        private NumericUpDown numericUpDownExecution_Count;
        private Label labelExecution_Count;
        private NumericUpDown numericUpDownExecution_Parallel;
        private Label labelExecution_Parallel;
        private Panel panelPathEDX;
        private Panel panelPathDTSA;
        private Panel panel4;
        private Panel panel2;
        private FlowLayoutPanel flowLayoutPanel1;
        private Crystallography.Controls.NumericBox numericBoxProbeCurrent;
        private Crystallography.Controls.NumericBox numericBox3;
        private Crystallography.Controls.NumericBox numericBoxLiveTime;
        private Crystallography.Controls.NumericBox numericBoxBeamEnergy;
        private Crystallography.Controls.NumericBox numericBoxCarbonThickness;
    }
}

