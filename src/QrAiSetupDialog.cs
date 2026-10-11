using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QQImageSwitch
{
    sealed class QrAiSetupDialog : Form
    {
        sealed class Choice
        {
            public QrAiModel Model;public string DefaultText;public bool Recommended;
            public override string ToString(){return Model==null?L.T(DefaultText):Model.ToString()+" · "+L.T(Recommended?"默认推荐方案":"备用方案");}
        }
        readonly QrAiOptions draft;readonly List<QrAiModel> models;
        readonly SoftCombo family,basis,control,environment;readonly Label summary;
        readonly CheckBox custom;readonly SoftInput positive,negative;readonly NumericUpDown steps,guidance,strength,clip;
        readonly TableLayoutPanel profilePanel;bool loading;string editingBase,activeFamily;
        internal QrAiOptions Options {get{return draft;}}
        public QrAiSetupDialog(QrAiOptions options,IEnumerable<QrAiModel> available)
        {
            draft=QrAiService.CopyOptions(options);models=available.ToList();foreach(string path in new[]{draft.Base,draft.Control}.Concat(draft.Selections.Values.Where(s=>s!=null).SelectMany(s=>new[]{s.Base,s.Control}))){var model=QrAiService.ReadModel(path);if(model!=null&&!models.Any(m=>string.Equals(m.Path,model.Path,StringComparison.OrdinalIgnoreCase)))models.Add(model);}
            Ui.Configure(this,"模型与环境方案",new Size(850,740));MinimumSize=new Size(690,580);ShowInTaskbar=false;MaximizeBox=false;MinimizeBox=false;
            var outer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(18)};outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(outer);
            var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};var body=Ui.Column(new Padding(0));body.Dock=DockStyle.Top;scroll.Controls.Add(body);outer.Controls.Add(scroll,0,0);
            Ui.Add(body,Ui.Text("默认使用 SD 1.5、DreamShaper 8、QR Monster SD 1.5 与自动兼容环境。其他模型和独立 CPU 环境为备用，主动选择后才下载；切换方案保留已下载文件。",10));
            Ui.Add(body,Ui.Flow(Ui.Button("使用默认方案（推荐）",delegate{UseRecommended();})));
            family=new SoftCombo{Width=300};family.Items.AddRange(new object[]{L.T("SD 1.5（默认推荐）"),L.T("SDXL（备用）")});family.SelectedIndex=draft.Family=="sdxl"?1:0;
            environment=new SoftCombo{Width=430};environment.Items.AddRange(new object[]{L.T("CUDA / CPU 自动兼容环境"),L.T("独立 CPU 环境（备用，速度较慢）")});environment.SelectedIndex=draft.Environment=="cpu"?1:0;
            Ui.Add(body,Ui.Flow(Ui.Text("模型类型"),family));Ui.Add(body,Ui.Flow(Ui.Text("生图环境"),environment));
            basis=new SoftCombo{Width=590,VisibleRows=5};control=new SoftCombo{Width=590,VisibleRows=5};Ui.Add(body,Ui.Text("基础模型"));Ui.Add(body,Ui.Flow(basis));Ui.Add(body,Ui.Text("二维码 ControlNet"));Ui.Add(body,Ui.Flow(control));
            Ui.Add(body,Ui.Flow(Ui.Button("添加本地基础模型…",delegate{ImportModel(false);}),Ui.Button("添加本地 ControlNet…",delegate{ImportModel(true);}))); 
            Ui.Add(body,Ui.Text("本地模型可选择完整 Diffusers 目录或完整 safetensors 文件。普通 Canny / Depth ControlNet 不等于二维码 ControlNet；自定义 ControlNet 需确认经过二维码训练。",9));
            summary=Ui.RawText("",9);Ui.Add(body,summary);
            Ui.Add(body,Ui.Text("提示词按当前模型保存。内置方案使用底图材质和风格，不猜测人物与服装。自定义模型默认采用对应家族方案，尚未逐一验证；有专用触发词时可在下面配套设置。",9));
            custom=new CheckBox{AutoSize=true,Text="为选中模型自定义自动提示词与参数",BackColor=Color.Transparent};L.Bind(custom);Ui.Add(body,custom);
            profilePanel=Ui.Column(new Padding(0));Ui.Add(body,profilePanel);positive=new SoftInput{Dock=DockStyle.Fill,Multiline=true,MaxLength=1800};negative=new SoftInput{Dock=DockStyle.Fill,Multiline=true,MaxLength=1800};Ui.Add(profilePanel,Ui.Text("正向模板（保留 {prompt}，可加入模型专用触发词）",9));Ui.Add(profilePanel,positive);Ui.Add(profilePanel,Ui.Text("负向模板（保留 {negative}）",9));Ui.Add(profilePanel,negative);
            steps=Number(60,0);guidance=Number(12,2);strength=Number(.85m,2);clip=Number(2,0);
            Ui.Add(profilePanel,Ui.Flow(Ui.Text("步数"),steps,Ui.Text("CFG"),guidance,Ui.Text("重绘强度"),strength,Ui.Text("CLIP skip"),clip));Ui.Add(profilePanel,Ui.Text("0 使用配套自动参数；皮肤重绘强度仍受保护上限限制。SDXL 使用默认 CLIP 层。",9));
            Ui.Add(body,Ui.Text("仅支持常规 SD 1.5 / SDXL 四通道基础模型。SD2、FLUX、SD3、Refiner、Inpaint 与 Turbo / Lightning / LCM 等特殊方案不能直接套用；模型能加载不代表融合或扫码已通过。",9));
            var cancel=Ui.Button("取消",delegate{DialogResult=DialogResult.Cancel;Close();});cancel.DialogResult=DialogResult.Cancel;var apply=Ui.Button("应用方案",delegate{Apply();},true);outer.Controls.Add(Ui.Flow(apply,cancel),0,1);AcceptButton=apply;CancelButton=cancel;
            family.SelectedIndexChanged+=delegate{if(!loading){StoreProfile();StoreSelection();LoadFamily();}};basis.SelectedIndexChanged+=delegate{if(!loading){StoreProfile();LoadProfile();}};environment.SelectedIndexChanged+=delegate{UpdateSummary();};custom.CheckedChanged+=delegate{profilePanel.Visible=custom.Checked;};
            LoadFamily();
        }
        static NumericUpDown Number(decimal maximum,int places){return new NumericUpDown{Minimum=0,Maximum=maximum,DecimalPlaces=places,Increment=places==0?1:.05m,Width=85};}
        string Family {get{return family.SelectedIndex==1?"sdxl":"sd15";}}
        string Runtime {get{return environment.SelectedIndex==1?"cpu":"cuda";}}
        static string PathOf(SoftCombo combo){return combo.SelectedIndex<0?null:((Choice)combo.Items[combo.SelectedIndex]).Model==null?null:((Choice)combo.Items[combo.SelectedIndex]).Model.Path;}
        void Fill(SoftCombo combo,bool isControl,string selected)
        {
            combo.Items.Clear();combo.SelectedIndex=-1;string defaultText=isControl?(Family=="sdxl"?"自动下载：QR Monster SDXL（备用）":"自动下载：QR Monster SD 1.5（默认推荐）"):(Family=="sdxl"?"自动下载：SDXL Base 1.0（备用）":"自动下载：DreamShaper 8（默认推荐）");combo.Items.Add(new Choice{DefaultText=defaultText});
            string recommendedPath=QrAiService.ResolveModelPackage(draft.Root,isControl?"controlnet/qr-monster-sd15":"base/dreamshaper-8");foreach(var model in models.Where(m=>m.Control==isControl&&m.Family==Family))combo.Items.Add(new Choice{Model=model,Recommended=Family=="sd15"&&string.Equals(model.Path,recommendedPath,StringComparison.OrdinalIgnoreCase)});int index=combo.Items.FindIndex(item=>((Choice)item).Model!=null&&string.Equals(((Choice)item).Model.Path,selected,StringComparison.OrdinalIgnoreCase));combo.SelectedIndex=index<0?0:index;
        }
        void LoadFamily()
        {
            loading=true;try{QrAiSelection selection;if(!draft.Selections.TryGetValue(Family,out selection)||selection==null)selection=Family==draft.Family?new QrAiSelection{Base=draft.Base,Control=draft.Control,Environment=draft.Environment}:new QrAiSelection();Fill(basis,false,selection.Base);Fill(control,true,selection.Control);environment.SelectedIndex=selection.Environment=="cpu"?1:0;activeFamily=Family;LoadProfile();}finally{loading=false;}UpdateSummary();
        }
        void StoreSelection(){if(activeFamily!=null)draft.Selections[activeFamily]=new QrAiSelection{Base=PathOf(basis),Control=PathOf(control),Environment=Runtime};}
        void UseRecommended()
        {
            StoreProfile();StoreSelection();draft.Family=Family;draft.Base=PathOf(basis);draft.Control=PathOf(control);draft.Environment=Runtime;QrAiService.UseRecommendedScheme(draft);loading=true;try{family.SelectedIndex=0;}finally{loading=false;}LoadFamily();
        }
        void LoadProfile()
        {
            editingBase=PathOf(basis);custom.Enabled=editingBase!=null;QrAiGenerationProfile profile=null;bool found=editingBase!=null&&draft.Profiles.TryGetValue(editingBase,out profile)&&profile!=null;if(!found)profile=new QrAiGenerationProfile{Family=Family,Id="custom",PromptTemplate="{prompt}",NegativeTemplate="{negative}"};
            custom.Checked=found;positive.Text=profile.PromptTemplate;negative.Text=profile.NegativeTemplate;steps.Value=Math.Max(0,Math.Min(60,profile.Steps));guidance.Value=Finite(profile.Guidance,12);strength.Value=Finite(profile.Strength,.85m);clip.Value=Math.Max(0,Math.Min(2,profile.ClipSkip));clip.Enabled=Family=="sd15";if(Family=="sdxl")clip.Value=0;profilePanel.Visible=custom.Checked;UpdateSummary();
        }
        static decimal Finite(double value,decimal max){return double.IsNaN(value)||double.IsInfinity(value)?0:(decimal)Math.Max(0,Math.Min((double)max,value));}
        void StoreProfile()
        {
            if(editingBase==null)return;if(!custom.Checked){draft.Profiles.Remove(editingBase);return;}
            draft.Profiles[editingBase]=new QrAiGenerationProfile{Family=activeFamily??Family,Id="custom",PromptTemplate=positive.Text,NegativeTemplate=negative.Text,Steps=(int)steps.Value,Guidance=(double)guidance.Value,Strength=(double)strength.Value,ClipSkip=(int)clip.Value};
        }
        void UpdateSummary(){if(summary==null||basis==null)return;summary.Text=L.T("生图环境实际位置：")+QrAiService.RuntimeFolder(draft.Root,Runtime);}
        void ImportModel(bool isControl)
        {
            using(var dialog=new QrAiModelImportDialog())if(L.Show(dialog,this)==DialogResult.OK)
            {
                var model=QrAiService.ReadModel(dialog.ModelPath,isControl);if(model==null||model.Family!=Family){MessageBox.Show(this,L.T("所选模型不完整或类型不受支持，请重新扫描 / 校验模型。"),L.T("模型与环境方案"));return;}
                if(!models.Any(m=>string.Equals(m.Path,model.Path,StringComparison.OrdinalIgnoreCase)))models.Add(model);StoreProfile();loading=true;try{Fill(isControl?control:basis,isControl,model.Path);if(!isControl)LoadProfile();}finally{loading=false;}
            }
        }
        void Apply()
        {
            try{StoreProfile();StoreSelection();draft.Family=Family;draft.Environment=Runtime;draft.Base=PathOf(basis);draft.Control=PathOf(control);QrAiService.ValidateScheme(draft);DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,L.T(ex.Message),L.T("模型与环境方案"),MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
#if SELF_TEST
        internal void CaptureImage(string path)
        {
            Opacity=0;Show();CreateControl();PerformLayout();Application.DoEvents();using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Hide();
        }
        internal static void CheckBehavior(string folder)
        {
            var options=new QrAiOptions{Root=folder};var entries=new[]{new QrAiModel{Path=Path.Combine(folder,"a"),Name="A",Family="sd15"},new QrAiModel{Path=Path.Combine(folder,"b"),Name="B",Family="sdxl"},new QrAiModel{Path=Path.Combine(folder,"q"),Name="Q",Family="sdxl",Control=true}};
            using(var dialog=new QrAiSetupDialog(options,entries))
            {
                dialog.CaptureImage(Path.Combine(folder,"qr-ai-schemes-"+(L.English?"en":"zh")+".png"));
                if(dialog.basis.Items.Count!=2||dialog.control.Items.Count!=1||dialog.custom.Enabled)throw new Exception("Initial model scheme filters/defaults failed");dialog.basis.SelectedIndex=1;dialog.environment.SelectedIndex=1;dialog.custom.Checked=true;dialog.positive.Text="material, {prompt}";dialog.family.SelectedIndex=1;if(dialog.basis.Items.Count!=2||dialog.control.Items.Count!=2||dialog.environment.SelectedIndex!=0)throw new Exception("Family scheme crossed model or runtime choices");dialog.family.SelectedIndex=0;if(dialog.basis.SelectedIndex!=1||dialog.environment.SelectedIndex!=1||!dialog.custom.Checked||dialog.positive.Text!="material, {prompt}")throw new Exception("Per-model prompt/runtime scheme not retained");if(options.Profiles.Count!=0||options.Selections.Count!=0)throw new Exception("Cancelled scheme changed live options");dialog.CaptureImage(Path.Combine(folder,"qr-ai-schemes-custom-"+(L.English?"en":"zh")+".png"));
                dialog.family.SelectedIndex=1;dialog.basis.SelectedIndex=1;dialog.environment.SelectedIndex=1;dialog.UseRecommended();if(dialog.family.SelectedIndex!=0||dialog.basis.SelectedIndex!=0||dialog.control.SelectedIndex!=0||dialog.environment.SelectedIndex!=0||dialog.custom.Checked||dialog.draft.Selections["sdxl"].Base!=entries[1].Path||dialog.draft.Selections["sdxl"].Environment!="cpu")throw new Exception("Recommended reset lost standby choices or kept custom resources");
            }
        }
#endif
    }
    sealed class QrAiModelImportDialog : Form
    {
        internal string ModelPath;
        public QrAiModelImportDialog()
        {
            Ui.Configure(this,"选择本地模型",new Size(540,190));ShowInTaskbar=false;MaximizeBox=MinimizeBox=false;var body=Ui.Column(new Padding(18));body.Dock=DockStyle.Fill;Controls.Add(body);Ui.Add(body,Ui.Text("选择完整模型目录，或完整 safetensors 文件。",10));
            Ui.Add(body,Ui.Flow(Ui.Button("选择模型目录…",delegate{using(var d=new FolderBrowserDialog())if(L.Show(d,this)==DialogResult.OK){ModelPath=d.SelectedPath;DialogResult=DialogResult.OK;Close();}}),Ui.Button("选择模型文件…",delegate{using(var d=new OpenFileDialog{Filter="Safetensors|*.safetensors",CheckFileExists=true})if(L.Show(d,this)==DialogResult.OK){ModelPath=d.FileName;DialogResult=DialogResult.OK;Close();}})));
        }
    }
}
