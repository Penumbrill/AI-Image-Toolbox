using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Linq;

namespace QQImageSwitch
{
    public static class QrArt
    {
        public static Bitmap Compose(Image image,string url,int percent,int x,int y,CoverTextOptions text,out QrRegion region,int margin=4,CancellationToken token=default(CancellationToken),bool raw=false)
        {
            if(image==null)throw new ArgumentException("请先选择底图。");
            margin=Math.Max(4,margin);
            using(var scaffold=QrService.Compose(image,url,1024,percent,x,y,text,out region,margin,raw))
            using(var source=Codec.Fit(image,image.Width,image.Height,Color.White))
            using(var code=QrService.Code(url,region.Size,margin,raw))
            {
                if(text!=null)text.Draw(source,false);
                int modules=new ZXing.QrCode.QRCodeWriter().encode(region.Url,ZXing.BarcodeFormat.QR_CODE,1,1,new System.Collections.Generic.Dictionary<ZXing.EncodeHintType,object>{{ZXing.EncodeHintType.ERROR_CORRECTION,ZXing.QrCode.Internal.ErrorCorrectionLevel.H},{ZXing.EncodeHintType.CHARACTER_SET,"UTF-8"},{ZXing.EncodeHintType.MARGIN,margin},{ZXing.EncodeHintType.DISABLE_ECI,true}}).Width;
                int scale=region.Size/modules;
                for(int pass=0;pass<3;pass++)
                {
                    token.ThrowIfCancellationRequested();
                    Bitmap output=(Bitmap)source.Clone();
                    try
                    {
                        Blend(output,code,region,modules,scale,pass,token);
                        if(QrService.Scan(output)==region.Url&&ReducedScan(output)==region.Url&&CompressedScan(output)==region.Url)return output;
                    }
                    catch{output.Dispose();throw;}
                    output.Dispose();
                }
                throw new InvalidOperationException("艺术二维码自检失败，请增大二维码或改用图片附上二维码。");
            }
        }
        static string ReducedScan(Bitmap image)
        {
            double ratio=Math.Min(1,900.0/Math.Max(image.Width,image.Height));
            using(var small=Codec.Fit(image,Math.Max(1,(int)(image.Width*ratio)),Math.Max(1,(int)(image.Height*ratio)),Color.White))return QrService.Scan(small);
        }
        static string CompressedScan(Bitmap image)
        {
            using(var data=new MemoryStream())using(var parameters=new EncoderParameters(1))
            {
                parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,90L);
                image.Save(data,ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid),parameters);data.Position=0;
                using(var decoded=new Bitmap(data))return QrService.Scan(decoded);
            }
        }
        static bool Function(int x,int y,int n)
        {
            return (x<9&&y<9)||(x>=n-8&&y<9)||(x<9&&y>=n-8)||x==6||y==6;
        }
        static void Blend(Bitmap output,Bitmap code,QrRegion r,int modules,int scale,int pass,CancellationToken token)
        {
            BitmapData target=null,qr=null;
            try
            {
                target=output.LockBits(new Rectangle(0,0,output.Width,output.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
                qr=code.LockBits(new Rectangle(0,0,code.Width,code.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                int[] pixels=new int[r.Size],mask=new int[r.Size];double core=pass==0?.48:pass==1?.72:1;
                for(int py=0;py<r.Size;py++)
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.Copy(IntPtr.Add(target.Scan0,(r.Y+py)*target.Stride+r.X*4),pixels,0,pixels.Length);
                    Marshal.Copy(IntPtr.Add(qr.Scan0,py*qr.Stride),mask,0,mask.Length);
                    for(int px=0;px<r.Size;px++)
                    {
                        int mx=px/scale-r.Margin,my=py/scale-r.Margin,n=modules-2*r.Margin;
                        bool quiet=mx<0||my<0||mx>=n||my>=n,solid=quiet||Function(mx,my,n);
                        double dx=Math.Abs((px%scale+.5)/scale-.5),dy=Math.Abs((py%scale+.5)/scale-.5);
                        bool center=dx<=core/2&&dy<=core/2;
                        if(!solid&&!center)continue;
                        bool ink=(mask[px]&0xffffff)==0;
                        int low=solid?28:pass==0?80:pass==1?58:40,high=solid?244:pass==0?211:pass==1?227:238;
                        pixels[px]=Constrain(pixels[px],ink,low,high);
                    }
                    Marshal.Copy(pixels,0,IntPtr.Add(target.Scan0,(r.Y+py)*target.Stride+r.X*4),pixels.Length);
                }
            }
            finally{if(target!=null)output.UnlockBits(target);if(qr!=null)code.UnlockBits(qr);}
        }
        static int Constrain(int pixel,bool ink,int low,int high)
        {
            int r=(pixel>>16)&255,g=(pixel>>8)&255,b=pixel&255;double lum=.299*r+.587*g+.114*b;
            if(ink&&lum>low){double f=low/Math.Max(1,lum);r=(int)Math.Round(r*f);g=(int)Math.Round(g*f);b=(int)Math.Round(b*f);}
            else if(!ink&&lum<high){double f=(high-lum)/Math.Max(1,255-lum);r=(int)Math.Round(r+(255-r)*f);g=(int)Math.Round(g+(255-g)*f);b=(int)Math.Round(b+(255-b)*f);}
            return unchecked((int)0xff000000)|r<<16|g<<8|b;
        }
    }
}
