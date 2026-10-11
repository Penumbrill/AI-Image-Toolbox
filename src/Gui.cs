using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQImageSwitch
{
    static class Ui
    {
        static Icon applicationIcon;
        public static Icon AppIcon
        {
            get
            {
                if(applicationIcon==null)using(var stream=typeof(Ui).Assembly.GetManifestResourceStream("AppIcon"))using(var icon=new Icon(stream))applicationIcon=(Icon)icon.Clone();
                return applicationIcon;
            }
        }
        public static readonly Color Ink=Color.FromArgb(63,65,87), Purple=Color.FromArgb(146,123,211), Muted=Color.FromArgb(126,130,155);
        public static Label Text(string text,float size=10, bool bold=false)
        {
            var label=new Label {Text=text,AutoSize=true,Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular),
                ForeColor=bold?Ink:Muted,BackColor=Color.Transparent,Margin=new Padding(0,0,0,10),UseMnemonic=false};
            L.Bind(label);return label;
        }
        public static Label RawText(string text,float size=9)
        {
            var label=new Label{Text=text,Tag="raw",AutoSize=true,Font=new Font("Microsoft YaHei UI",size),ForeColor=Muted,BackColor=Color.Transparent,Margin=new Padding(0,0,0,10),UseMnemonic=false};L.Bind(label);return label;
        }
        public static Button Button(string text,EventHandler action,bool primary=false)
        {
            var b=new SoftButton {Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(16,9,16,9),
                Margin=new Padding(0,0,10,8),FlatStyle=FlatStyle.Flat,BackColor=primary?Purple:Color.White,ForeColor=primary?Color.White:Ink,
                UseVisualStyleBackColor=false,Cursor=Cursors.Hand};
            b.FlatAppearance.BorderColor=primary?Purple:Color.FromArgb(227,225,241);b.FlatAppearance.BorderSize=0;b.Click+=action;L.Bind(b);return b;
        }
        public static FlowLayoutPanel Flow(params Control[] controls)
        {
            var f=new SoftFlow {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,WrapContents=true,BackColor=Color.Transparent,Margin=new Padding(0)};
            foreach(var control in controls){control.Anchor=AnchorStyles.Left;control.Margin=new Padding(control.Margin.Left,0,control.Margin.Right,10);var label=control as Label;if(label!=null)label.TextAlign=ContentAlignment.MiddleLeft;}
            f.Controls.AddRange(controls);return f;
        }
        public static TableLayoutPanel Column(Padding padding)
        {
            var t=new SoftColumn {ColumnCount=1,RowCount=0,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                Dock=DockStyle.Top,Padding=padding,BackColor=Color.Transparent,Margin=new Padding(0)};
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return t;
        }
        public static TableLayoutPanel Footer(Padding padding){return new CompactFooter {Padding=padding,BackColor=Color.White};}
        public static void Add(TableLayoutPanel column,Control control)
        {
            int row=column.RowCount++;column.RowStyles.Add(new RowStyle(SizeType.AutoSize));column.Controls.Add(control,0,row);
            if(control is Label)control.Anchor=AnchorStyles.Left|AnchorStyles.Top;
        }
        public static void Configure(Form f,string title,Size size)
        {
            f.AutoScaleDimensions=new SizeF(96,96);f.AutoScaleMode=AutoScaleMode.Dpi;
            f.Icon=AppIcon;
            f.Text=title;f.ClientSize=size;f.Font=new Font("Microsoft YaHei UI",10);f.BackColor=SoftTheme.Background;
            f.ForeColor=Ink;f.StartPosition=FormStartPosition.CenterParent;f.MinimumSize=new Size(560,420);
            L.Bind(f);
        }
    }
    public class MainForm : Form
    {
        protected override void Dispose(bool disposing)
        {
            if(disposing){SuspendLayout();foreach(Control child in Descendants(this))child.SuspendLayout();}
            base.Dispose(disposing);
        }
        readonly List<BatchItem> items=new List<BatchItem>();
        readonly List<string> ownedTempFiles=new List<string>();
        readonly ToolTip tips=new ToolTip();
        readonly ExportLocation exportLocation;
        readonly ToolPreferences preferences;
        FileToolPage mergedRestore;
        TableLayoutPanel restorePlaceholder;
        Panel disguisePage,pageHost;
        TableLayoutPanel navigation;
        readonly List<Control> pages=new List<Control>();
        readonly List<Button> navigationButtons=new List<Button>();
        bool applyingPreferences;
        int activePage;
        Bitmap cover,sharedCover,real,frontPreview,previewBase;
        string coverLabel="默认封面";
        string sharedCoverLabel="默认封面";
        string sharedCoverPath;
        Panel scroll;
        TableLayoutPanel content,cards,coverCard,realCard;
        ImageCanvas coverView,realView;
#if SELF_TEST
        Func<string> testCoverPicker;
#endif
        Label coverName,realName,queueTitle,status;
        CheckBox numbered;
        SoftCombo resolution;
        Label dualDescription,coverHeading,realHeading;
        SoftInput coverText;TextLayerSelector textLayers;bool loadingTextLayer;
        CoverTextOptions ActiveText {get{return textLayers==null?preferences.CoverText:textLayers.Active;}}
        void LoadTextLayer(CoverTextOptions t){loadingTextLayer=true;try{coverText.Text=t.Text;textPosition.SelectedIndex=Array.IndexOf(PositionIds,t.Position);textSize.Value=t.SizePercent;textX.Value=t.X;textY.Value=t.Y;}finally{loadingTextLayer=false;}ConfigureTextPlacement();}
        SoftCombo textPosition;
        NumericUpDown textSize,textX,textY;
        bool placingText;
        System.Windows.Forms.Timer textDebounce;
        DataGridView queue;
        Button batchExport,singleExport,cancel;
        ProgressBar progress;
        SoftInput exportFolder;
        Button browseFolder;
        Label locationHint;
        CancellationTokenSource cancellation;
        bool busy,refreshing,resizing;
        bool stacked;
        Color previewColor;
        string coverKey;
        bool layoutReady;
        static readonly int[] PositionIds={3,4,6,5,7,0,8,9,2,1};
        const string ImageFilter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*";

        public MainForm(string settingsFile=null)
        {
            exportLocation=new ExportLocation(settingsFile);
            preferences=new ToolPreferences(exportLocation.SettingsFile);
            L.Set(preferences.Language);
            Ui.Configure(this,"AI Image Editing Tools",new Size(1260,820));StartPosition=FormStartPosition.CenterScreen;
            DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);SuspendLayout();
            MinimumSize=new Size(880,580);
            scroll=new SoftScrollPanel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(20,18,20,18)};Controls.Add(scroll);
            content=Ui.Column(new Padding(0));content.SuspendLayout();scroll.Controls.Add(content);

            var header=Ui.Column(new Padding(0,0,0,6));
            Ui.Add(header,Ui.Text("双图切换",21,true));
            dualDescription=Ui.Text("将封面与原图合成一个 PNG；每张图可单独换封面，也可共用封面并编号。",11);Ui.Add(header,dualDescription);Ui.Add(content,header);
            cards=new TableLayoutPanel {ColumnCount=2,RowCount=1,AutoSize=false,Height=480,Dock=DockStyle.Top,Margin=new Padding(0,0,0,12)};
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            coverCard=CreateCard(false);realCard=CreateCard(true);cards.Controls.Add(coverCard,0,0);cards.Controls.Add(realCard,1,0);Ui.Add(content,cards);

            var settings=Ui.Column(new Padding(16,14,16,6));settings.BackColor=Color.White;settings.Margin=new Padding(0,0,0,12);
            settings.SuspendLayout();
            Ui.Add(settings,Ui.Text("封面文字",13,true));
            coverText=new SoftInput {Text=preferences.CoverText.Text,Width=330,Margin=new Padding(0,0,12,10)};
            textPosition=new SoftCombo {Width=145,VisibleRows=3,Margin=new Padding(0,0,12,10)};textPosition.Items.AddRange(new object[]{"自由拖动","上左","上右","上中","中左","正中","中右","下左","下中","下右"});textPosition.SelectedIndex=Array.IndexOf(PositionIds,preferences.CoverText.Position);
            Ui.Add(settings,Ui.Flow(Ui.Text("文字内容"),coverText,Ui.Text("放置位置"),textPosition));
            textSize=FileToolPage.Number(2,18,preferences.CoverText.SizePercent);textSize.Width=80;
            textX=FileToolPage.Number(0,100,preferences.CoverText.X);textY=FileToolPage.Number(0,100,preferences.CoverText.Y);textX.Width=80;textY.Width=80;
            Ui.Add(settings,Ui.Flow(Ui.Text("自由位置：横向 %"),textX,Ui.Text("纵向 %"),textY,Ui.Text("选「自由拖动」后，可直接在封面上拖动文字。",9)));
            textLayers=new TextLayerSelector(()=>preferences.CoverText,LoadTextLayer,delegate{RefreshCover();RememberPreferences();},UpdateCoverText);Ui.Add(settings,textLayers);
            Ui.Add(settings,Ui.Flow(Ui.Text("字号（占短边 %）"),textSize,Ui.Button("文字颜色…",delegate{using(var d=new ColorDialog {Color=ActiveText.Color,FullOpen=true})if(L.Show(d,this)==DialogResult.OK){ActiveText.Color=d.Color;UpdateCoverText();}}),Ui.Button("清空文字",delegate{coverText.Text="";UpdateCoverText();})));
            Ui.Add(settings,Ui.Text("留空不添加。文字应用到全部配对封面，原图保持不变；预设底部位置自动避开序号。",9));
            numbered=new SoftCheck {Text="封面右下角自动编号（①、②、③…）",Checked=preferences.Numbered,AutoSize=true,Margin=new Padding(0,0,20,10)};
            resolution=new SoftCombo {DropDownStyle=ComboBoxStyle.DropDownList,Width=210,Margin=new Padding(0,0,0,10)};
            resolution.Items.AddRange(new object[]{"最长边 1200 像素","最长边 2048 像素","最长边 3200 像素","保留原尺寸"});resolution.SelectedIndex=preferences.Resolution;
            Ui.Add(settings,Ui.Flow(numbered,Ui.Text("导出尺寸："),resolution));
            Ui.Add(settings,Ui.Text("编号按下方队列顺序写入封面；原图保持不变。取消勾选可关闭编号。",9));settings.ResumeLayout(true);Ui.Add(content,settings);

            var batch=Ui.Column(new Padding(16,14,16,12));batch.BackColor=Color.White;batch.Margin=new Padding(0,0,0,12);
            queueTitle=Ui.Text("原图队列 · 0 张",13,true);Ui.Add(batch,queueTitle);
            Ui.Add(batch,Ui.Flow(Ui.Button("＋ 批量导入原图",async delegate{await PickOriginals();},true),
                Ui.Button("移除选中",delegate{RemoveSelected();}),Ui.Button("上移",delegate{MoveSelected(-1);}),
                Ui.Button("下移",delegate{MoveSelected(1);}),Ui.Button("清空队列",delegate{ClearQueue();})));
            queue=new SoftGrid {Dock=DockStyle.Fill,Height=320,MinimumSize=new Size(0,320),AllowUserToAddRows=false,AllowUserToDeleteRows=false,
                AllowUserToResizeRows=false,ReadOnly=true,MultiSelect=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect,RowHeadersVisible=false,
                BackgroundColor=Color.FromArgb(253,252,255),BorderStyle=BorderStyle.None,AutoGenerateColumns=false,ScrollBars=ScrollBars.Both,
                AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                EnableHeadersVisualStyles=false,Margin=new Padding(0),AllowDrop=true};
            queue.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(241,237,250);queue.ColumnHeadersDefaultCellStyle.ForeColor=Ui.Ink;
            queue.GridColor=Color.FromArgb(237,233,246);
            queue.DefaultCellStyle.Padding=new Padding(8,6,8,6);queue.DefaultCellStyle.SelectionBackColor=Color.FromArgb(237,232,253);
            queue.DefaultCellStyle.SelectionForeColor=Ui.Ink;queue.DefaultCellStyle.WrapMode=DataGridViewTriState.False;
            queue.Columns.Add(new DataGridViewTextBoxColumn {Name="number",HeaderText="序号",Width=80,SortMode=DataGridViewColumnSortMode.NotSortable});
            queue.Columns.Add(new DataGridViewTextBoxColumn {Name="file",HeaderText="原图文件",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=220,SortMode=DataGridViewColumnSortMode.NotSortable});
            queue.Columns.Add(new DataGridViewTextBoxColumn {Name="cover",HeaderText="配对封面",Width=180,SortMode=DataGridViewColumnSortMode.NotSortable});
            queue.Columns.Add(new DataGridViewTextBoxColumn {Name="size",HeaderText="尺寸",Width=145,SortMode=DataGridViewColumnSortMode.NotSortable});
            queue.Columns.Add(new DataGridViewTextBoxColumn {Name="state",HeaderText="状态",Width=110,SortMode=DataGridViewColumnSortMode.NotSortable});
            Ui.Add(batch,queue);Ui.Add(batch,Ui.Text("点击一行可单独换封面；按 Ctrl / Shift 多选，再为选中的图片设置同一封面。",9));Ui.Add(content,batch);
            Ui.Add(content,Ui.Flow(Ui.Button("预览当前生成效果",async delegate{await ShowPreview();}),Ui.Button("定位还原区",delegate{scroll.AutoScrollPosition=new Point(0,content.Height);})));
            Ui.Add(content,Ui.Text("在 QQ 图片选择器勾选「原图」发送。实际变图效果请先用小号测试。",9));

            var footer=Ui.Footer(new Padding(20,12,20,12));Controls.Add(footer);scroll.BringToFront();
            var locationRow=new TableLayoutPanel {ColumnCount=3,RowCount=1,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,Margin=new Padding(0),BackColor=Color.Transparent};
            locationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));locationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));locationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            locationRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var locationLabel=Ui.Text("保存位置",10);locationLabel.Anchor=AnchorStyles.Left;locationLabel.Margin=new Padding(0,0,14,8);
            exportFolder=new SoftInput {Text=exportLocation.Load(),Dock=DockStyle.Fill,Margin=new Padding(0,5,12,12)};
            browseFolder=Ui.Button("浏览…",delegate{ChooseExportFolder();});
            locationRow.Controls.Add(locationLabel,0,0);locationRow.Controls.Add(exportFolder,1,0);locationRow.Controls.Add(browseFolder,2,0);Ui.Add(footer,locationRow);
            locationHint=Ui.Text("自动记住根目录；导出时自动放入「双图切换」子文件夹。",9);Ui.Add(footer,locationHint);
            batchExport=Ui.Button("批量导出",async delegate{await ExportBatch();},true);
            singleExport=Ui.Button("仅导出当前图片…",async delegate{await ExportCurrent();});
            cancel=Ui.Button("停止任务",delegate{if(cancellation!=null)cancellation.Cancel();status.Text="正在停止；已完成的文件会保留。";});cancel.Visible=false;
            Ui.Add(footer,Ui.Flow(batchExport,singleExport,cancel));
            progress=new ProgressBar {Dock=DockStyle.Top,Height=8,Visible=false,Margin=new Padding(0,0,0,8)};Ui.Add(footer,progress);
            status=Ui.Text("先选择封面，再批量导入原图。所有图片都在本机处理。",9);Ui.Add(footer,status);
            exportFolder.Leave+=delegate{RememberExportFolder(false);};
            queue.SelectionChanged+=delegate{if(!refreshing&&!busy)ShowSelected();};
            numbered.CheckedChanged+=delegate{RefreshCover();RememberPreferences();};resolution.SelectedIndexChanged+=delegate{RememberPreferences();};
            textDebounce=new System.Windows.Forms.Timer {Interval=300};textDebounce.Tick+=delegate{textDebounce.Stop();UpdateCoverText();};
            coverText.TextChanged+=delegate{textLayers.RefreshNames(coverText.Text);textDebounce.Stop();textDebounce.Start();};textPosition.SelectedIndexChanged+=delegate{UpdateCoverText();if(textPosition.SelectedIndex==0){CaptureScroll(false);status.Text="可直接在左侧封面上拖到任意位置；文字中心使用横向、纵向百分比定位。";}};textSize.ValueChanged+=delegate{UpdateCoverText();};
            textX.ValueChanged+=delegate{if(!placingText)UpdateCoverText();};textY.ValueChanged+=delegate{if(!placingText)UpdateCoverText();};
            EnableDrop(queue,async paths=>await ImportPaths(paths));
            scroll.ClientSizeChanged+=delegate{Relayout();};Shown+=delegate{FitScreen();Relayout();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy||(mergedRestore!=null&&mergedRestore.Busy)||pages.OfType<ToolPage>().Any(p=>p.Busy)){if(cancellation!=null)cancellation.Cancel();foreach(var page in pages.OfType<ToolPage>())if(page.Busy)page.Cancel();if(mergedRestore!=null&&mergedRestore.Busy)mergedRestore.Cancel();e.Cancel=true;status.Text="请等待任务停止后再关闭。";}else SaveActiveLocation();};
            FormClosed+=delegate{textDebounce.Dispose();if(mergedRestore!=null)mergedRestore.Dispose();DisposeImages();foreach(string p in ownedTempFiles)try{File.Delete(p);}catch(IOException){} tips.Dispose();};
            restorePlaceholder=Ui.Column(new Padding(18,16,18,12));restorePlaceholder.BackColor=Color.White;restorePlaceholder.Margin=new Padding(0,0,0,16);
            Ui.Add(restorePlaceholder,Ui.Text("还原双图 PNG",16,true));Ui.Add(restorePlaceholder,Ui.Text("导入双图 PNG，查看聊天封面与隐藏原图，批量导出还原结果。",10));
            Ui.Add(restorePlaceholder,Ui.Flow(Ui.Button("批量导入双图 PNG…",async delegate{EnsureMergedRestore();await mergedRestore.Pick();},true),Ui.Button("显示还原工作台",delegate{EnsureMergedRestore();})));Ui.Add(content,restorePlaceholder);
            content.ResumeLayout(true);CreateNavigation();ConfigureTextPlacement();UpdateActions();layoutReady=true;ResumeLayout(true);Relayout();HeightResize.Tree(this);
            Action<int> resizePair=h=>{HeightResize.Set(coverView,h);HeightResize.Set(realView,h);Relayout();};
            HeightResize.Enable(coverView,resizePair);HeightResize.Enable(realView,resizePair);
            HeightResize.Enable(coverCard,h=>resizePair(h>0?Math.Max(120,coverView.Height+h-coverCard.Height):0));
            HeightResize.Enable(realCard,h=>resizePair(h>0?Math.Max(120,realView.Height+h-realCard.Height):0));
        }
        void EnsureMergedRestore()
        {
            if(mergedRestore!=null)return;content.SuspendLayout();
            try{int row=content.GetRow(restorePlaceholder);mergedRestore=new FileToolPage(2,exportLocation,preferences,SetNavigationBusy);mergedRestore.Visible=false;disguisePage.Controls.Add(mergedRestore);mergedRestore.ConfigureEmbeddedRestore(delegate{if(string.IsNullOrWhiteSpace(exportFolder.Text)&&!ChooseExportFolder())return null;if(!RememberExportFolder(false))return null;return exportLocation.Load();});content.Controls.Remove(restorePlaceholder);restorePlaceholder.Dispose();restorePlaceholder=null;content.Controls.Add(mergedRestore.EmbedBody(),0,row);mergedRestore.SetPreviewViewport(scroll);}
            finally{content.ResumeLayout(true);Relayout();}
            scroll.AutoScrollPosition=new Point(0,content.Height);
        }
        void CreateNavigation()
        {
            disguisePage=new SoftPanel {Dock=DockStyle.Fill};var existing=Controls.Cast<Control>().ToArray();
            foreach(var control in existing){Controls.Remove(control);disguisePage.Controls.Add(control);}scroll.BringToFront();
            pageHost=new ToolPageHost {Dock=DockStyle.Fill};Controls.Add(pageHost);pageHost.Controls.Add(disguisePage);pages.Add(disguisePage);
            for(int i=1;i<11;i++)pages.Add(null);
            var navScroll=new SoftScrollPanel {Dock=DockStyle.Left,Width=164,AutoScroll=true,BackColor=Color.White,Padding=new Padding(14,22,14,12)};Controls.Add(navScroll);pageHost.BringToFront();
            navigation=Ui.Column(new Padding(0));navScroll.Controls.Add(navigation);
            var navTitle=Ui.Text("工具集合",14,true);var navSubtitle=Ui.Text("图片小助手",9);navTitle.Tag=navSubtitle.Tag="singleline";Ui.Add(navigation,navTitle);Ui.Add(navigation,navSubtitle);
            string[] names={"双图切换","合成 GIF","图片混淆","清信息","打码","tag读取","设置","文件伪装","背景显图","二维码制作","使用教程"};
            foreach(string name in names)navigationButtons.Add(null);
            foreach(int i in new[]{0,8,2,7,1,3,4,5,9,6,10})
            {
                int index=i;var button=Ui.Button(names[i],delegate{SwitchPage(index);});button.Dock=DockStyle.Top;button.Margin=new Padding(0,0,0,12);button.TextAlign=ContentAlignment.MiddleLeft;
                ((SoftButton)button).NavigationIcon=i==10?11:i==9?10:i==8?9:i==2?8:i;button.Padding=new Padding(40,9,12,9);
                navigationButtons[i]=button;Ui.Add(navigation,button);
            }
            bool sizingNavigation=false;
            Action sizeNavigation=delegate
            {
                if(sizingNavigation||IsDisposed||Disposing)return;sizingNavigation=true;
                try
                {
                    int needed=100;
                    foreach(Control item in navigation.Controls)
                    {
                        var label=item as Label;if(label!=null)label.MaximumSize=Size.Empty;
                        int textWidth=TextRenderer.MeasureText(item.Text,item.Font,Size.Empty,TextFormatFlags.SingleLine).Width;
                        int inset=item is Button?item.Padding.Horizontal:8;
                        needed=Math.Max(needed,textWidth+inset+item.Margin.Horizontal+8);
                    }
                    int outer=Math.Min(Math.Max(164,ClientSize.Width*2/5),needed+navScroll.Padding.Horizontal+SystemInformation.VerticalScrollBarWidth);
                    if(navScroll.Width!=outer)navScroll.Width=outer;
                    int width=Math.Max(100,navScroll.ClientSize.Width-navScroll.Padding.Horizontal);
                    if(navigation.Width!=width)navigation.Width=width;
                }
                finally{sizingNavigation=false;}
            };
            navScroll.ClientSizeChanged+=delegate{sizeNavigation();};
            foreach(Control item in navigation.Controls){item.FontChanged+=delegate{sizeNavigation();};item.TextChanged+=delegate{sizeNavigation();};item.PaddingChanged+=delegate{sizeNavigation();};}
            ClientSizeChanged+=delegate{sizeNavigation();};navigation.Width=136;sizeNavigation();SwitchPage(0);
            Action languageLayout=delegate{sizeNavigation();coverKey=null;RefreshCover();Relayout();};L.Changed+=languageLayout;Disposed+=delegate{L.Changed-=languageLayout;};languageLayout();
        }
        void SetNavigationBusy(bool value)
        {
            foreach(var button in navigationButtons)button.Enabled=!value;
            batchExport.Enabled=singleExport.Enabled=!value&&items.Count>0;exportFolder.Enabled=browseFolder.Enabled=!value;
            if(mergedRestore!=null&&mergedRestore.Busy)foreach(Control c in content.Controls)if(c!=mergedRestore.EmbeddedBody)c.Enabled=!value;
            if(!value)foreach(Control c in content.Controls)c.Enabled=true;
        }
        void EnsurePage(int index)
        {
            if(pages[index]!=null)return;
            ToolPage page=index==2?(ToolPage)new ObfuscationPage(exportLocation,SetNavigationBusy):index<=3?(ToolPage)new FileToolPage(index,exportLocation,preferences,SetNavigationBusy):
                index==4?(ToolPage)new RedactPage(exportLocation,SetNavigationBusy):index==5?(ToolPage)new InspectionPage(exportLocation,SetNavigationBusy):index==7?(ToolPage)new FileDisguisePage(exportLocation,SetNavigationBusy):index==8?(ToolPage)new BackgroundRevealPage(exportLocation,SetNavigationBusy):index==9?(ToolPage)new QrPage(exportLocation,SetNavigationBusy):index==10?(ToolPage)new TutorialPage(exportLocation,SetNavigationBusy):new SettingsPage(exportLocation,preferences,ApplyPreferences,SetNavigationBusy);
            page.Visible=false;pages[index]=page;pageHost.Controls.Add(page);HeightResize.Tree(page);
        }
        void SaveActiveLocation(){if(activePage==0)RememberExportFolder(false);else ((ToolPage)pages[activePage]).OutputFolder.Remember();}
        public void SwitchPage(int index)
        {
            if(index<0||index>=pages.Count||busy||(mergedRestore!=null&&mergedRestore.Busy)||pages.OfType<ToolPage>().Any(p=>p.Busy&&!p.BackgroundBusy))return;
            if(activePage==index&&pages[index]!=null&&pages[index].Visible)return;
            
            pageHost.SuspendLayout();
            try
            {
                EnsurePage(index);
                pages[index].BringToFront();pages[index].Visible=true;SaveActiveLocation();if(activePage>0)((ToolPage)pages[activePage]).OnLeavePage();activePage=index;
                for(int i=0;i<pages.Count;i++)
                {
                    if(pages[i]!=null)pages[i].Visible=i==index;navigationButtons[i].BackColor=i==index?Ui.Purple:Color.White;
                    navigationButtons[i].ForeColor=i==index?Color.White:Ui.Ink;
                }
                pages[index].BringToFront();((ScrollableControl)navigation.Parent).ScrollControlIntoView(navigationButtons[index]);
                if(index==0){exportFolder.Text=exportLocation.Load();ApplyPreferences();}
                else ((ToolPage)pages[index]).OnEnterPage();
            }
            finally{pageHost.ResumeLayout(true);pageHost.Invalidate(false);}
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
        void RememberPreferences()
        {
            if(applyingPreferences)return;preferences.Numbered=numbered.Checked;preferences.Resolution=resolution.SelectedIndex;
            try{preferences.Save();}catch(Exception ex){status.Text="默认设置无法记住："+ex.Message;}
        }
        void UpdateCoverText()
        {
            if(loadingTextLayer)return;textDebounce.Stop();ActiveText.Text=coverText.Text.Length>200?coverText.Text.Substring(0,200):coverText.Text;
            ActiveText.Position=PositionIds[textPosition.SelectedIndex];ActiveText.SizePercent=(int)textSize.Value;
            ActiveText.X=(int)textX.Value;ActiveText.Y=(int)textY.Value;ConfigureTextPlacement();
            textLayers.RefreshNames();RefreshCover();RememberPreferences();
        }
        void ConfigureTextPlacement()
        {
            bool free=textPosition.SelectedIndex==0;textX.Enabled=free;textY.Enabled=free;
            var canvas=(CoverCanvas)coverView;canvas.TextPlacement=free;canvas.Cursor=free?Cursors.SizeAll:Cursors.Hand;
        }
        void PlaceText(object sender,TextPositionEventArgs e)
        {
            placingText=true;try{textX.Value=e.X;textY.Value=e.Y;}finally{placingText=false;}
            ActiveText.X=e.X;ActiveText.Y=e.Y;RefreshCover();
            textDebounce.Stop();if(e.Finished)RememberPreferences();else textDebounce.Start();
        }
        void ApplyPreferences()
        {
            applyingPreferences=true;
            try{bool changed=numbered.Checked!=preferences.Numbered||resolution.SelectedIndex!=preferences.Resolution||previewColor!=preferences.PaddingColor;numbered.Checked=preferences.Numbered;resolution.SelectedIndex=preferences.Resolution;if(changed)RefreshCover();}
            finally{applyingPreferences=false;}
        }
        public void DemoPage(int index){SwitchPage(index);if(index>0)((ToolPage)pages[index]).Demo();}
        TableLayoutPanel CreateCard(bool original)
        {
            var card=Ui.Column(new Padding(16,14,16,10));card.BackColor=Color.White;card.Margin=original?new Padding(7,0,0,0):new Padding(0,0,7,0);
            card.AutoSize=false;
            card.Dock=DockStyle.Fill;
            var heading=Ui.Text(original?"02  当前原图":"01  当前配对封面",13,true);if(original)realHeading=heading;else coverHeading=heading;Ui.Add(card,heading);
            ImageCanvas canvas=original?new ImageCanvas():new CoverCanvas();
            canvas.Dock=DockStyle.Fill;canvas.Height=240;canvas.MinimumSize=new Size(0,240);canvas.Margin=new Padding(0,0,0,10);canvas.AllowDrop=true;
            canvas.EmptyText=
                original?"点击批量选择原图\n或拖入多个图片文件":"为当前原图选择封面\n也可批量应用到其他图片";
            if(!original)((CoverCanvas)canvas).PositionChanged+=PlaceText;
            canvas.MouseClick+=async delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;if(original)await PickOriginals();else if(canvas.Image==null||!((CoverCanvas)canvas).TextPlacement)await PickCover();};
            EnableDrop(canvas,async paths=>{if(original)await ImportPaths(paths);else if(paths.Length>0)await LoadCover(paths[0]);});Ui.Add(card,canvas);
            var name=Ui.Text(original?"尚未导入原图":"默认封面",9);name.MaximumSize=new Size(430,0);Ui.Add(card,name);
            Ui.Add(card,Ui.Flow(Ui.Button(original?"批量导入原图…":"为选中图片设置封面…",async delegate{if(original)await PickOriginals();else await PickCover();}),
                Ui.Button("粘贴",async delegate{await Paste(original);} )));
            if(!original)Ui.Add(card,Ui.Flow(Ui.Button("此封面应用到全部",delegate{ApplyCoverToAll();}),Ui.Button("使用默认封面",delegate{SetCover(null,"默认封面");})));
            else Ui.Add(card,Ui.Text("选中队列中的原图，可单独设置封面。",9));
            if(original){realView=canvas;realName=name;}else{coverView=canvas;coverName=name;}
            return card;
        }
        void EnableDrop(Control control,Func<string[],Task> import)
        {
            control.AllowDrop=true;
            control.DragEnter+=delegate(object sender,DragEventArgs e){if(!busy&&e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
            control.DragDrop+=async delegate(object sender,DragEventArgs e){if(!busy)await import((string[])e.Data.GetData(DataFormats.FileDrop));};
        }
        void FitScreen()
        {
            var area=Screen.FromControl(this).WorkingArea;
            Width=Math.Min(Width,area.Width-40);Height=Math.Min(Height,area.Height-40);
            Left=area.Left+(area.Width-Width)/2;Top=area.Top+(area.Height-Height)/2;
        }
        void Relayout()
        {
            if(!layoutReady||resizing)return;resizing=true;
            try
            {
                int width=Math.Max(100,scroll.ClientSize.Width-scroll.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
                content.Width=width;
                bool next=width<Font.Height*36;
                if(next!=stacked)
                {
                    cards.SuspendLayout();cards.Controls.Remove(coverCard);cards.Controls.Remove(realCard);cards.ColumnStyles.Clear();cards.RowStyles.Clear();
                    cards.ColumnCount=next?1:2;cards.RowCount=next?2:1;cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,next?100:50));
                    if(!next)cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
                    cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));if(next)cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    cards.Controls.Add(coverCard,0,0);cards.Controls.Add(realCard,next?0:1,next?1:0);
                    coverCard.Margin=next?new Padding(0,0,0,12):new Padding(0,0,7,0);realCard.Margin=next?new Padding(0):new Padding(7,0,0,0);
                    stacked=next;cards.ResumeLayout(true);
                }
                int previewHeight=HeightResize.Resolve(coverView,ImageCanvas.ViewportHeight(scroll,.54,240,720));
                if(coverView.MinimumSize.Height!=previewHeight||coverCard.RowStyles[1].SizeType!=SizeType.Absolute)
                {coverView.MinimumSize=realView.MinimumSize=new Size(0,previewHeight);coverCard.RowStyles[1].SizeType=realCard.RowStyles[1].SizeType=SizeType.Absolute;coverCard.RowStyles[1].Height=realCard.RowStyles[1].Height=previewHeight+coverView.Margin.Vertical;}
                foreach(var label in Descendants(content).OfType<Label>())
                {var limit=new Size(Math.Max(80,label.Parent.ClientSize.Width-label.Parent.Padding.Horizontal-8),0);if(label.MaximumSize!=limit)label.MaximumSize=limit;}
                cards.PerformLayout();coverCard.PerformLayout();realCard.PerformLayout();
                int coverHeight=CardHeight(coverCard),realHeight=CardHeight(realCard);
                cards.RowStyles[0].SizeType=SizeType.Absolute;cards.RowStyles[0].Height=stacked?coverHeight+coverCard.Margin.Vertical:Math.Max(coverHeight,realHeight);
                if(stacked){cards.RowStyles[1].SizeType=SizeType.Absolute;cards.RowStyles[1].Height=realHeight;}
                cards.Height=stacked?coverHeight+coverCard.Margin.Vertical+realHeight:Math.Max(coverHeight,realHeight);
                int pageWidth=disguisePage==null?ClientSize.Width:disguisePage.ClientSize.Width;
                status.MaximumSize=new Size(Math.Max(120,pageWidth-60),0);
                locationHint.MaximumSize=new Size(Math.Max(120,pageWidth-60),0);
            }
            finally{resizing=false;}
        }
        static IEnumerable<Control> Descendants(Control c)
        {foreach(Control child in c.Controls){yield return child;foreach(var sub in Descendants(child))yield return sub;}}
        static int CardHeight(Control card){return card.Controls.Cast<Control>().Max(c=>c.Bottom+c.Margin.Bottom)+card.Padding.Bottom;}

        int SelectedIndex(){return queue.CurrentCell==null?-1:queue.CurrentCell.RowIndex;}
        List<int> SelectedIndices()
        {
            var selected=queue.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.Index).OrderBy(i=>i).ToList();
            if(selected.Count==0&&SelectedIndex()>=0)selected.Add(SelectedIndex());return selected;
        }
        string CoverNameFor(BatchItem item){return item.OwnCover?(item.CoverName??"默认封面"):sharedCoverLabel;}
        void RefreshRows(int select=-1)
        {
            int current=select>=0?select:SelectedIndex();var selected=SelectedIndices();refreshing=true;
            try
            {
                queue.Rows.Clear();
                for(int i=0;i<items.Count;i++)
                {
                    var item=items[i];int row=queue.Rows.Add(Batch.SequenceLabel(i+1),item.Name,CoverNameFor(item),item.Width+" × "+item.Height,item.State);
                    queue.Rows[row].Cells["file"].ToolTipText=item.Path;
                    queue.Rows[row].Cells["cover"].ToolTipText=item.OwnCover?(item.CoverPath??"默认封面"):sharedCoverLabel;
                }
                if(items.Count>0)
                {
                    current=Math.Max(0,Math.Min(items.Count-1,current));queue.CurrentCell=queue.Rows[current].Cells[0];queue.ClearSelection();
                    if(select>=0)queue.Rows[current].Selected=true;
                    else {foreach(int i in selected)if(i<items.Count)queue.Rows[i].Selected=true;if(queue.SelectedRows.Count==0)queue.Rows[current].Selected=true;}
                }
                queueTitle.Text="原图队列 · "+items.Count+" 张";
            }
            finally{refreshing=false;}
            ShowSelected();UpdateActions();Relayout();
        }
        async Task PickOriginals()
        {
            if(busy)return;
            using(var d=new OpenFileDialog {Title="批量选择原图（可多选）",Filter=ImageFilter,Multiselect=true})
                if(L.Show(d,this)==DialogResult.OK)await ImportPaths(d.FileNames);
        }
        async Task ImportPaths(string[] paths)
        {
            if(busy||paths.Length==0)return;SetBusy(true);var accepted=new List<BatchItem>();var errors=new List<string>();
            var existing=new HashSet<string>(items.Select(i=>System.IO.Path.GetFullPath(i.Path)),StringComparer.OrdinalIgnoreCase);
            var report=new Progress<string>(text=>status.Text=text);
            try
            {
                CancellationToken token=cancellation.Token;
                await Task.Run(delegate
                {
                    for(int i=0;i<paths.Length;i++)
                    {
                        if(token.IsCancellationRequested)break;
                        ((IProgress<string>)report).Report("正在导入 "+(i+1)+" / "+paths.Length+"："+System.IO.Path.GetFileName(paths[i]));
                        try
                        {
                            string path=System.IO.Path.GetFullPath(paths[i]);if(!existing.Add(path))continue;
                            using(var image=Codec.Load(path)) accepted.Add(new BatchItem {Path=path,Name=System.IO.Path.GetFileName(path),Width=image.Width,Height=image.Height});
                        }
                        catch(Exception ex){errors.Add(System.IO.Path.GetFileName(paths[i])+"："+ex.Message);}
                    }
                });
                items.AddRange(accepted);RefreshRows(items.Count-accepted.Count);
                status.Text="已加入 "+accepted.Count+" 张原图，共 "+items.Count+" 张。"+(errors.Count>0?" 有 "+errors.Count+" 个文件无法导入。":"");
                if(errors.Count>0)using(var d=new ResultForm("部分图片无法导入",string.Join(Environment.NewLine,errors),null))L.Show(d,this);
            }
            catch(Exception ex){Error("导入失败",ex);}
            finally{SetBusy(false);}
        }
        async Task PickCover()
        {
            if(busy)return;
#if SELF_TEST
            if(testCoverPicker!=null){string selected=testCoverPicker();if(selected!=null)await LoadCover(selected);return;}
#endif
            using(var d=new OpenFileDialog {Title=items.Count>0?"为选中的原图设置封面":"选择初始共用封面",Filter=ImageFilter})
                if(L.Show(d,this)==DialogResult.OK)await LoadCover(d.FileName);
        }
        async Task LoadCover(string path)
        {
            if(busy)return;SetBusy(true);
            try{Bitmap image=await Task.Run(()=>Codec.Load(path));SetCover(image,System.IO.Path.GetFileName(path),path);status.Text="已设置封面。可单独调整，或点击「此封面应用到全部」。";}
            catch(Exception ex){Error("无法设置封面",ex);}
            finally{SetBusy(false);}
        }
        string SaveTemporary(Bitmap image)
        {
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"QQImageSwitch_"+Guid.NewGuid().ToString("N")+".png");
            Codec.SaveAtomic(path,Codec.StaticPng(image));ownedTempFiles.Add(path);return path;
        }
        void SetCover(Bitmap image,string label,string path=null)
        {
            coverKey=null;
            if(image!=null&&path==null)path=SaveTemporary(image);
            var selected=SelectedIndices();
            if(items.Count==0||selected.Count==0)
            {
                if(sharedCover!=null)sharedCover.Dispose();sharedCover=image;sharedCoverPath=path;sharedCoverLabel=label;
            }
            else
            {
                foreach(int i in selected){items[i].OwnCover=true;items[i].CoverPath=path;items[i].CoverName=label;items[i].State="待导出";}
                if(image!=null)image.Dispose();
            }
            RefreshRows();
        }
        void ApplyCoverToAll()
        {
            int index=SelectedIndex();if(index<0){status.Text="后续导入的原图会使用当前封面。";return;}
            if(coverView.Image==null){status.Text="请先重新设置可读取的封面。";return;}
            BatchItem selected=items[index];string path=selected.OwnCover?selected.CoverPath:sharedCoverPath;string label=CoverNameFor(selected);
            Bitmap next=cover==null?null:(Bitmap)cover.Clone();if(sharedCover!=null)sharedCover.Dispose();sharedCover=next;sharedCoverPath=path;sharedCoverLabel=label;
            foreach(var item in items){item.OwnCover=false;item.CoverPath=null;item.CoverName=null;item.State="待导出";}
            RefreshRows();status.Text="同一封面已应用到全部 "+items.Count+" 张原图。仍可选中任意一张，单独替换它的封面。";
        }
        async Task Paste(bool original)
        {
            if(busy)return;
            try
            {
                if(Clipboard.ContainsFileDropList())
                {
                    string[] files=Clipboard.GetFileDropList().Cast<string>().ToArray();
                    if(original)await ImportPaths(files);else if(files.Length>0)await LoadCover(files[0]);return;
                }
                if(!Clipboard.ContainsImage()){status.Text="剪贴板里没有图片或图片文件。";return;}
                using(var img=Clipboard.GetImage())
                {
                    Codec.CheckSize(img.Width,img.Height);var image=new Bitmap(img);
                    if(original)
                    {
                        string file=SaveTemporary(image);items.Add(new BatchItem {Path=file,Name="粘贴图片_"+(items.Count+1)+".png",Width=image.Width,Height=image.Height});image.Dispose();RefreshRows(items.Count-1);
                    }
                    else SetCover(image,"粘贴的封面");
                }
            }
            catch(Exception ex){Error("粘贴失败",ex);}
        }
        void ShowSelected()
        {
            if(real!=null){real.Dispose();real=null;}realView.Image=null;
            int i=SelectedIndex();
            if(i>=0&&i<items.Count)
            {
                try{real=Codec.Load(items[i].Path);realView.Image=real;realName.Text=Batch.SequenceLabel(i+1)+"  "+items[i].Name+"  ·  "+real.Width+" × "+real.Height;tips.SetToolTip(realName,items[i].Path);}
                catch(Exception ex){realName.Text="无法读取："+ex.Message;}
            }
            else realName.Text="尚未导入原图";
            realView.Invalidate();RefreshCover();
        }
        void RefreshCover()
        {
            if(coverView==null)return;
            Bitmap previous=frontPreview;frontPreview=null;
            int index=SelectedIndex();
            try
            {
                bool own=index>=0&&index<items.Count&&items[index].OwnCover;
                string key=(own?"own:":"shared:")+(own?items[index].CoverPath:sharedCoverPath);
                if(key!=coverKey)
                {
                    if(cover!=null){cover.Dispose();cover=null;}
                    if(own){if(items[index].CoverPath!=null)cover=Codec.Load(items[index].CoverPath);}
                    else if(sharedCover!=null)cover=(Bitmap)sharedCover.Clone();
                    coverKey=key;
                    if(previewBase!=null){previewBase.Dispose();previewBase=null;}
                }
                coverLabel=own?(items[index].CoverName??"默认封面"):sharedCoverLabel;
                Size size=real==null?new Size(640,480):Codec.OutputSize(real,640);
                if(previewBase==null||previewBase.Size!=size||previewColor!=preferences.PaddingColor)
                {if(previewBase!=null)previewBase.Dispose();previewBase=Batch.MakeCover(cover,size.Width,size.Height,1,false,preferences.PaddingColor);}
                bool stamp=numbered!=null&&numbered.Checked&&index>=0;
                frontPreview=(Bitmap)previewBase.Clone();preferences.CoverText.Draw(frontPreview,stamp);if(stamp)Batch.StampNumber(frontPreview,Math.Max(1,index+1));
                previewColor=preferences.PaddingColor;
                coverView.Image=frontPreview;coverName.Text=coverLabel+(index>=0?"  ·  配对第 "+(index+1)+" 张原图":"  ·  新导入图片的初始封面");
            }
            catch(Exception ex){coverView.Image=null;coverName.Text="封面无法读取："+ex.Message;}
            if(previous!=null)previous.Dispose();coverView.Invalidate();
        }
        void RemoveSelected()
        {
            if(busy)return;int current=SelectedIndex();foreach(int i in SelectedIndices().OrderByDescending(i=>i))items.RemoveAt(i);
            RefreshRows(Math.Max(0,current));status.Text="已移除选中项，封面序号已按当前队列重新排列。";
        }
        void MoveSelected(int delta)
        {
            if(busy)return;
            var selected=SelectedIndices();if(selected.Count!=1){status.Text="请只选中一张原图，再上移或下移。";return;}
            int i=selected[0],next=i+delta;if(next<0||next>=items.Count)return;
            var item=items[i];items.RemoveAt(i);items.Insert(next,item);RefreshRows(next);
        }
        void ClearQueue(){if(busy)return;items.Clear();RefreshRows();status.Text="队列已清空；磁盘上的原图没有删除。";}
        void UpdateActions(){batchExport.Enabled=!busy&&items.Count>0;singleExport.Enabled=!busy&&items.Count>0;exportFolder.Enabled=!busy;browseFolder.Enabled=!busy;}
        void SetBusy(bool value)
        {
            busy=value;content.Enabled=!value;UseWaitCursor=value;cancel.Visible=value;progress.Visible=value;
            SetNavigationBusy(value);
            if(value){cancellation=new CancellationTokenSource();progress.Value=0;}
            else {if(cancellation!=null)cancellation.Dispose();cancellation=null;}
            UpdateActions();Relayout();
        }
        int SelectedSize(){int[] values={1200,2048,3200,0};return values[resolution.SelectedIndex];}
        async Task<byte[]> BuildCurrent()
        {
            int index=SelectedIndex();int size=SelectedSize();bool stamp=numbered.Checked;CancellationToken token=cancellation==null?CancellationToken.None:cancellation.Token;
            if(real==null)throw new InvalidOperationException("请先选择一张可读取的原图。");
            if(coverView.Image==null)throw new InvalidOperationException("当前封面无法读取，请重新设置封面。");
            UpdateCoverText();var text=preferences.CoverText.Copy();
            using(var r=(Bitmap)real.Clone())using(var c=cover==null?null:(Bitmap)cover.Clone())
                return await Task.Run(()=>Batch.Build(c,r,size,index+1,stamp,preferences.PaddingColor,text,token));
        }
        async Task ShowPreview()
        {
            if(busy||SelectedIndex()<0){status.Text="请先在队列中选择一张原图。";return;}
            SetBusy(true);
            try{byte[] png=await BuildCurrent();using(var dialog=new PreviewForm(png))L.Show(dialog,this);status.Text="预览完成：显示的是当前图片配对及实际编号效果。";}
            catch(Exception ex){Error("预览失败",ex);}finally{SetBusy(false);}
        }
        async Task ExportCurrent()
        {
            int i=SelectedIndex();if(busy||i<0)return;
            if(string.IsNullOrWhiteSpace(exportFolder.Text)&&!ChooseExportFolder())return;
            if(!RememberExportFolder(true))return;string destination;
            try{destination=ExportLocation.FunctionFolder(exportFolder.Text,"双图切换");}catch(Exception ex){Error("保存位置无法使用",ex);return;}
            using(var d=new SaveFileDialog {Title="保存当前配对图片",Filter="PNG 图片|*.png",DefaultExt="png",AddExtension=true,FileName=(i+1).ToString("D3")+"_双图.png",InitialDirectory=destination})
            {
                if(L.Show(d,this)!=DialogResult.OK)return;SetBusy(true);
                try{byte[] png=await BuildCurrent();Codec.SaveAtomic(d.FileName,png);items[i].State="已导出";queue.Rows[i].Cells["state"].Value="已导出";status.Text="已导出当前图片。";using(var f=new SavedForm(d.FileName))L.Show(f,this);}
                catch(Exception ex){Error("导出失败",ex);}finally{SetBusy(false);}
            }
        }
        bool ChooseExportFolder()
        {
            if(busy)return false;
            using(var d=new FolderBrowserDialog {Description="选择保存位置（会自动记住）",ShowNewFolderButton=true,SelectedPath=Directory.Exists(exportFolder.Text)?exportFolder.Text:""})
            {
                if(L.Show(d,this)!=DialogResult.OK)return false;
                exportFolder.Text=d.SelectedPath;return RememberExportFolder(true);
            }
        }
        bool RememberExportFolder(bool showError)
        {
            try
            {
                string path=ExportLocation.Normalize(exportFolder.Text);exportFolder.Text=path;
                exportLocation.Save(path);locationHint.Text="自动记住根目录；导出时自动放入「双图切换」子文件夹。";return true;
            }
            catch(Exception ex)
            {
                locationHint.Text="保存位置未记住："+ex.Message;if(showError)Error("保存位置无法保存",ex);return false;
            }
        }
        async Task ExportBatch()
        {
            if(busy||items.Count==0)return;
            if(string.IsNullOrWhiteSpace(exportFolder.Text)&&!ChooseExportFolder())return;
            if(!RememberExportFolder(true))return;
            {
                UpdateCoverText();var text=preferences.CoverText.Copy();int max=SelectedSize();bool stamp=numbered.Checked;string folder=exportFolder.Text;var snapshot=items.ToList();SetBusy(true);
                try
                {
                    folder=ExportLocation.FunctionFolder(folder,"双图切换");
                    var reporter=new Progress<BatchProgress>(p=>
                    {
                        progress.Value=Math.Min(100,p.Completed*100/Math.Max(1,p.Total));
                        items[p.Index].State=p.State;queue.Rows[p.Index].Cells["state"].Value=p.State;
                        status.Text="正在导出 "+p.Completed+" / "+p.Total+"："+items[p.Index].Name+"（"+p.State+"）";
                    });
                    BatchResult result;
                    using(var shared=sharedCover==null?null:(Bitmap)sharedCover.Clone())
                    {
                        CancellationToken token=cancellation.Token;
                        Color background=preferences.PaddingColor;
                        result=await Task.Run(()=>Batch.Export(snapshot,shared,folder,max,stamp,token,reporter,background,text));
                    }
                    status.Text=(result.Cancelled?"任务已停止。":"批量导出完成。")+" 成功 "+result.Files.Count+" 张，失败 "+result.Errors.Count+" 张。";
                    string summary=L.Canonical(status.Text)+Environment.NewLine+"输出文件夹："+folder+Environment.NewLine+Environment.NewLine;
                    summary+=result.Errors.Count>0?string.Join(Environment.NewLine,result.Errors):"每张原图使用各自的封面配对；开启编号时，封面右下角按队列顺序标记。";
                    using(var f=new ResultForm("批量导出结果",summary,folder))L.Show(f,this);
                }
                catch(Exception ex){Error("批量导出失败",ex);}finally{SetBusy(false);}
            }
        }
        async Task RestoreFile()
        {
            if(busy)return;
            using(var input=new OpenFileDialog {Title="选择双图 PNG 原文件",Filter="PNG / APNG|*.png;*.apng|所有文件|*.*"})
            {
                if(L.Show(input,this)!=DialogResult.OK)return;
                if(string.IsNullOrWhiteSpace(exportFolder.Text)&&!ChooseExportFolder())return;if(!RememberExportFolder(true))return;
                string folder;try{folder=ExportLocation.FunctionFolder(exportFolder.Text,"双图切换");}catch(Exception ex){Error("保存位置无法使用",ex);return;}
                using(var output=new SaveFileDialog {Title="保存还原的隐藏图",Filter="PNG 图片|*.png",DefaultExt="png",AddExtension=true,FileName=System.IO.Path.GetFileNameWithoutExtension(input.FileName)+"_还原.png",InitialDirectory=folder})
                {
                    if(L.Show(output,this)!=DialogResult.OK)return;
                    if(string.Equals(System.IO.Path.GetFullPath(input.FileName),System.IO.Path.GetFullPath(output.FileName),StringComparison.OrdinalIgnoreCase))
                    {status.Text="请选择新的文件名，以保留双图原文件。";return;}
                    SetBusy(true);
                    try
                    {
                        byte[] restored=await Task.Run(delegate{if(new FileInfo(input.FileName).Length>160L*1024*1024)throw new InvalidDataException("输入文件超过 160 MB。");return Codec.Restore(File.ReadAllBytes(input.FileName));});
                        using(var image=Codec.Decode(restored))Codec.CheckSize(image.Width,image.Height);
                        Codec.SaveAtomic(output.FileName,restored);status.Text="隐藏图已还原。";using(var f=new SavedForm(output.FileName))L.Show(f,this);
                    }
                    catch(Exception ex){Error("还原失败",ex);}finally{SetBusy(false);}
                }
            }
        }
        void Error(string title,Exception ex){status.Text=title+"："+ex.Message;using(var f=new ResultForm(title,ex.Message,null))L.Show(f,this);}
        void DisposeImages(){coverView.Image=realView.Image=null;if(cover!=null)cover.Dispose();if(sharedCover!=null)sharedCover.Dispose();if(real!=null)real.Dispose();if(frontPreview!=null)frontPreview.Dispose();if(previewBase!=null)previewBase.Dispose();}
        public void CaptureScroll(bool bottom){if(activePage>0){((ToolPage)pages[activePage]).CaptureScroll(bottom);return;}scroll.AutoScrollPosition=new Point(0,bottom?content.Height:0);}
