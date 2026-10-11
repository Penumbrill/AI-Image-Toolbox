using System;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQImageSwitch
{
    sealed class QrImportDialog : Form
    {
        readonly string[] values;readonly ListBox choices;readonly TextBox content;
        public string Result{get;private set;}
        public QrImportDialog(string[] candidates)
        {
            if(candidates==null||candidates.Length<2)throw new ArgumentException("candidates");values=candidates;
            Ui.Configure(this,"选择二维码内容",new Size(760,490));MinimumSize=new Size(540,380);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(20)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,100));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
            layout.Controls.Add(Ui.Text("图片中识别到多个二维码，请选择要使用的内容。这里只显示解码内容，不会打开链接。",10),0,0);
            choices=new ListBox{Dock=DockStyle.Fill,IntegralHeight=false,Margin=new Padding(0,10,0,12)};
            for(int i=0;i<values.Length;i++)choices.Items.Add((i+1)+" · "+Encoding.UTF8.GetByteCount(values[i])+" UTF-8 bytes");layout.Controls.Add(choices,0,1);
            content=new TextBox{ReadOnly=true,Multiline=true,WordWrap=false,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Tag="raw",BackColor=Color.White,Margin=new Padding(0,0,0,14)};layout.Controls.Add(content,0,2);
            choices.SelectedIndexChanged+=delegate{content.Text=values[choices.SelectedIndex];};choices.SelectedIndex=0;
            var use=Ui.Button("使用选中的二维码",delegate{Result=values[choices.SelectedIndex];DialogResult=DialogResult.OK;Close();},true);
            var cancel=Ui.Button("取消",delegate{DialogResult=DialogResult.Cancel;Close();});layout.Controls.Add(Ui.Flow(use,cancel),0,3);AcceptButton=use;CancelButton=cancel;
        }
#if SELF_TEST
        internal string TestChoose(int index){choices.SelectedIndex=index;return values[choices.SelectedIndex];}
        internal string TestDisplayed{get{return content.Text;}}
#endif
    }
    public sealed partial class QrPage
    {
        const string ImportMissing="未识别到标准二维码。请提供清晰完整原图；小程序码等专有码式无法转换为通用二维码。";
        string ChooseQrContent(string[] candidates,int selected=-1)
        {
            if(candidates.Length==0)return null;
            if(selected>=0){if(selected>=candidates.Length)throw new ArgumentOutOfRangeException("selected");return candidates[selected];}
            if(candidates.Length==1)return candidates[0];
            using(var dialog=new QrImportDialog(candidates))return L.Show(dialog,FindForm())==DialogResult.OK?dialog.Result:null;
        }
        void UseImportedPayload(string value)
        {
            value=QrService.Payload(value,true);if(value.Length>url.MaxLength)throw new ArgumentException("二维码内容过长，请使用较短内容。");
            // Some native text controls normalize line endings. Retain the exact
            // decoded string while its displayed form has not been edited.
            raw.Checked=true;url.Text=value;importedPayload=value;importedDisplay=url.Text;SetResult(value);Schedule();
        }
        async Task ImportQrImage(string path,int selected=-1)
        {
            if(Busy)return;
            await Run(async token=>
            {
                string[] candidates=await Task.Run(()=>{using(var image=Codec.Load(path))return QrService.ScanAll(image,token);},token);
                token.ThrowIfCancellationRequested();if(candidates.Length==0)return L.T(ImportMissing);
                string chosen=selected==-2?null:ChooseQrContent(candidates,selected);token.ThrowIfCancellationRequested();
                if(chosen==null)return L.T("已取消二维码导入。");
                UseImportedPayload(chosen);return L.T("已提取二维码内容。请选择底图和生成方式；截图中的头像、昵称与界面不会参与生成。");
            },false);
        }
#if SELF_TEST
        async Task CheckImportUiBehavior(string folder)
        {
            string oldPayload=url.Text,oldImage=imagePath;bool oldRaw=raw.Checked;int oldMode=mode.SelectedIndex;
            try
            {
                string key=AiGenerationKey();raw.Checked=!raw.Checked;if(key==AiGenerationKey())throw new Exception("Raw setting did not invalidate AI generation");
                UseImportedPayload(" \tline one\nline two \r\n");if(CurrentPayload()!=" \tline one\nline two \r\n"||!raw.Checked)throw new Exception("Import changed whitespace");
                using(var dialog=new QrImportDialog(new[]{QrImportTests.ExactUrl,QrImportTests.Contact})){if(dialog.TestChoose(1)!=QrImportTests.Contact||dialog.TestDisplayed!=QrImportTests.Contact)throw new Exception("Multi QR selection lost content");}
                string screenshot=System.IO.Path.Combine(folder,"import-screenshot.png");using(var bitmap=QrImportTests.Screenshot(false))Codec.SaveAtomic(screenshot,Codec.StaticPng(bitmap));
                await ImportQrImage(screenshot);if(CurrentPayload()!=QrImportTests.ExactUrl||!raw.Checked||imagePath!=oldImage||!open.Enabled)throw new Exception("QR screenshot import changed base image or exact content");
                UseImportedPayload(QrImportTests.Contact);if(open.Enabled)throw new Exception("Contact payload enabled website opening");
                mode.TestChoice(0);await Preview();while(calculating)await Task.Delay(25);await Preview();if(preview==null||QrService.Scan(preview)!=QrImportTests.Contact)throw new Exception("Imported contact preview failed");
                UseImportedPayload("javascript:alert(1)");if(open.Enabled)throw new Exception("Raw import enabled unsafe website opening");
                string[] choices={QrImportTests.ExactUrl,QrImportTests.Contact};if(ChooseQrContent(choices,1)!=QrImportTests.Contact)throw new Exception("Selected import content changed");
                string multi=System.IO.Path.Combine(folder,"import-multiple.png");using(var image=QrImportTests.Screenshot(true))Codec.SaveAtomic(multi,Codec.StaticPng(image));
                string retained=CurrentPayload();bool retainedRaw=raw.Checked;await ImportQrImage(multi,-2);if(CurrentPayload()!=retained||raw.Checked!=retainedRaw||imagePath!=oldImage)throw new Exception("Cancelled import changed current state");
                string[] found;using(var image=Codec.Load(multi))found=QrService.ScanAll(image);int contact=Array.IndexOf(found,QrImportTests.Contact);await ImportQrImage(multi,contact);if(CurrentPayload()!=QrImportTests.Contact||open.Enabled||imagePath!=oldImage)throw new Exception("Multi code selected import failed");
                foreach(bool english in new[]{false,true}){bool prior=L.English;L.Set(english?"en":"zh");using(var dialog=new QrImportDialog(choices)){L.Bind(dialog);dialog.CreateControl();dialog.TestChoose(1);if(dialog.TestDisplayed!=QrImportTests.Contact)throw new Exception("Translation altered imported content");using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(System.IO.Path.Combine(folder,"qr-import-choice-"+(english?"en":"zh")+".png"));}}L.Set(prior?"en":"zh");}
            }
            finally{importedPayload=importedDisplay=null;url.Text=oldPayload;raw.Checked=oldRaw;imagePath=oldImage;mode.TestChoice(oldMode);SetResult(oldPayload);}
        }
#endif
    }
}
