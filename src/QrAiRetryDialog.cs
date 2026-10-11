using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace QQImageSwitch
{
    sealed class QrAiRetryDialog : Form
    {
        internal const string Explanation="手动位置未通过扫码。可以调整位置或大小后重新生成，也可以让 AI 自动避开皮肤和头发重试。";
        readonly Button chooseAuto,keepManual;

        public QrAiRetryDialog(string message)
        {
            // Only display our fixed explanation. Exception details can contain
            // the imported contact payload or URL and do not belong in this prompt.
            Ui.Configure(this,"二维码位置需要调整",new Size(740,260));
            MinimumSize=new Size(540,250);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;ClientSize=new Size(740,260);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(22)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
            layout.Controls.Add(Ui.Text("手动位置未通过扫码",15,true),0,0);
            var explanation=Ui.Text(Explanation,10);explanation.AutoSize=false;explanation.Dock=DockStyle.Fill;explanation.Margin=new Padding(0,6,0,16);layout.Controls.Add(explanation,0,1);
            keepManual=Ui.Button("保留手动位置",KeepManual,true);keepManual.TabIndex=0;keepManual.DialogResult=DialogResult.Cancel;
            chooseAuto=Ui.Button("帮我选择自动避让",ChooseAuto);chooseAuto.TabIndex=1;
            layout.Controls.Add(Ui.Flow(keepManual,chooseAuto),0,2);
            AcceptButton=keepManual;CancelButton=keepManual;ActiveControl=keepManual;
        }
        void KeepManual(object sender,EventArgs e){DialogResult=DialogResult.Cancel;Close();}
        void ChooseAuto(object sender,EventArgs e){DialogResult=DialogResult.Retry;Close();}
        bool KeepManualKey(Keys keyData)
        {
            Keys key=keyData&Keys.KeyCode;if(key!=Keys.Enter&&key!=Keys.Escape)return false;
            KeepManual(this,EventArgs.Empty);return true;
        }
        // Enter remains conservative even after keyboard focus reaches retry.
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData){return KeepManualKey(keyData)||base.ProcessCmdKey(ref msg,keyData);}
        protected override bool ProcessDialogKey(Keys keyData){return KeepManualKey(keyData)||base.ProcessDialogKey(keyData);}
#if SELF_TEST
        internal bool TestDefaultIsManual{get{return AcceptButton==keepManual&&CancelButton==keepManual&&ActiveControl==keepManual;}}
        internal DialogResult TestKeepManual(){KeepManual(this,EventArgs.Empty);return DialogResult;}
        internal DialogResult TestChooseAuto(){ChooseAuto(this,EventArgs.Empty);return DialogResult;}
        internal DialogResult TestDialogKey(Keys key){ActiveControl=chooseAuto;var message=new Message();if(!ProcessCmdKey(ref message,key))throw new Exception("Retry dialog did not handle default or cancel key.");return DialogResult;}
        internal void TestCapture(string path)
        {
            bool wasVisible=Visible;double priorOpacity=Opacity;
            try{if(!wasVisible){Opacity=0;Show();}CreateControl();PerformLayout();using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
            finally{if(!wasVisible)Hide();Opacity=priorOpacity;}
        }
        internal static void CheckBehavior(string folder)
        {
            bool priorEnglish=L.English;Directory.CreateDirectory(folder);const string privateDetail="PRIVATE_TEST_PAYLOAD_SHOULD_NOT_BE_DISPLAYED";
            try
            {
                foreach(bool english in new[]{false,true})
                {
                    L.Set(english?"en":"zh");
                    using(var dialog=new QrAiRetryDialog(privateDetail))
                    {
                        if(!dialog.TestDefaultIsManual)throw new Exception("Retry dialog default was not the manual position.");
                        CheckPrivateDetail(dialog,privateDetail);
                        dialog.TestCapture(Path.Combine(folder,"qr-ai-retry-"+(english?"en":"zh")+".png"));
                    }
                    foreach(var key in new[]{Keys.Enter,Keys.Escape,Keys.Enter|Keys.Shift})using(var dialog=new QrAiRetryDialog(privateDetail))
                        if(dialog.TestDialogKey(key)!=DialogResult.Cancel)throw new Exception("Retry dialog keyboard changed the manual position.");
                    using(var dialog=new QrAiRetryDialog(privateDetail))if(dialog.TestKeepManual()!=DialogResult.Cancel)throw new Exception("Retry dialog manual action did not cancel.");
                    using(var dialog=new QrAiRetryDialog(privateDetail))if(dialog.TestChooseAuto()!=DialogResult.Retry)throw new Exception("Retry dialog explicit automatic action did not request retry.");
                }
            }
            finally{L.Set(priorEnglish?"en":"zh");}
        }
        static void CheckPrivateDetail(Control control,string privateDetail)
        {
            if(control.Text.Contains(privateDetail))throw new Exception("Retry dialog displayed private exception details.");
            foreach(Control child in control.Controls)CheckPrivateDetail(child,privateDetail);
        }
#endif
    }
}