#if SELF_TEST
        public void LoadDemo(int count=4)
        {
            EnsureMergedRestore();mergedRestore.Demo();
            SetCover(SelfTest.Art(700,520,false),"日光封面.png");
            for(int i=0;i<count;i++)using(var b=SelfTest.Art(700,520,true))
                items.Add(new BatchItem {Path=SaveTemporary(b),Name="原图_"+(i+1)+".png",Width=700,Height=520});
            using(var alternative=SelfTest.Art(700,520,true)){items[1].OwnCover=true;items[1].CoverPath=SaveTemporary(alternative);items[1].CoverName="单独设置的封面.png";}
            RefreshRows(0);status.Text="示例：第 2 张使用独立封面，其他图片共用日光封面。";
        }
        public string LayoutReport()
        {
            var lines=new List<string>();
            foreach(Control c in Descendants(this))lines.Add(c.GetType().Name+" | "+c.Text.Replace("\n"," ")+" | "+c.Bounds+" | preferred "+c.GetPreferredSize(new Size(c.Width,0))+" | dock "+c.Dock+" | visible "+c.Visible);
            return string.Join(Environment.NewLine,lines);
        }
        public void ValidateLayout()
        {
            // A 50/50 layout can allocate one extra pixel to the second column.
            if(activePage==0&&!stacked&&(coverCard.Height!=realCard.Height||coverView.Height!=realView.Height||Math.Abs(coverView.Width-realView.Width)>1))throw new Exception("Parallel image cards or preview areas differ in size");
            foreach(var c in Descendants(this).Where(c=>c.Visible&&(c is Button||c is Label)))
            {
                Size preferred=c.GetPreferredSize(new Size(c.Width,0));
                if(c.Height<preferred.Height)throw new Exception("Text clipped vertically: "+c.Text+"; bounds="+c.Bounds+"; preferred="+preferred+"; max="+c.MaximumSize+"; min="+c.MinimumSize+"; parent="+c.Parent.ClientSize);
                foreach(Control sibling in c.Parent.Controls)
                {
                    if(sibling==c||!sibling.Visible||!(sibling is Button||sibling is Label||sibling is SoftCombo||sibling is SoftInput||sibling is NumericUpDown))continue;
                    if(c.Bounds.IntersectsWith(sibling.Bounds))throw new Exception("Text controls overlap: "+c.Text+" / "+sibling.Text);
                }
            }
        }
        public void SimulateScale(float scale)
        {
            for(int index=1;index<pages.Count;index++)EnsurePage(index);
            if(Math.Abs(scale-1)<0.001f)return;
            var fonts=Descendants(this).Select(c=>new {Control=c,Size=c.Font.Size,Style=c.Font.Style,Family=c.Font.FontFamily.Name}).ToList();
            Scale(new SizeF(scale,scale));
            foreach(var entry in fonts)entry.Control.Font=new Font(entry.Family,entry.Size*scale,entry.Style);
            Font=new Font("Microsoft YaHei UI",10*scale);
            Relayout();
        }
        public void CheckPairingBehavior()
        {
            LoadDemo();string originalCustom=items[1].CoverPath;
            queue.CurrentCell=queue.Rows[0].Cells[0];queue.ClearSelection();queue.Rows[0].Selected=true;
            SetCover(SelfTest.Art(240,160,true),"第一张专用封面.png");
            if(!items[0].OwnCover||items[1].CoverPath!=originalCustom||items[2].OwnCover)throw new Exception("Single cover assignment changed other pairs");
            ApplyCoverToAll();
            if(items.Any(i=>i.OwnCover))throw new Exception("Apply all did not establish shared cover");
            queue.CurrentCell=queue.Rows[1].Cells[0];queue.ClearSelection();queue.Rows[1].Selected=true;
            SetCover(SelfTest.Art(240,160,false),"第二张专用封面.png");
            if(!items[1].OwnCover||items[0].OwnCover||items[2].OwnCover)throw new Exception("Per-image override lost after applying shared cover");
            string name=items[1].Name;MoveSelected(1);
            if(items[2].Name!=name||!items[2].OwnCover)throw new Exception("Reorder detached cover pairing");
            queue.ClearSelection();queue.Rows[0].Selected=true;queue.Rows[1].Selected=true;
            SetCover(SelfTest.Art(240,160,false),"多选批量封面.png");
            if(items[0].CoverPath!=items[1].CoverPath||items[2].CoverName!="第二张专用封面.png")throw new Exception("Selected-only cover assignment failed");
            RemoveSelected();if(items.Count!=2||items[0].Name!=name)throw new Exception("Removal or renumbering lost pairing");
        }
        public void CheckExportLocation(string expected,string next)
        {
            if(exportFolder.Text!=expected)throw new Exception("Remembered export folder was not loaded into the UI");
            exportFolder.Text=next;
            if(!RememberExportFolder(false))throw new Exception("UI failed to save export folder");
        }
        public void CheckNavigationBehavior()
        {
            int count=items.Count;string paired=count>0?items[0].CoverPath:null;
            for(int index=1;index<pages.Count;index++)
            {
                navigationButtons[index].PerformClick();Application.DoEvents();
                if(activePage!=index||!pages[index].Visible||pages.Where((p,i)=>i!=index&&p!=null).Any(p=>p.Visible))throw new Exception("Navigation did not switch independent page "+index);
                if(navigationButtons[index].BackColor!=Ui.Purple)throw new Exception("Active page button not highlighted");
                if(((ToolPage)pages[index]).OutputFolder.Folder!=exportLocation.Load())throw new Exception("Folder was not shared between pages");
            }
            navigationButtons[0].PerformClick();Application.DoEvents();
            if(items.Count!=count||(count>0&&items[0].CoverPath!=paired))throw new Exception("Page switching discarded cover pairing");
            string previous=exportFolder.Text;SwitchPage(3);
            string chosen=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exportLocation.SettingsFile)),"共享保存位置测试");
            ((ToolPage)pages[3]).OutputFolder.TestSetFolder(chosen);SwitchPage(0);
            if(exportFolder.Text!=chosen)throw new Exception("Folder selected in another page did not reach disguise page");
            exportFolder.Text=previous;RememberExportFolder(false);
        }
        public Task CheckMergedRestore(string folder){SwitchPage(0);EnsureMergedRestore();folder=Path.GetFullPath(folder);exportFolder.Text=folder;RememberExportFolder(false);return mergedRestore.CheckMergedRestore(folder);}
        public Task CheckObfuscationBehavior(string folder){SwitchPage(2);return ((ObfuscationPage)pages[2]).CheckBehavior(folder);}
        public Task CheckBackgroundBehavior(string folder){SwitchPage(8);return ((BackgroundRevealPage)pages[8]).CheckBehavior(folder);}
        public Task CheckEditorBehavior(){SwitchPage(4);return ((RedactPage)pages[4]).CheckEditingBehavior();}
        public Task CheckInspectionBehavior(string folder){SwitchPage(5);return ((InspectionPage)pages[5]).CheckExport(folder);}
        public Task CheckTutorialBehavior(string folder){SwitchPage(10);return ((TutorialPage)pages[10]).CheckBehavior(folder);}
        public Task CheckQrBehavior(string folder){SwitchPage(9);return ((QrPage)pages[9]).CheckBehavior(folder);}
        internal int TestActivePage {get{return activePage;}}
        public Task CheckFileBehavior(string folder){SwitchPage(7);return ((FileDisguisePage)pages[7]).CheckBehavior(folder);}
        internal void ChooseLanguage(bool english){EnsurePage(6);((SettingsPage)pages[6]).ChooseLanguage(english);}
        internal void CheckLanguageBehavior(string report)
        {
            foreach(int index in new[]{0,8,2,7,1,3,4,5,9,6,10})DemoPage(index);
            string originalFolder=exportFolder.Text;var paths=items.Select(i=>i.Path).ToArray();string text=coverText.Text;string fileState=((FileDisguisePage)pages[7]).StateFingerprint();
            ChooseLanguage(true);Application.DoEvents();if(!L.English||new ToolPreferences(exportLocation.SettingsFile).Language!="en"||Text!="AI Image Editing Tools")throw new Exception("Language did not switch or persist");
            var untranslated=new List<string>();
            foreach(int index in new[]{0,8,2,7,1,3,4,5,9,6,10})
            {
                SwitchPage(index);Application.DoEvents();ValidateLayout();
                foreach(Control c in ControlTree(pages[index]))
                {
                    if((c.Tag as string)!="raw"&&(c is Label||c is Button||c is CheckBox||(c is TextBox&&((TextBox)c).ReadOnly))&&System.Text.RegularExpressions.Regex.IsMatch(c.Text,@"[\u4e00-\u9fff]"))untranslated.Add(index+": "+c.Text);
                    var combo=c as SoftCombo;if(combo!=null&&(combo.Tag as string)!="raw")foreach(var item in combo.Items)if(item.ToString()!="简体中文")untranslated.AddRange(L.Untranslated(new[]{item.ToString()}).Select(s=>index+" combo: "+s));
                }
                foreach(var b in ButtonTree(pages[index]))b.TestHint();
            }
            File.WriteAllLines(report+".captions.txt",untranslated.Distinct());
            if(untranslated.Any(s=>!System.Text.RegularExpressions.Regex.IsMatch(s,@"\.(png|jpg|jpeg|zip|mp4|rar|7z)\b|Root folder is remembered\.|Created by: 光影若水")))throw new Exception("Untranslated application caption: "+string.Join("\n",untranslated));
            if(exportFolder.Text!=originalFolder||!items.Select(i=>i.Path).SequenceEqual(paths)||coverText.Text!=text||((FileDisguisePage)pages[7]).StateFingerprint()!=fileState)throw new Exception("Changing language modified user data or queued files");
            ChooseLanguage(false);Application.DoEvents();if(L.English||Text!="AI Image Editing Tools"||new ToolPreferences(exportLocation.SettingsFile).Language!="zh")throw new Exception("Switching back did not restore Chinese captions");
            foreach(var b in navigationButtons)if(b.Text!=L.Canonical(b.Text)||!System.Text.RegularExpressions.Regex.IsMatch(b.Text,@"[\u4e00-\u9fff]"))throw new Exception("Chinese navigation caption was lost");
            if(L.T("参数读取")!="参数读取")throw new Exception("Inactive translation modified text");
            File.WriteAllText(report,"PASS: live Chinese/English switching across eleven pages, saved language preference, navigation and full button hints, original filenames/paths/prompts/queue preserved, switching back restores Chinese.");
        }
        static IEnumerable<Control> ControlTree(Control c){foreach(Control child in c.Controls){yield return child;foreach(var next in ControlTree(child))yield return next;}}
        public void CheckButtonHints()
        {
            foreach(int index in new[]{0,8,2,7,1,3,4,5,9,6,10}){SwitchPage(index);foreach(var button in ButtonTree(pages[index]))button.TestHint();}
            foreach(var button in navigationButtons)((SoftButton)button).TestHint();
            using(var button=new SoftButton{Text="完整的很长按钮名称"}){button.TestHint();button.Text="更新后的名称";button.TestHint();button.Text="";button.AccessibleName="复制正面 tag";button.TestHint();}
        }
        static IEnumerable<SoftButton> ButtonTree(Control c){foreach(Control child in c.Controls){var button=child as SoftButton;if(button!=null)yield return button;foreach(var b in ButtonTree(child))yield return b;}}
        public void CheckTextBehavior()
        {
            SwitchPage(0);var previous=preferences.CoverText.Copy();
            coverText.Text="点开原图";for(int index=0;index<10;index++)textPosition.TestChoice(index);textPosition.TestChoice(0);textSize.Value=8;UpdateCoverText();
            ((CoverCanvas)coverView).TestDrag(new Point(coverView.Width/2,coverView.Height/2),new Point(coverView.Width*2/3,coverView.Height*2/3));
            if(preferences.CoverText.X<55||preferences.CoverText.Y<55||preferences.CoverText.Position!=3)throw new Exception("Free text drag did not update image coordinates");
            if(coverCard.Height!=realCard.Height&&!stacked)throw new Exception("Parallel cards have different heights");
            textLayers.TestAdd();coverText.Text="第二条文字";UpdateCoverText();textLayers.TestChoose(0);if(preferences.CoverText.Text!="点开原图"||preferences.CoverText.Extra[preferences.CoverText.Extra.Count-1].Text!="第二条文字")throw new Exception("Layer selection lost text");textLayers.TestChoose(preferences.CoverText.Extra.Count);textLayers.TestDelete();
            preferences.CoverText=previous;textLayers.RefreshChoices();coverText.Text=previous.Text;textPosition.SelectedIndex=Array.IndexOf(PositionIds,previous.Position);textSize.Value=previous.SizePercent;textX.Value=previous.X;textY.Value=previous.Y;UpdateCoverText();
        }
        internal async Task CheckEmptyCoverClick(string folder)
        {
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,"click-cover.png");using(var image=SelfTest.Art(100,80,false))Codec.SaveAtomic(path,Codec.StaticPng(image));
            var canvas=(CoverCanvas)coverView;bool placement=canvas.TextPlacement;int picks=0;testCoverPicker=delegate{picks++;return path;};
            try
            {
                canvas.TextPlacement=true;canvas.Image=null;canvas.TestMouseClick(MouseButtons.Right);if(picks!=0)throw new Exception("Right click opened a cover picker");
                canvas.TestMouseClick(MouseButtons.Left);for(int retry=0;retry<100&&busy;retry++)await Task.Delay(20);
                if(busy||picks!=1||canvas.Image==null)throw new Exception("Empty dual-image cover canvas did not load the selected cover");
                canvas.TestMouseClick(MouseButtons.Left);if(picks!=1)throw new Exception("Loaded text placement canvas opened a cover picker");
            }
            finally{testCoverPicker=null;canvas.TextPlacement=placement;RefreshCover();}
        }
        public async Task CheckDualPage(string folder)
        {
            Directory.CreateDirectory(folder);SwitchPage(0);LoadDemo();EnsureMergedRestore();
            var paths=items.Select(i=>i.Path).ToArray();string text=coverText.Text;
            foreach(bool english in new[]{false,true})
            {
                ChooseLanguage(english);
                foreach(Size bounds in new[]{new Size(900,700),new Size(1500,950)})
                {
                    Size=bounds;Application.DoEvents();Relayout();ValidateLayout();
                    if(Descendants(this).Any(c=>L.Canonical(c.Text)=="彩色背景双图"||L.Canonical(c.Text)=="灰度背景双图"))throw new Exception("Removed dual mode remains visible");
                    if(english&&L.Untranslated(new[]{dualDescription.Text,coverHeading.Text,realHeading.Text}).Length!=0)throw new Exception("Dual captions were not translated");
                    if(!items.Select(i=>i.Path).SequenceEqual(paths)||coverText.Text!=text)throw new Exception("Layout changed queued data");
                    Codec.Restore(await BuildCurrent());
                    if(bounds.Width==900){CaptureScroll(false);using(var shot=new Bitmap(Width,Height)){DrawToBitmap(shot,new Rectangle(Point.Empty,Size));shot.Save(Path.Combine(folder,(english?"en":"zh")+"-dual.png"));}}
                }
            }
            ChooseLanguage(false);
        }
        public void ShowTextDemo(){coverText.Text="点击查看原图";textPosition.SelectedIndex=0;textX.Value=62;textY.Value=70;UpdateCoverText();CaptureScroll(false);}
        public void ShowBrushDemo(){SwitchPage(4);((RedactPage)pages[4]).ShowBrushDemo();}
#endif
    }
}
