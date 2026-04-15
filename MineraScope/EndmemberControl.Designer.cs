namespace MineraScope
{
    partial class EndmemberControl
    {
        /// <summary> 
        /// 必要なデザイナー変数です。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 使用中のリソースをすべてクリーンアップします。
        /// </summary>
        /// <param name="disposing">マネージド リソースを破棄する場合は true を指定し、その他の場合は false を指定します。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region コンポーネント デザイナーで生成されたコード

        /// <summary> 
        /// デザイナー サポートに必要なメソッドです。このメソッドの内容を 
        /// コード エディターで変更しないでください。
        /// </summary>
        private void InitializeComponent()
        {
            textBoxEndmember_Name = new TextBox();
            textBoxEndmember_Formula = new TextBox();
            SuspendLayout();
            // 
            // textBoxEndmember_Name
            // 
            textBoxEndmember_Name.BorderStyle = BorderStyle.FixedSingle;
            textBoxEndmember_Name.Location = new Point(0, 0);
            textBoxEndmember_Name.Name = "textBoxEndmember_Name";
            textBoxEndmember_Name.Size = new Size(105, 23);
            textBoxEndmember_Name.TabIndex = 85;
            // 
            // textBoxEndmember_Formula
            // 
            textBoxEndmember_Formula.BorderStyle = BorderStyle.FixedSingle;
            textBoxEndmember_Formula.Location = new Point(120, 0);
            textBoxEndmember_Formula.Name = "textBoxEndmember_Formula";
            textBoxEndmember_Formula.Size = new Size(140, 23);
            textBoxEndmember_Formula.TabIndex = 86;
            // 
            // EndmemberControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(textBoxEndmember_Formula);
            Controls.Add(textBoxEndmember_Name);
            Name = "EndmemberControl";
            Size = new Size(260, 23);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox textBoxEndmember_Name;
        private TextBox textBoxEndmember_Formula;
    }
}

