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
        readonly Label detail,previewSize;
        readonly SoftCombo scrambleMode,decodeMode;
        readonly NumericUpDown scrambleQuality,decodeQuality;
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
            Ui.Add(Body,Ui.Flow(Ui.Button("＋ 批量导入图片",async delegate{await Pick();},true),Ui.Button("移除选中",delegate{foreach(int i in queue.SelectedIndices.Cast<int>().OrderByDescending(i=>i))files.RemoveAt(i);RefreshQueue();}),Ui.Button("清空队列",delegate{files.Clear();RefreshQueue();})));
            queue=new SoftList{Dock=DockStyle.Fill,IntegralHeight=false,SelectionMode=SelectionMode.MultiExtended,HorizontalScrollbar=true,BorderStyle=BorderStyle.None,Margin=new Padding(0)};
            var queueCard=Ui.Column(new Padding(12));queueCard.AutoSize=false;queueCard.Dock=DockStyle.Fill;queueCard.BackColor=Color.White;queueCard.Margin=new Padding(0,0,8,0);Ui.Add(queueCard,Ui.Text("图片队列",12,true));Ui.Add(queueCard,queue);queueCard.RowStyles[1].SizeType=SizeType.Percent;queueCard.RowStyles[1].Height=100;
            var cards=new TableLayoutPanel{ColumnCount=3,RowCount=1,Dock=DockStyle.Top,Height=350,Margin=new Padding(0,0,0,12)};
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));cards.RowStyles.Add(new RowStyle(SizeType.Percent,100));cards.Controls.Add(queueCard,0,0);
            inputView=new ImageCanvas{Dock=DockStyle.Fill,EmptyText="原图预览"};resultView=new ImageCanvas{Dock=DockStyle.Fill,EmptyText="处理结果预览"};
            var previewCards=new Control[2];for(int i=0;i<2;i++){var card=Ui.Column(new Padding(12));card.AutoSize=false;card.Dock=DockStyle.Fill;card.BackColor=Color.White;card.Margin=new Padding(i==0?0:6,0,i==0?6:0,0);Ui.Add(card,Ui.Text(i==0?"原图预览":"处理结果预览",12,true));Ui.Add(card,i==0?inputView:resultView);card.RowStyles[1].SizeType=SizeType.Percent;card.RowStyles[1].Height=100;cards.Controls.Add(card,i+1,0);previewCards[i]=card;}Ui.Add(Body,cards);
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
            scrambleMode=new SoftCombo{Width=260};scrambleMode.Items.AddRange(new object[]{"原图质量（可还原原文件）","无损 PNG（仅像素）","压缩 JPG（自定义质量）"});scrambleMode.SelectedIndex=0;
            decodeMode=new SoftCombo{Width=260};decodeMode.Items.AddRange(new object[]{"无损 PNG（仅像素）","压缩 JPG（自定义质量）"});decodeMode.SelectedIndex=0;
            scrambleQuality=FileToolPage.Number(1,100,95);decodeQuality=FileToolPage.Number(1,100,95);scrambleQuality.Width=decodeQuality.Width=80;
            Ui.Add(options,Ui.Flow(Ui.Text("混淆质量"),scrambleMode,Ui.Text("质量 %"),scrambleQuality));
            Ui.Add(options,Ui.Flow(Ui.Text("解混淆质量"),decodeMode,Ui.Text("质量 %"),decodeQuality));
            Ui.Add(options,Ui.Flow(Ui.Button("预览混淆与大小",async delegate{reversePreview=false;await Preview();}),Ui.Button("预览解混淆与大小",async delegate{reversePreview=true;await Preview();}),Ui.Button("还原预览",delegate{ClearPreview();previewSize.Text="预览已重置；输入文件保持不变。";})));
            previewSize=Ui.Text("选择队列中的图片后，可预览实际编码后的文件大小。",9);Ui.Add(options,previewSize);
            Ui.Add(options,Ui.Text("原图质量输出 PNG，附带原文件；点击「还原原文件」可恢复全部字节及信息。无损 PNG 只保留像素；JPG 有损，透明处填白，不支持原文件还原。",9));
            Ui.Add(options,Ui.Text("解混淆不会消除 JPG 已有的压缩损失。尺寸改变、截图或像素修改会影响还原。动图的像素混淆只处理首帧；原文件模式保留完整动图。",9));
            options.ResumeLayout(true);Ui.Add(Body,options);
            batchDescramble=Ui.Button("批量解混淆",async delegate{await Export(1,true,true);});
            Ui.Add(Body,Ui.Flow(Ui.Button("批量混淆",async delegate{await Export(0,true,true);},true),Ui.Button("混淆选中",async delegate{await Export(0,false,true);}),batchDescramble,Ui.Button("解混淆选中",async delegate{await Export(1,false,true);}))); 
            Ui.Add(Body,Ui.Flow(Ui.Button("批量还原原文件",async delegate{await Export(2,true,true);}),Ui.Button("还原选中原文件",async delegate{await Export(2,false,true);}))); 
            debounce=new System.Windows.Forms.Timer{Interval=500};debounce.Tick+=async delegate{debounce.Stop();await Preview();};
            EventHandler change=delegate(object sender,EventArgs e){if(sender==scrambleMode||sender==scrambleQuality)reversePreview=false;if(sender==decodeMode||sender==decodeQuality)reversePreview=true;scrambleQuality.Enabled=scrambleMode.SelectedIndex==2;decodeQuality.Enabled=decodeMode.SelectedIndex==1;if(files.Count>0)SchedulePreview();};
            scrambleMode.SelectedIndexChanged+=change;decodeMode.SelectedIndexChanged+=change;scrambleQuality.ValueChanged+=change;decodeQuality.ValueChanged+=change;change(null,EventArgs.Empty);
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
        int Quality(bool reverse){return reverse?(decodeMode.SelectedIndex==0?0:(int)decodeQuality.Value):scrambleMode.SelectedIndex==0?-1:scrambleMode.SelectedIndex==1?0:(int)scrambleQuality.Value;}
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
            int generation=inputGeneration;string path=files[queue.SelectedIndex];Bitmap image=null;
            try{await inputGate.WaitAsync();try{if(disposed||generation!=inputGeneration)return;image=await Task.Run(()=>{using(var source=Obfuscation.Load(path)){var size=Codec.OutputSize(source,900);return Codec.Fit(source,size.Width,size.Height,Color.Transparent);}});}finally{inputGate.Release();}
                if(disposed||generation!=inputGeneration){image.Dispose();image=null;return;}inputView.Image=image;if(inputImage!=null)inputImage.Dispose();inputImage=image;image=null;detail.Text=Path.GetFileName(path)+" · "+SizeText(new FileInfo(path).Length);}
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
            previewSize.Text="正在后台生成预览…";
            try
            {
                await previewGate.WaitAsync(source.Token);
                try
                {
                    await inputGate.WaitAsync(source.Token);
                    try{await Task.Run(()=>{string temp=Path.Combine(Path.GetTempPath(),"aipt-preview-"+Guid.NewGuid().ToString("N")+(quality>0?".jpg":".png"));try{Obfuscation.Write(input,temp,reverse,quality,source.Token);bytes=new FileInfo(temp).Length;using(var stream=new FileStream(temp,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))using(var image=Image.FromStream(stream,false,true)){Codec.CheckSize(image.Width,image.Height);var size=Codec.OutputSize(image,900);bitmap=Codec.Fit(image,size.Width,size.Height,Color.Transparent);}}finally{if(File.Exists(temp))File.Delete(temp);}},source.Token);}
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
            await Run(token=>Task.Run(()=>{var errors=new List<string>();for(int i=0;i<inputs.Length;i++){token.ThrowIfCancellationRequested();try{string output=operation==2?Obfuscation.RestoreOriginal(inputs[i],folder,token):Obfuscation.Write(inputs[i],ImageTools.Unique(folder,inputs[i],operation==0?"_混淆":"_解混淆",quality>0?".jpg":".png"),operation==1,quality,token);LastOutputs.Add(output);}catch(OperationCanceledException){throw;}catch(Exception ex){errors.Add(Path.GetFileName(inputs[i])+"："+ex.Message);}progress.Report(i+1);}return "处理完成：成功 "+LastOutputs.Count+" 张，失败 "+errors.Count+" 张。\n保存位置："+folder+(errors.Count>0?"\n"+string.Join("\n",errors):"");},token),report);
            if(operation<2&&LastOutputs.Count>0&&!disposed)await Preview();
        }
        public override void OnEnterPage(){base.OnEnterPage();previewActive=true;if(previewPending)SchedulePreview();}
        public override void OnLeavePage(){previewActive=false;debounce.Stop();if(previewCancel!=null){previewPending=true;previewSize.Text="选择队列中的图片后，可预览实际编码后的文件大小。";}CancelPreview();}
#if SELF_TEST
        internal async Task CheckBehavior(string folder)
        {
            folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);string a=Path.Combine(folder,"first.png"),b=Path.Combine(folder,"second.png");
            using(var art=SelfTest.Art(73,51,false))File.WriteAllBytes(a,Codec.StaticPng(art));using(var art=SelfTest.Art(51,73,true))File.WriteAllBytes(b,Codec.StaticPng(art));
            OutputFolder.TestSetFolder(folder);await Import(new[]{a,b});await ShowInput();
            if(files.Count!=2||inputView.Image==null)throw new Exception("Scramble import/thumbnail failed");
            scrambleMode.SelectedIndex=2;scrambleQuality.Value=37;await Preview();await Export(0,false,false);
            if(LastOutputs.Count!=1||LastPreviewBytes!=new FileInfo(LastOutputs[0]).Length)throw new Exception("Custom JPEG preview size differs from actual export: outputs="+LastOutputs.Count+", preview="+LastPreviewBytes+", actual="+(LastOutputs.Count==0?0:new FileInfo(LastOutputs[0]).Length)+", preview state="+previewSize.Text+", status="+Status.Text);
            scrambleMode.SelectedIndex=0;await Preview();await Export(0,true,false);var wrapped=LastOutputs.ToArray();
            if(wrapped.Length!=2||new FileInfo(wrapped[0]).Length!=LastPreviewBytes)throw new Exception("Original quality batch/size failed");
            files.Clear();files.AddRange(wrapped);RefreshQueue();await Export(2,true,false);
            if(LastOutputs.Count!=2||!File.ReadAllBytes(LastOutputs[0]).SequenceEqual(File.ReadAllBytes(a))||!File.ReadAllBytes(LastOutputs[1]).SequenceEqual(File.ReadAllBytes(b)))throw new Exception("Batch original-file restoration differs");
            decodeMode.SelectedIndex=0;
            Scroller.ScrollControlIntoView(decodeMode);await Task.Delay(60);decodeMode.TestOpen();
            Scroller.ScrollControlIntoView(batchDescramble);await Task.Delay(60);
            if(decodeMode.PopupVisible)throw new Exception("Choice popup remained visible after scrolling its owner");
            Control hit=FindForm();Point center=batchDescramble.PointToScreen(new Point(batchDescramble.Width/2,batchDescramble.Height/2));
            while(hit!=null){Control child=hit.GetChildAtPoint(hit.PointToClient(center),GetChildAtPointSkip.Invisible|GetChildAtPointSkip.Disabled);if(child==null)break;hit=child;}
            if(hit!=batchDescramble)throw new Exception("Batch descramble hit area belongs to another control: "+(hit==null?"null":hit.GetType().Name));
            using(var closeResults=new System.Windows.Forms.Timer{Interval=30})
            {
                closeResults.Tick+=delegate{foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray())if(dialog is ResultForm)dialog.Close();};closeResults.Start();
                Scroller.ScrollControlIntoView(batchDescramble);await Task.Delay(60);batchDescramble.PerformClick();
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
