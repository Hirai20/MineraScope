using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MineraScope
{
    public partial class EndmemberControl : UserControl
    {
        public EndmemberControl()
        {
            InitializeComponent();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string EndmemberName
        {
            get =>@textBoxEndmember_Name.Text; set => textBoxEndmember_Name.Text = value;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string EndmemberFormula
        {
            get => textBoxEndmember_Formula.Text; set => textBoxEndmember_Formula.Text = value;
        }
        public void Reset()
        {
            if (!this.DesignMode) this.Visible = false;
            textBoxEndmember_Name.Text = "";
            textBoxEndmember_Formula.Text = "";
        }
    }
}

