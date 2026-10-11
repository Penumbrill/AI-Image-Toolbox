using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace QQImageSwitch
{
    static class SelfTest
    {
        static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        public static Bitmap Art(int w,int h,bool hidden)
        {
            var b=new Bitmap(w,h,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(b))
            {
                g.Clear(hidden?Color.FromArgb(25,34,82):Color.FromArgb(238,200,166));
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var gradient=new LinearGradientBrush(new Rectangle(0,0,w,h),
                    hidden?Color.FromArgb(25,34,82):Color.FromArgb(238,200,166),
                    hidden?Color.FromArgb(96,76,161):Color.FromArgb(255,236,199),90f))g.FillRectangle(gradient,0,0,w,h);
                using(var sky=new SolidBrush(hidden?Color.FromArgb(244,234,185):Color.FromArgb(245,153,89)))
                    g.FillEllipse(sky,w*.57f,h*.15f,w*.22f,w*.22f);
                if(hidden)
                {
                    using(var shade=new SolidBrush(Color.FromArgb(45,43,100)))g.FillEllipse(shade,w*.63f,h*.105f,w*.22f,w*.22f);
                    for(int i=0;i<22;i++)g.FillEllipse(Brushes.White,(i*117+49)%w,(i*73+21)%(h/2),3,3);
                }
                using(var mountain=new SolidBrush(hidden?Color.FromArgb(71,63,122):Color.FromArgb(142,165,151)))
                    g.FillPolygon(mountain,new PointF[]{new PointF(0,h*.85f),new PointF(w*.3f,h*.36f),new PointF(w*.64f,h*.86f),new PointF(w*.82f,h*.45f),new PointF(w,h*.73f),new PointF(w,h),new PointF(0,h)});
                using(var foreground=new SolidBrush(hidden?Color.FromArgb(40,39,83):Color.FromArgb(102,137,124)))
                    g.FillPolygon(foreground,new PointF[]{new PointF(0,h*.82f),new PointF(w*.48f,h*.61f),new PointF(w,h*.9f),new PointF(w,h),new PointF(0,h)});
                using(var title=new Font("Microsoft YaHei UI",w/28f,FontStyle.Bold,GraphicsUnit.Pixel))
                {
                    g.DrawString(hidden?"另一张图 · 月光":"聊天封面 · 日光",title,hidden?Brushes.White:Brushes.DarkSlateGray,new PointF(w*.06f,h*.08f));
                }
            }
            return b;
        }
        public static void Run(string dir)
        {
            QueueWheelTests.Run();
            Directory.CreateDirectory(dir);QrTests.Run(dir);QrAiTests.Run(dir);QrAiProgressTests.Run(dir);
            BackgroundRevealTests.Run(dir);
            DualBlendTests.Run(dir);
            using(var c=Art(240,160,false))
            using(var r=Art(240,160,true))
            {
                // Exercise alpha and exact pixel recovery, including completely transparent pixels.
                r.SetPixel(0,0,Color.FromArgb(0,0,0,0));r.SetPixel(1,0,Color.FromArgb(128,80,90,100));
                Codec.SaveAtomic(Path.Combine(dir,"cover.png"),Codec.StaticPng(c));
                Codec.SaveAtomic(Path.Combine(dir,"real.png"),Codec.StaticPng(r));
                byte[] png=Codec.Encode(c,r);Codec.SaveAtomic(Path.Combine(dir,"double.png"),png);
                byte[] restored=Codec.Restore(png);Codec.SaveAtomic(Path.Combine(dir,"restored.png"),restored);
                using(var front=Codec.Decode(png))using(var back=Codec.Decode(restored))
                {
                    for(int y=0;y<r.Height;y++)for(int x=0;x<r.Width;x++)
                    {
                        Assert(front.GetPixel(x,y).ToArgb()==c.GetPixel(x,y).ToArgb(),"Static cover pixel mismatch");
                        Assert(back.GetPixel(x,y).ToArgb()==r.GetPixel(x,y).ToArgb(),"Hidden pixel mismatch");
                    }
                }
                byte[] corrupt=(byte[])png.Clone();corrupt[50]^=1;bool rejected=false;
                try {Codec.Restore(corrupt);}catch(InvalidDataException){rejected=true;}
                Assert(rejected,"Corrupt PNG was accepted");
                rejected=false;try {Codec.Restore(Codec.StaticPng(c));}catch(InvalidDataException){rejected=true;}
                Assert(rejected,"Ordinary PNG was accepted as disguised PNG");
                var small=Codec.OutputSize(r,100);Assert(small.Width==100&&small.Height==67,"Aspect ratio failed");
                using(var fitted=Codec.Fit(c,120,240,Color.White))
                {Assert(fitted.GetPixel(0,0).ToArgb()==Color.White.ToArgb(),"Letterbox failed");Codec.SaveAtomic(Path.Combine(dir,"fitted.png"),Codec.StaticPng(fitted));}
                // Replacing existing output exercises atomic replacement.
                Codec.SaveAtomic(Path.Combine(dir,"double.png"),png);
                using(var one=new Bitmap(1,1,PixelFormat.Format32bppArgb))
                    Assert(Codec.Restore(Codec.Encode(one,one)).Length>0,"One-pixel hidden image failed");
            }
            File.WriteAllText(Path.Combine(dir,"self-test.txt"),"PASS: exact cover and hidden pixels, alpha, checksum rejection, ordinary PNG rejection, resize, letterbox, atomic replacement, 1x1 image.\r\n",Encoding.UTF8);
        }
    }
}
