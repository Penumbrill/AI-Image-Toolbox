using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQImageSwitch
{
    public class ToolPreferences
    {
        readonly string file;
        public Color PaddingColor=Color.White;
        public bool Numbered=true,Loop=true;
        public int Resolution=1,GifDelay=400,GifSize=800;
        public CoverTextOptions CoverText=new CoverTextOptions();
        public string Language="zh";
        public ToolPreferences(string settingsFile)
        {
            file=settingsFile+".options";
            try
            {
                if(!File.Exists(file))return;var lines=File.ReadAllLines(file);int v;
                foreach(var line in lines)
                {
                    int split=line.IndexOf('=');if(split<0)continue;var parts=new[]{line.Substring(0,split),line.Substring(split+1)};
                    if(parts[0]=="covertext"){try{CoverText.Text=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));if(CoverText.Text.Length>200)CoverText.Text=CoverText.Text.Substring(0,200);}catch(FormatException){}continue;}
                    if(parts[0]=="textlayers"){CoverText.LoadLayers(parts[1]);continue;}
                    if(parts[0]=="language"){Language=parts[1]=="en"?"en":"zh";continue;}
                    if(!int.TryParse(parts[1],out v))continue;
                    switch(parts[0])
                    {
                        case "color":PaddingColor=Color.FromArgb(255,Color.FromArgb(v));break;
                        case "number":Numbered=v!=0;break;
                        case "resolution":if(v>=0&&v<=3)Resolution=v;break;
                        case "delay":if(v>=20&&v<=10000)GifDelay=v;break;
                        case "gifsize":if(v>=100&&v<=2048)GifSize=v;break;
                        case "loop":Loop=v!=0;break;
                        case "textposition":if(v>=0&&v<=9)CoverText.Position=v;break;
                        case "textx":if(v>=0&&v<=100)CoverText.X=v;break;
                        case "texty":if(v>=0&&v<=100)CoverText.Y=v;break;
                        case "textsize":if(v>=2&&v<=18)CoverText.SizePercent=v;break;
                        case "textcolor":CoverText.Color=Color.FromArgb(255,Color.FromArgb(v));break;
                    }
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        }
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            Codec.SaveAtomic(file,System.Text.Encoding.UTF8.GetBytes("language="+Language+"\ncolor="+PaddingColor.ToArgb()+"\nnumber="+(Numbered?1:0)+"\nresolution="+Resolution+"\ndelay="+GifDelay+"\ngifsize="+GifSize+"\nloop="+(Loop?1:0)+"\ncovertext="+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(CoverText.Text??""))+"\ntextposition="+CoverText.Position+"\ntextsize="+CoverText.SizePercent+"\ntextcolor="+CoverText.Color.ToArgb()+"\ntextx="+CoverText.X+"\ntexty="+CoverText.Y+"\ntextlayers="+CoverText.SaveLayers()));
        }
    }
    public class FolderPicker : TableLayoutPanel
    {
        readonly ExportLocation location;
        readonly SoftInput text;
        readonly Label hint;
        string feature;
        public string Folder {get{return text.Text;}}
        public string Destination {get{return feature==null?ExportLocation.Normalize(Folder):ExportLocation.FunctionPath(Folder,feature);}}
        public string ExistingDestination {get{try{string path=Destination;return Directory.Exists(path)?path:null;}catch(ArgumentException){return null;}catch(NotSupportedException){return null;}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}}}
        internal void SetFeature(string function){feature=function;hint.Text=Hint;}
        string Hint {get{return feature==null?"自动记住；所有功能共用这个保存位置。":"自动记住根目录；导出时自动放入「"+feature+"」子文件夹。";}}
        public FolderPicker(ExportLocation settings,string function=null)
        {
            location=settings;feature=function;ColumnCount=3;RowCount=2;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;Dock=DockStyle.Top;Margin=new Padding(0);BackColor=Color.Transparent;
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label=Ui.Text("保存位置");label.Anchor=AnchorStyles.Left;label.Margin=new Padding(0,0,12,8);Controls.Add(label,0,0);
            text=new SoftInput {Text=location.Load(),Dock=DockStyle.Fill,Margin=new Padding(0,5,12,12)};Controls.Add(text,1,0);
            Controls.Add(Ui.Button("浏览…",delegate{Browse();}),2,0);
            hint=Ui.Text(Hint,9);Controls.Add(hint,0,1);SetColumnSpan(hint,3);
            text.Leave+=delegate{Remember();};
            SizeChanged+=delegate{hint.MaximumSize=new Size(Math.Max(100,ClientSize.Width-12),0);};
        }
        public void Reload(){text.Text=location.Load();}
        public bool Remember()
        {
            try{string path=ExportLocation.Normalize(text.Text);location.Save(path);text.Text=path;hint.Text=Hint;return true;}
            catch(Exception ex){hint.Text="保存位置有误："+ex.Message;return false;}
        }
        bool Browse()
        {
            using(var d=new FolderBrowserDialog {Description="选择保存位置（会自动记住）",ShowNewFolderButton=true,SelectedPath=Directory.Exists(text.Text)?text.Text:""})
                if(L.Show(d,FindForm())==DialogResult.OK){text.Text=d.SelectedPath;return Remember();}
            return false;
        }
        public bool Ready()
        {
            if(string.IsNullOrWhiteSpace(text.Text)&&!Browse())return false;if(!Remember())return false;
            try{if(feature!=null)ExportLocation.FunctionFolder(text.Text,feature);return true;}
            catch(Exception ex){hint.Text="无法创建导出文件夹："+ex.Message;return false;}
        }
        public void TestSetFolder(string folder){text.Text=folder;if(!Remember())throw new Exception("Folder widget did not persist folder");}
    }
    public abstract class ToolPage : UserControl
    {
        protected readonly TableLayoutPanel Body,Footer;
        protected readonly Panel Scroller;
        protected readonly Label Status;
        protected readonly FlowLayoutPanel Actions;
        public readonly FolderPicker OutputFolder;
        public bool Busy {get;private set;}
        public bool BackgroundBusy {get;private set;}
        CancellationTokenSource cancellation;
        readonly Action<bool> changed;
        readonly Button stop;
        bool layoutReady,layingOut,embedded;
        int lastLayoutWidth=-1;
        Action languageLayout;bool languageLayoutQueued,layoutSettled;
        protected ToolPage(string title,string description,ExportLocation location,Action<bool> busyChanged)
        {
            DoubleBuffered=true;SuspendLayout();Size=new Size(1100,800);Dock=DockStyle.Fill;BackColor=SoftTheme.Background;changed=busyChanged;
            Scroller=new SoftScrollPanel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(22,20,22,20)};Controls.Add(Scroller);
            Body=Ui.Column(new Padding(0));Body.Width=1030;Body.SuspendLayout();Scroller.Controls.Add(Body);
            Ui.Add(Body,Ui.Text(title,21,true));Ui.Add(Body,Ui.Text(description,10));
            Footer=Ui.Footer(new Padding(22,12,22,12));Footer.SuspendLayout();Controls.Add(Footer);Scroller.BringToFront();
            string feature=title=="合成 GIF"?"合成GIF":title=="还原隐藏图"?"双图切换":title=="清除图片信息"?"清信息":title=="打码 / 模糊"?"打码":title=="tag读取"?"tag读取":title=="文件伪装"?"文件伪装":title=="图片混淆"?"图片混淆":title=="二维码制作"?"二维码制作":null;
            OutputFolder=new FolderPicker(location,feature);Ui.Add(Footer,OutputFolder);
            Actions=Ui.Flow();Ui.Add(Footer,Actions);
            stop=Ui.Button("停止任务",delegate{Cancel();});stop.Visible=false;Actions.Controls.Add(stop);
            if(new[]{"双图切换","还原隐藏图","背景显图","文件伪装","打码 / 模糊","二维码制作"}.Contains(title))Actions.Controls.Add(Ui.Button("压缩模拟检查…",async delegate{await TransmissionChecks.Show(this);}));
            Status=Ui.Text("所有处理在本机完成。",9);Ui.Add(Footer,Status);
            Scroller.ClientSizeChanged+=delegate{LayoutContent();};SizeChanged+=delegate{LayoutContent();};
            languageLayout=delegate{layoutSettled=false;QueueLanguageLayout();};L.Changed+=languageLayout;
        }
        void LayoutContent()
        {
            if(!layoutReady||layingOut||Disposing||IsDisposed||Body.Disposing||Body.IsDisposed)return;
            int width=Math.Max(100,Scroller.ClientSize.Width-Scroller.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
            if(width==lastLayoutWidth)return;lastLayoutWidth=width;layingOut=true;
            try
            {
                Body.SuspendLayout();
                Body.Width=width;
                foreach(Control c in Walk(Body))if(c is Label&&StrTag(c)!="fixedwrap"){var limit=new Size(Math.Max(80,c.Parent.ClientSize.Width-c.Parent.Padding.Horizontal-10),0);if(c.MaximumSize!=limit)c.MaximumSize=limit;}
                Status.MaximumSize=new Size(Math.Max(120,ClientSize.Width-50),0);
            }
            finally{Body.ResumeLayout(true);layingOut=false;}
        }
        static string StrTag(Control c){return c.Tag as string;}
        protected void FinishLayout(){Body.ResumeLayout(true);Footer.ResumeLayout(true);layoutReady=true;ResumeLayout(true);LayoutContent();}
        static IEnumerable<Control> Walk(Control c){foreach(Control child in c.Controls){yield return child;foreach(var next in Walk(child))yield return next;}}
        protected Button ActionButton(string title,EventHandler action,bool primary=true)
        {var b=Ui.Button(title,action,primary);Actions.Controls.Add(b);Actions.Controls.SetChildIndex(stop,Actions.Controls.Count-1);return b;}
        public void Cancel(){if(cancellation!=null)cancellation.Cancel();Status.Text="正在停止；已完成的文件保留。";}
        protected async Task Run(Func<CancellationToken,Task<string>> work,bool showResult=true,bool background=false)
        {
            if(Busy)return;Busy=true;BackgroundBusy=background;cancellation=new CancellationTokenSource();if(embedded){foreach(Control c in Body.Controls)if(c!=Actions&&c!=Status)c.Enabled=false;}else Body.Enabled=false;OutputFolder.Enabled=false;stop.Visible=true;UseWaitCursor=!background;
            foreach(Control c in Actions.Controls)if(c!=stop)c.Enabled=false;if(!background)changed(true);
            try
            {
                Status.Text="正在处理…";string summary=await work(cancellation.Token);Status.Text=background?summary:summary.Split('\n')[0];
                if(showResult)using(var f=new ResultForm("处理结果",summary,OutputFolder.ExistingDestination))L.Show(f,FindForm());
            }
            catch(OperationCanceledException){Status.Text="任务已停止；已完成的文件保留。";}
            catch(Exception ex)
            {
                Status.Text="处理失败："+ex.Message;
#if SELF_TEST
                if(Environment.GetCommandLineArgs().Contains("--ui-test"))throw;
#endif
                using(var f=new ResultForm("处理失败",ex.Message,null))L.Show(f,FindForm());
            }
            finally
            {
                Busy=false;BackgroundBusy=false;if(embedded){foreach(Control c in Body.Controls)c.Enabled=true;}else Body.Enabled=true;OutputFolder.Enabled=true;stop.Visible=false;UseWaitCursor=false;
                foreach(Control c in Actions.Controls)c.Enabled=true;cancellation.Dispose();cancellation=null;if(!background)changed(false);
            }
        }
        internal TableLayoutPanel EmbeddedBody{get{return Body;}}
        internal TableLayoutPanel EmbedBody()
        {
            embedded=true;Scroller.Controls.Remove(Body);Footer.Controls.Remove(Actions);Footer.Controls.Remove(Status);Ui.Add(Body,Actions);Ui.Add(Body,Status);Body.BackColor=Color.White;Body.Padding=new Padding(18,16,18,12);Body.Margin=new Padding(0,0,0,16);HeightResize.Tree(Body);return Body;
        }
        void QueueLanguageLayout()
        {
            if(languageLayoutQueued||!IsHandleCreated||IsDisposed||Disposing)return;
            languageLayoutQueued=true;BeginInvoke((MethodInvoker)delegate
            {
                languageLayoutQueued=false;if(IsDisposed||Disposing)return;
                // Apply caption sizes after all locale bindings and delayed DPI work.
                foreach(Control child in Walk(Body).Reverse())child.ResumeLayout(true);
                Body.ResumeLayout(true);lastLayoutWidth=-1;LayoutContent();layoutSettled=true;
            });
        }
        public virtual void OnEnterPage(){OutputFolder.Reload();if(!layoutSettled)QueueLanguageLayout();}
        public void CaptureScroll(bool bottom){Scroller.AutoScrollPosition=new Point(0,bottom?Body.Height:0);}
        public virtual void OnLeavePage(){}
        protected override void Dispose(bool disposing){if(disposing){L.Changed-=languageLayout;layoutReady=false;SuspendLayout();Body.SuspendLayout();Footer.SuspendLayout();Scroller.SuspendLayout();if(cancellation!=null)cancellation.Cancel();}base.Dispose(disposing);}
        public virtual void Demo(){}
    }
    public class FileToolPage : ToolPage
    {
        readonly int kind;
        readonly System.Collections.Generic.List<string> videoFiles=new System.Collections.Generic.List<string>();
        SoftList videoList;
        Func<string> embeddedRoot;
        readonly List<string> files=new List<string>(),temp=new List<string>();
        readonly ListBox list;
        readonly ImageCanvas canvas;
        readonly Label fileInfo;
        readonly ToolPreferences preferences;
        readonly NumericUpDown delay,size;
        readonly CheckBox loop;
        readonly System.Windows.Forms.Timer timer;
        readonly Button preview;
        Bitmap image;
        bool hidden=true,playing;
        int dualPreviewVersion;
#if SELF_TEST
        Action testCanvasPicker;
#endif
        Button hiddenButton,coverButton,restoreButton;
        int frame;
        public FileToolPage(int mode,ExportLocation location,ToolPreferences prefs,Action<bool> busyChanged)
            :base(mode==1?"合成 GIF":mode==2?"还原隐藏图":"清除图片信息",
                mode==1?"把多张图片按顺序合成为动图，可预览播放、调整顺序和间隔。":mode==2?"导入双图 PNG，查看聊天封面与隐藏原图，批量导出还原结果。":"清除 ComfyUI / WebUI 的工作流、提示词、模型与参数信息；保留原始图像数据与色彩配置。",location,busyChanged)
        {
            kind=mode;preferences=prefs;
            Ui.Add(Body,Ui.Flow(Ui.Button("＋ 批量导入图片",async delegate{await Pick();},true),Ui.Button("移除选中",delegate{Remove();}),
                Ui.Button("上移",delegate{MoveFrame(-1);}),Ui.Button("下移",delegate{MoveFrame(1);}),Ui.Button("清空",delegate{Clear();})));
            list=new SoftList {Dock=DockStyle.Top,Height=240,SelectionMode=SelectionMode.MultiExtended,HorizontalScrollbar=true,IntegralHeight=false,Margin=new Padding(0,0,0,12),BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(253,252,255),ForeColor=Ui.Ink};
            var listCard=Ui.Column(new Padding(14,12,14,8));listCard.BackColor=Color.White;listCard.Margin=new Padding(0,0,0,12);Ui.Add(listCard,list);Ui.Add(Body,listCard);
            canvas=new ImageCanvas {Dock=DockStyle.Top,Height=270,MinimumSize=new Size(0,230),Margin=new Padding(0,0,0,12),EmptyText="点击导入图片\n或把文件拖到这里"};
            canvas.Click+=async delegate{await PickEmptyCanvas();};Ui.Add(Body,canvas);
            fileInfo=Ui.Text("尚未导入图片",9);Ui.Add(Body,fileInfo);
            list.SelectedIndexChanged+=delegate{if(!playing)ShowSelected();};
            if(kind==1)
            {
                delay=Number(20,10000,prefs.GifDelay);delay.Increment=10;size=Number(100,2048,prefs.GifSize);
                loop=new SoftCheck {Text="循环播放",Checked=prefs.Loop,AutoSize=true,Margin=new Padding(0,4,12,10)};
                Ui.Add(Body,Ui.Flow(Ui.Text("每帧间隔（毫秒）"),delay,Ui.Text("最长边（像素）"),size,loop));
                timer=new System.Windows.Forms.Timer();timer.Tick+=delegate{if(files.Count==0){StopPreview();return;}frame=(frame+1)%files.Count;ShowFrame(frame);};
                preview=Ui.Button("播放预览",delegate{if(playing)StopPreview();else if(files.Count>0){playing=true;frame=0;timer.Interval=(int)delay.Value;ShowFrame(0);timer.Start();preview.Text="停止预览";}});
                Ui.Add(Body,Ui.Flow(preview));
                delay.ValueChanged+=delegate{timer.Interval=(int)delay.Value;};
                Ui.Add(Body,Ui.Text("画布比例取第一张图，其他图片等比居中；留边使用设置中的颜色。GIF 限制为 256 色，透明处填充留边颜色。输入动图只取首帧。",9));
            }
            if(kind==2){hiddenButton=Ui.Button("查看隐藏图",delegate{hidden=true;ShowSelected();});coverButton=Ui.Button("查看聊天封面",delegate{hidden=false;ShowSelected();});Ui.Add(Body,Ui.Flow(hiddenButton,coverButton));}
            if(kind==3)Ui.Add(Body,Ui.Text("支持 PNG / APNG、JPEG、WebP 和 GIF。删除工作流、提示词、文本、拍摄信息等，保留原始图像数据、色彩配置、方向和动画。PNG 清理后会检查文本 / comf 工作流块确已移除。",9));
            restoreButton=ActionButton(kind==1?"导出 GIF":kind==2?"批量还原并导出":"批量清信息并导出",async delegate{await Export();});
            foreach(Control c in new Control[]{canvas,list,this})
            {
                c.AllowDrop=true;c.DragEnter+=delegate(object s,DragEventArgs e){if(!Busy&&e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
                c.DragDrop+=async delegate(object s,DragEventArgs e){if(!Busy)await Import((string[])e.Data.GetData(DataFormats.FileDrop));};
            }
            if(kind==3){
                var imageCard=Ui.Column(new Padding(18));imageCard.BackColor=Color.White;imageCard.Margin=new Padding(0,0,0,16);Ui.Add(imageCard,Ui.Text("图片信息清理",16,true));
                foreach(Control c in Body.Controls.Cast<Control>().Skip(2).ToArray()){Body.Controls.Remove(c);Ui.Add(imageCard,c);}Actions.Controls.Remove(restoreButton);restoreButton.Text="清理全部图片";Ui.Add(imageCard,Ui.Flow(restoreButton,Ui.Button("清理选中图片",async delegate{await Export(true,true);})));
                Ui.Add(Body,imageCard);
                var videoCard=Ui.Column(new Padding(18));videoCard.BackColor=Color.White;videoCard.Margin=new Padding(0,0,0,16);Ui.Add(videoCard,Ui.Text("视频信息清理",16,true));Ui.Add(videoCard,Ui.Text(VideoGuide,9));
                videoList=new SoftList{Height=190,Dock=DockStyle.Top,SelectionMode=SelectionMode.MultiExtended,IntegralHeight=false,BorderStyle=BorderStyle.None};
                Ui.Add(videoCard,Ui.Flow(Ui.Button("批量导入视频",delegate{PickVideos();}),Ui.Button("移除选中视频",delegate{foreach(int i in videoList.SelectedIndices.Cast<int>().OrderByDescending(i=>i))videoFiles.RemoveAt(i);RefreshVideos();}),Ui.Button("清空视频",delegate{videoFiles.Clear();RefreshVideos();})));
                Ui.Add(videoCard,videoList);Ui.Add(videoCard,Ui.Flow(Ui.Button("清理全部视频",async delegate{await ExportVideos(false);},true),Ui.Button("清理选中视频",async delegate{await ExportVideos(true);})));Ui.Add(Body,videoCard);
                videoCard.AllowDrop=true;videoCard.DragEnter+=delegate(object sender,DragEventArgs e){if(!Busy&&e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};videoCard.DragDrop+=delegate(object sender,DragEventArgs e){if(!Busy)AddVideos((string[])e.Data.GetData(DataFormats.FileDrop));};
            }
            FinishLayout();SetPreviewViewport(Scroller);
        }
        internal const string VideoGuide="支持 MP4 / MOV / M4V、WebM / MKV。清理 ComfyUI / WebUI 写入容器的工作流、提示词、文本、日期与附件，不重编码音视频。保留尺寸、帧率、音轨、时长及旋转；原文件保留。不清除画面水印或编码流内的数据；不支持的容器会报错。";
        void PickVideos(){if(Busy)return;using(var d=new OpenFileDialog{Title="批量导入视频",Filter="视频|*.mp4;*.mov;*.m4v;*.webm;*.mkv",Multiselect=true})if(L.Show(d,FindForm())==DialogResult.OK)AddVideos(d.FileNames);}
        internal void AddVideos(string[] paths){foreach(string p in paths){string full=Path.GetFullPath(p);if(File.Exists(full)&&VideoMetadata.Supported(Path.GetExtension(full).ToLowerInvariant())&&!videoFiles.Contains(full,StringComparer.OrdinalIgnoreCase))videoFiles.Add(full);}RefreshVideos();}
        void RefreshVideos(){videoList.Items.Clear();foreach(string path in videoFiles)videoList.Items.Add(Path.GetFileName(path)+" · "+new FileInfo(path).Length.ToString("N0")+" B");}
        internal async Task ExportVideos(bool selected,bool report=true){if(Busy)return;var input=(selected?videoList.SelectedIndices.Cast<int>().Select(i=>videoFiles[i]):videoFiles).ToArray();if(input.Length==0){Status.Text="请先导入或选中视频。";return;}if(!OutputFolder.Ready())return;string folder=OutputFolder.Destination;IProgress<int> progress=new Progress<int>(n=>Status.Text="正在处理 "+n+" / "+input.Length+"…");await Run(token=>Task.Run(()=>{int count=0;var errors=new List<string>();for(int i=0;i<input.Length;i++){token.ThrowIfCancellationRequested();try{VideoMetadata.Clean(input[i],folder,token);count++;}catch(OperationCanceledException){throw;}catch(Exception ex){errors.Add(Path.GetFileName(input[i])+"："+ex.Message);}progress.Report(i+1);}return "视频处理完成："+count+" / "+input.Length+"。"+(errors.Count>0?"\n"+string.Join("\n",errors):"");},token),report);}
        internal void SetPreviewViewport(Control viewport){canvas.FollowViewport(viewport,.54,230,720);}
        async Task PickEmptyCanvas()
        {
            if(Busy||canvas.Image!=null)return;
#if SELF_TEST
            if(testCanvasPicker!=null){testCanvasPicker();return;}
#endif
            await Pick();
        }
        public static NumericUpDown Number(int min,int max,int value){return new SoftNumber {Minimum=min,Maximum=max,Value=value,Width=115,Margin=new Padding(0,0,16,10)};}
        internal async Task Pick()
        {
            if(Busy)return;using(var d=new OpenFileDialog {Title="批量导入图片",Filter=kind==2?"双图 PNG|*.png;*.apng":kind==3?"图片|*.png;*.apng;*.webp;*.jpg;*.jpeg;*.gif":"图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff",Multiselect=true})
                if(L.Show(d,FindForm())==DialogResult.OK)await Import(d.FileNames);
        }
        async Task Import(string[] paths)
        {
            if(Busy)return;if(kind==3){AddVideos(paths.Where(p=>VideoMetadata.Supported(Path.GetExtension(p).ToLowerInvariant())).ToArray());paths=paths.Where(p=>!VideoMetadata.Supported(Path.GetExtension(p).ToLowerInvariant())).ToArray();if(paths.Length==0)return;}StopPreview();var accepted=new List<string>();var errors=new List<string>();var existing=new HashSet<string>(files,StringComparer.OrdinalIgnoreCase);
            await Run(async token=>
            {
                await Task.Run(()=>
                {
                    foreach(string path in paths)
                    {
                        token.ThrowIfCancellationRequested();try
                        {
                            string full=Path.GetFullPath(path);if(!existing.Add(full))continue;
                            if(kind==3){byte[] b=ImageTools.Read(full);if(b.Length<12)throw new InvalidDataException("不是有效图片。");}
                            else using(var b=Codec.Load(full)){}
                            if(kind==1&&files.Count+accepted.Count>=200)throw new ArgumentException("最多导入 200 张 GIF 帧。");accepted.Add(full);
                        }
                        catch(OperationCanceledException){throw;}
                        catch(Exception ex){errors.Add(Path.GetFileName(path)+"："+ex.Message);}
                    }
                });
                files.AddRange(accepted);Refresh();return "已导入 "+accepted.Count+" 张图片。"+(errors.Count>0?"\n"+string.Join("\n",errors):"");
            },false);
            if(errors.Count>0)using(var f=new ResultForm("导入结果",string.Join("\n",errors),null))L.Show(f,FindForm());
        }
        void Refresh(int selected=0)
        {
            list.BeginUpdate();list.Items.Clear();for(int i=0;i<files.Count;i++)list.Items.Add((i+1).ToString("D3")+"   "+DisplayName(i));
            list.HorizontalExtent=list.Items.Count==0?0:list.Items.Cast<string>().Max(text=>TextRenderer.MeasureText(text,list.Font).Width)+30;
            list.EndUpdate();if(files.Count>0)list.SelectedIndex=Math.Min(Math.Max(0,selected),files.Count-1);else ShowSelected();
        }
        string DisplayName(int index){return temp.Contains(files[index])?"示例图片_"+(index+1)+".png":Path.GetFileName(files[index]);}
        void ShowSelected(){dualPreviewVersion++;if(list.SelectedIndex>=0)ShowFrame(list.SelectedIndex);else{if(image!=null)image.Dispose();image=null;canvas.Image=null;canvas.EmptyText="点击导入图片\n或把文件拖到这里";canvas.Invalidate();fileInfo.Text="尚未导入图片";}}
        void ShowFrame(int index)
        {
            if(kind==2){ShowDualFrame(index);return;}
            try
            {
                var next=kind==2&&hidden?Codec.Decode(Codec.Restore(ImageTools.Read(files[index]))):Codec.Load(files[index]);
                if(kind==1&&playing)
                {
                    using(var first=Codec.Load(files[0]))
                    {Size bounds=Codec.OutputSize(first,(int)size.Value);var fitted=Codec.Fit(next,bounds.Width,bounds.Height,preferences.PaddingColor);next.Dispose();next=fitted;}
                }
                if(image!=null)image.Dispose();image=next;canvas.Image=image;canvas.Invalidate();
                fileInfo.Text=(index+1)+" / "+files.Count+"  ·  "+DisplayName(index)+"  ·  "+image.Width+" × "+image.Height;
            }
            catch(Exception ex)
            {if(image!=null)image.Dispose();image=null;canvas.Image=null;canvas.EmptyText=kind==3&&Path.GetExtension(files[index]).Equals(".webp",StringComparison.OrdinalIgnoreCase)?"WebP 支持无损清信息\n此预览区暂不显示 WebP":"无法预览此图\n请检查图片文件";canvas.Invalidate();fileInfo.Text=Path.GetFileName(files[index])+"："+ex.Message;}
        }
        void StopPreview(){playing=false;if(timer!=null)timer.Stop();if(preview!=null)preview.Text="播放预览";}
        void Remove(){if(Busy)return;StopPreview();int current=list.SelectedIndex;foreach(int i in list.SelectedIndices.Cast<int>().OrderByDescending(i=>i))files.RemoveAt(i);Refresh(current);}
        void Clear(){if(Busy)return;StopPreview();files.Clear();Refresh();}
        void MoveFrame(int offset)
        {if(Busy)return;StopPreview();if(list.SelectedIndices.Count!=1){Status.Text="请只选中一张，再调整顺序。";return;}int i=list.SelectedIndex,n=i+offset;if(n<0||n>=files.Count)return;string value=files[i];files.RemoveAt(i);files.Insert(n,value);Refresh(n);}
        async void ShowDualFrame(int index)
        {
            int version=++dualPreviewVersion;bool black=hidden;string path=files[index];Bitmap next=null;
            try
            {
                next=await Task.Run(()=>
                {
                    using(var source=DualBlend.DecodeView(ImageTools.Read(path),black,CancellationToken.None))
                    {
                        Size bounds=Codec.OutputSize(source,720);
                        using(var fitted=Codec.Fit(source,bounds.Width,bounds.Height,Color.Transparent))
                            return (Bitmap)fitted.Clone();
                    }
                });
                if(IsDisposed||Disposing||version!=dualPreviewVersion){next.Dispose();return;}
                if(image!=null)image.Dispose();image=next;next=null;canvas.Image=image;canvas.Invalidate();
                fileInfo.Text=(index+1)+" / "+files.Count+"  ·  "+DisplayName(index)+"  ·  "+image.Width+" × "+image.Height;
            }
            catch(Exception ex)
            {
                if(next!=null)next.Dispose();if(IsDisposed||Disposing||version!=dualPreviewVersion)return;
                canvas.Image=null;if(image!=null){image.Dispose();image=null;}canvas.EmptyText="无法预览此图\n请检查图片文件";canvas.Invalidate();fileInfo.Text=Path.GetFileName(path)+"："+ex.Message;
            }
        }
        internal void ConfigureEmbeddedRestore(Func<string> root){embeddedRoot=root;OutputFolder.SetFeature("双图切换");foreach(Control c in Body.Controls)if(c is Label&&c.Text=="还原隐藏图")c.Text="还原双图 PNG";}
        async Task Export(bool report=true,bool selected=false)
        {
            if(Busy)return;
            if(files.Count==0){Status.Text="请先导入图片。";return;}if(kind==1&&files.Count<2){Status.Text="合成 GIF 至少需要两张图片。";return;}
            if(embeddedRoot!=null){string root=embeddedRoot();if(root==null)return;OutputFolder.TestSetFolder(root);}
            if(!OutputFolder.Ready()){Status.Text="请填写有效的保存位置。";return;}StopPreview();string folder=OutputFolder.Destination;var snapshot=(selected?list.SelectedIndices.Cast<int>().Select(i=>files[i]):files).ToList();if(snapshot.Count==0){Status.Text="请先选中图片。";return;}
            int ms=kind==1?(int)delay.Value:0,max=kind==1?(int)size.Value:0;bool repeat=kind==1&&loop.Checked;Color background=preferences.PaddingColor;
            if(kind==1){preferences.GifDelay=ms;preferences.GifSize=max;preferences.Loop=repeat;try{preferences.Save();}catch(Exception ex){Status.Text="设置无法记住："+ex.Message;}}
            IProgress<int> reporter=new Progress<int>(n=>Status.Text="正在处理 "+n+" / "+snapshot.Count+"…");
            await Run(token=>Task.Run(()=>
            {
                if(kind==1)
                {
                    byte[] bytes=GifCodec.Merge(snapshot,max,ms,repeat,background,token,reporter);
                    token.ThrowIfCancellationRequested();string path=ImageTools.Unique(folder,snapshot[0],"_合成",".gif");Codec.SaveAtomic(path,bytes);
                    return "GIF 已导出，包含 "+snapshot.Count+" 帧。\n"+path;
                }
                int count=0;var errors=new List<string>();bool stopped=false;
                for(int i=0;i<snapshot.Count;i++)
                {
                    if(token.IsCancellationRequested){stopped=true;break;}
                    try
                    {
                        if(kind==2){byte[] input=ImageTools.Read(snapshot[i]);if(DualBlend.IsLegacy(input))DualBlend.RestorePair(snapshot[i],folder,token);else{byte[] restored=Codec.Restore(input);token.ThrowIfCancellationRequested();Codec.SaveAtomic(ImageTools.Unique(folder,snapshot[i],"_已还原",".png"),restored);}}
                        else ImageTools.Clean(snapshot[i],folder);count++;
                    }
                    catch(OperationCanceledException){stopped=true;break;}
                    catch(Exception ex){errors.Add(Path.GetFileName(snapshot[i])+"："+ex.Message);}reporter.Report(i+1);
                }
                return (stopped?"任务已停止":"处理完成")+"：成功 "+count+" 张，失败 "+errors.Count+" 张。\n保存位置："+folder+(errors.Count>0?"\n\n"+string.Join("\n",errors):"\n原始文件保留。");
            }),report);
        }
#if SELF_TEST
        internal async Task CheckVideoBehavior(string folder){
            if(kind!=3)throw new Exception("Wrong metadata page");Directory.CreateDirectory(folder);OutputFolder.TestSetFolder(folder);Demo();int images=files.Count;
            Func<string,byte[],byte[]> box=(type,data)=>{using(var ms=new MemoryStream()){byte[] length=new byte[4];length[0]=(byte)((data.Length+8)>>24);length[1]=(byte)((data.Length+8)>>16);length[2]=(byte)((data.Length+8)>>8);length[3]=(byte)(data.Length+8);ms.Write(length,0,4);byte[] name=System.Text.Encoding.ASCII.GetBytes(type);ms.Write(name,0,4);ms.Write(data,0,data.Length);return ms.ToArray();}};
            byte[] fixture=box("ftyp",System.Text.Encoding.ASCII.GetBytes("isom0000" )).Concat(box("moov",box("udta",System.Text.Encoding.UTF8.GetBytes("PRIVATE_WORKFLOW")))).Concat(box("mdat",new byte[]{1,2,3,4})).ToArray();
            string first=Path.Combine(folder,"first.mp4"),second=Path.Combine(folder,"second.mp4");File.WriteAllBytes(first,fixture);File.WriteAllBytes(second,fixture);AddVideos(new[]{first,second,first});if(videoFiles.Count!=2||files.Count!=images)throw new Exception("Video import mixed image state or kept duplicates");videoList.SelectedIndex=1;await ExportVideos(true,false);if(Directory.GetFiles(OutputFolder.Destination,"*.mp4").Length!=1||files.Count!=images||videoFiles.Count!=2)throw new Exception("Selected video export/isolation failed");
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();string target=Path.Combine(folder,"cancel");bool stopped=false;try{VideoMetadata.Clean(first,target,cancel.Token);}catch(OperationCanceledException){stopped=true;}if(!stopped||Directory.GetFiles(target).Length!=0)throw new Exception("Video cancellation left files");}
            videoFiles.Clear();RefreshVideos();
        }
        internal void CheckEmptyCanvasClick()
        {
            Clear();int picks=0;testCanvasPicker=delegate{picks++;};
            try
            {
                canvas.TestClick();if(picks!=1)throw new Exception("Empty file canvas did not request image selection");
                Demo();list.ClearSelected();if(files.Count==0||canvas.Image!=null)throw new Exception("File canvas deselection fixture was not empty");
                canvas.TestClick();if(picks!=2)throw new Exception("Empty file canvas ignored image selection while queue retained files");
                using(var shown=new Bitmap(20,20)){canvas.Image=shown;canvas.TestClick();canvas.Image=null;}
                if(picks!=2)throw new Exception("Loaded file canvas unexpectedly requested image selection");
            }
            finally{testCanvasPicker=null;Clear();}
        }
        internal async Task CheckMergedRestore(string folder)
        {
            if(kind!=2)throw new Exception("Wrong restore component");Clear();string input=Path.Combine(folder,"merged-double.png"),expected=Path.Combine(folder,"expected.png");Directory.CreateDirectory(folder);
            using(var a=SelfTest.Art(53,37,false))using(var b=SelfTest.Art(53,37,true)){File.WriteAllBytes(input,Codec.Encode(a,b));File.WriteAllBytes(expected,Codec.StaticPng(b));}
            await Import(new[]{input,input});if(files.Count!=1)throw new Exception("Merged restore duplicate import failed");
            OutputFolder.TestSetFolder(folder);string destination=OutputFolder.Destination;
            if(Directory.Exists(Path.Combine(folder,"还原")))throw new Exception("Restore import created an obsolete folder");
            var before=Directory.Exists(destination)?Directory.GetFiles(destination).ToList():new List<string>();await Export(false);var created=Directory.GetFiles(destination).Except(before).ToArray();
            if(Busy||created.Length!=1||!Codec.Restore(File.ReadAllBytes(input)).SequenceEqual(File.ReadAllBytes(created[0])))throw new Exception("Embedded PNG restoration/shared folder failed");
            if(OutputFolder.ExistingDestination!=destination||Directory.Exists(Path.Combine(folder,"还原")))throw new Exception("Restore created/reported an obsolete folder");
            Clear();string legacy=Path.Combine(folder,"old-transparent.png");File.WriteAllBytes(legacy,Convert.FromBase64String(DualBlendTests.Samples[1]));await Import(new[]{input,legacy});
            before=Directory.GetFiles(destination).ToList();await Export(false);created=Directory.GetFiles(destination).Except(before).ToArray();
            if(Busy||created.Length!=3)throw new Exception("Mixed dynamic/legacy restoration did not export three files");
            Clear();string empty=Path.Combine(folder,"empty-export-"+Guid.NewGuid().ToString("N"));OutputFolder.TestSetFolder(empty);var previousRoot=embeddedRoot;
            try{embeddedRoot=delegate{throw new Exception("Empty restore requested an output folder");};await Export(false);}finally{embeddedRoot=previousRoot;}
            if(Directory.Exists(empty))throw new Exception("Empty restore export created a directory");
            OutputFolder.TestSetFolder(folder);
        }
#endif
        public override void OnEnterPage(){base.OnEnterPage();if(kind==1){delay.Value=preferences.GifDelay;size.Value=preferences.GifSize;loop.Checked=preferences.Loop;}}
        public override void OnLeavePage(){StopPreview();}
#if SELF_TEST
        public override void Demo()
        {
            if(files.Count>0)return;
            for(int i=0;i<3;i++)
            {
                string path=Path.Combine(Path.GetTempPath(),"QQImageSwitch_demo_"+Guid.NewGuid().ToString("N")+".png");temp.Add(path);
                using(var a=SelfTest.Art(700,520,i%2!=0))using(var b=SelfTest.Art(700,520,true))Codec.SaveAtomic(path,kind==2?Codec.Encode(a,b):Codec.StaticPng(a));files.Add(path);
            }
            Refresh();Status.Text="示例图片；点击导入可添加你的图片。";
        }
#endif
        protected override void Dispose(bool disposing)
        {if(disposing){dualPreviewVersion++;if(timer!=null)timer.Dispose();if(image!=null){canvas.Image=null;image.Dispose();}foreach(string p in temp)try{File.Delete(p);}catch(IOException){}}base.Dispose(disposing);}
    }
    public class RedactCanvas : ImageCanvas
    {
        bool brush;
        public bool Brush {get{return brush;}set{brush=value;Cursor=brush?BrushCursor.Blank:Cursors.Cross;RefreshBrush();}}
        public int BrushWidth=36;
        public int Strength=16;
        public string Effect="mosaic";
        public Color FillColor=Color.Black;
        Bitmap brushPreview;
        Rectangle previewBounds;
        Point pointer;
        bool hasPointer;
        public double CircleDiameter {get{return BrushWidth*PixelScale;}}
        public event Action<int> BrushSizeRequested;
        public Rectangle Selection {get;set;}
        public readonly List<Point> Stroke=new List<Point>();
        public event EventHandler SelectionChanged;
        Point start;
        bool drawing;
        public RedactCanvas(){ViewChanged+=delegate{RefreshBrush();};}
        Point ImagePoint(Point p)
        {
            var image=ToImage(p);return new Point(Math.Max(0,Math.Min(Image.Width-1,(int)Math.Round(image.X))),Math.Max(0,Math.Min(Image.Height-1,(int)Math.Round(image.Y))));
        }
        public void RefreshBrush(){if(brushPreview!=null){brushPreview.Dispose();brushPreview=null;}Invalidate();}
        protected override void OnKeyDown(KeyEventArgs e)
        {base.OnKeyDown(e);if(e.KeyCode==Keys.OemOpenBrackets||e.KeyCode==Keys.OemCloseBrackets){if(BrushSizeRequested!=null)BrushSizeRequested(e.KeyCode==Keys.OemOpenBrackets?-4:4);e.Handled=true;}}
        public void ClearSelection(){Selection=Rectangle.Empty;Stroke.Clear();Invalidate();if(SelectionChanged!=null)SelectionChanged(this,EventArgs.Empty);}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(Image==null||e.Button!=MouseButtons.Left||!ImageBounds.Contains(e.Location))return;
            ClearSelection();start=ImagePoint(e.Location);drawing=true;Capture=true;if(Brush)Stroke.Add(start);Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);if(Image==null)return;
            if(ImageBounds.Contains(e.Location)){pointer=e.Location;hasPointer=true;RefreshBrush();}
            if(!drawing||e.Button==MouseButtons.Middle)return;Point p=ImagePoint(e.Location);
            if(Brush){if(Stroke.Count==0||Stroke[Stroke.Count-1]!=p)Stroke.Add(p);}
            else Selection=Rectangle.FromLTRB(Math.Min(start.X,p.X),Math.Min(start.Y,p.Y),Math.Max(start.X,p.X)+1,Math.Max(start.Y,p.Y)+1);Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {base.OnMouseUp(e);if(!drawing)return;if(!Brush&&Selection.IsEmpty)Selection=new Rectangle(start,new Size(1,1));drawing=false;Capture=false;if(SelectionChanged!=null)SelectionChanged(this,EventArgs.Empty);Invalidate();}
        public void TestGesture(Point a,Point b)
        {OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,a.X,a.Y,0));OnMouseMove(new MouseEventArgs(MouseButtons.Left,1,b.X,b.Y,0));OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,b.X,b.Y,0));}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(Image==null)return;var rect=ImageBounds;var g=e.Graphics;
            var state=g.Save();g.SetClip(rect,System.Drawing.Drawing2D.CombineMode.Intersect);g.TranslateTransform(rect.X,rect.Y);g.ScaleTransform((float)PixelScale,(float)PixelScale);
            using(var fill=new SolidBrush(Color.FromArgb(80,108,75,218)))using(var pen=new Pen(Color.FromArgb(180,108,75,218),Math.Max(1,Image.Width*2f/rect.Width)))
            {
                if(Brush&&Stroke.Count>0)
                {
                    using(var brush=new Pen(fill,BrushWidth){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                    {if(Stroke.Count==1)g.FillEllipse(fill,Stroke[0].X-BrushWidth/2,Stroke[0].Y-BrushWidth/2,BrushWidth,BrushWidth);else g.DrawLines(brush,Stroke.ToArray());}
                }
                else if(!Selection.IsEmpty){g.FillRectangle(fill,Selection);g.DrawRectangle(pen,Selection);}
            }
            g.Restore(state);
            if(Brush&&hasPointer&&ImageBounds.Contains(pointer))PaintBrush(g);
        }
        void PaintBrush(Graphics g)
        {
            var center=ImagePoint(pointer);float radius=(float)Math.Max(1.5,CircleDiameter/2);
            var circle=new RectangleF(pointer.X-radius,pointer.Y-radius,radius*2,radius*2);
            if(brushPreview==null)
            {
                int pad=Math.Max(1,Strength),half=BrushWidth/2;
                previewBounds=Rectangle.Intersect(new Rectangle(center.X-half-pad,center.Y-half-pad,BrushWidth+pad*2,BrushWidth+pad*2),new Rectangle(Point.Empty,Image.Size));
                if(previewBounds.Width>0&&previewBounds.Height>0)
                using(var crop=new Bitmap(previewBounds.Width,previewBounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using(var paint=Graphics.FromImage(crop)){paint.CompositingMode=CompositingMode.SourceCopy;paint.DrawImage(Image,new Rectangle(Point.Empty,crop.Size),previewBounds,GraphicsUnit.Pixel);}
                    brushPreview=ImageTools.Redact(crop,Rectangle.Empty,new List<Point>{new Point(center.X-previewBounds.X,center.Y-previewBounds.Y)},BrushWidth,Strength,Effect,FillColor);
                }
            }
            var state=g.Save();g.SetClip(ImageBounds,CombineMode.Intersect);
            using(var path=new GraphicsPath()){path.AddEllipse(circle);g.SetClip(path,CombineMode.Intersect);}
            if(brushPreview!=null)
            {var location=ToScreen(previewBounds.Location);g.InterpolationMode=PixelScale>=1?InterpolationMode.NearestNeighbor:InterpolationMode.HighQualityBilinear;g.DrawImage(brushPreview,new RectangleF(location.X,location.Y,(float)(previewBounds.Width*PixelScale),(float)(previewBounds.Height*PixelScale)));}
            g.Restore(state);g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var dark=new Pen(Color.FromArgb(230,25,25,30),3))using(var light=new Pen(Color.White,1.2f)){g.DrawEllipse(dark,circle);g.DrawEllipse(light,circle);}
        }
        internal void TestPointer(Point at){OnMouseMove(new MouseEventArgs(MouseButtons.None,0,at.X,at.Y,0));}
        internal byte[] PreviewBytes(){using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return Codec.StaticPng(bitmap);}}
        protected override void Dispose(bool disposing){if(disposing&&brushPreview!=null)brushPreview.Dispose();base.Dispose(disposing);}
    }
    public class RedactPage : ToolPage
    {
        readonly RedactCanvas canvas;
        readonly SoftCombo mode,effect;
        readonly NumericUpDown brush,strength;
        readonly Label info;
        readonly BitmapHistory history=new BitmapHistory();
        Bitmap original,current;
        string name="图片.png";
        Color fill=Color.Black;
        readonly FlowLayoutPanel brushRow;
        readonly Label zoomLabel;
        readonly System.Windows.Forms.Timer zoomDebounce=new System.Windows.Forms.Timer{Interval=150};
        bool disposingView;
        public RedactPage(ExportLocation location,Action<bool> busyChanged):base("打码 / 模糊","先导入图片，在图上框选或涂抹，然后应用效果。支持撤销和恢复原图。",location,busyChanged)
        {
            Ui.Add(Body,Ui.Flow(Ui.Button("打开图片…",async delegate{await Pick();},true),Ui.Button("撤销一步",async delegate{await MoveHistory(false);}),Ui.Button("重做一步",async delegate{await MoveHistory(true);}),Ui.Button("恢复原图",delegate{Reset();}),Ui.Button("适应窗口",delegate{canvas.ResetView();}),Ui.Button("原始比例",delegate{canvas.ActualPixels();})));
            mode=new SoftCombo {DropDownStyle=ComboBoxStyle.DropDownList,Width=135,Margin=new Padding(0,0,14,10)};mode.Items.AddRange(new object[]{"框选区域","涂抹画笔"});mode.SelectedIndex=0;
            effect=new SoftCombo {DropDownStyle=ComboBoxStyle.DropDownList,Width=135,Margin=new Padding(0,0,14,10)};effect.Items.AddRange(new object[]{"马赛克","模糊","纯色遮盖"});effect.SelectedIndex=0;
            brush=FileToolPage.Number(4,500,36);strength=FileToolPage.Number(2,100,16);
            var color=Ui.Button("遮盖颜色",delegate{using(var d=new ColorDialog {Color=fill,FullOpen=true})if(L.Show(d,FindForm())==DialogResult.OK){fill=d.Color;ConfigureBrush();}});
            brushRow=Ui.Flow(Ui.Text("选区"),mode,Ui.Text("效果"),effect,Ui.Text("画笔 px"),brush,Ui.Text("强度"),strength,color);Ui.Add(Body,brushRow);
            zoomLabel=Ui.Text("滚轮缩放 · 中键拖动 · [ / ] 调画笔 · 圆圈内实时预览效果",9);zoomLabel.AutoSize=false;zoomLabel.Dock=DockStyle.Top;zoomLabel.Tag="fixedwrap";Ui.Add(Body,zoomLabel);
            EventHandler fitZoomLabel=delegate{int height=zoomLabel.Font.Height*2+4;zoomLabel.Height=height;var row=Body.RowStyles[Body.GetRow(zoomLabel)];row.SizeType=SizeType.Absolute;row.Height=height+zoomLabel.Margin.Vertical;};zoomLabel.FontChanged+=fitZoomLabel;fitZoomLabel(null,EventArgs.Empty);
            canvas=new RedactCanvas {Dock=DockStyle.Fill,Height=640,MinimumSize=new Size(0,300),Margin=new Padding(0,0,0,12),Cursor=Cursors.Cross,EmptyText="点击打开图片\n或拖入一张图片"};Ui.Add(Body,canvas);
            info=Ui.Text("尚未打开图片",9);Ui.Add(Body,info);
            ActionButton("应用到选区",async delegate{await Apply();});ActionButton("清除选区",delegate{canvas.ClearSelection();},false);
            Ui.Add(Body,Ui.Text("选区外的像素保持不变，保存为原尺寸 PNG。GIF / 多页 TIFF 仅编辑首帧。撤销与重做最多保留 50 步，临时历史总计最多 256 MiB；新操作会清空重做历史。",9));
            mode.SelectedIndexChanged+=delegate{canvas.Brush=mode.SelectedIndex==1;canvas.ClearSelection();};brush.ValueChanged+=delegate{ConfigureBrush();};strength.ValueChanged+=delegate{ConfigureBrush();};effect.SelectedIndexChanged+=delegate{ConfigureBrush();};
            canvas.BrushSizeRequested+=delta=>brush.Value=Math.Max(brush.Minimum,Math.Min(brush.Maximum,brush.Value+delta));
            zoomDebounce.Tick+=delegate{zoomDebounce.Stop();zoomLabel.Text="缩放 "+(canvas.PixelScale*100).ToString("0")+"% · 滚轮缩放 · 中键拖动 · [ / ] 调画笔 · 圆圈实时预览";};
            canvas.ViewChanged+=delegate{if(disposingView)return;zoomDebounce.Stop();zoomDebounce.Start();};
            canvas.SelectionChanged+=delegate{if(current!=null)Status.Text=canvas.Brush?"已涂抹，可点击「应用到选区」。":"已框选，可点击「应用到选区」。";};
            canvas.Click+=async delegate{if(current==null)await Pick();};
            canvas.AllowDrop=true;canvas.DragEnter+=delegate(object s,DragEventArgs e){if(!Busy&&e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
            canvas.DragDrop+=async delegate(object s,DragEventArgs e){if(!Busy){var paths=(string[])e.Data.GetData(DataFormats.FileDrop);if(paths.Length>0)await LoadImage(paths[0]);}};
            ActionButton("保存打码图片",async delegate{if(current==null){Status.Text="请先打开图片。";return;}if(!OutputFolder.Ready())return;string folder=OutputFolder.Destination;using(var bitmap=(Bitmap)current.Clone())await Run(token=>Task.Run(()=>{token.ThrowIfCancellationRequested();string path=ImageTools.Unique(folder,name,"_已打码",".png");Codec.SaveAtomic(path,Codec.StaticPng(bitmap));return "图片已保存。\n"+path;}));});
            Action fitPreview=delegate{int height=HeightResize.Resolve(canvas,Math.Max(300,Scroller.ClientSize.Height-canvas.Top-Scroller.Padding.Bottom-65));var row=Body.RowStyles[Body.GetRow(canvas)];row.SizeType=SizeType.Absolute;row.Height=height+canvas.Margin.Vertical;if(canvas.Height!=height)canvas.Height=height;};Scroller.ClientSizeChanged+=delegate{fitPreview();};
            FinishLayout();fitPreview();HeightResize.Enable(canvas,h=>fitPreview());
        }
        void ConfigureBrush(){canvas.BrushWidth=(int)brush.Value;canvas.Strength=(int)strength.Value;canvas.Effect=new[]{"mosaic","blur","solid"}[effect.SelectedIndex];canvas.FillColor=fill;canvas.RefreshBrush();}
        async Task Pick(){if(Busy)return;using(var d=new OpenFileDialog {Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff"})if(L.Show(d,FindForm())==DialogResult.OK)await LoadImage(d.FileName);}
        async Task LoadImage(string path)
        {
            await Run(async token=>{var bitmap=await Task.Run(()=>Codec.Load(path));DisposeImages();original=bitmap;current=(Bitmap)original.Clone();name=Path.GetFileName(path);ShowImage();canvas.ResetView();return "已打开 "+name+"，可用滚轮缩放和中键平移进行编辑。";},false);
        }
        void ShowImage(){canvas.Image=current;canvas.ClearSelection();canvas.RefreshBrush();canvas.Invalidate();info.Text=current==null?"尚未打开图片":name+"  ·  "+current.Width+" × "+current.Height+"  ·  可撤销 "+history.Count+" 步 · "+L.T("可重做")+" "+history.RedoCount+" 步";}
        async Task Apply()
        {
            if(Busy||current==null)return;if(canvas.Selection.IsEmpty&&canvas.Stroke.Count==0){Status.Text="请先在图上框选或涂抹。";return;}
            var selection=canvas.Selection;var path=canvas.Brush?canvas.Stroke.ToList():null;int width=(int)brush.Value,block=(int)strength.Value;string operation=new[]{"mosaic","blur","solid"}[effect.SelectedIndex];Color color=fill;
            await Run(async token=>
            {
                var next=await Task.Run(()=>{var b=ImageTools.Redact(current,selection,path,width,block,operation,color);try{token.ThrowIfCancellationRequested();history.Push(current);return b;}catch{b.Dispose();throw;}});current.Dispose();current=next;
                ShowImage();return "已应用效果。可继续选择其他区域，也可撤销。";
            },false);
        }
        void Undo(){if(Busy||history.Count==0)return;var next=history.Move(current,false);current.Dispose();current=next;ShowImage();}
        async Task MoveHistory(bool forward){if(Busy||current==null||(forward?history.RedoCount:history.Count)==0)return;await Run(async token=>{var next=await Task.Run(()=>history.Move(current,forward));if(next!=null){current.Dispose();current=next;ShowImage();}return L.T(forward?"已重做。":"已撤销。");},false);}
        void Reset(){if(Busy||original==null)return;if(current!=null)current.Dispose();history.Clear();current=(Bitmap)original.Clone();ShowImage();}
        void DisposeImages(){canvas.Image=null;if(original!=null)original.Dispose();if(current!=null)current.Dispose();history.Clear();original=current=null;}
#if SELF_TEST
        public override void Demo(){if(original!=null)return;original=SelfTest.Art(700,520,true);name="编辑示例.png";current=ImageTools.Redact(original,new Rectangle(190,230,280,110),null,36,24,"mosaic",Color.Black);ShowImage();}
        public void ShowBrushDemo(){Demo();mode.SelectedIndex=1;brush.Value=90;strength.Value=32;canvas.TestPointer(Point.Round(canvas.ToScreen(new Point(350,240))));}
        public async Task CheckEditingBehavior()
        {
            Demo();Reset();byte[] before=Codec.StaticPng(current);
            canvas.TestGesture(new Point(canvas.Width/3,canvas.Height/3),new Point(canvas.Width*2/3,canvas.Height*2/3));
            if(canvas.Selection.IsEmpty)throw new Exception("Rectangle gesture was not mapped to image pixels");
            await Apply();if(history.Count!=1||before.SequenceEqual(Codec.StaticPng(current)))throw new Exception("Apply button did not edit selection");
            byte[] edited=Codec.StaticPng(current);await MoveHistory(false);await MoveHistory(true);if(!edited.SequenceEqual(Codec.StaticPng(current)))throw new Exception("Redo failed");Undo();if(!before.SequenceEqual(Codec.StaticPng(current)))throw new Exception("Undo did not restore original image");
            for(int i=0;i<4;i++){mode.TestChoice(i%2);effect.TestChoice(i%3);}
            mode.TestChoice(1);effect.TestChoice(0);
            canvas.TestGesture(new Point(canvas.Width/3,canvas.Height/2),new Point(canvas.Width*2/3,canvas.Height/2));
            if(canvas.Stroke.Count<2)throw new Exception("Brush gesture was not captured");
            await Apply();if(history.Count!=1)throw new Exception("Brush application did not create undo history");
            Reset();if(!before.SequenceEqual(Codec.StaticPng(current)))throw new Exception("Reset original did not restore image");
            var anchor=new Point(canvas.Width/2,canvas.Height/2);var coordinate=canvas.ToImage(anchor);canvas.TestWheel(anchor,120);
            var after=canvas.ToImage(anchor);if(Math.Abs(after.X-coordinate.X)>1||Math.Abs(after.Y-coordinate.Y)>1||canvas.Zoom<=1)throw new Exception("Wheel zoom did not preserve mouse anchor");
            canvas.TestPan(anchor,new Point(anchor.X+35,anchor.Y+28));if(Math.Abs(canvas.Pan.X-35)>1||Math.Abs(canvas.Pan.Y-28)>1)throw new Exception("Middle mouse pan failed");
            var begin=Point.Round(canvas.ToScreen(new Point(280,240)));var end=Point.Round(canvas.ToScreen(new Point(340,260)));canvas.TestGesture(begin,end);
            if(canvas.Stroke.Count<2||Math.Abs(canvas.Stroke[0].X-280)>2||Math.Abs(canvas.Stroke[0].Y-240)>2)throw new Exception("Zoomed/panned brush mapped to incorrect pixels");
            double zoom=canvas.Zoom;var pan=canvas.Pan;await Apply();if(canvas.Zoom!=zoom||canvas.Pan!=pan)throw new Exception("Applying effect reset the viewport");Reset();
            canvas.ResetView();mode.SelectedIndex=1;canvas.TestPointer(anchor);double diameter=canvas.CircleDiameter;brush.Value=72;if(canvas.CircleDiameter<diameter*1.9)throw new Exception("Brush ring did not follow width");
            byte[] weak=canvas.PreviewBytes();strength.Value=36;byte[] strong=canvas.PreviewBytes();if(weak.SequenceEqual(strong)||!before.SequenceEqual(Codec.StaticPng(current)))throw new Exception("Live strength preview failed or changed original pixels");
            canvas.ResetView();
        }
#endif
        public override void OnLeavePage(){zoomDebounce.Stop();}
        public override void OnEnterPage(){base.OnEnterPage();zoomDebounce.Start();}
        protected override void Dispose(bool disposing){if(disposing){disposingView=true;DisposeImages();zoomDebounce.Stop();zoomDebounce.Dispose();}base.Dispose(disposing);}
    }
    public class SettingsPage : ToolPage
    {
        readonly ToolPreferences preferences;
        readonly Label colorLabel;
        CheckBox defaultNumber,defaultLoop;
        SoftCombo defaultResolution;
        SoftCombo language;
        NumericUpDown defaultDelay,defaultSize;
        bool refreshing;
        public SettingsPage(ExportLocation location,ToolPreferences prefs,Action changed,Action<bool> busyChanged)
            :base("设置","设置默认导出方式；修改后自动记住。保存位置在所有页面共用。",location,busyChanged)
        {
            preferences=prefs;
            Ui.Add(Body,Ui.Text("语言 / Language",13,true));language=new SoftCombo{Width=230,Margin=new Padding(0,0,0,14)};language.Items.AddRange(new object[]{"简体中文","English"});language.SelectedIndex=prefs.Language=="en"?1:0;Ui.Add(Body,language);
            Ui.Add(Body,Ui.Text("切换后立即生效并自动记住。文件名、保存路径、提示词与原始参数不会被翻译。",9));
            language.SelectedIndexChanged+=delegate{prefs.Language=language.SelectedIndex==1?"en":"zh";Save(changed);L.Set(prefs.Language);};
            Ui.Add(Body,Ui.Text("多余边颜色",13,true));colorLabel=Ui.Text("当前：#"+prefs.PaddingColor.R.ToString("X2")+prefs.PaddingColor.G.ToString("X2")+prefs.PaddingColor.B.ToString("X2"));Ui.Add(Body,colorLabel);
            var colors=Ui.Flow();
            foreach(Color color in new[]{Color.White,Color.FromArgb(243,244,248),Color.Black,Color.FromArgb(255,214,231),Color.FromArgb(219,228,255),Color.FromArgb(255,243,191),Color.FromArgb(211,249,216)})
            {
                Color selected=color;var b=Ui.Button("  ",delegate{SetColor(selected,changed);});b.BackColor=color;b.MinimumSize=new Size(42,38);colors.Controls.Add(b);
            }
            colors.Controls.Add(Ui.Button("自选颜色…",delegate{using(var d=new ColorDialog {Color=preferences.PaddingColor,FullOpen=true})if(L.Show(d,FindForm())==DialogResult.OK)SetColor(d.Color,changed);}));Ui.Add(Body,colors);
            Ui.Add(Body,Ui.Text("用于双图封面和 GIF 画布的留边，默认白色。不会修改原图内容。",9));
            Ui.Add(Body,Ui.Text("双图 PNG 默认设置",13,true));
            var numbered=new SoftCheck {Text="封面右下角自动编号",Checked=prefs.Numbered,AutoSize=true,Margin=new Padding(0,0,0,12)};Ui.Add(Body,numbered);
            var resolution=new SoftCombo {DropDownStyle=ComboBoxStyle.DropDownList,Width=235,Margin=new Padding(0,0,0,16)};resolution.Items.AddRange(new object[]{"最长边 1200 像素","最长边 2048 像素","最长边 3200 像素","保留原尺寸"});resolution.SelectedIndex=prefs.Resolution;Ui.Add(Body,resolution);
            Ui.Add(Body,Ui.Text("GIF 默认设置",13,true));
            var delay=FileToolPage.Number(20,10000,prefs.GifDelay);delay.Increment=10;var size=FileToolPage.Number(100,2048,prefs.GifSize);
            var loop=new SoftCheck {Text="循环播放",Checked=prefs.Loop,AutoSize=true,Margin=new Padding(0,0,0,12)};
            Ui.Add(Body,Ui.Flow(Ui.Text("每帧间隔（毫秒）"),delay,Ui.Text("最长边（像素）"),size));Ui.Add(Body,loop);
            defaultNumber=numbered;defaultResolution=resolution;defaultDelay=delay;defaultSize=size;defaultLoop=loop;
            Action save=()=>{if(refreshing)return;prefs.Numbered=numbered.Checked;prefs.Resolution=resolution.SelectedIndex;prefs.GifDelay=(int)delay.Value;prefs.GifSize=(int)size.Value;prefs.Loop=loop.Checked;Save(changed);};
            numbered.CheckedChanged+=delegate{save();};resolution.SelectedIndexChanged+=delegate{save();};delay.ValueChanged+=delegate{save();};size.ValueChanged+=delegate{save();};loop.CheckedChanged+=delegate{save();};
            Ui.Add(Body,Ui.Text("关于",13,true));Ui.Add(Body,Ui.Text("工具集合 · Windows\n图片与二维码均在本机处理；打开网站需要网络及网站规定的访问权限。各页面的图片队列在切换时保留，关闭程序后清空。原始文件保留，输出重名时自动加后缀。",10));
            Ui.Add(Body,Ui.Flow(Ui.Button("检查导出图片的压缩兼容性…",async delegate{await TransmissionChecks.Show(FindForm());})));
            Ui.Add(Body,Ui.Text("项目链接",13,true));
            Ui.Add(Body,Ui.Text("https://github.com/Penumbrill/AI-Image-Toolbox",10));
            Ui.Add(Body,Ui.Flow(Ui.Button("打开 GitHub 项目",delegate{try{Process.Start(new ProcessStartInfo("https://github.com/Penumbrill/AI-Image-Toolbox"){UseShellExecute=true});}catch(Exception){Status.Text=string.Format(L.T("无法打开浏览器，请手动访问：{0}"),"https://github.com/Penumbrill/AI-Image-Toolbox");}})));
            ActionButton("打开保存文件夹",delegate{try{if(OutputFolder.Ready()){Directory.CreateDirectory(OutputFolder.Folder);Process.Start("explorer.exe","\""+OutputFolder.Folder+"\"");}}catch(Exception ex){Status.Text="无法打开文件夹："+ex.Message;}},false);
            FinishLayout();
        }
        void Save(Action changed){try{preferences.Save();changed();Status.Text="设置已记住。";}catch(Exception ex){Status.Text="设置保存失败："+ex.Message;}}
        void SetColor(Color color,Action changed){preferences.PaddingColor=color;colorLabel.Text="当前：#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2");Save(changed);}
        public override void OnEnterPage()
        {
            base.OnEnterPage();refreshing=true;
            try{defaultNumber.Checked=preferences.Numbered;defaultResolution.SelectedIndex=preferences.Resolution;defaultDelay.Value=preferences.GifDelay;defaultSize.Value=preferences.GifSize;defaultLoop.Checked=preferences.Loop;}
            finally{refreshing=false;}
        }
        public override void Demo(){}
        internal void ChooseLanguage(bool english){language.SelectedIndex=english?1:0;}
    }
}
