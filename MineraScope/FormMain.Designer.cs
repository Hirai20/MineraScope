namespace MineraScope
{
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
            exportToolStripMenuItem = new ToolStripMenuItem();
            comboBoxModelPath = new ComboBox();
            graphControl1 = new Crystallography.Controls.GraphControl();
            panel2 = new Panel();
            label7 = new Label();
            textBoxlPathSaveModel = new TextBox();
            buttonPathSaveMode = new Button();
            textBoxPathEDX = new TextBox();
            panelPathEDX = new Panel();
            buttonPathEDX = new Button();
            labelPathEDX = new Label();
            buttonPathDTSA = new Button();
            panelPathDTSA = new Panel();
            textBoxPathDTSA = new TextBox();
            labelPathDTSA = new Label();
            textBoxAnalysisResult = new TextBox();
            panel1 = new Panel();
            comboBoxSpectrumFile = new ComboBox();
            panel3 = new Panel();
            menuStrip1.SuspendLayout();
            panel2.SuspendLayout();
            panelPathEDX.SuspendLayout();
            panelPathDTSA.SuspendLayout();
            panel1.SuspendLayout();
            panel3.SuspendLayout();
            SuspendLayout();
            //
            // buttonOpenGenerator
            //
            buttonOpenGenerator.AutoSize = true;
            buttonOpenGenerator.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonOpenGenerator.Location = new Point(118, 3);
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
            buttonOpenAnalyzer.Location = new Point(230, 3);
            buttonOpenAnalyzer.Name = "buttonOpenAnalyzer";
            buttonOpenAnalyzer.Size = new Size(67, 25);
            buttonOpenAnalyzer.TabIndex = 0;
            buttonOpenAnalyzer.Text = "マップ分析";
            buttonOpenAnalyzer.UseVisualStyleBackColor = true;
            buttonOpenAnalyzer.Click += buttonOpenAnalyzer_Click;
            //
            // menuStrip1
            //
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(481, 24);
            menuStrip1.TabIndex = 1;
            menuStrip1.Text = "menuStrip1";
            //
            // fileToolStripMenuItem
            //
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { folderSettingToolStripMenuItem, exportToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(50, 20);
            fileToolStripMenuItem.Text = "Menu";
            //
            // folderSettingToolStripMenuItem
            //
            folderSettingToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { dTSAIIFileToolStripMenuItem });
            folderSettingToolStripMenuItem.Name = "folderSettingToolStripMenuItem";
            folderSettingToolStripMenuItem.Size = new Size(180, 22);
            folderSettingToolStripMenuItem.Text = "Folder setting";
            //
            // dTSAIIFileToolStripMenuItem
            //
            dTSAIIFileToolStripMenuItem.Name = "dTSAIIFileToolStripMenuItem";
            dTSAIIFileToolStripMenuItem.Size = new Size(155, 22);
            dTSAIIFileToolStripMenuItem.Text = "DTSA-IIFilePath";
            //
            // exportToolStripMenuItem
            //
            exportToolStripMenuItem.Name = "exportToolStripMenuItem";
            exportToolStripMenuItem.Size = new Size(180, 22);
            exportToolStripMenuItem.Text = "Export";
            exportToolStripMenuItem.Click += exportToolStripMenuItem_Click;
            //
            // comboBoxModelPath
            //
            comboBoxModelPath.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxModelPath.FormattingEnabled = true;
            comboBoxModelPath.Location = new Point(3, 42);
            comboBoxModelPath.Name = "comboBoxModelPath";
            comboBoxModelPath.Size = new Size(148, 23);
            comboBoxModelPath.TabIndex = 26;
            comboBoxModelPath.SelectedIndexChanged += comboBoxModelPath_SelectedIndexChanged;
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
            graphControl1.Dock = DockStyle.Fill;
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
            graphControl1.Location = new Point(0, 0);
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
            graphControl1.Padding = new Padding(0, 3, 0, 3);
            graphControl1.Size = new Size(481, 171);
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
            panel2.Controls.Add(textBoxlPathSaveModel);
            panel2.Controls.Add(buttonPathSaveMode);
            panel2.Location = new Point(171, 34);
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
            // textBoxlPathSaveModel
            // 
            textBoxlPathSaveModel.Location = new Point(115, 10);
            textBoxlPathSaveModel.Name = "textBoxlPathSaveModel";
            textBoxlPathSaveModel.Size = new Size(155, 23);
            textBoxlPathSaveModel.TabIndex = 23;
            textBoxlPathSaveModel.TextChanged += textBoxlPathSaveModel_TextChanged;
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
            buttonPathSaveMode.Click += buttonFilePathBrowse_Click;
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
            panelPathEDX.Controls.Add(buttonPathEDX);
            panelPathEDX.Controls.Add(labelPathEDX);
            panelPathEDX.Controls.Add(textBoxPathEDX);
            panelPathEDX.Location = new Point(171, 117);
            panelPathEDX.Name = "panelPathEDX";
            panelPathEDX.Size = new Size(304, 31);
            panelPathEDX.TabIndex = 110;
            //
            // buttonPathEDX
            //
            buttonPathEDX.AutoSize = true;
            buttonPathEDX.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonPathEDX.Location = new Point(275, 3);
            buttonPathEDX.Name = "buttonPathEDX";
            buttonPathEDX.Size = new Size(26, 25);
            buttonPathEDX.TabIndex = 1;
            buttonPathEDX.Text = "...";
            buttonPathEDX.UseVisualStyleBackColor = true;
            buttonPathEDX.Click += buttonFilePathBrowse_Click;
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
            buttonPathDTSA.Location = new Point(275, 3);
            buttonPathDTSA.Name = "buttonPathDTSA";
            buttonPathDTSA.Size = new Size(26, 25);
            buttonPathDTSA.TabIndex = 1;
            buttonPathDTSA.Text = "...";
            buttonPathDTSA.UseVisualStyleBackColor = true;
            buttonPathDTSA.Click += buttonFilePathBrowse_Click;
            // 
            // panelPathDTSA
            // 
            panelPathDTSA.AutoSize = true;
            panelPathDTSA.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelPathDTSA.Controls.Add(textBoxPathDTSA);
            panelPathDTSA.Controls.Add(labelPathDTSA);
            panelPathDTSA.Controls.Add(buttonPathDTSA);
            panelPathDTSA.Location = new Point(171, 80);
            panelPathDTSA.Name = "panelPathDTSA";
            panelPathDTSA.Size = new Size(304, 31);
            panelPathDTSA.TabIndex = 109;
            // 
            // textBoxPathDTSA
            // 
            textBoxPathDTSA.Location = new Point(115, 5);
            textBoxPathDTSA.Name = "textBoxPathDTSA";
            textBoxPathDTSA.Size = new Size(155, 23);
            textBoxPathDTSA.TabIndex = 0;
            textBoxPathDTSA.Text = "C:\\Users\\mineral\\AppData\\Local\\NIST\\NIST DTSA-II Oberon 2026-01-07";
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
            textBoxAnalysisResult.Dock = DockStyle.Bottom;
            textBoxAnalysisResult.Location = new Point(0, 354);
            textBoxAnalysisResult.Multiline = true;
            textBoxAnalysisResult.Name = "textBoxAnalysisResult";
            textBoxAnalysisResult.ScrollBars = ScrollBars.Both;
            textBoxAnalysisResult.Size = new Size(481, 106);
            textBoxAnalysisResult.TabIndex = 112;
            textBoxAnalysisResult.Text = ".msa / .emsa / .eds ファイルをファイルドラッグ＆ドロップしてください。";
            //
            // panel1
            //
            panel1.Controls.Add(comboBoxSpectrumFile);
            panel1.Controls.Add(buttonOpenGenerator);
            panel1.Controls.Add(panelPathEDX);
            panel1.Controls.Add(panel2);
            panel1.Controls.Add(panelPathDTSA);
            panel1.Controls.Add(comboBoxModelPath);
            panel1.Controls.Add(buttonOpenAnalyzer);
            panel1.Dock = DockStyle.Top;
            panel1.Location = new Point(0, 24);
            panel1.Name = "panel1";
            panel1.Size = new Size(481, 159);
            panel1.TabIndex = 114;
            //
            // comboBoxSpectrumFile
            //
            comboBoxSpectrumFile.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxSpectrumFile.FormattingEnabled = true;
            comboBoxSpectrumFile.Location = new Point(3, 80);
            comboBoxSpectrumFile.Name = "comboBoxSpectrumFile";
            comboBoxSpectrumFile.Size = new Size(148, 23);
            comboBoxSpectrumFile.TabIndex = 114;
            comboBoxSpectrumFile.SelectedIndexChanged += comboBoxSpectrumFile_SelectedIndexChanged;
            //
            // panel3
            //
            panel3.Controls.Add(graphControl1);
            panel3.Dock = DockStyle.Fill;
            panel3.Location = new Point(0, 183);
            panel3.Name = "panel3";
            panel3.Size = new Size(481, 171);
            panel3.TabIndex = 115;
            //
            // FormMain
            //
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(481, 460);
            Controls.Add(panel3);
            Controls.Add(panel1);
            Controls.Add(textBoxAnalysisResult);
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
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            panel3.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button buttonOpenGenerator;
        private Button buttonOpenAnalyzer;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem folderSettingToolStripMenuItem;
        private ComboBox comboBoxModelPath;
        private Crystallography.Controls.GraphControl graphControl1;
        private Panel panel2;
        private Label label7;
        private TextBox textBoxlPathSaveModel;
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
        private Panel panel1;
        private Panel panel3;
        private ComboBox comboBoxSpectrumFile;
        private ToolStripMenuItem exportToolStripMenuItem;
    }
}
