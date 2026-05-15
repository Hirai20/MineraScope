using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;


namespace MineraScope

{
    public class ProgressForm : Form
    {
        private IContainer components;

        internal ProgressBar progressBar1;

        internal Button Btn_Abort;

        internal Label Lbl_message;

        public ProgressForm()
        {
            InitializeComponent();
            base.MinimizeBox = false;
            base.MaximizeBox = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.Btn_Abort = new System.Windows.Forms.Button();
            this.Lbl_message = new System.Windows.Forms.Label();
            base.SuspendLayout();
            this.progressBar1.Location = new System.Drawing.Point(12, 12);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(435, 23);
            this.progressBar1.Step = 1;
            this.progressBar1.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.progressBar1.TabIndex = 0;
            this.Btn_Abort.Location = new System.Drawing.Point(372, 41);
            this.Btn_Abort.Name = "Btn_Abort";
            this.Btn_Abort.Size = new System.Drawing.Size(75, 23);
            this.Btn_Abort.TabIndex = 1;
            this.Btn_Abort.Text = "Abort";
            this.Btn_Abort.UseVisualStyleBackColor = true;
            this.Lbl_message.AutoSize = true;
            this.Lbl_message.Location = new System.Drawing.Point(12, 45);
            this.Lbl_message.Name = "Lbl_message";
            this.Lbl_message.Size = new System.Drawing.Size(0, 15);
            this.Lbl_message.TabIndex = 2;
            base.AutoScaleDimensions = new System.Drawing.SizeF(8f, 15f);
            base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            base.ClientSize = new System.Drawing.Size(459, 79);
            base.Controls.Add(this.Lbl_message);
            base.Controls.Add(this.Btn_Abort);
            base.Controls.Add(this.progressBar1);
            base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            base.Name = "ProgressForm";
            this.Text = "Progress";
            base.ResumeLayout(false);
            base.PerformLayout();
        }
    }

}
