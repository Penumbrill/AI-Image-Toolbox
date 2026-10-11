using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using ZXing;
using ZXing.QrCode.Internal;

namespace QQImageSwitch
{
    public static class QrAiCondition
    {
        public static Bitmap Create(string link,Size image,int edge,int percent,int px,int py,out QrRegion region,int mask=-1,bool raw=false)
        {
            link=QrService.Payload(link,raw);var matrix=Matrix(link,mask);region=Locate(link,image,edge,percent,px,py,matrix.Width,raw);int scale=region.Size/matrix.Width;
            var condition=new Bitmap(region.Width,region.Height);using(var g=Graphics.FromImage(condition)){g.Clear(Color.FromArgb(128,128,128));for(int row=0;row<matrix.Height;row++)for(int col=0;col<matrix.Width;col++)g.FillRectangle(matrix[col,row]?Brushes.Black:Brushes.White,region.X+col*scale,region.Y+row*scale,scale,scale);}
            return condition;
        }
        public static QrRegion Layout(string link,Size image,int edge,int percent,int px,int py,bool raw=false)
        {link=QrService.Payload(link,raw);return Locate(link,image,edge,percent,px,py,Matrix(link,-1).Width,raw);}
        public static void DrawPreview(Bitmap preview,string link,RectangleF bounds,bool raw=false)
        {
            var matrix=Matrix(QrService.Payload(link,raw),-1);float scale=bounds.Width/matrix.Width,margin=scale*4;
            using(var g=Graphics.FromImage(preview))
            {
                g.SmoothingMode=SmoothingMode.None;g.PixelOffsetMode=PixelOffsetMode.Half;
                g.FillRectangle(Brushes.White,bounds.X-margin,bounds.Y-margin,bounds.Width+margin*2,bounds.Height+margin*2);
                for(int row=0;row<matrix.Height;row++)for(int col=0;col<matrix.Width;col++)if(matrix[col,row])g.FillRectangle(Brushes.Black,bounds.X+col*scale,bounds.Y+row*scale,scale,scale);
            }
        }
        static ZXing.Common.BitMatrix Matrix(string link,int mask)
        {
            var hints=new Dictionary<EncodeHintType,object>{{EncodeHintType.ERROR_CORRECTION,ErrorCorrectionLevel.H},{EncodeHintType.CHARACTER_SET,"UTF-8"},{EncodeHintType.MARGIN,0},{EncodeHintType.DISABLE_ECI,true}};
            if(mask<-1||mask>7)throw new ArgumentOutOfRangeException("mask");if(mask>=0)hints.Add(EncodeHintType.QR_MASK_PATTERN,mask);
            return new ZXing.QrCode.QRCodeWriter().encode(link,BarcodeFormat.QR_CODE,1,1,hints);
        }
        static QrRegion Locate(string link,Size image,int edge,int percent,int px,int py,int modules,bool raw)
        {
            if(image.Width<1||image.Height<1)throw new ArgumentException("底图尺寸无效。");double ratio=image.Width/(double)image.Height;
            if(ratio<.25||ratio>4)throw new ArgumentException("底图比例过长，请先选择需要嵌码的区域；原图不会自动裁切。");
            percent=Math.Max(30,Math.Min(90,percent));
            // The shorter side is rounded down to 64 below. Round its minimum
            // up first, including four quiet modules on each side, before
            // converting to the longer edge so that rounding cannot clip them.
            int requiredShort=(int)Math.Ceiling(Math.Max(modules*16*100.0/percent,(modules+8)*16)/64.0)*64;
            int required=(int)Math.Ceiling(requiredShort/Math.Min(ratio,1/ratio)/64.0)*64;
            edge=Math.Max(edge,required);if(edge>3072)throw new ArgumentException("二维码内容太长、码区太小或底图比例过长，请缩短内容、增大码区或先选择需要嵌码的区域。");
            int width=ratio>=1?edge:Math.Max(384,(int)(edge*ratio)/64*64),height=ratio>=1?Math.Max(384,(int)(edge/ratio)/64*64):edge;
            int scale=Math.Min(Math.Min(width,height)/(modules+8),Math.Min(width,height)*percent/100/modules),side=scale*modules;
            if(scale<16||side+8*scale>Math.Min(width,height))throw new ArgumentException("二维码内容过长，无法嵌入当前 AI 尺寸，请使用较短内容。");
            int margin=scale*4;
            int x=Math.Max(margin,Math.Min(width-side-margin,(int)Math.Round(width*px/100.0-side/2.0))),y=Math.Max(margin,Math.Min(height-side-margin,(int)Math.Round(height*py/100.0-side/2.0)));
            return new QrRegion{Url=link,X=x,Y=y,Size=side,Width=width,Height=height,Margin=0,Raw=raw};
        }
        public static RectangleF PreviewBounds(QrRegion region,Size original,Size preview)
        {
            double scale=Math.Min(region.Width/(double)original.Width,region.Height/(double)original.Height),left=(region.Width-original.Width*scale)/2,top=(region.Height-original.Height*scale)/2;
            return new RectangleF((float)((region.X-left)/scale*preview.Width/original.Width),(float)((region.Y-top)/scale*preview.Height/original.Height),(float)(region.Size/scale*preview.Width/original.Width),(float)(region.Size/scale*preview.Height/original.Height));
        }
        public static QrRegion Resize(QrRegion input,Size output)
        {
            double sx=output.Width/(double)input.Width,sy=output.Height/(double)input.Height;
            return new QrRegion{Url=input.Url,X=(int)Math.Round(input.X*sx),Y=(int)Math.Round(input.Y*sy),Size=Math.Max(1,(int)Math.Round(input.Size*Math.Min(sx,sy))),Width=output.Width,Height=output.Height,Margin=0,Raw=input.Raw};
        }
        public static bool Validate(Bitmap image,string link)
        {
            if(QrService.Scan(image)!=link)return false;
            double ratio=Math.Min(1,800.0/Math.Max(image.Width,image.Height));using(var small=Codec.Fit(image,Math.Max(1,(int)(image.Width*ratio)),Math.Max(1,(int)(image.Height*ratio)),Color.White))return QrService.Scan(small)==link;
        }
    }
}
