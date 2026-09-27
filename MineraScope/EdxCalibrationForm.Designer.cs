namespace MineraScope
{
    partial class EdxCalibrationForm
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
        // 260724Claude: EmsaConverter Form1.Designer.cs の配置をそのまま移植 (button1 は button1_Click を移植しないため除外)。
        private void InitializeComponent()
        {
            textBoxSource = new TextBox();
            textBoxResult = new TextBox();
            numericUpDownOffset = new NumericUpDown();
            label1 = new Label();
            label2 = new Label();
            numericUpDownXperchan = new NumericUpDown();
            numericUpDownPointNum = new NumericUpDown();
            numericUpDownOrder = new NumericUpDown();
            label3 = new Label();
            label4 = new Label();
            buttonCopy = new Button();
            splitter1 = new Splitter();
            textBoxSimulated = new TextBox();
            label5 = new Label();
            textBoxRwp = new TextBox();
            textBoxenergy = new TextBox();
            textBoxOffset = new TextBox();
            label6 = new Label();
            label7 = new Label();
            buttonOptimize = new Button();
            statusStrip1 = new StatusStrip();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            ((System.ComponentModel.ISupportInitialize)numericUpDownOffset).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownXperchan).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownPointNum).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownOrder).BeginInit();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            //
            // textBoxSource
            //
            textBoxSource.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            textBoxSource.Location = new Point(14, 156);
            textBoxSource.Margin = new Padding(3, 4, 3, 4);
            textBoxSource.Multiline = true;
            textBoxSource.Name = "textBoxSource";
            textBoxSource.ScrollBars = ScrollBars.Vertical;
            textBoxSource.Size = new Size(140, 390);
            textBoxSource.TabIndex = 0;
            textBoxSource.DragDrop += textBoxSource_DragDrop;
            //
            // textBoxResult
            //
            textBoxResult.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            textBoxResult.Location = new Point(160, 156);
            textBoxResult.Margin = new Padding(3, 4, 3, 4);
            textBoxResult.Multiline = true;
            textBoxResult.Name = "textBoxResult";
            textBoxResult.ScrollBars = ScrollBars.Vertical;
            textBoxResult.Size = new Size(222, 390);
            textBoxResult.TabIndex = 0;
            //
            // numericUpDownOffset
            //
            numericUpDownOffset.Cursor = Cursors.SizeAll;
            numericUpDownOffset.DecimalPlaces = 6;
            numericUpDownOffset.Location = new Point(96, 5);
            numericUpDownOffset.Margin = new Padding(3, 4, 3, 4);
            numericUpDownOffset.Minimum = new decimal(new int[] { 100, 0, 0, int.MinValue });
            numericUpDownOffset.Name = "numericUpDownOffset";
            numericUpDownOffset.Size = new Size(88, 27);
            numericUpDownOffset.TabIndex = 1;
            numericUpDownOffset.ValueChanged += numericUpDownOffset_ValueChanged;
            //
            // label1
            //
            label1.AutoSize = true;
            label1.Location = new Point(18, 10);
            label1.Name = "label1";
            label1.Size = new Size(48, 20);
            label1.TabIndex = 2;
            label1.Text = "Offset";
            //
            // label2
            //
            label2.AutoSize = true;
            label2.Location = new Point(18, 48);
            label2.Name = "label2";
            label2.Size = new Size(77, 20);
            label2.TabIndex = 2;
            label2.Text = "X/Channel";
            //
            // numericUpDownXperchan
            //
            numericUpDownXperchan.DecimalPlaces = 6;
            numericUpDownXperchan.Location = new Point(96, 45);
            numericUpDownXperchan.Margin = new Padding(3, 4, 3, 4);
            numericUpDownXperchan.Name = "numericUpDownXperchan";
            numericUpDownXperchan.Size = new Size(88, 27);
            numericUpDownXperchan.TabIndex = 1;
            numericUpDownXperchan.Value = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDownXperchan.ValueChanged += numericUpDownOffset_ValueChanged;
            //
            // numericUpDownPointNum
            //
            numericUpDownPointNum.Location = new Point(270, 9);
            numericUpDownPointNum.Margin = new Padding(3, 4, 3, 4);
            numericUpDownPointNum.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            numericUpDownPointNum.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            numericUpDownPointNum.Name = "numericUpDownPointNum";
            numericUpDownPointNum.Size = new Size(110, 27);
            numericUpDownPointNum.TabIndex = 1;
            numericUpDownPointNum.Value = new decimal(new int[] { 3, 0, 0, 0 });
            numericUpDownPointNum.ValueChanged += numericUpDownOffset_ValueChanged;
            //
            // numericUpDownOrder
            //
            numericUpDownOrder.Location = new Point(270, 45);
            numericUpDownOrder.Margin = new Padding(3, 4, 3, 4);
            numericUpDownOrder.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDownOrder.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownOrder.Name = "numericUpDownOrder";
            numericUpDownOrder.Size = new Size(110, 27);
            numericUpDownOrder.TabIndex = 1;
            numericUpDownOrder.Value = new decimal(new int[] { 2, 0, 0, 0 });
            numericUpDownOrder.ValueChanged += numericUpDownOffset_ValueChanged;
            //
            // label3
            //
            label3.AutoSize = true;
            label3.Location = new Point(193, 16);
            label3.Name = "label3";
            label3.Size = new Size(70, 20);
            label3.TabIndex = 2;
            label3.Text = "Point No.";
            //
            // label4
            //
            label4.AutoSize = true;
            label4.Location = new Point(193, 48);
            label4.Name = "label4";
            label4.Size = new Size(47, 20);
            label4.TabIndex = 2;
            label4.Text = "Order";
            //
            // buttonCopy
            //
            buttonCopy.Location = new Point(8, 90);
            buttonCopy.Margin = new Padding(3, 4, 3, 4);
            buttonCopy.Name = "buttonCopy";
            buttonCopy.Size = new Size(146, 31);
            buttonCopy.TabIndex = 3;
            buttonCopy.Text = "Copy";
            buttonCopy.UseVisualStyleBackColor = true;
            buttonCopy.Click += buttonCopy_Click;
            //
            // splitter1
            //
            splitter1.Location = new Point(0, 0);
            splitter1.Margin = new Padding(2);
            splitter1.Name = "splitter1";
            splitter1.Size = new Size(3, 572);
            splitter1.TabIndex = 6;
            splitter1.TabStop = false;
            //
            // textBoxSimulated
            //
            textBoxSimulated.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            textBoxSimulated.Location = new Point(388, 156);
            textBoxSimulated.Margin = new Padding(3, 4, 3, 4);
            textBoxSimulated.Multiline = true;
            textBoxSimulated.Name = "textBoxSimulated";
            textBoxSimulated.ScrollBars = ScrollBars.Vertical;
            textBoxSimulated.Size = new Size(222, 390);
            textBoxSimulated.TabIndex = 7;
            textBoxSimulated.DragDrop += textBoxSimulated_DragDrop;
            //
            // label5
            //
            label5.AutoSize = true;
            label5.Location = new Point(413, 16);
            label5.Name = "label5";
            label5.Size = new Size(38, 20);
            label5.TabIndex = 8;
            label5.Text = "Rwp";
            //
            // textBoxRwp
            //
            textBoxRwp.Location = new Point(466, 14);
            textBoxRwp.Margin = new Padding(2, 2, 2, 2);
            textBoxRwp.Name = "textBoxRwp";
            textBoxRwp.Size = new Size(143, 27);
            textBoxRwp.TabIndex = 11;
            //
            // textBoxenergy
            //
            textBoxenergy.Location = new Point(466, 93);
            textBoxenergy.Margin = new Padding(2, 2, 2, 2);
            textBoxenergy.Name = "textBoxenergy";
            textBoxenergy.Size = new Size(143, 27);
            textBoxenergy.TabIndex = 12;
            //
            // textBoxOffset
            //
            textBoxOffset.Location = new Point(466, 50);
            textBoxOffset.Margin = new Padding(2, 2, 2, 2);
            textBoxOffset.Name = "textBoxOffset";
            textBoxOffset.Size = new Size(143, 27);
            textBoxOffset.TabIndex = 13;
            //
            // label6
            //
            label6.AutoSize = true;
            label6.Location = new Point(402, 53);
            label6.Name = "label6";
            label6.Size = new Size(48, 20);
            label6.TabIndex = 14;
            label6.Text = "Offset";
            //
            // label7
            //
            label7.AutoSize = true;
            label7.Location = new Point(388, 95);
            label7.Name = "label7";
            label7.Size = new Size(77, 20);
            label7.TabIndex = 15;
            label7.Text = "X/Channel";
            //
            // buttonOptimize
            //
            buttonOptimize.Location = new Point(294, 91);
            buttonOptimize.Margin = new Padding(2, 2, 2, 2);
            buttonOptimize.Name = "buttonOptimize";
            buttonOptimize.Size = new Size(87, 32);
            buttonOptimize.TabIndex = 16;
            buttonOptimize.Text = "最適化";
            buttonOptimize.UseVisualStyleBackColor = true;
            buttonOptimize.Click += buttonOptimize_Click;
            //
            // statusStrip1
            //
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1 });
            statusStrip1.Location = new Point(3, 546);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(615, 26);
            statusStrip1.TabIndex = 17;
            statusStrip1.Text = "statusStrip1";
            //
            // toolStripStatusLabel1
            //
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(13, 20);
            toolStripStatusLabel1.Text = " ";
            //
            // EdxCalibrationForm
            //
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(618, 572);
            Controls.Add(statusStrip1);
            Controls.Add(buttonOptimize);
            Controls.Add(label7);
            Controls.Add(label6);
            Controls.Add(textBoxOffset);
            Controls.Add(textBoxenergy);
            Controls.Add(textBoxRwp);
            Controls.Add(label5);
            Controls.Add(textBoxSimulated);
            Controls.Add(splitter1);
            Controls.Add(buttonCopy);
            Controls.Add(label2);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(label1);
            Controls.Add(numericUpDownOrder);
            Controls.Add(numericUpDownXperchan);
            Controls.Add(numericUpDownPointNum);
            Controls.Add(numericUpDownOffset);
            Controls.Add(textBoxResult);
            Controls.Add(textBoxSource);
            Margin = new Padding(3, 4, 3, 4);
            Name = "EdxCalibrationForm";
            Text = "EDX検出器キャリブレーション";
            FormClosing += EdxCalibrationForm_FormClosing;
            ((System.ComponentModel.ISupportInitialize)numericUpDownOffset).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownXperchan).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownPointNum).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownOrder).EndInit();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox textBoxSource;
        private TextBox textBoxResult;
        private NumericUpDown numericUpDownOffset;
        private Label label1;
        private Label label2;
        private NumericUpDown numericUpDownXperchan;
        private NumericUpDown numericUpDownPointNum;
        private NumericUpDown numericUpDownOrder;
        private Label label3;
        private Label label4;
        private Button buttonCopy;
        private Splitter splitter1;
        private TextBox textBoxSimulated;
        private Label label5;
        private TextBox textBoxRwp;
        private TextBox textBoxenergy;
        private TextBox textBoxOffset;
        private Label label6;
        private Label label7;
        private Button buttonOptimize;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel toolStripStatusLabel1;
    }
}
