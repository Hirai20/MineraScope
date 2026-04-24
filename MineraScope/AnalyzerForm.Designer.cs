namespace MineraScope
{
    partial class AnalyzerForm
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
            groupBoxMineralAnalysis = new GroupBox();
            groupBoxSpectrumFiles = new GroupBox();
            panel1 = new Panel();
            listBoxSpectrumFiles = new ListBox();
            flowLayoutPanel4 = new FlowLayoutPanel();
            buttonAnalyze = new Button();
            buttonRemoveSpectrumFiles = new Button();
            groupBoxAnalysisResult = new GroupBox();
            textBoxAnalysisResult = new TextBox();
            groupBoxModelFolder = new GroupBox();
            comboBox1 = new ComboBox();
            flowLayoutPanel1 = new FlowLayoutPanel();
            labelModelFolder = new Label();
            textBoxModelFolder = new TextBox();
            buttonSelectModelFolder = new Button();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            groupBoxMineralAnalysis.SuspendLayout();
            groupBoxSpectrumFiles.SuspendLayout();
            panel1.SuspendLayout();
            flowLayoutPanel4.SuspendLayout();
            groupBoxAnalysisResult.SuspendLayout();
            groupBoxModelFolder.SuspendLayout();
            flowLayoutPanel1.SuspendLayout();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // groupBoxMineralAnalysis
            // 
            groupBoxMineralAnalysis.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            groupBoxMineralAnalysis.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            groupBoxMineralAnalysis.Controls.Add(groupBoxSpectrumFiles);
            groupBoxMineralAnalysis.Controls.Add(groupBoxAnalysisResult);
            groupBoxMineralAnalysis.Controls.Add(groupBoxModelFolder);
            groupBoxMineralAnalysis.Location = new Point(12, 29);
            groupBoxMineralAnalysis.Name = "groupBoxMineralAnalysis";
            groupBoxMineralAnalysis.Size = new Size(539, 653);
            groupBoxMineralAnalysis.TabIndex = 88;
            groupBoxMineralAnalysis.TabStop = false;
            groupBoxMineralAnalysis.Text = "鉱物・成分判定";
            // 
            // groupBoxSpectrumFiles
            // 
            groupBoxSpectrumFiles.Controls.Add(panel1);
            groupBoxSpectrumFiles.Location = new Point(16, 165);
            groupBoxSpectrumFiles.Name = "groupBoxSpectrumFiles";
            groupBoxSpectrumFiles.Size = new Size(506, 192);
            groupBoxSpectrumFiles.TabIndex = 43;
            groupBoxSpectrumFiles.TabStop = false;
            groupBoxSpectrumFiles.Text = "EDXスペクトルデータ(msaファイル）";
            // 
            // panel1
            // 
            panel1.Controls.Add(listBoxSpectrumFiles);
            panel1.Controls.Add(flowLayoutPanel4);
            panel1.Location = new Point(9, 22);
            panel1.Name = "panel1";
            panel1.Size = new Size(483, 148);
            panel1.TabIndex = 44;
            // 
            // listBoxSpectrumFiles
            // 
            listBoxSpectrumFiles.AllowDrop = true;
            listBoxSpectrumFiles.Dock = DockStyle.Top;
            listBoxSpectrumFiles.FormattingEnabled = true;
            listBoxSpectrumFiles.Location = new Point(0, 0);
            listBoxSpectrumFiles.Name = "listBoxSpectrumFiles";
            listBoxSpectrumFiles.Size = new Size(483, 124);
            listBoxSpectrumFiles.TabIndex = 0;
            listBoxSpectrumFiles.UseWaitCursor = true;
            // 
            // flowLayoutPanel4
            // 
            flowLayoutPanel4.AutoScroll = true;
            flowLayoutPanel4.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanel4.Controls.Add(buttonAnalyze);
            flowLayoutPanel4.Controls.Add(buttonRemoveSpectrumFiles);
            flowLayoutPanel4.Dock = DockStyle.Bottom;
            flowLayoutPanel4.FlowDirection = FlowDirection.RightToLeft;
            flowLayoutPanel4.Location = new Point(0, 122);
            flowLayoutPanel4.Name = "flowLayoutPanel4";
            flowLayoutPanel4.Size = new Size(483, 26);
            flowLayoutPanel4.TabIndex = 45;
            // 
            // buttonAnalyze
            // 
            buttonAnalyze.AutoSize = true;
            buttonAnalyze.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonAnalyze.Location = new Point(398, 2);
            buttonAnalyze.Margin = new Padding(3, 2, 3, 2);
            buttonAnalyze.Name = "buttonAnalyze";
            buttonAnalyze.Size = new Size(65, 25);
            buttonAnalyze.TabIndex = 1;
            buttonAnalyze.Text = "判定開始";
            buttonAnalyze.UseVisualStyleBackColor = true;
            // 
            // buttonRemoveSpectrumFiles
            // 
            buttonRemoveSpectrumFiles.AutoSize = true;
            buttonRemoveSpectrumFiles.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonRemoveSpectrumFiles.Font = new Font("Yu Gothic UI", 8F);
            buttonRemoveSpectrumFiles.Location = new Point(353, 3);
            buttonRemoveSpectrumFiles.Name = "buttonRemoveSpectrumFiles";
            buttonRemoveSpectrumFiles.Size = new Size(39, 23);
            buttonRemoveSpectrumFiles.TabIndex = 98;
            buttonRemoveSpectrumFiles.Text = "削除";
            buttonRemoveSpectrumFiles.UseVisualStyleBackColor = true;
            // 
            // groupBoxAnalysisResult
            // 
            groupBoxAnalysisResult.Controls.Add(textBoxAnalysisResult);
            groupBoxAnalysisResult.Dock = DockStyle.Bottom;
            groupBoxAnalysisResult.Location = new Point(3, 360);
            groupBoxAnalysisResult.Name = "groupBoxAnalysisResult";
            groupBoxAnalysisResult.Size = new Size(533, 290);
            groupBoxAnalysisResult.TabIndex = 43;
            groupBoxAnalysisResult.TabStop = false;
            groupBoxAnalysisResult.Text = "判定結果";
            // 
            // textBoxAnalysisResult
            // 
            textBoxAnalysisResult.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBoxAnalysisResult.Location = new Point(6, 27);
            textBoxAnalysisResult.Multiline = true;
            textBoxAnalysisResult.Name = "textBoxAnalysisResult";
            textBoxAnalysisResult.ScrollBars = ScrollBars.Both;
            textBoxAnalysisResult.Size = new Size(513, 234);
            textBoxAnalysisResult.TabIndex = 23;
            // 
            // groupBoxModelFolder
            // 
            groupBoxModelFolder.Controls.Add(comboBox1);
            groupBoxModelFolder.Controls.Add(flowLayoutPanel1);
            groupBoxModelFolder.Location = new Point(16, 22);
            groupBoxModelFolder.Name = "groupBoxModelFolder";
            groupBoxModelFolder.Size = new Size(506, 119);
            groupBoxModelFolder.TabIndex = 42;
            groupBoxModelFolder.TabStop = false;
            groupBoxModelFolder.Text = "モデル選択";
            groupBoxModelFolder.VisibleChanged += groupBoxModelFolder_VisibleChanged;
            // 
            // comboBox1
            // 
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox1.FormattingEnabled = true;
            comboBox1.Location = new Point(249, 87);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(121, 23);
            comboBox1.TabIndex = 25;
            // 
            // flowLayoutPanel1
            // 
            flowLayoutPanel1.AutoScroll = true;
            flowLayoutPanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanel1.Controls.Add(labelModelFolder);
            flowLayoutPanel1.Controls.Add(textBoxModelFolder);
            flowLayoutPanel1.Controls.Add(buttonSelectModelFolder);
            flowLayoutPanel1.Location = new Point(9, 35);
            flowLayoutPanel1.Name = "flowLayoutPanel1";
            flowLayoutPanel1.Size = new Size(429, 34);
            flowLayoutPanel1.TabIndex = 24;
            // 
            // labelModelFolder
            // 
            labelModelFolder.AutoSize = true;
            labelModelFolder.Location = new Point(3, 0);
            labelModelFolder.Name = "labelModelFolder";
            labelModelFolder.Size = new Size(70, 15);
            labelModelFolder.TabIndex = 24;
            labelModelFolder.Text = "モデルフォルダ";
            // 
            // textBoxModelFolder
            // 
            textBoxModelFolder.Location = new Point(79, 3);
            textBoxModelFolder.Name = "textBoxModelFolder";
            textBoxModelFolder.Size = new Size(286, 23);
            textBoxModelFolder.TabIndex = 23;
            // 
            // buttonSelectModelFolder
            // 
            buttonSelectModelFolder.AutoSize = true;
            buttonSelectModelFolder.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonSelectModelFolder.Location = new Point(371, 3);
            buttonSelectModelFolder.Name = "buttonSelectModelFolder";
            buttonSelectModelFolder.Size = new Size(26, 25);
            buttonSelectModelFolder.TabIndex = 1;
            buttonSelectModelFolder.Text = "...";
            buttonSelectModelFolder.UseVisualStyleBackColor = true;
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(569, 24);
            menuStrip1.TabIndex = 89;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "File";
            // 
            // AnalyzerForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(569, 694);
            Controls.Add(groupBoxMineralAnalysis);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "AnalyzerForm";
            Text = "AnalyzerForm";
            FormClosing += AnalyzerForm_FormClosing;
            groupBoxMineralAnalysis.ResumeLayout(false);
            groupBoxSpectrumFiles.ResumeLayout(false);
            panel1.ResumeLayout(false);
            flowLayoutPanel4.ResumeLayout(false);
            flowLayoutPanel4.PerformLayout();
            groupBoxAnalysisResult.ResumeLayout(false);
            groupBoxAnalysisResult.PerformLayout();
            groupBoxModelFolder.ResumeLayout(false);
            flowLayoutPanel1.ResumeLayout(false);
            flowLayoutPanel1.PerformLayout();
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox groupBoxMineralAnalysis;
        // 260416Codex: フィールド宣言を現在のデザイナー名に合わせます。
        private GroupBox groupBoxSpectrumFiles;
        private GroupBox groupBoxAnalysisResult;
        private TextBox textBoxAnalysisResult;
        private GroupBox groupBoxModelFolder;
        private TextBox textBoxModelFolder;
        private Button buttonSelectModelFolder;
        private FlowLayoutPanel flowLayoutPanel1;
        private Label labelModelFolder;
        private ListBox listBoxSpectrumFiles;
        private FlowLayoutPanel flowLayoutPanel4;
        private Button buttonAnalyze;
        private Button buttonRemoveSpectrumFiles;
        private Panel panel1;
        private ComboBox comboBox1;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
    }
}
