using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace QQImageSwitch
{
    static class ToolsTests
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        public static void Run(string dir)
        {
            dir=Path.GetFullPath(dir);Directory.CreateDirectory(dir);
            var files=new List<string>();
            for(int i=0;i<3;i++)
            {
                string path=Path.Combine(dir,"frame"+i+".png");files.Add(path);
                using(var image=new Bitmap(i==2?80:160,i==2?160:100))using(var g=Graphics.FromImage(image))
                {g.Clear(new[]{Color.Red,Color.Lime,Color.Blue}[i]);Codec.SaveAtomic(path,Codec.StaticPng(image));}
            }
            byte[] gif=GifCodec.Merge(files,160,400,true,Color.White,CancellationToken.None,null);
            CheckCanvasClicks(dir);
            CheckCoverText(dir);
            string animated=Path.Combine(dir,"animated.gif");Codec.SaveAtomic(animated,gif);
            using(var image=Image.FromFile(animated))
            {
                Check(image.GetFrameCount(FrameDimension.Time)==3,"GIF frame count incorrect");
                for(int i=0;i<3;i++)
                {
                    image.SelectActiveFrame(FrameDimension.Time,i);using(var frame=new Bitmap(image))
                    {
                        Color expected=new[]{Color.Red,Color.Lime,Color.Blue}[i],pixel=frame.GetPixel(80,50);
                        Check(Math.Abs(pixel.R-expected.R)<8&&Math.Abs(pixel.G-expected.G)<8&&Math.Abs(pixel.B-expected.B)<8,"GIF order or palette mismatch");
                        if(i==2)Check(frame.GetPixel(0,0).ToArgb()==Color.White.ToArgb(),"GIF letterbox not preserved");
                    }
                }
            }
            byte[] plain=GifCodec.Merge(files,160,230,false,Color.Black,CancellationToken.None,null);Codec.SaveAtomic(Path.Combine(dir,"no-loop.gif"),plain);
            Check(System.Text.Encoding.ASCII.GetString(plain).IndexOf("NETSCAPE")<0,"Loop disabled but loop metadata remains");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();bool cancelled=false;try{GifCodec.Merge(files,160,400,true,Color.White,cancellation.Token,null);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"GIF ignored cancellation");
            }
            using(var image=SelfTest.Art(160,100,true))
            {
                var rect=new Rectangle(40,30,50,30);
                foreach(string mode in new[]{"mosaic","blur","solid"})using(var edited=ImageTools.Redact(image,rect,null,16,12,mode,Color.Black))
                {
                    bool changed=false;
                    for(int y=0;y<100;y++)for(int x=0;x<160;x++)
                    {
                        bool different=image.GetPixel(x,y).ToArgb()!=edited.GetPixel(x,y).ToArgb();
                        if(!rect.Contains(x,y))Check(!different,"Redaction changed outside selection");else if(different)changed=true;
                    }
                    Check(changed,"Redaction did not apply "+mode);Codec.SaveAtomic(Path.Combine(dir,mode+".png"),Codec.StaticPng(edited));
                }
                using(var edited=ImageTools.Redact(image,Rectangle.Empty,new List<Point>{new Point(20,20),new Point(60,20)},10,12,"solid",Color.Black))
                {
                    Check(edited.GetPixel(40,20).ToArgb()==Color.Black.ToArgb(),"Brush omitted segment between points");
                    Check(edited.GetPixel(40,40).ToArgb()==image.GetPixel(40,40).ToArgb(),"Brush changed outside path");
                }
                using(var tiny=new Bitmap(1,1))using(var edited=ImageTools.Redact(tiny,new Rectangle(0,0,1,1),null,2,100,"blur",Color.Black)){}
                using(var cover=SelfTest.Art(80,80,false))using(var png=Codec.Decode(Batch.Build(cover,image,0,1,false,Color.HotPink)))
                    Check(png.GetPixel(0,0).ToArgb()==Color.HotPink.ToArgb(),"Padding color not used by disguise");
                image.Save(Path.Combine(dir,"photo.jpg"),ImageFormat.Jpeg);
            }
            File.WriteAllText(Path.Combine(dir,"tools-tests.txt"),"PASS: GIF frame count, order, palette, loop option, letterbox and cancellation; mosaic, blur and solid masks preserve outside pixels; brush covers segment; tiny blur; configurable disguise padding color.");
        }
        internal static void CheckCanvasClicks(string dir)
        {
            Directory.CreateDirectory(dir);int clicks=0,positions=0,finished=0;
            using(var canvas=new CoverCanvas{Width=300,Height=220,TextPlacement=true})
            using(var image=new Bitmap(200,120))
            {
                canvas.Click+=delegate{clicks++;};canvas.PositionChanged+=delegate(object sender,TextPositionEventArgs e){positions++;if(e.Finished)finished++;};
                canvas.TestClick();Check(clicks==1,"Empty placement canvas suppressed the image picker click");
                canvas.TestDrag(new Point(30,30),new Point(90,90));Check(positions==0,"Empty placement canvas raised placement events");
                canvas.TextPlacement=false;canvas.TestClick();Check(clicks==2,"Empty ordinary canvas suppressed clicks");
                canvas.Image=image;canvas.TestClick();Check(clicks==3,"Loaded ordinary canvas suppressed clicks");
                canvas.TextPlacement=true;canvas.TestDrag(new Point(100,80),new Point(150,110));canvas.TestClick();
                Check(clicks==3&&positions>=2&&finished==1&&!canvas.PlacementDragging,"Loaded placement drag triggered a picker or failed to finish");
                canvas.Image=null;canvas.TestClick();Check(clicks==4,"Cleared placement canvas remained unclickable");
            }
            string settings=Path.Combine(dir,"canvas-picker.settings");
            for(int kind=1;kind<=3;kind++)using(var page=new FileToolPage(kind,new ExportLocation(settings),new ToolPreferences(settings),delegate{}))page.CheckEmptyCanvasClick();
            File.WriteAllText(Path.Combine(dir,"canvas-click-tests.txt"),"PASS: empty and cleared placement canvases forward actual Click events; loaded placement canvases preserve dragging without picking; ordinary canvas clicks remain active; GIF, double PNG restore, and metadata cleaning empty preview clicks work after queue deselection.");
        }
        static void CheckCoverText(string dir)
        {
            using(var cover=new Bitmap(600,400))using(var real=SelfTest.Art(600,400,true))
            {
                using(var g=Graphics.FromImage(cover))g.Clear(Color.DarkSlateBlue);
                for(int position=0;position<10;position++)
                {
                    var text=new CoverTextOptions {Text="查看原图",Position=position,SizePercent=8,Color=Color.White,X=25,Y=30};
                    byte[] png=Batch.Build(cover,real,0,1,false,Color.White,text);
                    using(var result=Codec.Decode(png))using(var restored=Codec.Decode(Codec.Restore(png)))
                    {
                        int left=600,top=400,right=-1,bottom=-1;
                        for(int y=0;y<400;y++)for(int x=0;x<600;x++)
                        {
                            Check(real.GetPixel(x,y).ToArgb()==restored.GetPixel(x,y).ToArgb(),"Cover text changed hidden original");
                            if(result.GetPixel(x,y).ToArgb()!=cover.GetPixel(x,y).ToArgb()){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
                        }
                        Check(right>left&&bottom>top,"Text did not draw");
                        if(position==0)Check(Math.Abs((left+right)/2-300)<12&&Math.Abs((top+bottom)/2-200)<20,"Centre text misplaced");
                        if(position==1)Check(left>400&&top>330,"Bottom-right text misplaced");
                        if(position==2)Check(Math.Abs((left+right)/2-300)<12&&top>330,"Bottom-centre text misplaced");
                        if(position==3)Check(Math.Abs((left+right)/2-150)<15&&Math.Abs((top+bottom)/2-120)<20,"Free position text misplaced");
                        if(position==4)Check(left<50&&bottom<80,"Top-left text misplaced");
                        if(position==5)Check(Math.Abs((left+right)/2-300)<12&&bottom<80,"Top-centre text misplaced");
                        if(position==6)Check(left>400&&bottom<80,"Top-right text misplaced");
                        if(position==7)Check(left<50&&Math.Abs((top+bottom)/2-200)<20,"Middle-left text misplaced");
                        if(position==8)Check(left>400&&Math.Abs((top+bottom)/2-200)<20,"Middle-right text misplaced");
                        if(position==9)Check(left<50&&top>330,"Bottom-left text misplaced");
                    }
                    Codec.SaveAtomic(Path.Combine(dir,"cover-text-"+position+".png"),png);
                }
                using(var small=Batch.MakeCover(cover,20,20,1,true,Color.White,new CoverTextOptions {Text=new string('字',200),Position=3,X=100,Y=0})){}
            }
        }
    }
}
