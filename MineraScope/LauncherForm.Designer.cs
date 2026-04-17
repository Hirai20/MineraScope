namespace MineraScope
{
    partial class LauncherForm
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
            SuspendLayout();
            // 
            // buttonOpenGenerator
            // 
            buttonOpenGenerator.AutoSize = true;
            buttonOpenGenerator.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonOpenGenerator.Location = new Point(51, 78);
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
            buttonOpenAnalyzer.Location = new Point(253, 78);
            buttonOpenAnalyzer.Name = "buttonOpenAnalyzer";
            buttonOpenAnalyzer.Size = new Size(102, 25);
            buttonOpenAnalyzer.TabIndex = 0;
            buttonOpenAnalyzer.Text = "既存モデルを使用";
            buttonOpenAnalyzer.UseVisualStyleBackColor = true;
            buttonOpenAnalyzer.Click += buttonOpenAnalyzer_Click;
            // 
            // LauncherForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(475, 161);
            Controls.Add(buttonOpenAnalyzer);
            Controls.Add(buttonOpenGenerator);
            Name = "LauncherForm";
            Text = "LauncherForm";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button buttonOpenGenerator;
        private Button buttonOpenAnalyzer;
    }
}