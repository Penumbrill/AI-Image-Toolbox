// SPDX-License-Identifier: BSD-2-Clause (Gilbert traversal portion)
// Copyright (c) 2018, Jakub Červený. See THIRD_PARTY_NOTICES.md.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace QQImageSwitch
{
    // UI-independent Gilbert permutation. Port of jakubcerveny/gilbert (BSD-2-Clause).
    public static class Obfuscation
    {
        static readonly byte[] Magic=Encoding.ASCII.GetBytes("AIPTSCR1");
        public sealed class Record
        {
            public int Schema {get;set;}
            public string Name {get;set;}
            public long ImageLength {get;set;}
            public long Length {get;set;}
            public string Sha256 {get;set;}
            public string ImageSha256 {get;set;}
        }
        internal static Bitmap Load(string path)
        {
            if(new FileInfo(path).Length<=160L*1024*1024)return Codec.Load(path);
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {var record=ReadRecord(stream);if(record.ImageLength>160L*1024*1024||record.Length>160L*1024*1024)throw new InvalidDataException("输入文件超过 160 MB。");}
            using(var image=Image.FromFile(path)){Codec.CheckSize(image.Width,image.Height);return ((Bitmap)image).Clone(new Rectangle(0,0,image.Width,image.Height),PixelFormat.Format32bppArgb);}
        }
        public static int[] Curve(int width,int height,CancellationToken token)
        {
            Codec.CheckSize(width,height);var map=new int[checked(width*height)];int index=0;
            if(width>=height)Generate(0,0,width,0,0,height,width,map,ref index,token);
            else Generate(0,0,0,height,width,0,width,map,ref index,token);
            if(index!=map.Length)throw new InvalidDataException("Invalid curve length");return map;
        }
        static int Half(int n){return n>>1;} // JS Math.floor, including negative odd coordinates.
        static void Generate(int x,int y,int ax,int ay,int bx,int by,int width,int[] map,ref int index,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();int w=Math.Abs(ax+ay),h=Math.Abs(bx+by),dax=Math.Sign(ax),day=Math.Sign(ay),dbx=Math.Sign(bx),dby=Math.Sign(by);
            if(h==1){for(int i=0;i<w;i++){map[index++]=x+y*width;x+=dax;y+=day;}return;}
            if(w==1){for(int i=0;i<h;i++){map[index++]=x+y*width;x+=dbx;y+=dby;}return;}
            int ax2=Half(ax),ay2=Half(ay),bx2=Half(bx),by2=Half(by),w2=Math.Abs(ax2+ay2),h2=Math.Abs(bx2+by2);
            if(2*w>3*h)
            {
                if(w2%2!=0&&w>2){ax2+=dax;ay2+=day;}
                Generate(x,y,ax2,ay2,bx,by,width,map,ref index,token);
                Generate(x+ax2,y+ay2,ax-ax2,ay-ay2,bx,by,width,map,ref index,token);
            }
            else
            {
                if(h2%2!=0&&h>2){bx2+=dbx;by2+=dby;}
                Generate(x,y,bx2,by2,ax2,ay2,width,map,ref index,token);
                Generate(x+bx2,y+by2,ax,ay,bx-bx2,by-by2,width,map,ref index,token);
                Generate(x+ax-dax+bx2-dbx,y+ay-day+by2-dby,-bx2,-by2,-(ax-ax2),-(ay-ay2),width,map,ref index,token);
            }
        }
        public static Bitmap Transform(Bitmap source,bool reverse,CancellationToken token)
        {
            var map=Curve(source.Width,source.Height,token);var pixels=new int[map.Length];
            var rect=new Rectangle(0,0,source.Width,source.Height);var locked=source.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{for(int y=0;y<source.Height;y++)Marshal.Copy(IntPtr.Add(locked.Scan0,y*locked.Stride),pixels,y*source.Width,source.Width);}finally{source.UnlockBits(locked);}
            int offset=(int)Math.Floor((Math.Sqrt(5)-1)/2*map.Length+.5)%map.Length;if(reverse&&offset!=0)offset=map.Length-offset;
            // Rotate permutation cycles in place: retain two pixel-sized arrays
            // instead of three, without changing the existing image algorithm.
            int divisor=map.Length,step=offset;while(step!=0){int remainder=divisor%step;divisor=step;step=remainder;}int moved=0;
            for(int start=0;start<divisor;start++)
            {
                int current=start,saved=pixels[map[start]];
                do
                {
                    if((moved++&65535)==0)token.ThrowIfCancellationRequested();int next=(current+offset)%map.Length,index=map[next],old=pixels[index];pixels[index]=saved;saved=old;current=next;
                }while(current!=start);
            }
            var bitmap=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
            locked=bitmap.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try{for(int y=0;y<source.Height;y++)Marshal.Copy(pixels,y*source.Width,IntPtr.Add(locked.Scan0,y*locked.Stride),source.Width);}finally{bitmap.UnlockBits(locked);}
            return bitmap;
        }
        static void Encode(Bitmap image,Stream stream,int quality,CancellationToken token,bool black=false)
        {
            if(quality<=0){LosslessPng.WriteBest(image,stream,token);return;}
            if(quality>100)throw new ArgumentOutOfRangeException("quality");
            using(var white=new Bitmap(image.Width,image.Height,PixelFormat.Format24bppRgb))
            {
                using(var g=Graphics.FromImage(white)){g.Clear(black?Color.Black:Color.White);g.DrawImageUnscaled(image,0,0);}
                var encoder=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);
                using(var p=new EncoderParameters(1)){p.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)quality);white.Save(stream,encoder,p);}
            }
        }
        // quality: -1 includes original bytes, 0 lossless pixels, 1..100 JPEG.
        public static string WriteReference(string input,string output,bool reverse,int quality,CancellationToken token,ScrambleOptions options)
        {
            if(quality<0||quality>100)throw new ArgumentOutOfRangeException("quality");
            var effective=options.Copy();effective.Algorithm=0;
            using(var source=Load(input))using(var result=ScrambleParameters.Transform(source,effective,reverse,token))
                return WriteTransformed(input,output,result,quality,token,null,true);
        }
        public static string Write(string input,string output,bool reverse,int quality,CancellationToken token)
        { return Write(input,output,reverse,quality,token,null,false); }
        public static string Write(string input,string output,bool reverse,int quality,CancellationToken token,ScrambleOptions options,bool automatic)
        {
            if(quality < -1||quality>100||(reverse&&quality<0))throw new ArgumentOutOfRangeException("quality");
            ScrambleOptions effective=options==null?null:options.Copy();
            if(reverse&&automatic){var stored=ScrambleParameters.Read(input);if(stored!=null){stored.Key=effective==null?"":effective.Key;effective=stored;}}
            using(var source=Load(input))
            {
                if(reverse&&effective!=null&&effective.Width>0&&(effective.Width!=source.Width||effective.Height!=source.Height))throw new InvalidDataException("混淆图片尺寸已改变 / Scrambled image dimensions changed");
                using(var transformed=effective==null?Transform(source,reverse,token):ScrambleParameters.Transform(source,effective,reverse,token))
                {
                    if(effective!=null){effective.Width=source.Width;effective.Height=source.Height;}
                    return WriteTransformed(input,output,transformed,quality,token,reverse?null:effective);
                }
            }
        }
        public static string WriteTransformed(string input,string output,Bitmap transformed,int quality,CancellationToken token)
        { return WriteTransformed(input,output,transformed,quality,token,null); }
        public static string WriteTransformed(string input,string output,Bitmap transformed,int quality,CancellationToken token,ScrambleOptions options,bool black=false)
        {
            if(quality < -1||quality>100)throw new ArgumentOutOfRangeException("quality");
            output=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(output));
            string temp=Path.Combine(Path.GetDirectoryName(output),".scramble-"+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                using(var target=new FileStream(temp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,1048576))
                {
                    token.ThrowIfCancellationRequested();Encode(transformed,target,quality,token,black);token.ThrowIfCancellationRequested();
                    if(options!=null)ScrambleParameters.Append(target,options);
                    if(quality==-1)
                    {
                        long imageLength=target.Position;target.Position=0;string imageHash=HashRange(target,imageLength,null,token);
                        using(var source=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read,1048576))
                        {
                            CheckSpace(output,source.Length+4096);target.Position=imageLength;
                            var r=new Record{Schema=1,Name=Path.GetFileName(input),ImageLength=imageLength,Length=source.Length,ImageSha256=imageHash};
                            r.Sha256=HashRange(source,source.Length,target,token);byte[] metadata=Encoding.UTF8.GetBytes(Inspection.Json().Serialize(r));
                            target.Write(metadata,0,metadata.Length);byte[] length=BitConverter.GetBytes(metadata.Length);target.Write(length,0,4);target.Write(Magic,0,Magic.Length);
                        }
                    }
                    target.Flush(true);
                }
                token.ThrowIfCancellationRequested();File.Move(temp,output);return output;
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        static void CheckSpace(string output,long needed)
        {
            var root=Path.GetPathRoot(Path.GetFullPath(output));var drive=new DriveInfo(root);
            if(drive.IsReady&&drive.AvailableFreeSpace<needed)throw new IOException("磁盘空间不足。");
        }
        static string HashRange(Stream input,long count,Stream output,CancellationToken token)
        {
            using(var sha=SHA256.Create())
            {
                var buffer=new byte[1048576];while(count>0){token.ThrowIfCancellationRequested();int n=input.Read(buffer,0,(int)Math.Min(count,buffer.Length));if(n==0)throw new InvalidDataException("混淆文件已截断。");sha.TransformBlock(buffer,0,n,null,0);if(output!=null)output.Write(buffer,0,n);count-=n;}
                sha.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
            }
        }
        static Record ReadRecord(Stream stream)
        {
            if(stream.Length<12)throw new InvalidDataException("此图片没有原文件记录；请使用解混淆。");
            stream.Position=stream.Length-12;var tail=new byte[12];if(stream.Read(tail,0,12)!=12||!tail.Skip(4).SequenceEqual(Magic))throw new InvalidDataException("此图片没有原文件记录；请使用解混淆。");
            int length=BitConverter.ToInt32(tail,0);if(length<2||length>8192||length>stream.Length-12)throw new InvalidDataException("混淆文件记录损坏。");
            long start=stream.Length-12-length;stream.Position=start;var data=new byte[length];if(stream.Read(data,0,length)!=length)throw new InvalidDataException("混淆文件已截断。");
            Record r;try{r=Inspection.Json().Deserialize<Record>(Encoding.UTF8.GetString(data));}catch(Exception ex){throw new InvalidDataException("混淆文件记录损坏。",ex);}
            if(r==null||r.Schema!=1||r.ImageLength<8||r.Length<1||r.ImageLength>start||r.Length!=start-r.ImageLength||string.IsNullOrEmpty(r.Sha256)||r.Sha256.Length!=64||string.IsNullOrEmpty(r.ImageSha256)||r.ImageSha256.Length!=64)throw new InvalidDataException("混淆文件长度或校验记录有误。");
            if(string.IsNullOrWhiteSpace(r.Name)||Path.GetFileName(r.Name)!=r.Name||r.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||r.Name.EndsWith(".")||r.Name.EndsWith(" "))throw new InvalidDataException("原文件名无效。");
            return r;
        }
        public static string RestoreOriginal(string input,string folder,CancellationToken token)
        {
            folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);string temp=Path.Combine(folder,".scramble-"+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                string output;using(var source=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read,1048576))
                {
                    var r=ReadRecord(source);CheckSpace(Path.Combine(folder,r.Name),r.Length);source.Position=0;
                    if(HashRange(source,r.ImageLength,null,token)!=r.ImageSha256)throw new InvalidDataException("混淆图片已被修改，无法校验原文件。");
                    using(var target=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1048576))
                    {if(HashRange(source,r.Length,target,token)!=r.Sha256)throw new InvalidDataException("原文件校验失败，数据已损坏。");target.Flush(true);}
                    output=FileDisguise.RestoredName(folder,r.Name);
                }
                token.ThrowIfCancellationRequested();File.Move(temp,output);return output;
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
