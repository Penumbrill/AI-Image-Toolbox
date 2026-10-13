using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace QQImageSwitch
{
    public class BatchItem
    {
        public string Path;
        public string Name;
        public int Width;
        public int Height;
        public bool OwnCover;
        public string CoverPath;
        public string CoverName;
        public string State = "待导出";
    }
    public class BatchProgress
    {
        public int Index;
        public int Completed;
        public int Total;
        public string State;
    }
    public class BatchResult
    {
        public readonly List<string> Files = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public bool Cancelled;
    }
    public static class Batch
    {
        public static string SequenceLabel(int number)
        {
            if(number>=1 && number<=20) return ((char)(0x2460+number-1)).ToString();
            if(number>=21 && number<=35) return ((char)(0x3251+number-21)).ToString();
            if(number>=36 && number<=50) return ((char)(0x32b1+number-36)).ToString();
            return number.ToString();
        }
        public static Bitmap MakeCover(Image cover, int w, int h, int number, bool stamp,Color? background=null,CoverTextOptions text=null,bool scrambleCover=false,CancellationToken token=default(CancellationToken))
        {
            var b=cover==null ? Codec.DefaultCover(w,h) : Codec.Fit(cover,w,h,background??Color.White);
            try
            {
                if(scrambleCover){var mixed=ScrambleParameters.Transform(b,new ScrambleOptions(),false,token);b.Dispose();b=mixed;}
                if(text!=null)text.Draw(b,stamp);
                if(stamp) StampNumber(b,number);
                return b;
            }
            catch{b.Dispose();throw;}
        }
        // Draw the circle ourselves so numbering works even when a font lacks circled glyphs.
        public static void StampNumber(Bitmap b,int number)
        {
            if(number<1) throw new ArgumentOutOfRangeException("number");
            float unit=Math.Min(b.Width,b.Height);
            float diameter=Math.Max(1,Math.Min(unit,Math.Max(24,unit*0.12f)));
            float margin=Math.Min(unit*0.025f,Math.Max(0,(unit-diameter)/2));
            var circle=new RectangleF(b.Width-diameter-margin,b.Height-diameter-margin,diameter,diameter);
            using(var g=Graphics.FromImage(b))
            using(var fill=new SolidBrush(Color.FromArgb(244,255,255,255)))
            using(var pen=new Pen(Color.FromArgb(104,73,209),Math.Max(1,diameter*.035f)))
            using(var ink=new SolidBrush(Color.FromArgb(87,56,188)))
            using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.FillEllipse(fill,circle);
                float border=pen.Width/2;
                g.DrawEllipse(pen,new RectangleF(circle.X+border,circle.Y+border,circle.Width-2*border,circle.Height-2*border));
                string text=number.ToString();
                float fontSize=diameter*.59f;
                while(fontSize>1)
                {
                    using(var probe=new Font("Microsoft YaHei UI",fontSize,FontStyle.Bold,GraphicsUnit.Pixel))
                        if(g.MeasureString(text,probe).Width<diameter*.83f) break;
                    fontSize*=.9f;
                }
                using(var font=new Font("Microsoft YaHei UI",Math.Max(1,fontSize),FontStyle.Bold,GraphicsUnit.Pixel))
                    g.DrawString(text,font,ink,circle,format);
            }
        }
        public static byte[] Build(Image cover,Image real,int maxSide,int number,bool stamp,Color? background=null,CoverTextOptions text=null,CancellationToken token=default(CancellationToken),bool scrambleCover=false)
        {
            token.ThrowIfCancellationRequested();Size size=Codec.OutputSize(real,maxSide);
            using(var r=Codec.Fit(real,size.Width,size.Height,Color.Transparent))
            using(var c=MakeCover(cover,size.Width,size.Height,number,stamp,background,text,scrambleCover,token))
                return Codec.Encode(c,r);
        }
        public static string UniqueOutput(string folder,int number,string originalName)
        {
            string leaf=System.IO.Path.GetFileNameWithoutExtension(originalName);
            foreach(char invalid in System.IO.Path.GetInvalidFileNameChars()) leaf=leaf.Replace(invalid,'_');
            leaf=leaf.Trim().TrimEnd('.');
            if(leaf.Length>70)leaf=leaf.Substring(0,70);
            if(leaf.Length==0)leaf="图片";
            string stem=number.ToString("D3")+"_"+leaf+"_双图";
            string target=System.IO.Path.Combine(folder,stem+".png");int suffix=2;
            while(File.Exists(target)||Directory.Exists(target))target=System.IO.Path.Combine(folder,stem+"_"+(suffix++)+".png");
            return target;
        }
        public static BatchResult Export(IList<BatchItem> items,Image cover,string folder,int maxSide,bool stamp,CancellationToken token,IProgress<BatchProgress> progress,Color? background=null,CoverTextOptions text=null,bool scrambleCover=false)
        {
            Directory.CreateDirectory(folder);
            var result=new BatchResult();
            for(int i=0;i<items.Count;i++)
            {
                if(token.IsCancellationRequested){result.Cancelled=true;break;}
                string state;
                try
                {
                    using(var real=Codec.Load(items[i].Path))
                    using(var localCover=items[i].OwnCover && items[i].CoverPath!=null ? Codec.Load(items[i].CoverPath) : null)
                    {
                        Image effectiveCover=items[i].OwnCover ? localCover : cover;
                        byte[] png=Build(effectiveCover,real,maxSide,i+1,stamp,background,text,token,scrambleCover);
                        if(token.IsCancellationRequested){result.Cancelled=true;break;}
                        string output=UniqueOutput(folder,i+1,items[i].Name);
                        Codec.SaveAtomic(output,png);result.Files.Add(output);state="已导出";
                    }
                }
                catch(OperationCanceledException){result.Cancelled=true;break;}
                catch(Exception ex){state="失败";result.Errors.Add((i+1)+". "+items[i].Name+"："+ex.Message);}
                if(progress!=null)progress.Report(new BatchProgress {Index=i,Completed=i+1,Total=items.Count,State=state});
            }
            return result;
        }
    }
}
