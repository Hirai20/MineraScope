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
            scalablePictureBoxSEM = new Crystallography.Controls.ScalablePictureBox();
            flowLayoutPanelBinning = new FlowLayoutPanel();
            labelBinning = new Label();
            comboBoxBinning = new ComboBox();
            textBox1 = new TextBox();
            button1 = new Button();
            graphControl1 = new Crystallography.Controls.GraphControl();
            flowLayoutPanelModellFolder = new FlowLayoutPanel();
            labelModelFolder = new Label();
            comboBoxMappingModellFolder = new ComboBox();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            groupBoxMineralAnalysis.SuspendLayout();
            flowLayoutPanelBinning.SuspendLayout();
            flowLayoutPanelModellFolder.SuspendLayout();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // groupBoxMineralAnalysis
            // 
            groupBoxMineralAnalysis.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            groupBoxMineralAnalysis.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            groupBoxMineralAnalysis.Controls.Add(scalablePictureBoxSEM);
            groupBoxMineralAnalysis.Controls.Add(flowLayoutPanelBinning);
            groupBoxMineralAnalysis.Controls.Add(textBox1);
            groupBoxMineralAnalysis.Controls.Add(button1);
            groupBoxMineralAnalysis.Controls.Add(graphControl1);
            groupBoxMineralAnalysis.Controls.Add(flowLayoutPanelModellFolder);
            groupBoxMineralAnalysis.Location = new Point(12, 29);
            groupBoxMineralAnalysis.Name = "groupBoxMineralAnalysis";
            groupBoxMineralAnalysis.Size = new Size(669, 653);
            groupBoxMineralAnalysis.TabIndex = 88;
            groupBoxMineralAnalysis.TabStop = false;
            groupBoxMineralAnalysis.Text = "マッピング分析";
            // 
            // scalablePictureBoxSEM
            // 
            scalablePictureBoxSEM.AllowDrop = true;
            scalablePictureBoxSEM.BackColor = SystemColors.ActiveCaption;
            scalablePictureBoxSEM.FixZoomAndCenter = false;
            scalablePictureBoxSEM.FocusEventEnabled = false;
            scalablePictureBoxSEM.HorizontalFlip = false;
            scalablePictureBoxSEM.Location = new Point(16, 93);
            scalablePictureBoxSEM.ManualSpotMode = false;
            scalablePictureBoxSEM.Margin = new Padding(0);
            scalablePictureBoxSEM.MouseScaling = true;
            scalablePictureBoxSEM.MouseTranslation = true;
            scalablePictureBoxSEM.Name = "scalablePictureBoxSEM";
            scalablePictureBoxSEM.ShowAreaRectangle = false;
            scalablePictureBoxSEM.ShowRimRentangle = false;
            scalablePictureBoxSEM.Size = new Size(332, 217);
            scalablePictureBoxSEM.TabIndex = 31;
            scalablePictureBoxSEM.TitleVisible = false;
            scalablePictureBoxSEM.VerticalFlip = false;
            scalablePictureBoxSEM.Zoom = 128D;
            scalablePictureBoxSEM.MouseUp2 += scalablePictureBoxSEM_MouseUp2;
            scalablePictureBoxSEM.MouseDown2 += scalablePictureBoxSEM_MouseDown2;
            scalablePictureBoxSEM.DragDrop += AnalyzerForm_DragDrop;
            scalablePictureBoxSEM.DragEnter += AnalyzerForm_DragEnter;
            // 
            // flowLayoutPanelBinning
            // 
            flowLayoutPanelBinning.AutoScroll = true;
            flowLayoutPanelBinning.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanelBinning.Controls.Add(labelBinning);
            flowLayoutPanelBinning.Controls.Add(comboBoxBinning);
            flowLayoutPanelBinning.Location = new Point(246, 35);
            flowLayoutPanelBinning.Name = "flowLayoutPanelBinning";
            flowLayoutPanelBinning.Size = new Size(207, 35);
            flowLayoutPanelBinning.TabIndex = 29;
            // 
            // labelBinning
            // 
            labelBinning.AutoSize = true;
            labelBinning.Location = new Point(3, 0);
            labelBinning.Name = "labelBinning";
            labelBinning.Size = new Size(41, 15);
            labelBinning.TabIndex = 24;
            labelBinning.Text = "ビニング";
            // 
            // comboBoxBinning
            // 
            comboBoxBinning.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxBinning.FormattingEnabled = true;
            comboBoxBinning.Location = new Point(50, 3);
            comboBoxBinning.Name = "comboBoxBinning";
            comboBoxBinning.Size = new Size(121, 23);
            comboBoxBinning.TabIndex = 25;
            // 
            // textBox1
            // 
            textBox1.Location = new Point(16, 364);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(527, 226);
            textBox1.TabIndex = 28;
            // 
            // button1
            // 
            button1.Location = new Point(491, 29);
            button1.Name = "button1";
            button1.Size = new Size(101, 26);
            button1.TabIndex = 27;
            button1.Text = "button1";
            button1.UseVisualStyleBackColor = true;
            // 
            // graphControl1
            // 
            graphControl1.AllowMouseOperation = true;
            graphControl1.AutoScroll = true;
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
            graphControl1.Location = new Point(372, 93);
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
            graphControl1.Size = new Size(273, 217);
            graphControl1.TabIndex = 26;
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
            // flowLayoutPanelModellFolder
            // 
            flowLayoutPanelModellFolder.AutoScroll = true;
            flowLayoutPanelModellFolder.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanelModellFolder.Controls.Add(labelModelFolder);
            flowLayoutPanelModellFolder.Controls.Add(comboBoxMappingModellFolder);
            flowLayoutPanelModellFolder.Location = new Point(16, 35);
            flowLayoutPanelModellFolder.Name = "flowLayoutPanelModellFolder";
            flowLayoutPanelModellFolder.Size = new Size(207, 35);
            flowLayoutPanelModellFolder.TabIndex = 24;
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
            // comboBoxMappingModellFolder
            // 
            comboBoxMappingModellFolder.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxMappingModellFolder.FormattingEnabled = true;
            comboBoxMappingModellFolder.Location = new Point(79, 3);
            comboBoxMappingModellFolder.Name = "comboBoxMappingModellFolder";
            comboBoxMappingModellFolder.Size = new Size(121, 23);
            comboBoxMappingModellFolder.TabIndex = 25;
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(711, 24);
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
            AutoScroll = true;
            ClientSize = new Size(711, 694);
            Controls.Add(groupBoxMineralAnalysis);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "AnalyzerForm";
            Text = "AnalyzerForm";
            FormClosing += AnalyzerForm_FormClosing;
            DragDrop += AnalyzerForm_DragDrop;
            DragEnter += AnalyzerForm_DragEnter;
            groupBoxMineralAnalysis.ResumeLayout(false);
            groupBoxMineralAnalysis.PerformLayout();
            flowLayoutPanelBinning.ResumeLayout(false);
            flowLayoutPanelBinning.PerformLayout();
            flowLayoutPanelModellFolder.ResumeLayout(false);
            flowLayoutPanelModellFolder.PerformLayout();
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox groupBoxMineralAnalysis;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private FlowLayoutPanel flowLayoutPanelModellFolder;
        private Label labelModelFolder;
        private ComboBox comboBoxMappingModellFolder;
        private Crystallography.Controls.GraphControl graphControl1;
        private Button button1;
        private TextBox textBox1;
        private FlowLayoutPanel flowLayoutPanelBinning;
        private Label labelBinning;
        private ComboBox comboBoxBinning;
        private Crystallography.Controls.ScalablePictureBox scalablePictureBoxSEM;
    }
}
