using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQImageSwitch
{
    public sealed partial class QrPage
    {
        FlowLayoutPanel borderRow;Label borderHelp,aiRootLabel,aiPromptResult,aiBasePath,aiControlPath,aiPositionHelp,aiAutoPlaceHelp,aiPromptLabel,aiSchemeSummary,aiRuntimePath;Button aiOpenBase,aiOpenControl,aiScheme;
        TableLayoutPanel aiPanel;SoftCombo aiSource,aiPolicy,aiBase,aiControl,aiSize;SoftInput aiPrompt;
        CheckBox aiAuto,aiBackground,aiPlace,aiManual;bool changingAiPlacement;Button aiGenerate;QrAiOptions aiOptions=QrAiService.LoadOptions();
        Bitmap aiImage;QrRegion aiRegion;string aiKey;bool aiPlaced,refreshingAi;
        readonly object aiProgressLock=new object();string aiPendingProgress,aiTerminalStatus;bool aiProgressQueued;
        void InitializeAi()
        {
            aiPositionHelp=Ui.Text("生成前，顶部预览显示底图与普通二维码示意；可拖动或修改大小、横向、纵向。AI 优先使用手动位置，包括皮肤区域；扫码失败后可在结果框选择「帮我选择自动避让」。调整后请重新生成。",9);Ui.Add(Body,aiPositionHelp);
            aiAutoPlaceHelp=Ui.Text("自动避让模式由 AI 选择位置；生成前只显示底图，生成后显示实际融合结果。切换到「手动放置（位置由你指定）」可自行定位。",9);Ui.Add(Body,aiAutoPlaceHelp);
            aiPanel=Ui.Column(new Padding(0));Ui.Add(Body,aiPanel);
            Ui.Add(aiPanel,Ui.Text("本地 AI：自动准备环境与模型、识别画面并生成提示词，扫码不通过时自动重试。图片与提示词不上传。首次下载需要联网与较大磁盘空间。",9));
            Ui.Add(aiPanel,Ui.Text("默认方案：SD 1.5 · DreamShaper 8 · QR Monster SD 1.5 · CUDA / CPU 自动兼容环境。点击一键 AI 生成即可准备所缺资源，其他模型仅在主动选择后下载。",9));
            Ui.Add(aiPanel,Ui.Flow(Ui.Button("使用默认方案（推荐）",delegate{if(Busy)return;var options=CaptureAi();QrAiService.UseRecommendedScheme(options);AdoptAiOptions(options);AiSettingsChanged();Status.Text=L.T("默认方案已选定；点击一键 AI 生成即可自动准备。");}),Ui.Button("模型与环境方案…",delegate{ChooseAiScheme();})));aiSchemeSummary=Ui.RawText("",9);Ui.Add(aiPanel,aiSchemeSummary);aiRuntimePath=Ui.RawText("",9);Ui.Add(aiPanel,aiRuntimePath);Ui.Add(aiPanel,Ui.Flow(Ui.Button("打开生图环境文件夹",delegate{string folder=QrAiService.RuntimeFolder(aiOptions.Root,aiOptions.Environment);if(Directory.Exists(folder))System.Diagnostics.Process.Start("explorer.exe","\""+folder+"\"");else Status.Text=L.T("模型目录尚未创建，请先下载 / 修复环境。");})));
            aiRootLabel=Ui.RawText(aiOptions.Root,9);Ui.Add(aiPanel,Ui.Flow(Ui.Button("选择模型目录…",delegate{if(Busy)return;using(var d=new FolderBrowserDialog{SelectedPath=Directory.Exists(aiOptions.Root)?aiOptions.Root:""})if(L.Show(d,FindForm())==DialogResult.OK){QrAiStorage.UseDirectory(aiOptions,d.SelectedPath);aiRootLabel.Text=aiOptions.Root;QrAiService.SaveOptions(aiOptions);RefreshAiModels();Schedule();}}),Ui.Button("打开模型目录",delegate{try{if(!Directory.Exists(aiOptions.Root)){Status.Text=L.T("模型目录尚未创建，请先下载 / 修复环境。");return;}System.Diagnostics.Process.Start("explorer.exe","\""+aiOptions.Root+"\"");}catch(Exception ex){Status.Text=L.T("无法打开模型文件夹：")+ex.Message;}}),Ui.Button("重新扫描 / 校验模型",async delegate{if(Busy)return;var options=CaptureAi();await Run(async token=>{await Task.Run(()=>QrAiService.CheckModels(options,AiProgress,token),token);RefreshAiModels();return L.T("模型扫描与完整性校验完成。");},false,true);RestoreAiControls();}),aiRootLabel));
            Ui.Add(aiPanel,Ui.Text("自动存储优先 D:\\AI-Models；空间不足时依次选择 E、F 等数据盘。模型、私有 Python 环境和依赖缓存均放在同一目录。没有足够空间时请手动选择目录，不会默认写入 C 盘。",9));
            aiSource=Combo(220,"自动选择下载源","官方源","中国镜像源");aiPolicy=Combo(220,"自动适配显存","质量优先","低显存优先");Ui.Add(aiPanel,Ui.Flow(Ui.Text("下载来源"),aiSource,Ui.Text("性能策略"),aiPolicy));
            Ui.Add(aiPanel,Ui.Text("自动模式会检测官方与镜像源的可达性和延迟；Windows 地区设为中国大陆时优先可达镜像。已校验缓存不测网。下载显示大小、实时速度与校验状态。",9));
            Ui.Add(aiPanel,Ui.Text("自动记住目录并查找已有模型。官方资源按大小与 SHA 校验；自定义模型检查结构并记录内容哈希。完整外部模型直接引用，外部文件不移动或修改；重新扫描会完整校验，缺失或损坏只补所需文件。",9));
            aiSize=Combo(260,"自动确保模块清晰","最长边至少 1024 像素","最长边至少 1536 像素","最长边至少 2048 像素");Ui.Add(aiPanel,Ui.Flow(Ui.Text("AI 输出尺寸"),aiSize));aiSize.SelectedIndexChanged+=delegate{AiSettingsChanged();};
            aiBase=new SoftCombo{Width=300,VisibleRows=5};aiControl=new SoftCombo{Width=300,VisibleRows=5};Ui.Add(aiPanel,Ui.Flow(Ui.Text("基础模型"),aiBase,Ui.Text("二维码 ControlNet"),aiControl));
            aiBasePath=Ui.RawText("",9);aiControlPath=Ui.RawText("",9);aiOpenBase=Ui.Button("打开基础模型文件夹",delegate{OpenAiModelFolder(aiBase);});aiOpenControl=Ui.Button("打开 ControlNet 文件夹",delegate{OpenAiModelFolder(aiControl);});Ui.Add(aiPanel,Ui.Text("基础模型实际位置：",9));Ui.Add(aiPanel,aiBasePath);Ui.Add(aiPanel,Ui.Flow(aiOpenBase));Ui.Add(aiPanel,Ui.Text("ControlNet 实际位置：",9));Ui.Add(aiPanel,aiControlPath);Ui.Add(aiPanel,Ui.Flow(aiOpenControl));
            aiBase.SelectedIndexChanged+=delegate{MatchAiControl();SaveAiSelection();AiSettingsChanged();};aiControl.SelectedIndexChanged+=delegate{SaveAiSelection();AiSettingsChanged();};aiPolicy.SelectedIndexChanged+=delegate{AiSettingsChanged();};
            aiAuto=new CheckBox{AutoSize=true,BackColor=Color.Transparent,Text="自动识别画面并生成提示词",Checked=true};L.Bind(aiAuto);Ui.Add(aiPanel,aiAuto);
            aiBackground=new CheckBox{AutoSize=true,BackColor=Color.Transparent,Text="空白背景自动补景",Checked=false};L.Bind(aiBackground);Ui.Add(aiPanel,aiBackground);aiBackground.CheckedChanged+=delegate{AiSettingsChanged();};
            aiPlace=new CheckBox{AutoSize=true,BackColor=Color.Transparent,Text="自动避开皮肤与头发放置",Checked=false};aiManual=new CheckBox{AutoSize=true,BackColor=Color.Transparent,Text="手动放置（位置由你指定）",Checked=true};L.Bind(aiPlace);L.Bind(aiManual);Ui.Add(aiPanel,aiPlace);Ui.Add(aiPanel,aiManual);aiPlace.CheckedChanged+=delegate{AiPlacementChanged(aiPlace);};aiManual.CheckedChanged+=delegate{AiPlacementChanged(aiManual);};
            Ui.Add(aiPanel,Ui.Text("「自动避开皮肤与头发放置」与「手动放置（位置由你指定）」最多勾选一个，也可全部关闭。「自动识别画面并生成提示词」独立控制提示词；取消勾选即可手动编辑。",9));
            aiPrompt=new SoftInput{Dock=DockStyle.Fill,Multiline=true,MaxLength=1800};aiPromptLabel=Ui.Text("手动提示词（关闭「自动识别画面并生成提示词」后可编辑）",9);Ui.Add(aiPanel,aiPromptLabel);Ui.Add(aiPanel,aiPrompt);aiPrompt.Enabled=false;
            aiPromptResult=Ui.Text("生成后在这里显示自动提示词。",9);Ui.Add(aiPanel,aiPromptResult);
            aiAuto.CheckedChanged+=delegate{aiPrompt.Enabled=!aiAuto.Checked;AiSettingsChanged();};aiPrompt.TextChanged+=delegate{AiSettingsChanged();};
            Ui.Add(aiPanel,Ui.Flow(Ui.Button("下载模型 / 检查并修复环境",async delegate{if(Busy)return;var options=CaptureAi();await Run(async token=>{await PrepareAiOptions(options,token);AdoptAiOptions(options);return L.T("本地 AI 环境与模型已准备，请选择底图后生成。")+"\n"+AiDownloadSummary();},false,true);RestoreAiControls();})));
            Ui.Add(aiPanel,Ui.Text("下载和校验在后台进行，可切换到其他工具。当前二维码页暂时锁定，防止修改正在使用的模型；返回后可查看整体进度或停止任务。模型完成数只计入校验通过的文件。",9));
            Ui.Add(aiPanel,Ui.Flow(Ui.Button("删除选中基础模型…",async delegate{await CleanAi(false,SelectedAi(aiBase));}),Ui.Button("删除选中 ControlNet…",async delegate{await CleanAi(false,SelectedAi(aiControl));}),Ui.Button("清理全部 AI 模型与环境…",async delegate{await CleanAi(true,null);}))); 
            Ui.Add(aiPanel,Ui.Text("默认目录可更换。模型放入 base 与 controlnet 子目录后自动发现；支持 SD 1.5 / SDXL Diffusers 目录或配套 safetensors。首次模型下载约 7–12 GiB，推理环境另占空间；中国镜像同样校验文件。8GB 显存使用卸载策略，高显存优先质量。无兼容 CUDA 时使用 CPU，速度较慢。",9));
            Ui.Add(aiPanel,Ui.Text("AI 模式不附加白色边框。生成会重绘部分内容，扫码成功与自然程度存在取舍。修改网址、底图或二维码位置后需重新生成；文字改动无需重新加载模型，但导出会再次扫码。任务完成、失败或停止后推理进程退出，释放模型资源。",9));
            aiScheme=ActionButton("模型与环境方案…",delegate{ChooseAiScheme();});aiGenerate=ActionButton("一键 AI 生成",async delegate{await GenerateAi();});RefreshAiModels();L.Changed+=AiLanguageChanged;
        }
        void UpdateAiMode(){if(aiPanel==null)return;bool ai=mode.SelectedIndex==3,automatic=aiPlace.Checked;aiPanel.Visible=aiGenerate.Visible=aiScheme.Visible=ai;aiPositionHelp.Visible=ai&&!automatic;aiAutoPlaceHelp.Visible=ai&&automatic;qrPositionRow.Visible=placement.Visible=placementCaption.Visible=!ai||!automatic;aiAuto.Visible=aiPrompt.Visible=aiPromptLabel.Visible=ai;aiPrompt.Enabled=ai&&!aiAuto.Checked;view.TextPlacement=mode.SelectedIndex!=0&&!(ai&&automatic);borderRow.Visible=borderHelp.Visible=!ai;bool prior=loading;loading=true;try{size.Minimum=ai?30:10;if(ai&&!aiPlaced){aiPlaced=true;size.Value=60;y.Value=75;}}finally{loading=prior;}if(!ai)view.PlacementRegion=RectangleF.Empty;}
        void AiPlacementChanged(CheckBox choice){if(changingAiPlacement)return;changingAiPlacement=true;try{if(choice.Checked){(choice==aiPlace?aiManual:aiPlace).Checked=false;aiAuto.Checked=true;}}finally{changingAiPlacement=false;}UpdateAiMode();AiSettingsChanged();}
        void ManualAiPosition(){if(loading||mode.SelectedIndex!=3)return;aiTerminalStatus=null;Schedule();}
        QrAiOptions CaptureAi()
        {
            return new QrAiOptions{Root=aiOptions.Root,AutoRoot=aiOptions.AutoRoot,Family=aiOptions.Family,Environment=aiOptions.Environment,Selections=aiOptions.Selections,Profiles=aiOptions.Profiles,Source=new[]{"auto","official","mirror"}[aiSource.SelectedIndex],Policy=new[]{"auto","quality","low"}[aiPolicy.SelectedIndex],Base=SelectedAi(aiBase),Control=SelectedAi(aiControl),AutoPrompt=aiAuto.Checked,AutoBackground=aiBackground.Checked,AutoPlace=aiPlace.Checked,Prompt=aiPrompt.Text,Edge=new[]{0,1024,1536,2048}[aiSize.SelectedIndex]};
        }
        void AiSettingsChanged(){aiTerminalStatus=null;Schedule();}
        string AiDownloadSummary(){lock(aiProgressLock)return aiPendingProgress==null?"":aiPendingProgress.Split('\n')[0];}
        void RestoreAiControls(){string terminal=Status.Text;ChangeText();aiTerminalStatus=terminal;}
        async Task PrepareAiOptions(QrAiOptions options,System.Threading.CancellationToken token,bool models=true)
        {
            await Task.Run(()=>QrAiStorage.EnsureStorage(options,models),token);token.ThrowIfCancellationRequested();
            aiOptions.Root=options.Root;aiOptions.AutoRoot=options.AutoRoot;aiRootLabel.Text=options.Root;QrAiService.SaveOptions(options);
            // Persist the chosen disk before transfers so cancellation/restart can resume
            // there even when a partial install has reduced its available free space.
            await Task.Run(()=>QrAiService.Prepare(options,AiProgress,token,models),token);
        }
        void AdoptAiOptions(QrAiOptions options){aiOptions.Root=options.Root;aiOptions.AutoRoot=options.AutoRoot;aiOptions.Family=options.Family;aiOptions.Environment=options.Environment;aiOptions.Selections=options.Selections;aiOptions.Profiles=options.Profiles;aiOptions.Base=options.Base;aiOptions.Control=options.Control;aiRootLabel.Text=options.Root;QrAiService.SaveOptions(options);RefreshAiModels();SelectAiModels(options);}
        void ChooseAiScheme()
        {
            if(Busy)return;var options=CaptureAi();QrAiService.RememberSelection(options);using(var dialog=new QrAiSetupDialog(options,QrAiService.Discover(options.Root)))if(L.Show(dialog,FindForm())==DialogResult.OK){var next=dialog.Options;AdoptAiOptions(next);AiSettingsChanged();Status.Text=L.T("模型方案已切换；点击下载 / 修复，或一键 AI 生成自动准备所选资源。");}
        }
        async Task CleanAi(bool all,string selected)
        {
            if(Busy)return;if(!all&&selected==null){Status.Text=L.T("请先选中要删除的模型。");return;}
            var options=CaptureAi();bool clearBase=all||string.Equals(SelectedAi(aiBase),selected,StringComparison.OrdinalIgnoreCase),clearControl=all||string.Equals(SelectedAi(aiControl),selected,StringComparison.OrdinalIgnoreCase);await Run(async token=>
            {
                var plans=await Task.Run(()=>{var list=new System.Collections.Generic.List<QrAiDeletePlan>();list.Add(all?QrAiStorage.PlanAll(options.Root):QrAiStorage.PlanSelected(options.Root,selected));string legacy=QrAiStorage.LegacyDefaultRoot;if(all&&Directory.Exists(legacy)&&!string.Equals(Path.GetFullPath(legacy),Path.GetFullPath(options.Root),StringComparison.OrdinalIgnoreCase))list.Add(QrAiStorage.PlanAll(legacy));return list;},token);
                token.ThrowIfCancellationRequested();string description=string.Join("\n\n",plans.Select(plan=>plan.Root+"\n"+plan.Description+"\n"+L.T("文件数：")+plan.FileCount+" · "+L.T("预计释放：")+(plan.Bytes/(1024d*1024*1024)).ToString("0.000")+" GiB · "+L.T("外部引用数：")+plan.ExternalReferences));
                if(!all)description+= "\n\n"+selected;description+="\n\n"+L.T("删除文件无法撤销；外部模型原文件保留，之后仍可能被自动扫描发现。确认继续？");
                if(MessageBox.Show(FindForm(),description,L.T("确认清理 AI 文件"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return L.T("已取消清理。");
                foreach(var plan in plans)await QrAiStorage.Execute(plan,AiProgress,token);
                if(clearBase)aiOptions.Base=null;if(clearControl)aiOptions.Control=null;if(aiImage!=null){aiImage.Dispose();aiImage=null;}aiKey=null;completed=null;RefreshAiModels();refreshingAi=true;try{if(clearBase)aiBase.SelectedIndex=-1;if(clearControl)aiControl.SelectedIndex=-1;}finally{refreshingAi=false;UpdateAiLocations();}QrAiService.SaveOptions(aiOptions);return L.T("AI 文件清理完成；外部模型原文件保留。");
            },false,true);RestoreAiControls();
        }
        static string SelectedAi(SoftCombo combo){return combo!=null&&combo.SelectedIndex>=0&&combo.SelectedIndex<combo.Items.Count?((QrAiModel)combo.Items[combo.SelectedIndex]).Path:null;}
        void RefreshAiModels()
        {
            refreshingAi=true;try{string chosen=aiOptions.Base??SelectedAi(aiBase),control=aiOptions.Control??SelectedAi(aiControl);var models=QrAiService.Discover(aiOptions.Root);aiBase.Items.Clear();aiControl.Items.Clear();aiBase.SelectedIndex=-1;aiControl.SelectedIndex=-1;
            foreach(var model in models.Where(m=>m.Family==aiOptions.Family))if(model.Control)aiControl.Items.Add(model);else aiBase.Items.Add(model);
            int index=aiBase.Items.FindIndex(m=>((QrAiModel)m).Path==chosen);aiBase.SelectedIndex=index>=0?index:-1;index=aiControl.Items.FindIndex(m=>((QrAiModel)m).Path==control);if(index>=0)aiControl.SelectedIndex=index;MatchAiControl();aiBase.Invalidate();aiControl.Invalidate();}finally{refreshingAi=false;UpdateAiLocations();}
        }
        void SaveAiSelection(){UpdateAiLocations();if(refreshingAi||aiBase==null||aiControl==null)return;aiOptions.Base=SelectedAi(aiBase);aiOptions.Control=SelectedAi(aiControl);QrAiService.SaveOptions(aiOptions);}
        void UpdateAiLocations()
        {
            if(aiBasePath==null||aiControlPath==null)return;string basePath=SelectedAi(aiBase),controlPath=SelectedAi(aiControl);
            aiBasePath.Text=basePath??"—";aiControlPath.Text=controlPath??"—";aiOpenBase.Enabled=QrAiService.ModelFolder(basePath)!=null;aiOpenControl.Enabled=QrAiService.ModelFolder(controlPath)!=null;
            if(aiSchemeSummary!=null){var options=CaptureAi();bool recommended=QrAiService.RecommendedScheme(options);aiSchemeSummary.Text=L.T("当前方案：")+L.T(recommended?"默认推荐方案":"备用方案")+" · "+(recommended?"SD 1.5 / DreamShaper 8 / QR Monster SD 1.5":options.Family=="sdxl"?"SDXL":"SD 1.5")+" · "+L.T(options.Environment=="cpu"?"独立 CPU 环境（备用，速度较慢）":"CUDA / CPU 自动兼容环境")+" · "+L.T("提示词方案：")+QrAiService.ProfileFor(options).Id;aiRuntimePath.Text=L.T("生图环境实际位置：")+QrAiService.RuntimeFolder(options.Root,options.Environment);}
        }
        void OpenAiModelFolder(SoftCombo combo)
        {
            string folder=QrAiService.ModelFolder(SelectedAi(combo));if(folder==null){Status.Text=L.T("模型所在目录不存在，请重新扫描或选择模型。");UpdateAiLocations();return;}
            try{System.Diagnostics.Process.Start("explorer.exe","\""+folder+"\"");}catch(Exception ex){Status.Text=L.T("无法打开模型文件夹：")+ex.Message;}
        }
        void SelectAiModels(QrAiOptions options){refreshingAi=true;try{int index=aiBase.Items.FindIndex(m=>((QrAiModel)m).Path==options.Base);aiBase.SelectedIndex=index;index=aiControl.Items.FindIndex(m=>((QrAiModel)m).Path==options.Control);aiControl.SelectedIndex=index;}finally{refreshingAi=false;UpdateAiLocations();}QrAiService.SaveOptions(CaptureAi());}
        void MatchAiControl(){if(refreshingAi||aiBase.SelectedIndex<0)return;string family=((QrAiModel)aiBase.Items[aiBase.SelectedIndex]).Family;int index=aiControl.Items.FindIndex(m=>((QrAiModel)m).Family==family&&QrAiService.KnownQrControl((QrAiModel)m));if(aiControl.SelectedIndex<0||((QrAiModel)aiControl.Items[aiControl.SelectedIndex]).Family!=family)aiControl.SelectedIndex=index;}
        string AiGenerationKey(){var o=CaptureAi();return imagePath+"|"+(imagePath!=null&&File.Exists(imagePath)?File.GetLastWriteTimeUtc(imagePath).Ticks.ToString():"")+"|"+CurrentPayload()+"|"+raw.Checked+"|"+size.Value+"|"+x.Value+"|"+y.Value+"|"+o.Root+"|"+o.Family+"|"+o.Environment+"|"+Inspection.Json().Serialize(QrAiService.ProfileFor(o).Request())+"|"+o.Base+"|"+o.Control+"|"+o.Policy+"|"+o.AutoPrompt+"|"+o.AutoBackground+"|"+o.AutoPlace+"|"+o.Prompt+"|"+o.Edge;}
        void AiProgress(string value)
        {
            if(IsDisposed||Disposing||!IsHandleCreated)return;lock(aiProgressLock){aiPendingProgress=value;if(aiProgressQueued)return;aiProgressQueued=true;}
            try{BeginInvoke((MethodInvoker)delegate{string next;lock(aiProgressLock){next=aiPendingProgress;aiProgressQueued=false;}if(!IsDisposed&&!Disposing&&Busy)Status.Text=next;});}catch(InvalidOperationException){lock(aiProgressLock)aiProgressQueued=false;}
        }
        void ShowAiPreview()
        {
            Bitmap next=null;RectangleF bounds=RectangleF.Empty;bool current=aiImage!=null&&aiKey==AiGenerationKey();
            if(current){next=(Bitmap)aiImage.Clone();text.Copy().Draw(next,false);}else if(imagePath!=null)using(var original=Codec.Load(imagePath)){next=Codec.Fit(original,Math.Max(1,(int)(original.Width*Math.Min(1,900.0/Math.Max(original.Width,original.Height)))),Math.Max(1,(int)(original.Height*Math.Min(1,900.0/Math.Max(original.Width,original.Height)))),Color.White);if(!aiPlace.Checked)try{var region=QrAiCondition.Layout(CurrentPayload(),original.Size,Math.Max(512,CaptureAi().Edge),(int)size.Value,(int)x.Value,(int)y.Value,raw.Checked);bounds=QrAiCondition.PreviewBounds(region,original.Size,next.Size);QrAiCondition.DrawPreview(next,CurrentPayload(),bounds,raw.Checked);}catch(ArgumentException){next.Dispose();view.PlacementRegion=RectangleF.Empty;throw;}}
            view.Image=next;view.PlacementRegion=RectangleF.Empty;if(preview!=null)preview.Dispose();preview=next;completed=PreviewKey();
            if(!Busy)Status.Text=aiTerminalStatus??L.T(current?"AI 结果已通过扫码，可调整文字后导出。":aiPlace.Checked?"自动避让模式：生成前只显示底图，AI 将自动选择二维码位置。":"预览中的普通二维码仅用于定位；拖动确定位置，再点击一键 AI 生成。");
        }
        async Task GenerateAi()
        {
            if(Busy)return;if(imagePath==null){Status.Text=L.T("请先选择底图。");return;}
            bool rawPayload=raw.Checked;string input=imagePath,link;try{link=QrService.Payload(CurrentPayload(),rawPayload);}catch(Exception e){Status.Text=L.T(e.Message);return;}
            var options=CaptureAi();bool automatic=options.AutoPlace;QrAiScanException scanFailure=null;int percent=(int)size.Value,px=(int)x.Value,py=(int)y.Value;
            await Run(async token=>
            {
                bool needModels=options.Base==null||options.Control==null;
                await PrepareAiOptions(options,token,needModels);AdoptAiOptions(options);await Task.Run(()=>QrAiService.EnsureCaption(options,AiProgress,token),token);RefreshAiModels();
                var hardware=await Task.Run(()=>QrAiService.Probe(options,AiProgress,token),token);
                if(needModels){SelectAiModels(options);MatchAiControl();options=CaptureAi();options.AutoPlace=automatic;}
                if(options.Base==null||options.Control==null)throw new InvalidOperationException("没有找到配套的基础模型和二维码 ControlNet。");
                string job=Path.Combine(options.Root,"engine","jobs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
                try
                {
                    string conditionPath=Path.Combine(job,"condition.png"),inputPath=Path.Combine(job,"input.png"),output=Path.Combine(job,"result.png");QrRegion region;
                    int edge=hardware.total_gb>=16&&hardware.free_gb>=12&&options.Policy!="low"?1024:hardware.total_gb>=7?768:512;
                    edge=Math.Max(edge,options.Edge);
                    using(var original=Codec.Load(input)){
                        Codec.SaveAtomic(inputPath,Codec.StaticPng(original));using(var condition=QrAiCondition.Create(link,original.Size,edge,percent,px,py,out region,-1,rawPayload))Codec.SaveAtomic(conditionPath,Codec.StaticPng(condition));
                        for(int mask=0;mask<8;mask++){QrRegion variant;using(var condition=QrAiCondition.Create(link,original.Size,edge,percent,px,py,out variant,mask,rawPayload))Codec.SaveAtomic(Path.Combine(job,"condition-mask"+mask+".png"),Codec.StaticPng(condition));}
                    }
                    var generation=await QrAiService.Generate(options,inputPath,conditionPath,output,AiProgress,value=>{if(IsHandleCreated&&!IsDisposed)BeginInvoke((MethodInvoker)delegate{aiPromptResult.Text=value;});},path=>{token.ThrowIfCancellationRequested();using(var candidate=Codec.Load(path))return QrAiCondition.Validate(candidate,link);},token,link,rawPayload);
                    object adjusted;if(generation.TryGetValue("region",out adjusted)&&adjusted is System.Collections.Generic.Dictionary<string,object>){var bounds=(System.Collections.Generic.Dictionary<string,object>)adjusted;region.X=Convert.ToInt32(bounds["x"]);region.Y=Convert.ToInt32(bounds["y"]);region.Size=Convert.ToInt32(bounds["size"]);region.Width=Convert.ToInt32(bounds["width"]);region.Height=Convert.ToInt32(bounds["height"]);}token.ThrowIfCancellationRequested();using(var generated=Codec.Load(output)){if(aiImage!=null)aiImage.Dispose();aiImage=(Bitmap)generated.Clone();aiRegion=QrAiCondition.Resize(region,generated.Size);}RefreshAiModels();SelectAiModels(options);aiKey=AiGenerationKey();ShowAiPreview();
                    return L.T("AI 生成完成并通过扫码，模型资源已释放。");
                }
                catch(QrAiScanException ex){if(automatic)throw;scanFailure=ex;return L.T("手动位置未通过扫码。请调整位置或大小，或选择自动避让重试。");}
                finally{try{foreach(string path in Directory.GetFiles(job))File.Delete(path);Directory.Delete(job);}catch{scanFailure=null;throw;}}
            },false,true);RestoreAiControls();Schedule();
            if(scanFailure!=null&&!IsDisposed&&!Disposing)using(var dialog=new QrAiRetryDialog(scanFailure.Message))if(L.Show(dialog,FindForm())==DialogResult.Retry){aiPlace.Checked=true;await GenerateAi();}
        }
        async Task ExportAi(bool show)
        {
            if(aiImage==null||aiKey!=AiGenerationKey()){Status.Text=L.T("请先生成当前设置的 AI 二维码。");return;}
            var bitmap=(Bitmap)aiImage.Clone();var options=text.Copy();var aiOptionsForExport=CaptureAi();string folder=OutputFolder.Destination,path=imagePath,link=aiRegion.Url;QrRegion region=aiRegion;
            using(bitmap)await Run(token=>Task.Run(async ()=>
            {
                token.ThrowIfCancellationRequested();options.Draw(bitmap,false);
                if(!QrAiCondition.Validate(bitmap,link)||!await QrAiService.ValidateOutput(aiOptionsForExport,bitmap,link,token,region.Raw))throw new InvalidOperationException("文字遮挡使二维码自检失败，请移动或删除遮挡文字。");
                byte[] bytes=QrService.Tag(Codec.StaticPng(bitmap),region);token.ThrowIfCancellationRequested();string output=ImageTools.Unique(folder,path??"ai.png","_ai_qr",".png");Codec.SaveAtomic(output,bytes);return L.T("图片已保存。")+"\n"+output;
            },token),show);
        }
        void AiLanguageChanged(){UpdateAiLocations();}
        void ClearAiResult(){if(aiImage!=null){aiImage.Dispose();aiImage=null;}aiKey=null;aiRegion=null;aiTerminalStatus=null;if(aiPromptResult!=null)aiPromptResult.Text=L.T("生成后在这里显示自动提示词。");}
        void DisposeAi(){L.Changed-=AiLanguageChanged;ClearAiResult();}
#if SELF_TEST
        async Task CheckAiUiBehavior(string folder)
        {
            mode.TestChoice(3);await Preview();if(!aiPanel.Visible||!aiGenerate.Visible||borderRow.Visible||size.Minimum!=30)throw new Exception("AI mode visibility failed");
            var window=(MainForm)FindForm();var held=new TaskCompletionSource<string>();var background=Run(token=>held.Task,false,true);int ticks=0;using(var pulse=new System.Windows.Forms.Timer{Interval=10}){pulse.Tick+=delegate{ticks++;};pulse.Start();window.SwitchPage(5);await Task.Delay(80);if(window.TestActivePage!=5||!Busy||!BackgroundBusy||UseWaitCursor||ticks==0)throw new Exception("Background preparation blocks navigation or message loop");window.SwitchPage(9);AiProgress("fixture overall 3 / 5");await Task.Delay(30);if(Status.Text!="fixture overall 3 / 5")throw new Exception("Background progress missing after navigation");Cancel();held.SetCanceled();await background;RestoreAiControls();await Preview();if(Busy||BackgroundBusy||!Status.Text.Contains(L.T("任务已停止；已完成的文件保留。")))throw new Exception("Cancelled preparation lost terminal state or stayed busy");}
            held=new TaskCompletionSource<string>();var foreground=Run(token=>held.Task,false);window.SwitchPage(5);if(window.TestActivePage!=9||BackgroundBusy||!UseWaitCursor)throw new Exception("Foreground task no longer locks navigation");held.SetResult("fixture complete");await foreground;aiTerminalStatus=null;
            if(aiBackground.Checked||CaptureAi().AutoPlace||!aiManual.Checked||aiPlace.Checked||!CaptureAi().AutoPrompt||!qrPositionRow.Visible||!placement.Visible||!aiAuto.Visible||!aiPrompt.Visible||aiPrompt.Enabled)throw new Exception("Manual/automatic placement defaults or prompt controls are misleading");string before=AiGenerationKey();aiBackground.Checked=true;if(before==AiGenerationKey())throw new Exception("Background change did not invalidate AI generation");aiBackground.Checked=false;
            decimal manualX=x.Value,manualY=y.Value;before=AiGenerationKey();aiAuto.Checked=false;
            if(!aiManual.Checked||aiPlace.Checked||!aiPrompt.Enabled||!aiAuto.Visible||!aiPrompt.Visible||CaptureAi().AutoPrompt||CaptureAi().AutoPlace||before==AiGenerationKey())throw new Exception("Manual placement cannot disable automatic prompts independently");
            before=AiGenerationKey();aiPrompt.Text="keep the chosen region and preserve its colors";
            if(!aiManual.Checked||x.Value!=manualX||y.Value!=manualY||CaptureAi().Prompt!=aiPrompt.Text||CaptureAi().AutoPrompt||before==AiGenerationKey())throw new Exception("Manual placement loses custom prompt or position");
            aiAuto.Checked=true;if(aiPrompt.Enabled||!CaptureAi().AutoPrompt||!aiManual.Checked)throw new Exception("Automatic prompt re-enable changed manual placement");
            string sourceBytes=Convert.ToBase64String(File.ReadAllBytes(imagePath));ShowAiPreview();if(preview==null||QrService.Scan(preview)!=CurrentPayload())throw new Exception("Initial AI preview does not contain the ordinary QR at the selected position");if(Convert.ToBase64String(File.ReadAllBytes(imagePath))!=sourceBytes)throw new Exception("Placement preview edited the original source file");await ExportAi(false);if(!Status.Text.Contains(L.T("请先生成当前设置的 AI 二维码。")))throw new Exception("Ordinary placement demo can be exported as an AI result");
            before=AiGenerationKey();decimal oldX=x.Value;x.Value=oldX>50?25:75;ShowAiPreview();if(before==AiGenerationKey()||QrService.Scan(preview)!=CurrentPayload()||CaptureAi().AutoPlace)throw new Exception("Manual position changed without a matching QR preview or enabled auto placement");
            using(var original=Codec.Load(imagePath))aiImage=(Bitmap)original.Clone();aiKey=AiGenerationKey();ShowAiPreview();using(var expected=(Bitmap)aiImage.Clone()){text.Copy().Draw(expected,false);if(!Codec.StaticPng(expected).SequenceEqual(Codec.StaticPng(preview)))throw new Exception("Completed AI preview still contains the ordinary demonstration QR");}if(!view.PlacementRegion.IsEmpty)throw new Exception("Completed image includes a placement marker");x.Value=oldX;ShowAiPreview();await ExportAi(false);if(!Status.Text.Contains(L.T("请先生成当前设置的 AI 二维码。"))||QrService.Scan(preview)!=CurrentPayload())throw new Exception("Moving a completed QR does not require regeneration or restore demo");ClearAiResult();
            QrAiRetryDialog.CheckBehavior(folder);
            QrAiSetupDialog.CheckBehavior(folder);
            aiPlace.Checked=true;ShowAiPreview();if(aiManual.Checked||!CaptureAi().AutoPlace||qrPositionRow.Visible||placement.Visible||view.TextPlacement||aiPositionHelp.Visible||!aiAutoPlaceHelp.Visible)throw new Exception("Automatic placement leaves manual placement controls or drag active");using(var original=Codec.Load(imagePath))using(var expected=Codec.Fit(original,preview.Width,preview.Height,Color.White)){if(!Codec.StaticPng(expected).SequenceEqual(Codec.StaticPng(preview)))throw new Exception("Automatic placement preview contains a misleading ordinary QR");}aiAuto.Checked=false;if(!aiPrompt.Enabled||!aiPrompt.Visible||CaptureAi().AutoPrompt)throw new Exception("Automatic avoidance custom prompt unavailable");aiAuto.Checked=true;if(aiPrompt.Enabled)throw new Exception("Automatic prompt edit enabled");aiManual.Checked=true;ShowAiPreview();if(aiPlace.Checked||!aiManual.Checked||!CaptureAi().AutoPrompt||!qrPositionRow.Visible||!placement.Visible||!view.TextPlacement||!aiAuto.Visible||!aiPrompt.Visible||aiPrompt.Enabled||QrService.Scan(preview)!=CurrentPayload())throw new Exception("Switching to manual does not restore ordinary QR / coordinates / independent automatic prompts");aiManual.Checked=false;ShowAiPreview();if(aiPlace.Checked||aiManual.Checked||!aiAuto.Checked||aiPrompt.Enabled||!aiAuto.Visible||!aiPrompt.Visible||CaptureAi().AutoPlace||!qrPositionRow.Visible||!view.TextPlacement||QrService.Scan(preview)!=CurrentPayload())throw new Exception("Turning placement choices off changed independent automatic prompts or manual coordinates");aiAuto.Checked=false;if(!aiPrompt.Enabled||CaptureAi().AutoPrompt)throw new Exception("Both placement choices off cannot edit the independent manual prompt");before=AiGenerationKey();aiPrompt.Text="preserve source material and color";if(CaptureAi().Prompt!=aiPrompt.Text||CaptureAi().AutoPrompt||before==AiGenerationKey())throw new Exception("Manual prompt is not passed to the generation options / result key");aiPlace.Checked=true;if(aiManual.Checked||!CaptureAi().AutoPlace||!CaptureAi().AutoPrompt)throw new Exception("Automatic retry mode failed to enable automatic prompts");aiPlace.Checked=false;if(aiPlace.Checked||aiManual.Checked||!aiAuto.Checked||aiPrompt.Enabled||!CaptureAi().AutoPrompt)throw new Exception("Unchecking avoidance changed the independent prompt switch or forced manual placement on");aiAuto.Checked=false;if(!aiPrompt.Enabled||CaptureAi().AutoPrompt)throw new Exception("Independent manual prompt unavailable after avoidance cancellation");aiManual.Checked=true;ShowAiPreview();
            var oldBase=aiBase.Items.ToArray();var oldControl=aiControl.Items.ToArray();int oldBaseIndex=aiBase.SelectedIndex,oldControlIndex=aiControl.SelectedIndex;bool oldRefresh=refreshingAi,oldLanguage=L.English;
            string modelDir=Path.Combine(folder,"actual models & 中文","a long nested 模型：folder"),modelFile=Path.Combine(modelDir,"control & 中文.safetensors");Directory.CreateDirectory(modelDir);File.WriteAllText(modelFile,"UI fixture only");refreshingAi=true;
            try
            {
                aiBase.Items.Clear();aiControl.Items.Clear();aiBase.SelectedIndex=aiControl.SelectedIndex=-1;aiBase.Items.Add(new QrAiModel{Path=modelDir,Name="same-name",Family="sd15"});aiControl.Items.Add(new QrAiModel{Path=modelFile,Name="same-name",Family="sd15",Control=true});aiBase.SelectedIndex=aiControl.SelectedIndex=0;UpdateAiLocations();
                foreach(bool en in new[]{false,true}){L.Set(en?"en":"zh");if(!aiBasePath.Text.EndsWith(modelDir)||!aiControlPath.Text.EndsWith(modelFile)||!aiOpenBase.Enabled||!aiOpenControl.Enabled)throw new Exception("Actual model paths/open buttons incorrect");if(QrAiService.ModelFolder(modelDir)!=modelDir||QrAiService.ModelFolder(modelFile)!=modelDir)throw new Exception("Model directory/single-file folder resolution incorrect");Application.DoEvents();((MainForm)FindForm()).ValidateLayout();}
                File.Delete(modelFile);UpdateAiLocations();if(aiOpenControl.Enabled||QrAiService.ModelFolder(modelFile)!=null)throw new Exception("Missing model file enables folder button");aiBase.SelectedIndex=aiControl.SelectedIndex=-1;UpdateAiLocations();if(aiOpenBase.Enabled||aiOpenControl.Enabled)throw new Exception("Unselected model enables folder button");
            }
            finally{aiBase.Items.Clear();aiControl.Items.Clear();aiBase.Items.AddRange(oldBase);aiControl.Items.AddRange(oldControl);aiBase.SelectedIndex=oldBaseIndex;aiControl.SelectedIndex=oldControlIndex;refreshingAi=oldRefresh;L.Set(oldLanguage?"en":"zh");UpdateAiLocations();}
            mode.TestChoice(2);if(aiPanel.Visible||aiGenerate.Visible||!borderRow.Visible||size.Minimum!=10)throw new Exception("AI controls visible in pixel mode");mode.TestChoice(0);
        }
#endif
    }
}
