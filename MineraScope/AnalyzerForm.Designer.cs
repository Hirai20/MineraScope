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
            listBoxLegend = new ListBox();
            buttonCancelMap = new Button();
            scalablePictureBoxMap = new Crystallography.Controls.ScalablePictureBox();
            scalablePictureBoxSEM = new Crystallography.Controls.ScalablePictureBox();
            flowLayoutPanelBinning = new FlowLayoutPanel();
            labelBinning = new Label();
            comboBoxBinning = new ComboBox();
            textBox1 = new TextBox();
            buttonClassifyMap = new Button();
            graphControl1 = new Crystallography.Controls.GraphControl();
            flowLayoutPanelModellFolder = new FlowLayoutPanel();
            labelModelFolder = new Label();
            comboBoxMappingModellFolder = new ComboBox();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            statusStrip1 = new StatusStrip();
            toolStripProgressBarMapping = new ToolStripProgressBar();
            toolStripStatusLabelMapping = new ToolStripStatusLabel();
            tableLayoutPanel1 = new TableLayoutPanel();
            panel1 = new Panel();
            label1 = new Label();
            comboBox1 = new ComboBox();
            panel2 = new Panel();
            panelContrast = new Panel();
            trackBarContrast = new TrackBar();
            labelContrast = new Label();
            panelBrightness = new Panel();
            trackBarBrightness = new TrackBar();
            labelBrightness = new Label();
            exportMapToolStripMenuItem = new ToolStripMenuItem();
            flowLayoutPanelBinning.SuspendLayout();
            flowLayoutPanelModellFolder.SuspendLayout();
            menuStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            panel1.SuspendLayout();
            panel2.SuspendLayout();
            panelContrast.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trackBarContrast).BeginInit();
            panelBrightness.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trackBarBrightness).BeginInit();
            SuspendLayout();
            // 
            // listBoxLegend
            // 
            listBoxLegend.DrawMode = DrawMode.OwnerDrawFixed;
            listBoxLegend.FormattingEnabled = true;
            listBoxLegend.HorizontalScrollbar = true;
            listBoxLegend.ItemHeight = 18;
            listBoxLegend.Location = new Point(9, 334);
            listBoxLegend.Name = "listBoxLegend";
            listBoxLegend.Size = new Size(142, 184);
            listBoxLegend.TabIndex = 34;
            listBoxLegend.DrawItem += listBoxLegend_DrawItem;
            listBoxLegend.SelectedIndexChanged += listBoxLegend_SelectedIndexChanged;
            listBoxLegend.MouseDown += listBoxLegend_MouseDown;
            // 
            // buttonCancelMap
            // 
            buttonCancelMap.AutoSize = true;
            buttonCancelMap.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonCancelMap.Location = new Point(548, 6);
            buttonCancelMap.Name = "buttonCancelMap";
            buttonCancelMap.Size = new Size(41, 25);
            buttonCancelMap.TabIndex = 33;
            buttonCancelMap.Text = "中止";
            buttonCancelMap.UseVisualStyleBackColor = true;
            buttonCancelMap.Click += buttonCancelMap_Click;
            // 
            // scalablePictureBoxMap
            // 
            scalablePictureBoxMap.AllowDrop = true;
            scalablePictureBoxMap.BackColor = SystemColors.ActiveCaption;
            scalablePictureBoxMap.Dock = DockStyle.Fill;
            scalablePictureBoxMap.FixZoomAndCenter = false;
            scalablePictureBoxMap.FocusEventEnabled = false;
            scalablePictureBoxMap.HorizontalFlip = false;
            scalablePictureBoxMap.Location = new Point(0, 340);
            scalablePictureBoxMap.ManualSpotMode = false;
            scalablePictureBoxMap.Margin = new Padding(0, 2, 0, 0);
            scalablePictureBoxMap.MouseScaling = true;
            scalablePictureBoxMap.MouseTranslation = true;
            scalablePictureBoxMap.Name = "scalablePictureBoxMap";
            scalablePictureBoxMap.ShowAreaRectangle = false;
            scalablePictureBoxMap.ShowRimRentangle = false;
            scalablePictureBoxMap.Size = new Size(448, 336);
            scalablePictureBoxMap.TabIndex = 32;
            scalablePictureBoxMap.TitleVisible = false;
            scalablePictureBoxMap.VerticalFlip = false;
            scalablePictureBoxMap.Zoom = 128D;
            scalablePictureBoxMap.MouseUp2 += scalablePictureBoxSEM_MouseUp2;
            scalablePictureBoxMap.MouseDown2 += scalablePictureBoxSEM_MouseDown2;
            // 
            // scalablePictureBoxSEM
            // 
            scalablePictureBoxSEM.AllowDrop = true;
            scalablePictureBoxSEM.BackColor = SystemColors.ActiveCaption;
            scalablePictureBoxSEM.Dock = DockStyle.Fill;
            scalablePictureBoxSEM.FixZoomAndCenter = false;
            scalablePictureBoxSEM.FocusEventEnabled = false;
            scalablePictureBoxSEM.HorizontalFlip = false;
            scalablePictureBoxSEM.Location = new Point(0, 0);
            scalablePictureBoxSEM.ManualSpotMode = false;
            scalablePictureBoxSEM.Margin = new Padding(0, 0, 0, 2);
            scalablePictureBoxSEM.MouseScaling = true;
            scalablePictureBoxSEM.MouseTranslation = true;
            scalablePictureBoxSEM.Name = "scalablePictureBoxSEM";
            scalablePictureBoxSEM.ShowAreaRectangle = false;
            scalablePictureBoxSEM.ShowRimRentangle = false;
            scalablePictureBoxSEM.Size = new Size(448, 336);
            scalablePictureBoxSEM.TabIndex = 31;
            scalablePictureBoxSEM.TitleVisible = false;
            scalablePictureBoxSEM.VerticalFlip = false;
            scalablePictureBoxSEM.Zoom = 128D;
            scalablePictureBoxSEM.MouseUp2 += scalablePictureBoxSEM_MouseUp2;
            scalablePictureBoxSEM.MouseDown2 += scalablePictureBoxSEM_MouseDown2;
            scalablePictureBoxSEM.DrawingAreaChanged += scalablePictureBoxSEM_DrawingAreaChanged;
            scalablePictureBoxSEM.DragDrop += AnalyzerForm_DragDrop;
            scalablePictureBoxSEM.DragEnter += AnalyzerForm_DragEnter;
            // 
            // flowLayoutPanelBinning
            // 
            flowLayoutPanelBinning.AutoScroll = true;
            flowLayoutPanelBinning.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanelBinning.Controls.Add(labelBinning);
            flowLayoutPanelBinning.Controls.Add(comboBoxBinning);
            flowLayoutPanelBinning.Location = new Point(225, 3);
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
            textBox1.Location = new Point(157, 334);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(194, 226);
            textBox1.TabIndex = 28;
            // 
            // buttonClassifyMap
            // 
            buttonClassifyMap.AutoSize = true;
            buttonClassifyMap.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonClassifyMap.Location = new Point(457, 6);
            buttonClassifyMap.Name = "buttonClassifyMap";
            buttonClassifyMap.Size = new Size(85, 25);
            buttonClassifyMap.TabIndex = 27;
            buttonClassifyMap.Text = "鉱物マッピング";
            buttonClassifyMap.UseVisualStyleBackColor = true;
            buttonClassifyMap.Click += buttonClassifyMap_Click;
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
            graphControl1.DivisionLineColor = Color.LightGray;
            graphControl1.DivisionLineXVisible = true;
            graphControl1.DivisionLineYVisible = true;
            graphControl1.Dock = DockStyle.Top;
            graphControl1.FixRangeHorizontal = false;
            graphControl1.FixRangeVertical = false;
            graphControl1.Font = new Font("Segoe UI Symbol", 9F);
            graphControl1.GraphTitle = "";
            graphControl1.IsIntegerX = false;
            graphControl1.IsIntegerY = false;
            graphControl1.LabelX = "X:";
            graphControl1.LabelY = "Y:";
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
            graphControl1.Size = new Size(363, 248);
            graphControl1.TabIndex = 26;
            graphControl1.UnitX = "";
            graphControl1.UnitY = "";
            graphControl1.UpperPanelFont = new Font("Segoe UI Symbol", 9F);
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
            flowLayoutPanelModellFolder.Location = new Point(3, 3);
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
            menuStrip1.Size = new Size(811, 24);
            menuStrip1.TabIndex = 89;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { exportMapToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(50, 20);
            fileToolStripMenuItem.Text = "Menu";
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripProgressBarMapping, toolStripStatusLabelMapping });
            statusStrip1.Location = new Point(0, 747);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(811, 22);
            statusStrip1.TabIndex = 90;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripProgressBarMapping
            // 
            toolStripProgressBarMapping.Name = "toolStripProgressBarMapping";
            toolStripProgressBarMapping.Size = new Size(100, 16);
            // 
            // toolStripStatusLabelMapping
            // 
            toolStripStatusLabelMapping.Name = "toolStripStatusLabelMapping";
            toolStripStatusLabelMapping.Size = new Size(0, 17);
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 1;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Controls.Add(scalablePictureBoxSEM, 0, 0);
            tableLayoutPanel1.Controls.Add(scalablePictureBoxMap, 0, 1);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 71);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Size = new Size(448, 676);
            tableLayoutPanel1.TabIndex = 35;
            // 
            // panel1
            // 
            panel1.Controls.Add(label1);
            panel1.Controls.Add(comboBox1);
            panel1.Controls.Add(flowLayoutPanelModellFolder);
            panel1.Controls.Add(flowLayoutPanelBinning);
            panel1.Controls.Add(buttonClassifyMap);
            panel1.Controls.Add(buttonCancelMap);
            panel1.Dock = DockStyle.Top;
            panel1.Location = new Point(0, 24);
            panel1.Name = "panel1";
            panel1.Size = new Size(811, 47);
            panel1.TabIndex = 91;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(640, 11);
            label1.Name = "label1";
            label1.Size = new Size(42, 15);
            label1.TabIndex = 35;
            label1.Text = "スイープ";
            // 
            // comboBox1
            // 
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox1.FormattingEnabled = true;
            comboBox1.Location = new Point(704, 8);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(95, 23);
            comboBox1.TabIndex = 34;
            // 
            // panel2
            // 
            panel2.Controls.Add(panelContrast);
            panel2.Controls.Add(panelBrightness);
            panel2.Controls.Add(graphControl1);
            panel2.Controls.Add(textBox1);
            panel2.Controls.Add(listBoxLegend);
            panel2.Dock = DockStyle.Right;
            panel2.Location = new Point(448, 71);
            panel2.Name = "panel2";
            panel2.Size = new Size(363, 676);
            panel2.TabIndex = 92;
            // 
            // panelContrast
            // 
            panelContrast.Controls.Add(trackBarContrast);
            panelContrast.Controls.Add(labelContrast);
            panelContrast.Location = new Point(132, 254);
            panelContrast.Name = "panelContrast";
            panelContrast.Size = new Size(118, 79);
            panelContrast.TabIndex = 37;
            // 
            // trackBarContrast
            // 
            trackBarContrast.Location = new Point(3, 34);
            trackBarContrast.Maximum = 100;
            trackBarContrast.Minimum = -100;
            trackBarContrast.Name = "trackBarContrast";
            trackBarContrast.Size = new Size(104, 45);
            trackBarContrast.TabIndex = 35;
            trackBarContrast.TickFrequency = 25;
            trackBarContrast.Scroll += trackBarContrast_Scroll;
            // 
            // labelContrast
            // 
            labelContrast.AutoSize = true;
            labelContrast.Location = new Point(21, 0);
            labelContrast.Name = "labelContrast";
            labelContrast.Size = new Size(57, 15);
            labelContrast.TabIndex = 36;
            labelContrast.Text = "コントラスト";
            // 
            // panelBrightness
            // 
            panelBrightness.Controls.Add(trackBarBrightness);
            panelBrightness.Controls.Add(labelBrightness);
            panelBrightness.Location = new Point(9, 257);
            panelBrightness.Name = "panelBrightness";
            panelBrightness.Size = new Size(117, 79);
            panelBrightness.TabIndex = 37;
            // 
            // trackBarBrightness
            // 
            trackBarBrightness.Location = new Point(10, 31);
            trackBarBrightness.Maximum = 255;
            trackBarBrightness.Minimum = -255;
            trackBarBrightness.Name = "trackBarBrightness";
            trackBarBrightness.Size = new Size(104, 45);
            trackBarBrightness.TabIndex = 35;
            trackBarBrightness.TickFrequency = 51;
            trackBarBrightness.Scroll += trackBarBrightness_Scroll;
            // 
            // labelBrightness
            // 
            labelBrightness.AutoSize = true;
            labelBrightness.Location = new Point(26, 0);
            labelBrightness.Name = "labelBrightness";
            labelBrightness.Size = new Size(60, 15);
            labelBrightness.TabIndex = 36;
            labelBrightness.Text = "ブライトネス";
            // 
            // exportMapToolStripMenuItem
            // 
            exportMapToolStripMenuItem.Name = "exportMapToolStripMenuItem";
            exportMapToolStripMenuItem.Size = new Size(180, 22);
            exportMapToolStripMenuItem.Text = "Export";
            exportMapToolStripMenuItem.Click += exportMapToolStripMenuItem_Click;
            // 
            // AnalyzerForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            ClientSize = new Size(811, 769);
            Controls.Add(tableLayoutPanel1);
            Controls.Add(panel2);
            Controls.Add(panel1);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "AnalyzerForm";
            Text = "AnalyzerForm";
            FormClosing += AnalyzerForm_FormClosing;
            DragDrop += AnalyzerForm_DragDrop;
            DragEnter += AnalyzerForm_DragEnter;
            flowLayoutPanelBinning.ResumeLayout(false);
            flowLayoutPanelBinning.PerformLayout();
            flowLayoutPanelModellFolder.ResumeLayout(false);
            flowLayoutPanelModellFolder.PerformLayout();
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            tableLayoutPanel1.ResumeLayout(false);
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            panelContrast.ResumeLayout(false);
            panelContrast.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trackBarContrast).EndInit();
            panelBrightness.ResumeLayout(false);
            panelBrightness.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trackBarBrightness).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private FlowLayoutPanel flowLayoutPanelModellFolder;
        private Label labelModelFolder;
        private ComboBox comboBoxMappingModellFolder;
        private Crystallography.Controls.GraphControl graphControl1;
        private Button buttonClassifyMap;
        private TextBox textBox1;
        private FlowLayoutPanel flowLayoutPanelBinning;
        private Label labelBinning;
        private ComboBox comboBoxBinning;
        private Crystallography.Controls.ScalablePictureBox scalablePictureBoxSEM;
        private Crystallography.Controls.ScalablePictureBox scalablePictureBoxMap;
        private Button buttonCancelMap;
        private StatusStrip statusStrip1;
        private ToolStripProgressBar toolStripProgressBarMapping;
        private ToolStripStatusLabel toolStripStatusLabelMapping;
        private ListBox listBoxLegend;
        private TableLayoutPanel tableLayoutPanel1;
        private Panel panel1;
        private Panel panel2;
        private TrackBar trackBarContrast;
        private TrackBar trackBarBrightness;
        private Label labelContrast;
        private Label labelBrightness;
        private Panel panelContrast;
        private Panel panelBrightness;
        private Label label1;
        private ComboBox comboBox1;
        private ToolStripMenuItem exportMapToolStripMenuItem;
    }
}
