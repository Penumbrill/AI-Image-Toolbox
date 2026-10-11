using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQImageSwitch
{
    static class Program
    {
        static Program(){QrDependencies.Register();}
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static int Main(string[] args)
        {
            try
            {
                if(args.Length>0&&args[0].StartsWith("--prototype-")){try{PackagingPrototype.Command(args);return 0;}catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
#if SELF_TEST
                if(args.Length>0&&args[0]=="--scramble-test"){ObfuscationTests.Run(args[1]);return 0;}
#endif
                if(args.Length>0&&(args[0]=="--scramble"||args[0]=="--descramble")){if(args.Length!=5)throw new ArgumentException("--scramble/--descramble input output quality report.json");string path=Obfuscation.Write(args[1],args[2],args[0]=="--descramble",int.Parse(args[3]),System.Threading.CancellationToken.None);File.WriteAllText(args[4],Inspection.Json().Serialize(new{Path=path,Length=new FileInfo(path).Length}));return 0;}
                if(args.Length>0&&args[0]=="--scramble-restore"){if(args.Length!=4)throw new ArgumentException("--scramble-restore input folder report.json");string path=Obfuscation.RestoreOriginal(args[1],args[2],System.Threading.CancellationToken.None);File.WriteAllText(args[3],Inspection.Json().Serialize(new{Path=path,Sha256=QQRoundTrip.Hash(path,System.Threading.CancellationToken.None)}));return 0;}
                if(args.Length>0&&args[0].StartsWith("--file-"))
                {
                    var token=System.Threading.CancellationToken.None;object result;
                    switch(args[0])
                    {
                        case "--file-pack":if(args.Length!=5)throw new ArgumentException("--file-pack cover input output.jpg report.json");result=FileDisguise.Pack(args[1],args[2],args[3],token);break;
                        case "--file-check":if(args.Length!=3)throw new ArgumentException("--file-check input.jpg report.json");result=FileDisguise.Check(args[1],token);break;
                        case "--file-restore":if(args.Length!=4)throw new ArgumentException("--file-restore input.jpg folder report.json");string restored=FileDisguise.Restore(args[1],args[2],token);result=new{Path=restored,Sha256=QQRoundTrip.Hash(restored,token)};break;
#if SELF_TEST
                        case "--file-self-test":FileDisguiseTests.Run(args[1]);return 0;
#endif
                        default:throw new ArgumentException("未知文件测试命令。");
                    }
                    File.WriteAllText(args[args.Length-1],Inspection.Json().Serialize(result),System.Text.Encoding.UTF8);return 0;
                }
                if(args.Length>0 && args[0]=="--encode")
                {
                    if(args.Length<4) throw new ArgumentException("--encode cover real output [maxSide]");
                    int max=args.Length>4?int.Parse(args[4]):2048;
                    using(var real=Codec.Load(args[2]))
                    using(var cover=Codec.Load(args[1]))
                    {
                        var size=Codec.OutputSize(real,max);
                        using(var r=Codec.Fit(real,size.Width,size.Height,Color.Transparent))
                        using(var c=Codec.Fit(cover,size.Width,size.Height,Color.White))
                            Codec.SaveAtomic(args[3],Codec.Encode(c,r));
                    }
                    return 0;
                }
                if(args.Length>0 && args[0]=="--restore")
                {
                    if(args.Length!=3) throw new ArgumentException("--restore input output");
                    Codec.SaveAtomic(args[2],Codec.Restore(File.ReadAllBytes(args[1])));return 0;
                }
#if SELF_TEST
                if(args.Length>0 && args[0]=="--self-test") {SelfTest.Run(args[1]);return 0;}
#endif
#if SELF_TEST
                if(args.Length>0 && args[0]=="--batch-test") {BatchTests.Run(args[1]);return 0;}
#endif
                if(args.Length>0 && args[0]=="--clean") {ImageTools.Clean(args[1],args[2]);return 0;}
#if SELF_TEST
                if(args.Length>0 && args[0]=="--tools-test") {ToolsTests.Run(args[1]);return 0;}
#endif
#if SELF_TEST
                if(args.Length>0 && args[0]=="--inspection-test") {InspectionTests.Run(args[1]);return 0;}
#endif
                if(args.Length>0 && args[0]=="--inspect") {var result=Inspection.Read(args[1]);File.WriteAllText(args[2],Inspection.Json().Serialize(result));return 0;}
                if(args.Length>0 && args[0]=="--inspect-txt") {var result=Inspection.Read(args[1]);InspectionReport.Export(args[2],result,args[1],result,args[1]);return 0;}
                SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
#if SELF_TEST
                if(args.Length>0 && args[0]=="--settings-test") {SettingsTests.Run(args[1]);return 0;}
#endif
#if SELF_TEST
                if(args.Length>0 && args[0]=="--feature-test") {FeatureTests.Run(args[1]);return 0;}
#endif
#if SELF_TEST
                if(args.Length>0 && args[0]=="--ui-test")
                {
                    using(var f=new MainForm(args[1]+".settings"))
                    {
                        Exception uiError=null;System.Threading.ThreadExceptionEventHandler handler=delegate(object s,System.Threading.ThreadExceptionEventArgs e){uiError=e.Exception;};Application.ThreadException+=handler;
                        // Use the same message loop as the app so async export continuations stay on the UI thread.
                        f.Shown+=async delegate
                        {
                            try{f.ChooseLanguage(false);ToolsTests.CheckCanvasClicks(args[1]+".canvas-clicks");await f.CheckEmptyCoverClick(args[1]+".canvas-clicks");f.CheckPairingBehavior();f.CheckNavigationBehavior();f.CheckTextBehavior();await f.CheckInspectionBehavior(args[1]+".export");await f.CheckEditorBehavior();await f.CheckFileBehavior(args[1]+".files");await f.CheckObfuscationBehavior(args[1]+".scramble");await f.CheckMergedRestore(args[1]+".merged");await f.CheckDualPage(args[1]+".dual");await f.CheckBackgroundBehavior(args[1]+".background");await f.CheckTutorialBehavior(args[1]+".tutorial");await f.CheckQrBehavior(args[1]+".qr");f.CheckButtonHints();f.CheckLanguageBehavior(args[1]+".language.txt");}
                            catch(Exception ex){uiError=ex;}
                            finally{f.Close();}
                        };
                        Application.Run(f);
                        Application.ThreadException-=handler;if(uiError!=null)throw new Exception("Unhandled UI exception",uiError);
                    }
                    File.WriteAllText(args[1],"PASS: cover pairing and queue operations, eleven independent pages, embedded double PNG restore, background reveal immediately below double PNG, custom JPEG percentage and actual preview size, batch scrambling and original-file SHA-256 restore, queue persists, active navigation highlighting, shared folder, equal-height parallel cards, nine text presets and free drag, three-row scrolling position dropdown, repeated dropdown open/select/close, per-field parameter copy buttons, separate WebUI/ComfyUI TXT files in tag读取 without overwrite, unloaded and empty WebUI panels skipped, wheel zoom at pointer, middle mouse pan, live brush width and strength preview without modifying pixels, rectangle/mosaic-brush gestures and apply/undo/reset, independent file card states, QR generation/import scan/overlay/multiple text layers and background linkage, 50-step undo/redo, async file generation and hash-checked restore, no unhandled UI exceptions.");return 0;
                }
                if(args.Length>0 && args[0]=="--render-ui")
                {
                    
                    Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){File.WriteAllText(args[1]+".failed.txt",e.Exception.ToString());Environment.Exit(1);};
                    using(var f=new MainForm(args[1]+".settings"))
                    {
                        f.LoadDemo(12);f.Show();Application.DoEvents();
                        if(args.Length>3){f.ClientSize=new Size(int.Parse(args[2]),int.Parse(args[3]));Application.DoEvents();}
                        if(args.Length>4){float scale=float.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture);if(Math.Abs(scale-1)>0.001f)f.SimulateScale(scale);Application.DoEvents();}
                        if(args.Length>6){f.DemoPage(int.Parse(args[6]));Application.DoEvents();}
                        if(System.Array.IndexOf(args,"en")>=0){f.ChooseLanguage(true);Application.DoEvents();}
                        f.CaptureScroll(args.Length>5&&args[5]=="bottom");Application.DoEvents();
                        if(args.Length>7&&args[7]=="text"){f.ShowTextDemo();Application.DoEvents();}
                        if(args.Length>7&&args[7]=="brush"){f.ShowBrushDemo();Application.DoEvents();}
                        using(var b=new Bitmap(f.Width,f.Height)) {f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(args[1],ImageFormat.Png);}
                        File.WriteAllText(args[1]+".layout.txt",f.LayoutReport());
                        f.ValidateLayout();f.Close();return 0;
                    }
                }
#endif
                Application.Run(new MainForm());return 0;
            }
            catch(Exception ex)
            {
                if(args.Length==0) MessageBox.Show(ex.Message,"无法启动",MessageBoxButtons.OK,MessageBoxIcon.Error);
                else
                {
                    Console.Error.WriteLine(ex.ToString());
                    if(args[0].StartsWith("--file-")&&args.Length>1)File.WriteAllText(args[args.Length-1]+".failed.txt",ex.ToString());
                    if(args[0]=="--ui-test"&&args.Length>1)File.WriteAllText(args[1]+".failed.txt",ex.ToString());
#if SELF_TEST
                    if(args[0]=="--self-test"&&args.Length>1){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"FAILED.txt"),ex.ToString());}
#endif
                }
                return 1;
            }
        }
    }

    public class ImageCanvas : Control
    {
        Control observedViewport;
        EventHandler viewportChanged;
        Image image;
        Size imageSize;
        Bitmap display;
        Bitmap reduced;
        readonly System.Windows.Forms.Timer settle=new System.Windows.Forms.Timer{Interval=140};
        bool interacting;
        Bitmap checkerTile;
        Color checkerColor;
        Rectangle displayBounds;
        public double Zoom {get;private set;}
        public PointF Pan {get;private set;}
        public event EventHandler ViewChanged;
        bool panning;
        Point lastPan;
        public Image Image {get{return image;}set{if(image==value)return;Size next=value==null?Size.Empty:value.Size;bool reset=image==null||value==null||imageSize!=next;image=value;imageSize=next;if(reduced!=null){reduced.Dispose();reduced=null;}ClearDisplay();if(reset)ResetView();Invalidate();}}
        protected void ClearDisplay(){if(display!=null){display.Dispose();display=null;}}
        public string EmptyText="点击选择图片\n或把文件拖到这里";
        public bool Checker=true;
        public ImageCanvas(){DoubleBuffered=true;BackColor=Color.FromArgb(248,246,253);Cursor=Cursors.Hand;TabStop=true;Zoom=1;settle.Tick+=delegate{settle.Stop();interacting=false;ClearDisplay();Invalidate();};}
        protected void Interact(){interacting=true;settle.Stop();settle.Start();}
        public static int ViewportHeight(Control viewport,double fraction,int minimum,int maximum)
        {
            float scale=Math.Max(.5f,viewport.Font.Size/10f);
            return Math.Max((int)(minimum*scale),Math.Min((int)(maximum*scale),(int)(viewport.ClientSize.Height*fraction)));
        }
        public void FollowViewport(Control viewport,double fraction,int minimum,int maximum)
        {
            if(observedViewport!=null&&viewportChanged!=null){observedViewport.ClientSizeChanged-=viewportChanged;observedViewport.FontChanged-=viewportChanged;}
            observedViewport=viewport;viewportChanged=delegate
            {
                if(IsDisposed||Disposing||viewport.IsDisposed||viewport.Disposing)return;int height=HeightResize.Resolve(this,ViewportHeight(viewport,fraction,minimum,maximum));
                if(MinimumSize.Height!=height)MinimumSize=new Size(MinimumSize.Width,height);if(Height!=height)Height=height;
            };
            viewport.ClientSizeChanged+=viewportChanged;viewport.FontChanged+=viewportChanged;HeightResize.Enable(this,h=>viewportChanged(null,EventArgs.Empty));viewportChanged(null,EventArgs.Empty);
        }
        public RectangleF ImageBounds
        {
            get
            {
                if(Image==null)return RectangleF.Empty;
                double fit=Math.Min((double)Math.Max(1,Width-28)/Image.Width,(double)Math.Max(1,Height-28)/Image.Height);
                float w=(float)(Image.Width*fit*Zoom),h=(float)(Image.Height*fit*Zoom);
                return new RectangleF((Width-w)/2+Pan.X,(Height-h)/2+Pan.Y,w,h);
            }
        }
        public double PixelScale {get{return Image==null?1:ImageBounds.Width/Image.Width;}}
        public PointF ToImage(PointF p){var r=ImageBounds;return new PointF((p.X-r.X)/(float)PixelScale,(p.Y-r.Y)/(float)PixelScale);}
        public PointF ToScreen(PointF p){var r=ImageBounds;return new PointF(r.X+p.X*(float)PixelScale,r.Y+p.Y*(float)PixelScale);}
        void Changed(){ClearDisplay();Invalidate();if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty);}
        public void ResetView(){Zoom=1;Pan=PointF.Empty;Changed();}
        public void ActualPixels(){if(Image==null)return;Zoom=Math.Max(.05,Math.Min(64,Zoom/PixelScale));Pan=PointF.Empty;Changed();}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);if(Image!=null)Focus();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();if(Image!=null&&e.Button==MouseButtons.Middle){panning=true;lastPan=e.Location;Capture=true;}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(panning){Interact();Pan=new PointF(Pan.X+e.X-lastPan.X,Pan.Y+e.Y-lastPan.Y);lastPan=e.Location;Changed();}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Middle){panning=false;Capture=false;}}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)panning=false;}
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if(Image==null){base.OnMouseWheel(e);return;}
            var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;
            Interact();var anchor=ToImage(e.Location);Zoom=Math.Max(.05,Math.Min(64,Zoom*Math.Pow(1.2,e.Delta/120.0)));
            var after=ToScreen(anchor);Pan=new PointF(Pan.X+e.X-after.X,Pan.Y+e.Y-after.Y);Changed();
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x20a&&Image!=null)
            {int delta=(short)((long)m.WParam>>16);var location=PointToClient(Cursor.Position);OnMouseWheel(new HandledMouseEventArgs(MouseButtons.None,0,location.X,location.Y,delta));m.Result=IntPtr.Zero;return;}
            base.WndProc(ref m);
        }
        internal void TestWheel(Point at,int delta){OnMouseWheel(new HandledMouseEventArgs(MouseButtons.None,0,at.X,at.Y,delta));}
        internal void TestClick(){OnClick(EventArgs.Empty);}
        internal void TestMouseClick(MouseButtons button){OnMouseClick(new MouseEventArgs(button,1,Width/2,Height/2,0));}
        internal void TestPan(Point a,Point b){OnMouseDown(new MouseEventArgs(MouseButtons.Middle,1,a.X,a.Y,0));OnMouseMove(new MouseEventArgs(MouseButtons.Middle,1,b.X,b.Y,0));OnMouseUp(new MouseEventArgs(MouseButtons.Middle,1,b.X,b.Y,0));}
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);ClearDisplay();if(Width<1||Height<1)return;
            using(var path=SoftTheme.Round(new RectangleF(0,0,Width,Height),18)){var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}
            if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            if(Image!=null)
            {
                if(Checker)
                {
                    if(checkerTile==null||checkerColor!=BackColor)
                    {
                        if(checkerTile!=null)checkerTile.Dispose();checkerTile=new Bitmap(36,36);checkerColor=BackColor;
                        using(var paint=Graphics.FromImage(checkerTile))using(var fill=new SolidBrush(Color.FromArgb(240,236,247)))
                        {paint.Clear(BackColor);paint.FillRectangle(fill,0,0,18,18);paint.FillRectangle(fill,18,18,18,18);}
                    }
                    using(var brush=new TextureBrush(checkerTile,WrapMode.Tile))g.FillRectangle(brush,ClientRectangle);
                }
                var bounds=ImageBounds;
                var visible=Rectangle.Intersect(ClientRectangle,Rectangle.Ceiling(bounds));
                if(visible.Width<1||visible.Height<1)return;
                if(display==null)
                {
                    displayBounds=visible;display=new Bitmap(visible.Width,visible.Height,PixelFormat.Format32bppPArgb);
                    var source=new RectangleF((visible.X-bounds.X)/(float)PixelScale,(visible.Y-bounds.Y)/(float)PixelScale,visible.Width/(float)PixelScale,visible.Height/(float)PixelScale);
                    Image paintSource=Image;
                    if(interacting&&PixelScale<.35&&Math.Max(Image.Width,Image.Height)>1600)
                    {
                        if(reduced==null)
                        {
                            double ratio=1280.0/Math.Max(Image.Width,Image.Height);reduced=new Bitmap(Math.Max(1,(int)Math.Round(Image.Width*ratio)),Math.Max(1,(int)Math.Round(Image.Height*ratio)),PixelFormat.Format32bppPArgb);
                            using(var shrink=Graphics.FromImage(reduced)){shrink.CompositingMode=CompositingMode.SourceCopy;shrink.InterpolationMode=InterpolationMode.Bilinear;shrink.DrawImage(Image,new Rectangle(Point.Empty,reduced.Size));}
                        }
                        source=new RectangleF(source.X*reduced.Width/Image.Width,source.Y*reduced.Height/Image.Height,source.Width*reduced.Width/Image.Width,source.Height*reduced.Height/Image.Height);paintSource=reduced;
                    }
                    using(var paint=Graphics.FromImage(display)){paint.InterpolationMode=PixelScale>=1?InterpolationMode.NearestNeighbor:interacting?InterpolationMode.Bilinear:InterpolationMode.HighQualityBicubic;paint.PixelOffsetMode=PixelOffsetMode.HighQuality;paint.DrawImage(paintSource,new Rectangle(0,0,visible.Width,visible.Height),source,GraphicsUnit.Pixel);}
                }
                g.DrawImageUnscaled(display,displayBounds.Location);
            }
            else
            {
                using(var pen=new Pen(Color.FromArgb(210,215,232),2))
                {
                    int cx=Width/2,cy=Height/2-36;
                    g.DrawEllipse(pen,cx-23,cy-23,46,46);g.DrawLine(pen,cx-9,cy,cx+9,cy);g.DrawLine(pen,cx,cy-9,cx,cy+9);
                }
                using(var brush=new SolidBrush(Color.FromArgb(124,132,154)))
                using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                    g.DrawString(L.T(EmptyText),Font,brush,new RectangleF(16,Height/2,Math.Max(1,Width-32),70),format);
            }
        }
        protected override void Dispose(bool disposing){if(disposing){settle.Stop();settle.Dispose();if(reduced!=null)reduced.Dispose();if(checkerTile!=null)checkerTile.Dispose();if(observedViewport!=null&&viewportChanged!=null){observedViewport.ClientSizeChanged-=viewportChanged;observedViewport.FontChanged-=viewportChanged;}ClearDisplay();}base.Dispose(disposing);}
    }

}
