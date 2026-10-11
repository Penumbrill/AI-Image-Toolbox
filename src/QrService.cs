using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace QQImageSwitch
{
    static class QrDependencies
    {
        static Assembly dependency;
        public static void Register(){AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){if(new AssemblyName(e.Name).Name!="zxing")return null;if(dependency==null)using(var stream=typeof(QrDependencies).Assembly.GetManifestResourceStream("ZxingAssembly"))using(var buffer=new MemoryStream()){stream.CopyTo(buffer);dependency=Assembly.Load(buffer.ToArray());}return dependency;};}
    }
    public sealed class QrRegion
    {
        public string Url;public int X,Y,Size,Width,Height;public int Margin=4;public bool Raw;
    }
    public static class QrService
    {
        public static string Website(string value)
        {
            Uri url;value=(value??"").Trim();if(!Uri.TryCreate(value,UriKind.Absolute,out url)||!(url.Scheme=="https"||url.Scheme=="http")||string.IsNullOrEmpty(url.Host)||!string.IsNullOrEmpty(url.UserInfo))throw new ArgumentException("请输入完整的 HTTP 或 HTTPS 网站链接。");
            if(Encoding.UTF8.GetByteCount(url.AbsoluteUri)>1100)throw new ArgumentException("链接过长，请使用网站提供的较短链接。");return url.AbsoluteUri;
        }
        public static string Payload(string value,bool raw=false)
        {
            if(!raw)return Website(value);
            if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("二维码内容不能为空。");
            // Imported content is inert data. Preserve whitespace and URL spelling;
            // opening a website always uses the separate strict Website validator.
            foreach(char c in value)if(char.IsControl(c)&&c!='\r'&&c!='\n'&&c!='\t')throw new ArgumentException("二维码包含不支持的控制字符，无法按文本重建。");
            try{if(new UTF8Encoding(false,true).GetByteCount(value)>1100)throw new ArgumentException("二维码内容过长，请使用较短内容。");}
            catch(EncoderFallbackException){throw new ArgumentException("二维码内容不是有效的 Unicode 文本。");}
            return value;
        }
        public static Bitmap Code(string value,int side,int margin=4,bool raw=false)
        {
            if(margin<0||margin>12)throw new ArgumentOutOfRangeException("margin");string url=Payload(value,raw);var writer=new BarcodeWriter{Format=BarcodeFormat.QR_CODE,Options=new ZXing.QrCode.QrCodeEncodingOptions{Width=side,Height=side,Margin=margin,ErrorCorrection=ErrorCorrectionLevel.H,CharacterSet="UTF-8",DisableECI=true}};
            return writer.Write(url);
        }
        static int Modules(string url,int margin=4){return new ZXing.QrCode.QRCodeWriter().encode(url,BarcodeFormat.QR_CODE,1,1,new Dictionary<EncodeHintType,object>{{EncodeHintType.ERROR_CORRECTION,ErrorCorrectionLevel.H},{EncodeHintType.CHARACTER_SET,"UTF-8"},{EncodeHintType.MARGIN,margin},{EncodeHintType.DISABLE_ECI,true}}).Width;}
        public static string Scan(Bitmap source)
        {
            var reader=new BarcodeReader{AutoRotate=true,Options=new DecodingOptions{TryHarder=true,TryInverted=true,PossibleFormats=new[]{BarcodeFormat.QR_CODE}}};
            // Local thresholding is sensitive to module size in textured artwork.
            // Bound allocations for large inputs and retry ordinary display sizes.
            int longest=Math.Max(source.Width,source.Height),previous=0;
            foreach(int limit in new[]{3200,1024,800,512})
            {
                int edge=Math.Min(longest,limit);if(edge==previous)continue;previous=edge;
                double ratio=edge/(double)longest;int width=Math.Max(1,(int)(source.Width*ratio)),height=Math.Max(1,(int)(source.Height*ratio));
                // Transparent composites are tried against both actual backgrounds.
                foreach(bool white in new[]{true,false})using(var flat=Codec.Fit(source,width,height,white?Color.White:Color.Black)){var result=reader.Decode(flat);if(result!=null)return result.Text;}
            }
            return null;
        }
        public static string[] ScanAll(Bitmap source,CancellationToken token=default(CancellationToken))
        {
            if(source==null)throw new ArgumentNullException("source");token.ThrowIfCancellationRequested();
            var found=new List<string>();var unique=new HashSet<string>(StringComparer.Ordinal);
            var reader=new BarcodeReader{AutoRotate=true,Options=new DecodingOptions{TryHarder=true,TryInverted=true,PossibleFormats=new[]{BarcodeFormat.QR_CODE}}};
            int longest=Math.Max(source.Width,source.Height),previous=0;
            foreach(int limit in new[]{3200,1024,800,512})
            {
                int edge=Math.Min(longest,limit);if(edge==previous)continue;previous=edge;double ratio=edge/(double)longest;
                int width=Math.Max(1,(int)(source.Width*ratio)),height=Math.Max(1,(int)(source.Height*ratio));
                foreach(bool white in new[]{true,false})using(var flat=Codec.Fit(source,width,height,white?Color.White:Color.Black))
                {
                    token.ThrowIfCancellationRequested();var results=reader.DecodeMultiple(flat);token.ThrowIfCancellationRequested();
                    if(results==null){var single=reader.Decode(flat);token.ThrowIfCancellationRequested();if(single!=null)results=new[]{single};}
                    if(results!=null)foreach(var result in results)if(result.Text!=null&&unique.Add(result.Text))found.Add(result.Text);
                }
            }
            foreach(string value in QrImportRecovery.Recover(source,token))if(unique.Add(value))found.Add(value);
            return found.ToArray();
        }
        public static Bitmap Compose(Image image,string value,int side,int percent,int x,int y,CoverTextOptions text,out QrRegion region,int margin=4,bool raw=false)
        {
            string url=Payload(value,raw);if(margin<0||margin>12)throw new ArgumentOutOfRangeException("margin");if(image==null){int count=Modules(url,margin),tile=Math.Max(4,side/count)*count;region=new QrRegion{Url=url,Size=tile,Width=tile,Height=tile,Margin=margin,Raw=raw};return Code(url,tile,margin,raw);}Bitmap result=image==null?new Bitmap(side,side):Codec.Fit(image,image.Width,image.Height,Color.White);
            try
            {
                if(image==null)using(var g=Graphics.FromImage(result))g.Clear(Color.White);
                if(text!=null)text.Draw(result,false);int shortEdge=Math.Min(result.Width,result.Height),modules=Modules(url,margin),requested=image==null?shortEdge*3/4:shortEdge*Math.Max(10,Math.Min(90,percent))/100;
                requested=shortEdge*Math.Max(10,Math.Min(90,percent))/100;int scale=Math.Max(4,requested/modules),tile=modules*scale;if(tile>shortEdge)throw new ArgumentException("图片太小，二维码至少需要每格 4 像素；请使用更大图片。");
                int left=Math.Max(0,Math.Min(result.Width-tile,(int)Math.Round(result.Width*x/100.0-tile/2.0))),top=Math.Max(0,Math.Min(result.Height-tile,(int)Math.Round(result.Height*y/100.0-tile/2.0)));
                using(var qr=Code(url,tile,margin,raw))using(var g=Graphics.FromImage(result)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(qr,left,top);}
                region=new QrRegion{Url=url,X=left,Y=top,Size=tile,Width=result.Width,Height=result.Height,Margin=margin,Raw=raw};return result;
            }catch{result.Dispose();throw;}
        }
        public static byte[] Tag(byte[] png,QrRegion region)
        {
            if(region==null)return png;byte[] data=Encoding.UTF8.GetBytes(Inspection.Json().Serialize(region));using(var output=new MemoryStream()){output.Write(png,0,8);foreach(var c in Codec.Parse(png)){if(c.Type=="qrLK")continue;if(c.Type=="IEND")Codec.WriteChunk(output,"qrLK",data);Codec.WriteChunk(output,c.Type,c.Data);}return output.ToArray();}
        }
        public static QrRegion Read(string path)
        {
            if(!string.Equals(Path.GetExtension(path),".png",StringComparison.OrdinalIgnoreCase))return null;
            try{using(var f=File.OpenRead(path))using(var reader=new BinaryReader(f)){
                if(!reader.ReadBytes(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return null;
                int width=0,height=0;while(f.Position<=f.Length-12){byte[] head=reader.ReadBytes(8);uint n=Codec.Read32(head,0);if((long)n+4>f.Length-f.Position)return null;string type=Encoding.ASCII.GetString(head,4,4);
                    if(type=="IHDR"&&n==13){byte[] d=reader.ReadBytes(13);width=(int)Codec.Read32(d,0);height=(int)Codec.Read32(d,4);f.Position+=4;continue;}
                    if(type=="qrLK"){if(n>8192)return null;byte[] d=reader.ReadBytes((int)n),crc=reader.ReadBytes(4);byte[] content=Encoding.ASCII.GetBytes(type).Concat(d).ToArray();if(Codec.Crc(content,0,content.Length)!=Codec.Read32(crc,0))return null;var r=Inspection.Json().Deserialize<QrRegion>(Encoding.UTF8.GetString(d));if(r==null)return null;Payload(r.Url,r.Raw);if(r.Margin<0||r.Margin>12||r.Width!=width||r.Height!=height||width<1||height<1||width>8192||height>8192||r.Size<1||r.X<0||r.Y<0||(long)r.X+r.Size>width||(long)r.Y+r.Size>height)return null;return r;}
                    if(type=="IEND")return null;f.Position+=(long)n+4;}
                return null;}}
            catch(IOException){return null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
        }
        public static int OptimizePixel(int cover,bool ink,BackgroundRevealAuto.Profile profile)
        {
            double luma=(((cover>>16)&255)*299+((cover>>8)&255)*587+(cover&255)*114)/1000.0;int lo=profile.WhiteLow,hi=profile.WhiteHigh;if(hi-lo<16){lo=0;hi=255;}
            int white=Math.Max(181,Math.Min(255,(int)Math.Floor(profile.WhiteFloor+(250-profile.WhiteFloor)*Math.Max(0,Math.Min(1,(luma-lo)/(hi-lo)))+.5))),black=ink?16:112,alpha=(int)Math.Floor(255*(1-(white-black)/250.0)+.5),channel=alpha==0?0:(int)Math.Floor(black*255.0/alpha+.5);
            return alpha<<24|channel<<16|channel<<8|channel;
        }
        public static QrRegion Optimize(Bitmap result,QrRegion input,Image cover,BackgroundRevealAuto.Profile profile,CancellationToken token,bool strict=true)
        {
            token.ThrowIfCancellationRequested();
            if(input==null||cover==null||profile==null)throw new ArgumentException("二维码优化需要本工具生成的二维码 PNG；请勿先清理元数据。");double sx=result.Width/(double)input.Width,sy=result.Height/(double)input.Height;int modules=Modules(input.Url,input.Margin),side=(int)Math.Round(input.Size*Math.Min(sx,sy));int scale=side/modules;
            if(strict&&scale<4)throw new ArgumentException("导出尺寸太小，二维码无法保持可扫描；请提高导出尺寸。");scale=Math.Max(1,scale);side=scale*modules;if(side>Math.Min(result.Width,result.Height))throw new ArgumentException("导出尺寸太小，二维码无法保持可扫描；请提高导出尺寸。");
            int box=Math.Max(side,(int)Math.Ceiling(input.Size*Math.Max(sx,sy))),x=Math.Max(0,Math.Min(result.Width-box,(int)Math.Floor(input.X*sx))),y=Math.Max(0,Math.Min(result.Height-box,(int)Math.Floor(input.Y*sy)));
            if(box>Math.Min(result.Width,result.Height))throw new ArgumentException("导出尺寸太小，二维码无法保持可扫描；请提高导出尺寸。");
            using(var qr=Code(input.Url,side,input.Margin,input.Raw))using(var fitted=Codec.Fit(cover,result.Width,result.Height,Color.White))
            {
                // Calibrate the whole grayscale appearance to near-white (250),
                // minimizing code contrast across common 245..255 chat backgrounds.
                var adjusted=result.LockBits(new Rectangle(0,0,result.Width,result.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
                try{int[] line=new int[result.Width];for(int py=0;py<result.Height;py++){token.ThrowIfCancellationRequested();Marshal.Copy(IntPtr.Add(adjusted.Scan0,py*adjusted.Stride),line,0,line.Length);for(int px=0;px<line.Length;px++){int value=line[px],a=(int)((uint)value>>24),black=(((value>>16)&255)*a+127)/255,white=black+255-a,alpha=(int)Math.Max(0,Math.Min(255,Math.Floor(255*(1-(white-black)/250.0)+.5))),channel=alpha==0?0:Math.Min(255,(int)Math.Floor(black*255.0/alpha+.5));line[px]=alpha<<24|channel<<16|channel<<8|channel;}Marshal.Copy(line,0,IntPtr.Add(adjusted.Scan0,py*adjusted.Stride),line.Length);}}finally{result.UnlockBits(adjusted);}
                BitmapData rd=null,cd=null,qd=null;try{rd=result.LockBits(new Rectangle(0,0,result.Width,result.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);cd=fitted.LockBits(new Rectangle(0,0,fitted.Width,fitted.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);qd=qr.LockBits(new Rectangle(0,0,side,side),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);int[] row=new int[box],coverRow=new int[box],codeRow=new int[side];int inset=(box-side)/2;for(int py=0;py<box;py++){token.ThrowIfCancellationRequested();Marshal.Copy(IntPtr.Add(cd.Scan0,(y+py)*cd.Stride+x*4),coverRow,0,box);bool inside=py>=inset&&py<inset+side;if(inside)Marshal.Copy(IntPtr.Add(qd.Scan0,(py-inset)*qd.Stride),codeRow,0,side);for(int px=0;px<box;px++){bool ink=inside&&px>=inset&&px<inset+side&&(codeRow[px-inset]&0xffffff)==0;row[px]=OptimizePixel(coverRow[px],ink,profile);}Marshal.Copy(row,0,IntPtr.Add(rd.Scan0,(y+py)*rd.Stride+x*4),box);}}finally{if(rd!=null)result.UnlockBits(rd);if(cd!=null)fitted.UnlockBits(cd);if(qd!=null)qr.UnlockBits(qd);}
            }
            if(strict){using(var black=DualBlend.Render(result,false,token))if(Scan(black)!=Payload(input.Url,input.Raw))throw new InvalidOperationException("二维码优化自检失败，请增大二维码或导出尺寸。");}
            return new QrRegion{Url=input.Url,X=x,Y=y,Size=box,Width=result.Width,Height=result.Height,Margin=input.Margin,Raw=input.Raw};
        }
    }
}
