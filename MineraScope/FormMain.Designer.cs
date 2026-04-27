namespace MineraScope
{
    // 260416Codex: Designer 側の partial class 名も FormMain に合わせます。
    partial class FormMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            buttonOpenGenerator = new Button();
            buttonOpenAnalyzer = new Button();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            folderSettingToolStripMenuItem = new ToolStripMenuItem();
            dTSAIIFileToolStripMenuItem = new ToolStripMenuItem();
            comboBoxModelPath = new ComboBox();
            graphControl1 = new Crystallography.Controls.GraphControl();
            panel2 = new Panel();
            label7 = new Label();
            textBoxlPathSaveMode = new TextBox();
            buttonPathSaveMode = new Button();
            textBoxPathEDX = new TextBox();
            panelPathEDX = new Panel();
            labelPathEDX = new Label();
            buttonPathDTSA = new Button();
            panelPathDTSA = new Panel();
            buttonPathEDX = new Button();
            textBoxPathDTSA = new TextBox();
            labelPathDTSA = new Label();
            textBoxAnalysisResult = new TextBox();
            textBoxSpectrumFile = new TextBox();
            menuStrip1.SuspendLayout();
            panel2.SuspendLayout();
            panelPathEDX.SuspendLayout();
            panelPathDTSA.SuspendLayout();
            SuspendLayout();
            // 
            // buttonOpenGenerator
            // 
            buttonOpenGenerator.AutoSize = true;
            buttonOpenGenerator.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonOpenGenerator.Location = new Point(111, 27);
            buttonOpenGenerator.Name = "buttonOpenGenerator";
            buttonOpenGenerator.Size = new Size(93, 25);
            buttonOpenGenerator.TabIndex = 0;
            buttonOpenGenerator.Text = "新規モデル作成";
            buttonOpenGenerator.UseVisualStyleBackColor = true;
            buttonOpenGenerator.Click += buttonOpenGenerator_Click;
            // 
            // buttonOpenAnalyzer
            // 
            buttonOpenAnalyzer.AutoSize = true;
            buttonOpenAnalyzer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonOpenAnalyzer.Location = new Point(201, 27);
            buttonOpenAnalyzer.Name = "buttonOpenAnalyzer";
            buttonOpenAnalyzer.Size = new Size(102, 25);
            buttonOpenAnalyzer.TabIndex = 0;
            buttonOpenAnalyzer.Text = "既存モデルを使用";
            buttonOpenAnalyzer.UseVisualStyleBackColor = true;
            buttonOpenAnalyzer.Click += buttonOpenAnalyzer_Click;
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(475, 24);
            menuStrip1.TabIndex = 1;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { folderSettingToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "File";
            // 
            // folderSettingToolStripMenuItem
            // 
            folderSettingToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { dTSAIIFileToolStripMenuItem });
            folderSettingToolStripMenuItem.Name = "folderSettingToolStripMenuItem";
            folderSettingToolStripMenuItem.Size = new Size(146, 22);
            folderSettingToolStripMenuItem.Text = "Folder setting";
            // 
            // dTSAIIFileToolStripMenuItem
            // 
            dTSAIIFileToolStripMenuItem.Name = "dTSAIIFileToolStripMenuItem";
            dTSAIIFileToolStripMenuItem.Size = new Size(155, 22);
            dTSAIIFileToolStripMenuItem.Text = "DTSA-IIFilePath";
            // 
            // comboBoxModelPath
            // 
            comboBoxModelPath.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxModelPath.FormattingEnabled = true;
            comboBoxModelPath.Location = new Point(12, 68);
            comboBoxModelPath.Name = "comboBoxModelPath";
            comboBoxModelPath.Size = new Size(148, 23);
            comboBoxModelPath.TabIndex = 26;
            // 
            // graphControl1
            // 
            graphControl1.AllowMouseOperation = true;
            graphControl1.AxisLineColor = Color.Gray;
            graphControl1.AxisTextColor = Color.Black;
            graphControl1.AxisTextFont = new Font("Segoe UI", 9F);
            graphControl1.AxisXTextVisible = true;
            graphControl1.AxisYTextVisible = true;
            graphControl1.BackgroundColor = Color.White;
            graphControl1.BottomMargin = 0D;
            graphControl1.DivisionLineColor = Color.LightGray;
            graphControl1.DivisionLineXVisible = true;
            graphControl1.DivisionLineYVisible = true;
            graphControl1.FixRangeHorizontal = false;
            graphControl1.FixRangeVertical = false;
            graphControl1.Font = new Font("Segoe UI Symbol", 9F);
            graphControl1.GraphTitle = "";
            graphControl1.IsIntegerX = false;
            graphControl1.IsIntegerY = false;
            graphControl1.LabelX = "X:";
            graphControl1.LabelY = "Y:";
            graphControl1.LeftMargin = 0F;
            graphControl1.LineWidth = 1F;
            graphControl1.Location = new Point(171, 178);
            graphControl1.LowerX = 0D;
            graphControl1.LowerY = 0D;
            graphControl1.MaximalX = 1D;
            graphControl1.MaximalY = 1D;
            graphControl1.MinimalX = 0D;
            graphControl1.MinimalY = 0D;
            graphControl1.Mode = Crystallography.Controls.GraphControl.DrawingMode.Line;
            graphControl1.MousePositionVisible = true;
            graphControl1.MousePositionXDigit = -1;
            graphControl1.MousePositionYDigit = -1;
            graphControl1.Name = "graphControl1";
            graphControl1.OriginPosition = new Point(40, 20);
            graphControl1.Size = new Size(292, 163);
            graphControl1.TabIndex = 108;
            graphControl1.UnitX = "";
            graphControl1.UnitY = "";
            graphControl1.UpperPanelFont = new Font("Segoe UI Symbol", 9F);
            graphControl1.UpperPanelVisible = true;
            graphControl1.UpperX = 1D;
            graphControl1.UpperY = 1D;
            graphControl1.UseLineWidth = true;
            graphControl1.VerticalLineColor = Color.Red;
            graphControl1.XLog = false;
            graphControl1.YLog = false;
            // 
            // panel2
            // 
            panel2.AutoSize = true;
            panel2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel2.Controls.Add(label7);
            panel2.Controls.Add(textBoxlPathSaveMode);
            panel2.Controls.Add(buttonPathSaveMode);
            panel2.Location = new Point(171, 58);
            panel2.Name = "panel2";
            panel2.Size = new Size(304, 36);
            panel2.TabIndex = 111;
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
            // textBoxlPathSaveMode
            // 
            textBoxlPathSaveMode.Location = new Point(115, 10);
            textBoxlPathSaveMode.Name = "textBoxlPathSaveMode";
            textBoxlPathSaveMode.Size = new Size(155, 23);
            textBoxlPathSaveMode.TabIndex = 23;
            // 
            // buttonPathSaveMode
            // 
            buttonPathSaveMode.AutoSize = true;
            buttonPathSaveMode.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonPathSaveMode.Location = new Point(275, 5);
            buttonPathSaveMode.Name = "buttonPathSaveMode";
            buttonPathSaveMode.Size = new Size(26, 25);
            buttonPathSaveMode.TabIndex = 1;
            buttonPathSaveMode.Text = "...";
            buttonPathSaveMode.UseVisualStyleBackColor = true;
            // 
            // textBoxPathEDX
            // 
            textBoxPathEDX.Location = new Point(115, 5);
            textBoxPathEDX.Name = "textBoxPathEDX";
            textBoxPathEDX.Size = new Size(155, 23);
            textBoxPathEDX.TabIndex = 0;
            // 
            // panelPathEDX
            // 
            panelPathEDX.AutoSize = true;
            panelPathEDX.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelPathEDX.Controls.Add(labelPathEDX);
            panelPathEDX.Controls.Add(textBoxPathEDX);
            panelPathEDX.Controls.Add(buttonPathDTSA);
            panelPathEDX.Location = new Point(171, 139);
            panelPathEDX.Name = "panelPathEDX";
            panelPathEDX.Size = new Size(304, 33);
            panelPathEDX.TabIndex = 110;
            // 
            // labelPathEDX
            // 
            labelPathEDX.AutoSize = true;
            labelPathEDX.Location = new Point(5, 10);
            labelPathEDX.Name = "labelPathEDX";
            labelPathEDX.Size = new Size(83, 15);
            labelPathEDX.TabIndex = 15;
            labelPathEDX.Text = "学習/EDXデータ";
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
            // 
            // panelPathDTSA
            // 
            panelPathDTSA.AutoSize = true;
            panelPathDTSA.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelPathDTSA.Controls.Add(buttonPathEDX);
            panelPathDTSA.Controls.Add(textBoxPathDTSA);
            panelPathDTSA.Controls.Add(labelPathDTSA);
            panelPathDTSA.Location = new Point(171, 100);
            panelPathDTSA.Name = "panelPathDTSA";
            panelPathDTSA.Size = new Size(304, 33);
            panelPathDTSA.TabIndex = 109;
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
            // textBoxAnalysisResult
            // 
            textBoxAnalysisResult.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBoxAnalysisResult.Location = new Point(26, 347);
            textBoxAnalysisResult.Multiline = true;
            textBoxAnalysisResult.Name = "textBoxAnalysisResult";
            textBoxAnalysisResult.ScrollBars = ScrollBars.Both;
            textBoxAnalysisResult.Size = new Size(426, 106);
            textBoxAnalysisResult.TabIndex = 112;
            // 260427Codex: 初期表示でドロップ可能なスペクトル形式を案内します。
            textBoxAnalysisResult.Text = ".msa または .emsa ファイルをファイルドラッグ＆ドロップしてください。";
            // 
            // textBoxSpectrumFile
            // 
            textBoxSpectrumFile.Location = new Point(12, 110);
            textBoxSpectrumFile.Name = "textBoxSpectrumFile";
            textBoxSpectrumFile.Size = new Size(148, 23);
            textBoxSpectrumFile.TabIndex = 113;
            // 
            // FormMain
            //
            // 260427Codex: フォームの余白部分にもスペクトルファイルをドロップできるようにします。
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(475, 465);
            Controls.Add(textBoxSpectrumFile);
            Controls.Add(textBoxAnalysisResult);
            Controls.Add(panel2);
            Controls.Add(panelPathEDX);
            Controls.Add(panelPathDTSA);
            Controls.Add(graphControl1);
            Controls.Add(comboBoxModelPath);
            Controls.Add(buttonOpenAnalyzer);
            Controls.Add(buttonOpenGenerator);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "FormMain";
            Text = "FormMain";
            Load += FormMain_Load;
            DragDrop += FormMain_DragDrop;
            DragEnter += FormMain_DragEnter;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            panelPathEDX.ResumeLayout(false);
            panelPathEDX.PerformLayout();
            panelPathDTSA.ResumeLayout(false);
            panelPathDTSA.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button buttonOpenGenerator;
        private Button buttonOpenAnalyzer;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem folderSettingToolStripMenuItem;
        // 260416Codex: InitializeComponent で使用している名前にフィールド宣言を揃えます。
        private ComboBox comboBoxModelPath;
        private Crystallography.Controls.GraphControl graphControl1;
        private Panel panel2;
        private Label label7;
        private TextBox textBoxlPathSaveMode;
        private Button buttonPathSaveMode;
        private TextBox textBoxPathEDX;
        private Panel panelPathEDX;
        private Label labelPathEDX;
        private Button buttonPathDTSA;
        private Panel panelPathDTSA;
        private Button buttonPathEDX;
        private TextBox textBoxPathDTSA;
        private Label labelPathDTSA;
        private ToolStripMenuItem dTSAIIFileToolStripMenuItem;
        private TextBox textBoxAnalysisResult;
        private TextBox textBoxSpectrumFile;
    }
}
