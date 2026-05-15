using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;

namespace MineraScope
{
    public class ProgressDialog : IDisposable
    {
        private volatile bool _canceled;

        private volatile ProgressForm form;

        private ManualResetEvent startEvent;

        private bool showed;

        private volatile bool closing;

        private Form ownerForm;

        private Thread thread;

        private volatile string _title = "進行状況";

        private volatile int _minimum;

        private volatile int _maximum = 100;

        private volatile int _value;

        private volatile string _message = "";

        public string Title
        {
            get
            {
                return _title;
            }
            set
            {
                _title = value;
                if (form != null)
                {
                    form.Invoke(new MethodInvoker(SetTitle));
                }
            }
        }

        public int Minimum
        {
            get
            {
                return _minimum;
            }
            set
            {
                _minimum = value;
                if (form != null)
                {
                    form.Invoke(new MethodInvoker(SetProgressMinimum));
                }
            }
        }

        public int Maximum
        {
            get
            {
                return _maximum;
            }
            set
            {
                _maximum = value;
                if (form != null)
                {
                    form.Invoke(new MethodInvoker(SetProgressMaximun));
                }
            }
        }

        public int Value
        {
            get
            {
                return _value;
            }
            set
            {
                _value = value;
                if (form != null)
                {
                    form.Invoke(new MethodInvoker(SetProgressValue));
                }
            }
        }

        public string Message
        {
            get
            {
                return _message;
            }
            set
            {
                _message = value;
                if (form != null)
                {
                    form.Invoke(new MethodInvoker(SetMessage));
                }
            }
        }

        public bool Canceled => _canceled;

        public void Show(Form owner)
        {
            if (!showed)
            {
                showed = true;
                _canceled = false;
                startEvent = new ManualResetEvent(initialState: false);
                ownerForm = owner;
                thread = new Thread(Run);
                thread.IsBackground = true;
                thread.ApartmentState = ApartmentState.STA;
                thread.Start();
                startEvent.WaitOne();
            }
        }

        public void Show()
        {
            Show(null);
        }

        private void Run()
        {
            form = new ProgressForm();
            form.Text = _title;
            form.Btn_Abort.Click += Btn_Abort_Click;
            form.Closing += form_Closing;
            form.Activated += form_Activated;
            form.progressBar1.Minimum = _minimum;
            form.progressBar1.Maximum = _maximum;
            form.progressBar1.Value = _value;
            if (ownerForm != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Left = ownerForm.Left + (ownerForm.Width - form.Width) / 2;
                form.Top = ownerForm.Top + (ownerForm.Height - form.Height) / 2;
            }
            form.ShowDialog();
            form.Dispose();
        }

        public void Close()
        {
            closing = true;
            form.Invoke(new MethodInvoker(form.Close));
        }

        public void Dispose()
        {
            form.Invoke(new MethodInvoker(form.Dispose));
        }

        private void SetProgressValue()
        {
            if (form != null && !form.IsDisposed)
            {
                form.progressBar1.Value = _value;
            }
        }

        private void SetMessage()
        {
            if (form != null && !form.IsDisposed)
            {
                form.Lbl_message.Text = _message;
            }
        }

        private void SetTitle()
        {
            if (form != null && !form.IsDisposed)
            {
                form.Text = _title;
            }
        }

        private void SetProgressMaximun()
        {
            if (form != null && !form.IsDisposed)
            {
                form.progressBar1.Maximum = _maximum;
            }
        }

        private void SetProgressMinimum()
        {
            if (form != null && !form.IsDisposed)
            {
                form.progressBar1.Minimum = _minimum;
            }
        }

        private void Btn_Abort_Click(object sender, EventArgs e)
        {
            _canceled = true;
        }

        private void form_Closing(object sender, CancelEventArgs e)
        {
            if (!closing)
            {
                e.Cancel = true;
                _canceled = true;
            }
        }

        private void form_Activated(object sender, EventArgs e)
        {
            form.Activated -= form_Activated;
            startEvent.Set();
        }
    }

}
