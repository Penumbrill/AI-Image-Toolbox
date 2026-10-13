using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQImageSwitch
{
    public sealed class ObfuscationPage : ToolPage
    {
        readonly List<string> files=new List<string>(),demoFiles=new List<string>();
        readonly SoftList queue;
        readonly ImageCanvas inputView,resultView;
        readonly Label detail,previewSize,algorithmHelp;
        readonly SoftCombo scrambleMode,decodeMode;
        readonly NumericUpDown scrambleQuality,decodeQuality;
        readonly NumericUpDown scrambleStrength,decodeStrength,scrambleRounds,decodeRounds;
        readonly CheckBox enhanceGreen,reduceGreen;
        readonly Button batchDescramble;
        readonly System.Windows.Forms.Timer debounce;
        readonly SemaphoreSlim previewGate=new SemaphoreSlim(1,1);
        readonly SemaphoreSlim inputGate=new SemaphoreSlim(1,1);
        CancellationTokenSource previewCancel;
        Bitmap inputImage,resultImage;
        bool reversePreview,disposed,previewActive,previewPending,refreshingQueue;
        int previewGeneration,inputGeneration;
        internal long LastPreviewBytes;
        public List<string> LastOutputs=new List<string>();
        public ObfuscationPage(ExportLocation location,Action<bool> changed)
            :base("图片混淆","本地处理图片，支持混淆、解混淆与批量还原。",location,changed)
        {
            scrambleRounds=FileToolPage.Number(1,20,1);decodeRounds=FileToolPage.Number(1,20,1);
            Ui.Add(Body,Ui.Flow(Ui.Text("混淆次数"),scrambleRounds,Ui.Text("解混淆次数"),decodeRounds,Ui.Button("识别解混淆参数…",async delegate{await IdentifyRounds();})));
            Ui.Add(Body,Ui.Text("次数为 1–20 的整数，默认 1。混淆 2 次的图片需解混淆 2 次；两边移位系数须一致。次数候选需预览确认。",9));
            Ui.Add(Body,Ui.Flow(Ui.Button("＋ 批量导入图片",async delegate{await Pick();},true),Ui.Button("移除选中",delegate{foreach(int i in queue.SelectedIndices.Cast<int>().OrderByDescending(i=>i))files.RemoveAt(i);RefreshQueue();}),Ui.Button("清空队列",delegate{files.Clear();RefreshQueue();})));
            queue=new SoftList{Dock=DockStyle.Fill,IntegralHeight=false,SelectionMode=SelectionMode.MultiExtended,HorizontalScrollbar=true,BorderStyle=BorderStyle.None,Margin=new Padding(0)};
            var queueCard=Ui.Column(new Padding(12));queueCard.AutoSize=false;queueCard.Dock=DockStyle.Fill;queueCard.BackColor=Color.White;queueCard.Margin=new Padding(0,0,8,0);Ui.Add(queueCard,Ui.Text("图片队列",12,true));Ui.Add(queueCard,queue);queueCard.RowStyles[1].SizeType=SizeType.Percent;queueCard.RowStyles[1].Height=100;
            var cards=new TableLayoutPanel{ColumnCount=3,RowCount=1,Dock=DockStyle.Top,Height=350,Margin=new Padding(0,0,0,12)};
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));cards.RowStyles.Add(new RowStyle(SizeType.Percent,100));cards.Controls.Add(queueCard,0,0);
            inputView=new ImageCanvas{Dock=DockStyle.Fill,EmptyText="原图预览"};resultView=new ImageCanvas{Dock=DockStyle.Fill,EmptyText="处理结果预览"};
            var previewCards=new Control[2];for(int i=0;i<2;i++){var card=Ui.Column(new Padding(12));card.AutoSize=false;card.Dock=DockStyle.Fill;card.BackColor=Color.White;card.Margin=new Padding(i==0?0:6,0,i==0?6:0,0);Ui.Add(card,Ui.Text(i==0?"原图预览":"处理结果预览",12,true));Ui.Add(card,i==0?inputView:resultView);card.RowStyles[1].SizeType=SizeType.Percent;card.RowStyles[1].Height=100;cards.Controls.Add(card,i+1,0);previewCards[i]=card;}Ui.Add(Body,cards);
            Ui.Add(Body,Ui.Text("导入后点击「生成混淆图」即可；默认系数 1.0、无损 PNG、原尺寸、增绿关闭。",9));
            detail=Ui.Text("尚未导入图片",9);detail.AutoSize=false;detail.Dock=DockStyle.Top;detail.Tag="fixedwrap";Ui.Add(Body,detail);
            int detailWidth=-1;string detailText=null;Font detailFont=null;bool sizingDetail=false;
            EventHandler sizeDetail=delegate
            {
                if(sizingDetail||disposed||Disposing)return;
                int width=Math.Max(80,Body.ClientSize.Width-Body.Padding.Horizontal);
                if(detailWidth==width&&detailText==detail.Text&&detailFont==detail.Font)return;
                sizingDetail=true;
                try
                {
                    detailWidth=width;detailText=detail.Text;detailFont=detail.Font;
                    // Remove the previous constraint before measuring text; it must
                    // never become the input to the next preferred-height query.
                    detail.MinimumSize=Size.Empty;
                    int height=detail.GetPreferredSize(new Size(width,0)).Height+4;
                    detail.MaximumSize=new Size(width,0);detail.MinimumSize=new Size(0,height);detail.Height=height;
                    var row=Body.RowStyles[Body.GetRow(detail)];row.SizeType=SizeType.Absolute;row.Height=height+detail.Margin.Vertical;
                }
                finally{sizingDetail=false;}
            };detail.TextChanged+=sizeDetail;detail.FontChanged+=sizeDetail;Body.ClientSizeChanged+=sizeDetail;
            var options=Ui.Column(new Padding(16,14,16,10));options.SuspendLayout();options.BackColor=Color.White;options.Margin=new Padding(0,0,0,12);
            Ui.Add(options,Ui.Text("画质与文件大小",13,true));
            algorithmHelp=Ui.Text("Gilbert 曲线混淆：按原尺寸重新排列像素，默认移位系数为 1.0。关闭颜色调整、保持尺寸与像素不变，并用相同系数和次数解混淆，可还原图像像素。",9);Ui.Add(options,algorithmHelp);
            scrambleStrength=FileToolPage.Number(0,2,1);decodeStrength=FileToolPage.Number(0,2,1);scrambleStrength.DecimalPlaces=decodeStrength.DecimalPlaces=1;scrambleStrength.Increment=decodeStrength.Increment=.1m;
            Ui.Add(options,Ui.Flow(Ui.Text("混淆移位系数"),scrambleStrength));
            var encPresets=Ui.Flow();foreach(decimal value in new[]{0m,.5m,1m,1.5m,2m}){decimal selected=value;encPresets.Controls.Add(Ui.Button(value.ToString("0.0"),delegate{scrambleStrength.Value=selected;}));}Ui.Add(options,encPresets);Ui.Add(options,Ui.Text("移位系数为 0–2，0 表示不移位。更大的数值或更多次数不代表一定更强；解混淆需要相同系数和次数。",9));
            enhanceGreen=new CheckBox{Text="混淆时增强绿色（可选）",AutoSize=true};L.Bind(enhanceGreen);Ui.Add(options,enhanceGreen);Ui.Add(options,Ui.Text("增强绿色默认关闭，只调节符合绿色条件的像素。会改变颜色并产生限幅损失；需要精确还原时保持关闭。",9));
            Ui.Add(options,Ui.Flow(Ui.Text("解混淆移位系数"),decodeStrength));
            var decPresets=Ui.Flow();foreach(decimal value in new[]{0m,.5m,1m,1.5m,2m}){decimal selected=value;decPresets.Controls.Add(Ui.Button(value.ToString("0.0"),delegate{decodeStrength.Value=selected;}));}Ui.Add(options,decPresets);Ui.Add(options,Ui.Text("解混淆移位系数默认 1.0，须与生成时一致。保持输入尺寸处理，不会自动放大或缩小。",9));
            reduceGreen=new CheckBox{Text="解混淆时减弱绿色（可选）",AutoSize=true};L.Bind(reduceGreen);Ui.Add(options,reduceGreen);Ui.Add(options,Ui.Text("减弱绿色默认关闭，仅作观感调整，不能精确逆转增绿。普通混淆图无需开启。",9));
            scrambleMode=new SoftCombo{Width=260};decodeMode=new SoftCombo{Width=260};foreach(var mode in new[]{scrambleMode,decodeMode}){mode.Items.AddRange(new object[]{"无损 PNG","压缩 JPG（自定义质量）"});mode.SelectedIndex=0;}
            scrambleQuality=FileToolPage.Number(1,100,95);decodeQuality=FileToolPage.Number(1,100,95);scrambleQuality.Width=decodeQuality.Width=80;
            var encQualityLabel=Ui.Text("质量 %");var decQualityLabel=Ui.Text("质量 %");
            Ui.Add(options,Ui.Flow(Ui.Text("混淆输出"),scrambleMode,encQualityLabel,scrambleQuality));Ui.Add(options,Ui.Flow(Ui.Text("解混淆输出"),decodeMode,decQualityLabel,decodeQuality));Ui.Add(options,Ui.Text("默认导出无损 PNG。JPG 质量默认 95%，可以减小体积但会损伤像素；输出为普通图片，不附带源文件或私有参数记录。",9));
            Ui.Add(options,Ui.Flow(Ui.Button("预览混淆与大小",async delegate{reversePreview=false;await Preview();}),Ui.Button("预览解混淆与大小",async delegate{reversePreview=true;await Preview();}),Ui.Button("还原预览",delegate{ClearPreview();previewSize.Text="预览已重置；输入文件保持不变。";})));
            previewSize=Ui.Text("选择队列中的图片后，可预览实际编码后的文件大小。",9);Ui.Add(options,previewSize);Ui.Add(options,Ui.Text("Bilibili 保存时先点「查看原图」并等待加载完成，再保存图片并核对宽×高。预览图、截图、缩放或重编码可能导致还原失败。",9));
            options.ResumeLayout(true);Ui.Add(Body,options);
            ActionButton("生成混淆图",async delegate{await Export(0,true,true);});
            ActionButton("混淆全部",async delegate{await Export(0,true,true);},false);
            ActionButton("混淆选中",async delegate{await Export(0,false,true);},false);
            batchDescramble=ActionButton("批量解混淆",async delegate{await Export(1,true,true);},false);
            ActionButton("解混淆选中",async delegate{await Export(1,false,true);},false);
            debounce=new System.Windows.Forms.Timer{Interval=500};debounce.Tick+=async delegate{debounce.Stop();await Preview();};
            EventHandler change=delegate(object sender,EventArgs e){if(sender==scrambleMode||sender==scrambleQuality)reversePreview=false;if(sender==decodeMode||sender==decodeQuality)reversePreview=true;scrambleQuality.Visible=encQualityLabel.Visible=scrambleMode.SelectedIndex==1;decodeQuality.Visible=decQualityLabel.Visible=decodeMode.SelectedIndex==1;if(files.Count>0)SchedulePreview();};
            scrambleMode.SelectedIndexChanged+=change;decodeMode.SelectedIndexChanged+=change;scrambleQuality.ValueChanged+=change;decodeQuality.ValueChanged+=change;change(null,EventArgs.Empty);
            scrambleRounds.ValueChanged+=delegate{reversePreview=false;SchedulePreview();};decodeRounds.ValueChanged+=delegate{reversePreview=true;SchedulePreview();};
            scrambleStrength.ValueChanged+=delegate{reversePreview=false;SchedulePreview();};enhanceGreen.CheckedChanged+=delegate{reversePreview=false;SchedulePreview();};decodeStrength.ValueChanged+=delegate{reversePreview=true;SchedulePreview();};reduceGreen.CheckedChanged+=delegate{reversePreview=true;SchedulePreview();};
            queue.SelectedIndexChanged+=delegate{if(!refreshingQueue)RefreshSelection();};
            foreach(Control c in new Control[]{queue,inputView,resultView}){c.AllowDrop=true;c.DragEnter+=delegate(object s,DragEventArgs e){if(!Busy&&e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};c.DragDrop+=async delegate(object s,DragEventArgs e){if(!Busy)await Import((string[])e.Data.GetData(DataFormats.FileDrop));};}
            bool fitting=false;int wideLayout=-1;EventHandler fit=delegate
            {
                if(fitting||disposed||Disposing||cards.IsDisposed||cards.Disposing)return;fitting=true;
                try
                {
                    bool wide=Scroller.ClientSize.Width>=1150*Font.Size/10;int mode=wide?1:0;
                    cards.SuspendLayout();
                    if(wideLayout!=mode)
                    {
                        wideLayout=mode;cards.RowCount=wide?1:2;cards.RowStyles.Clear();cards.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                        cards.ColumnStyles[0].Width=wide?Math.Max(180,(int)(210*Font.Size/10)):0;
                        cards.SetColumn(queueCard,0);cards.SetRow(queueCard,wide?0:1);cards.SetColumnSpan(queueCard,wide?1:3);queueCard.Margin=new Padding(0,wide?0:10,wide?8:0,0);
                        for(int i=0;i<2;i++){cards.SetColumn(previewCards[i],i+1);cards.SetRow(previewCards[i],0);}
                    }
                    int previewHeight=ImageCanvas.ViewportHeight(Scroller,.52,270,660)+(int)(Font.Height*2.5);
                    int queueHeight=wide?0:HeightResize.Resolve(queueCard,Math.Max(220,(int)(Font.Height*10))+10);
                    if(!wide){if(cards.RowStyles.Count<2)cards.RowStyles.Add(new RowStyle(SizeType.Absolute,queueHeight));else cards.RowStyles[1].Height=queueHeight;}
                    int total=HeightResize.Resolve(cards,previewHeight+queueHeight);if(cards.Height!=total)cards.Height=total;
                    cards.ResumeLayout(true);
                }
                finally{fitting=false;}
            };Scroller.ClientSizeChanged+=fit;FontChanged+=fit;
            FinishLayout();fit(null,EventArgs.Empty);
            Action<int> resizeWorkspace=h=>{HeightResize.Set(cards,h);fit(null,EventArgs.Empty);};
            for(int i=0;i<2;i++){Control pane=previewCards[i];HeightResize.Enable(pane,h=>resizeWorkspace(h>0?cards.Height+h-pane.Height:0));}
            HeightResize.Enable(inputView,h=>resizeWorkspace(h>0?cards.Height+h-inputView.Height:0));HeightResize.Enable(resultView,h=>resizeWorkspace(h>0?cards.Height+h-resultView.Height:0));
            HeightResize.Enable(queueCard,h=>{if(wideLayout==1)resizeWorkspace(h);else{HeightResize.Set(queueCard,h);fit(null,EventArgs.Empty);}});
            HeightResize.Enable(queue,h=>{int total=h>0?queueCard.Height+h-queue.Height:0;if(wideLayout==1)resizeWorkspace(total);else{HeightResize.Set(queueCard,total);fit(null,EventArgs.Empty);}});
        }
        int Quality(bool reverse){return reverse?(decodeMode.SelectedIndex==0?0:(int)decodeQuality.Value):(scrambleMode.SelectedIndex==0?0:(int)scrambleQuality.Value);}
        ScrambleOptions Options(bool reverse){return new ScrambleOptions{Rounds=(int)(reverse?decodeRounds.Value:scrambleRounds.Value),Strength=(double)(reverse?decodeStrength.Value:scrambleStrength.Value),Green=enhanceGreen.Checked,ReduceGreen=reduceGreen.Checked};}
        async Task Pick(){if(Busy)return;using(var d=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff",Multiselect=true})if(L.Show(d,FindForm())==DialogResult.OK)await Import(d.FileNames);}
        internal async Task Import(string[] paths)
        {
            if(Busy||disposed)return;debounce.Stop();CancelPreview();var snapshot=new HashSet<string>(files,StringComparer.OrdinalIgnoreCase);var accepted=new List<string>();var errors=new List<string>();
            await Run(async token=>{await Task.Run(()=>{foreach(string path in paths){token.ThrowIfCancellationRequested();try{string p=Path.GetFullPath(path);if(!snapshot.Add(p))continue;using(var image=Obfuscation.Load(p)){}accepted.Add(p);}catch(OperationCanceledException){throw;}catch(Exception ex){errors.Add(Path.GetFileName(path)+"："+ex.Message);}}},token);files.AddRange(accepted);RefreshQueue();return "已导入 "+accepted.Count+" 张图片。"+(errors.Count>0?"\n"+string.Join("\n",errors):"");},false);
            if(previewPending)SchedulePreview();
        }
        void ClearPreview()
        {
            debounce.Stop();previewPending=false;CancelPreview();LastPreviewBytes=0;
            resultView.Image=null;if(resultImage!=null)resultImage.Dispose();resultImage=null;
        }
        async void RefreshSelection()
        {
            if(disposed||Disposing)return;inputGeneration++;ClearPreview();
            previewSize.Text="选择队列中的图片后，可预览实际编码后的文件大小。";
            if(queue.SelectedIndex>=0){SchedulePreview();await ShowInput();}
            else{inputView.Image=null;if(inputImage!=null)inputImage.Dispose();inputImage=null;detail.Text="尚未导入图片";}
        }
        void RefreshQueue()
        {
            refreshingQueue=true;queue.BeginUpdate();
            try
            {
                queue.Items.Clear();foreach(string path in files)queue.Items.Add(Path.GetFileName(path));
                queue.HorizontalExtent=files.Count==0?0:files.Max(path=>TextRenderer.MeasureText(Path.GetFileName(path),queue.Font).Width)+28;
                if(files.Count>0)queue.SelectedIndex=0;
            }
            finally{queue.EndUpdate();refreshingQueue=false;}
            RefreshSelection();
        }
        async Task ShowInput()
        {
            int generation=inputGeneration;string path=files[queue.SelectedIndex];Bitmap image=null;int width=0,height=0;
            try{await inputGate.WaitAsync();try{if(disposed||generation!=inputGeneration)return;image=await Task.Run(()=>{using(var source=Obfuscation.Load(path)){width=source.Width;height=source.Height;var size=Codec.OutputSize(source,900);return Codec.Fit(source,size.Width,size.Height,Color.Transparent);}});}finally{inputGate.Release();}
                if(disposed||generation!=inputGeneration){image.Dispose();image=null;return;}inputView.Image=image;if(inputImage!=null)inputImage.Dispose();inputImage=image;image=null;detail.Text=Path.GetFileName(path)+" · "+width+" × "+height+" px · "+SizeText(new FileInfo(path).Length);}
            catch(Exception ex){if(!disposed&&generation==inputGeneration)detail.Text=ex.Message;}finally{if(image!=null)image.Dispose();}
        }
        static string SizeText(long length){return length>=1048576?(length/1048576.0).ToString("0.00")+" MiB":(length/1024.0).ToString("0.00")+" KiB";}
        void CancelPreview(){previewGeneration++;if(previewCancel!=null)previewCancel.Cancel();}
        void SchedulePreview()
        {
            debounce.Stop();if(disposed||queue.SelectedIndex<0)return;
            CancelPreview();previewPending=true;LastPreviewBytes=0;
            previewSize.Text="正在后台生成预览…";
            if(previewActive&&!Busy)debounce.Start();
        }
        async Task Preview()
        {
            if(Busy||disposed||queue.SelectedIndex<0)return;debounce.Stop();previewPending=false;CancelPreview();int generation=previewGeneration;string input=files[queue.SelectedIndex];int quality=Quality(reversePreview);bool reverse=reversePreview;
            var source=new CancellationTokenSource();previewCancel=source;Bitmap bitmap=null;long bytes=0;
            ScrambleOptions parameters=Options(reverse);
            previewSize.Text="正在后台生成预览…";
            try
            {
                await previewGate.WaitAsync(source.Token);
                try
                {
                    await inputGate.WaitAsync(source.Token);
                    try{await Task.Run(()=>{string temp=Path.Combine(Path.GetTempPath(),"aipt-preview-"+Guid.NewGuid().ToString("N")+(quality>0?".jpg":".png"));try{Obfuscation.WriteReference(input,temp,reverse,quality,source.Token,parameters);bytes=new FileInfo(temp).Length;using(var stream=new FileStream(temp,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))using(var image=Image.FromStream(stream,false,true)){Codec.CheckSize(image.Width,image.Height);var size=Codec.OutputSize(image,900);bitmap=Codec.Fit(image,size.Width,size.Height,Color.Transparent);}}finally{if(File.Exists(temp))File.Delete(temp);}},source.Token);}
                    finally{inputGate.Release();}
                }
                finally{previewGate.Release();}
                if(disposed||generation!=previewGeneration)return;resultView.Image=bitmap;if(resultImage!=null)resultImage.Dispose();resultImage=bitmap;bitmap=null;LastPreviewBytes=bytes;
                long originalLength=new FileInfo(input).Length;
                previewSize.Text="预览文件大小："+SizeText(bytes)+" · "+(quality>0?"JPG "+quality+"%":quality==-1?"原图质量（含原文件）":"无损 PNG")+"\n原文件大小："+SizeText(originalLength)+" · 体积倍率："+(bytes/(double)Math.Max(1,originalLength)).ToString("0.00")+"×";
            }
            catch(OperationCanceledException){}catch(Exception ex){if(!disposed&&generation==previewGeneration)previewSize.Text="预览失败："+ex.Message;}
            finally{if(bitmap!=null)bitmap.Dispose();if(previewCancel==source)previewCancel=null;source.Dispose();}
        }
        internal async Task Export(int operation,bool all,bool report)
        {
            if(Busy)return;string[] inputs=(all?files:queue.SelectedIndices.Cast<int>().Select(i=>files[i])).ToArray();if(inputs.Length==0){Status.Text=files.Count==0?"请先导入图片。":"请先选中要处理的图片。";return;}
            if(!OutputFolder.Ready()){Status.Text="请检查保存位置。";return;}CancelPreview();debounce.Stop();scrambleMode.CloseChoices();decodeMode.CloseChoices();if(operation<2)reversePreview=operation==1;int quality=Quality(operation==1);string folder=OutputFolder.Destination;LastOutputs.Clear();
            IProgress<int> progress=new Progress<int>(n=>{if(!disposed&&Busy)Status.Text="正在处理 "+n+" / "+inputs.Length+"…";});
            ScrambleOptions parameters=Options(operation==1);
            await Run(token=>Task.Run(()=>{var errors=new List<string>();for(int i=0;i<inputs.Length;i++){token.ThrowIfCancellationRequested();try{string output=Obfuscation.WriteReference(inputs[i],ImageTools.Unique(folder,inputs[i],operation==0?"_混淆":"_解混淆",quality>0?".jpg":".png"),operation==1,quality,token,parameters);LastOutputs.Add(output);}catch(OperationCanceledException){throw;}catch(Exception ex){errors.Add(Path.GetFileName(inputs[i])+"："+ex.Message);}progress.Report(i+1);}return "处理完成：成功 "+LastOutputs.Count+" 张，失败 "+errors.Count+" 张。\n保存位置："+folder+(errors.Count>0?"\n"+string.Join("\n",errors):"");},token),report);
            if(operation<2&&LastOutputs.Count>0&&!disposed)await Preview();
        }
        async Task IdentifyRounds(){
            if(Busy||queue.SelectedIndex<0)return;CancelPreview();string path=files[queue.SelectedIndex];double strength=(double)decodeStrength.Value;int preferredRounds=(int)decodeRounds.Value;var candidates=new List<Tuple<ScrambleRounds.Candidate,Bitmap>>();bool completed=false;
            try{
                await Run(async token=>{await Task.Run(()=>{using(var image=Obfuscation.Load(path)){var ranked=ScrambleRounds.RankParameters(ScrambleParameters.Pixels(image),image.Width,image.Height,strength,preferredRounds,token);foreach(var candidate in ranked.Take(3)){token.ThrowIfCancellationRequested();using(var restored=ScrambleParameters.Transform(image,new ScrambleOptions{Strength=candidate.Strength,Rounds=candidate.Rounds},true,token)){double ratio=Math.Min(1,300.0/Math.Max(restored.Width,restored.Height));candidates.Add(Tuple.Create(candidate,Codec.Fit(restored,Math.Max(1,(int)(restored.Width*ratio)),Math.Max(1,(int)(restored.Height*ratio)),Color.Transparent)));}}}},token);completed=true;return "参数候选已计算；请预览确认。";},false);
                if(!completed||candidates.Count==0||disposed)return;
                using(var dialog=new Form{Text=L.T("识别解混淆参数"),Size=new Size(1000,500),StartPosition=FormStartPosition.CenterParent,BackColor=SoftTheme.Background}){
                    var body=Ui.Column(new Padding(16));body.Dock=DockStyle.Fill;dialog.Controls.Add(body);Ui.Add(body,Ui.Text("自动搜索系数 0–2（步进 0.1）与次数 1–20，按图像连续性排序。不同参数可能对应同一置换，不能唯一识别；请对照预览确认。尺寸改变、增绿或范围外参数可能无正确候选。",9));var row=Ui.Flow();Ui.Add(body,row);foreach(var c in candidates){var choice=c;var card=Ui.Column(new Padding(8));var preview=new ImageCanvas{Width=280,Height=300,Image=choice.Item2};Ui.Add(card,preview);Ui.Add(card,Ui.Button(L.T("系数 ")+choice.Item1.Strength.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" · "+choice.Item1.Rounds+L.T(" 次"),delegate{decodeStrength.Value=(decimal)choice.Item1.Strength;decodeRounds.Value=choice.Item1.Rounds;dialog.DialogResult=DialogResult.OK;dialog.Close();}));row.Controls.Add(card);}L.Show(dialog,FindForm());
                }
                reversePreview=true;await Preview();
            }finally{foreach(var c in candidates)c.Item2.Dispose();}
        }
        public override void OnEnterPage(){base.OnEnterPage();previewActive=true;if(previewPending)SchedulePreview();}
        public override void OnLeavePage(){previewActive=false;debounce.Stop();if(previewCancel!=null){previewPending=true;previewSize.Text="选择队列中的图片后，可预览实际编码后的文件大小。";}CancelPreview();}
#if SELF_TEST
        internal async Task CheckBehavior(string folder)
        {
            if(scrambleRounds.Value!=1||decodeRounds.Value!=1||scrambleStrength.Value!=1||decodeStrength.Value!=1)throw new Exception("Default coefficient/rounds changed");
            if(scrambleMode.SelectedIndex!=0||decodeMode.SelectedIndex!=0||enhanceGreen.Checked||reduceGreen.Checked)throw new Exception("Reference defaults changed");
            string explanation=algorithmHelp.Text;L.Set("en");if(explanation==algorithmHelp.Text)throw new Exception("Reference explanation not translated");L.Set("zh");
            folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);string a=Path.Combine(folder,"first.png"),b=Path.Combine(folder,"second.png");
            using(var art=SelfTest.Art(73,51,false))File.WriteAllBytes(a,Codec.StaticPng(art));using(var art=SelfTest.Art(51,73,true))File.WriteAllBytes(b,Codec.StaticPng(art));
            OutputFolder.TestSetFolder(folder);await Import(new[]{a,b});await ShowInput();scrambleRounds.Value=decodeRounds.Value=2;
            if(files.Count!=2||inputView.Image==null)throw new Exception("Scramble import/thumbnail failed");
            scrambleMode.SelectedIndex=1;scrambleQuality.Value=37;await Preview();await Export(0,false,false);
            if(LastOutputs.Count!=1||LastPreviewBytes!=new FileInfo(LastOutputs[0]).Length)throw new Exception("Custom JPEG preview size differs from actual export: outputs="+LastOutputs.Count+", preview="+LastPreviewBytes+", actual="+(LastOutputs.Count==0?0:new FileInfo(LastOutputs[0]).Length)+", preview state="+previewSize.Text+", status="+Status.Text);
            scrambleMode.SelectedIndex=0;await Preview();await Export(0,true,false);var wrapped=LastOutputs.ToArray();
            if(wrapped.Length!=2||new FileInfo(wrapped[0]).Length!=LastPreviewBytes)throw new Exception("Original quality batch/size failed");
            foreach(string output in wrapped){byte[] png=File.ReadAllBytes(output);if(Codec.Parse(png).Last().Type!="IEND"||ScrambleParameters.Read(output)!=null||System.Text.Encoding.ASCII.GetString(png.Skip(Math.Max(0,png.Length-8)).ToArray())=="AIPTSCR1")throw new Exception("Reference PNG includes private records");using(var image=Codec.Load(output))if(image.Width!=(output==wrapped[0]?73:51)||image.Height!=(output==wrapped[0]?51:73))throw new Exception("Reference export resized input");}
            files.Clear();files.AddRange(wrapped);RefreshQueue();
            decodeMode.SelectedIndex=0;
            Scroller.ScrollControlIntoView(decodeMode);await Task.Delay(60);decodeMode.TestOpen();
            if(batchDescramble.Parent!=Actions||Actions.Parent!=Footer||OutputFolder.Parent!=Footer)throw new Exception("Scramble export actions must share the destination footer");
            CaptureScroll(false);
            await Task.Delay(60);
            if(decodeMode.PopupVisible)throw new Exception("Choice popup remained visible after scrolling its owner");
            Control hit=FindForm();Point center=batchDescramble.PointToScreen(new Point(batchDescramble.Width/2,batchDescramble.Height/2));
            while(hit!=null){Control child=hit.GetChildAtPoint(hit.PointToClient(center),GetChildAtPointSkip.Invisible|GetChildAtPointSkip.Disabled);if(child==null)break;hit=child;}
            if(hit!=batchDescramble)throw new Exception("Batch descramble hit area belongs to another control: "+(hit==null?"null":hit.GetType().Name));
            using(var closeResults=new System.Windows.Forms.Timer{Interval=30})
            {
                closeResults.Tick+=delegate{foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray())if(dialog is ResultForm)dialog.Close();};closeResults.Start();
                await Task.Delay(60);batchDescramble.PerformClick();
                for(int i=0;Busy&&i<200;i++)await Task.Delay(20);
                closeResults.Stop();
            }
            if(Busy||LastOutputs.Count!=2||scrambleMode.PopupVisible||decodeMode.PopupVisible||!reversePreview)throw new Exception("Batch descramble button did not execute the inverse operation");
            for(int i=0;i<2;i++)using(var original=Codec.Load(i==0?a:b))using(var decoded=Codec.Load(LastOutputs[i]))
                for(int row=0;row<original.Height;row++)for(int col=0;col<original.Width;col++)if(original.GetPixel(col,row)!=decoded.GetPixel(col,row))throw new Exception("Batch button did not restore exact pixels");
            if(!Status.Text.StartsWith("处理完成"))throw new Exception("Late batch progress overwrote completion status");
            decodeMode.SelectedIndex=1;decodeQuality.Value=53;reversePreview=true;await Preview();await Export(1,false,false);
            if(LastOutputs.Count!=1||!LastOutputs[0].EndsWith(".jpg")||LastPreviewBytes!=new FileInfo(LastOutputs[0]).Length)throw new Exception("Independent descramble quality/size failed");
            CancelPreview();debounce.Stop();
        }
        public override void Demo(){if(files.Count>0)return;for(int i=0;i<2;i++){string path=Path.Combine(Path.GetTempPath(),"aipt-scramble-demo-"+Guid.NewGuid().ToString("N")+".png");using(var b=SelfTest.Art(600,420,i!=0))File.WriteAllBytes(path,Codec.StaticPng(b));files.Add(path);demoFiles.Add(path);}RefreshQueue();}
#endif
        protected override void Dispose(bool disposing){if(disposing){disposed=true;CancelPreview();debounce.Stop();debounce.Dispose();inputView.Image=resultView.Image=null;if(inputImage!=null)inputImage.Dispose();if(resultImage!=null)resultImage.Dispose();foreach(string p in demoFiles)try{File.Delete(p);}catch(IOException){}}base.Dispose(disposing);}
    }
}
