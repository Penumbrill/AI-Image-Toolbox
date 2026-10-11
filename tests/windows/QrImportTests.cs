using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace QQImageSwitch
{
    static class QrImportTests
    {
        static void Require(bool value,string message){if(!value)throw new Exception("QR import: "+message);}
        internal const string Contact="BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Test Contact\r\nTEL:+12025550123\r\nEND:VCARD\r\n";
        internal const string ExactUrl="https://example.com/contact?token=A%2fb&x=1#profile";
        internal static Bitmap Screenshot(bool multiple)
        {
            var image=new Bitmap(1320,1000);using(var g=Graphics.FromImage(image))
            {
                g.Clear(Color.FromArgb(232,240,248));g.FillEllipse(Brushes.SteelBlue,55,35,110,110);
                using(var font=new Font("Arial",23))g.DrawString("Test nickname / interface text",font,Brushes.Black,185,70);
                using(var first=QrService.Code(ExactUrl,560,4,true))g.DrawImageUnscaled(first,45,200);
                if(multiple)using(var second=QrService.Code(Contact,560,4,true))g.DrawImageUnscaled(second,715,200);
                else using(var duplicate=QrService.Code(ExactUrl,560,4,true))g.DrawImageUnscaled(duplicate,715,200);
                using(var font=new Font("Arial",20))g.DrawString("Interface decoration must not become QR data",font,Brushes.Gray,55,850);
            }
            return image;
        }
        static Bitmap RoundedCode(string value)
        {
            var hints=new System.Collections.Generic.Dictionary<ZXing.EncodeHintType,object>{{ZXing.EncodeHintType.ERROR_CORRECTION,ZXing.QrCode.Internal.ErrorCorrectionLevel.H},{ZXing.EncodeHintType.CHARACTER_SET,"UTF-8"},{ZXing.EncodeHintType.MARGIN,0},{ZXing.EncodeHintType.DISABLE_ECI,true}};
            var matrix=new ZXing.QrCode.QRCodeWriter().encode(value,ZXing.BarcodeFormat.QR_CODE,1,1,hints);int n=matrix.Width,m=16,pad=5*m;var image=new Bitmap((n+10)*m,(n+10)*m+100);
            using(var g=Graphics.FromImage(image))
            {
                g.Clear(Color.White);using(var font=new Font("Arial",18))g.DrawString("Screenshot decoration",font,Brushes.DimGray,20,15);
                for(int y=0;y<n;y++)for(int x=0;x<n;x++)if(matrix[x,y])g.FillEllipse(Brushes.Black,pad+x*m+m*.15f,100+pad+y*m+m*.15f,m*.7f,m*.7f);
                foreach(var p in new[]{new Point(0,0),new Point(n-7,0),new Point(0,n-7)})
                {
                    int x=pad+p.X*m,y=100+pad+p.Y*m;g.FillRectangle(Brushes.White,x-m,y-m,9*m,9*m);
                    for(int inset=0;inset<3;inset++)g.FillEllipse(inset==1?Brushes.White:Brushes.Black,x+inset*m,y+inset*m,(7-2*inset)*m,(7-2*inset)*m);
                }
            }
            return image;
        }
        public static void Run(string folder)
        {
            Directory.CreateDirectory(folder);
            var payloads=new[]{Contact,ExactUrl," \tline one\nline two \r\n","中文内容 🙂","javascript:alert(1)","file:///C:/example","weixin://example-contact","BEGIN:VCARD\r\nVERSION:3.0\r\nFN:测试🙂\r\nEND:VCARD\r\n"};
            foreach(string value in payloads)
            {
                Require(QrService.Payload(value,true)==value,"raw validation altered content");
                QrRegion region;using(var code=QrService.Compose(null,value,1024,50,50,50,null,out region,4,true))
                {
                    Require(QrService.Scan(code)==value,"raw text round trip");Require(region.Raw&&region.Url==value,"raw metadata lost");
                    string path=Path.Combine(folder,"raw-"+Array.IndexOf(payloads,value)+".png");
                    Codec.SaveAtomic(path,QrService.Tag(Codec.StaticPng(code),region));var restored=QrService.Read(path);Require(restored!=null&&restored.Raw&&restored.Url==value,"raw PNG read lost exact content");
                }
            }
            foreach(string value in new[]{"", " \t\r\n", "text\0data", "\ud800", new string('x',1101)}){bool rejected=false;try{QrService.Payload(value,true);}catch(ArgumentException){rejected=true;}Require(rejected,"unsupported raw input accepted");}
            Require(QrService.Payload("  https://EXAMPLE.com  ")=="https://example.com/","manual URL normalization changed");
            Require(!Inspection.Json().Deserialize<QrRegion>("{\"Url\":\"https://example.com/\"}").Raw,"legacy metadata is raw");
            using(var image=Screenshot(true)){var found=QrService.ScanAll(image);Require(found.Length==2&&found.Contains(ExactUrl)&&found.Contains(Contact),"multi code screenshot did not preserve both exact payloads");Codec.SaveAtomic(Path.Combine(folder,"multiple.png"),Codec.StaticPng(image));}
            using(var duplicate=Screenshot(false))Require(QrService.ScanAll(duplicate).SequenceEqual(new[]{ExactUrl}),"repeated QR was not deduplicated");
            foreach(string value in new[]{"https://example.com/short#contact",ExactUrl})using(var rounded=RoundedCode(value))
            {
                for(int rotation=0;rotation<4;rotation++){Require(QrImportRecovery.Recover(rounded,CancellationToken.None).Contains(value),"automatic circular finder recovery failed");Require(QrService.ScanAll(rounded).Contains(value),"round QR import failed");rounded.RotateFlip(RotateFlipType.Rotate90FlipNone);}
            }
            foreach(bool dualRounded in new[]{false,true})using(var left=RoundedCode(ExactUrl))using(var right=dualRounded?RoundedCode(Contact):QrService.Code(Contact,700,4,true))using(var screenshot=new Bitmap(left.Width+right.Width+120,Math.Max(left.Height,right.Height)+120))
            {using(var g=Graphics.FromImage(screenshot)){g.Clear(Color.White);g.DrawImageUnscaled(left,30,60);g.DrawImageUnscaled(right,left.Width+90,60);}var found=QrService.ScanAll(screenshot);Require(found.Length==2&&found.Contains(ExactUrl)&&found.Contains(Contact),dualRounded?"dual circular QR screenshot missing content":"mixed square/circular QR screenshot missing content");}
            using(var unrelated=new Bitmap(700,700)){using(var g=Graphics.FromImage(unrelated)){g.Clear(Color.White);foreach(var point in new[]{new Point(100,100),new Point(400,100),new Point(100,400)}){g.FillEllipse(Brushes.Black,point.X,point.Y,84,84);g.FillEllipse(Brushes.White,point.X+12,point.Y+12,60,60);g.FillEllipse(Brushes.Black,point.X+24,point.Y+24,36,36);}}Require(QrService.ScanAll(unrelated).Length==0,"three decorative rings mistaken for valid QR data");}
            using(var blank=new Bitmap(400,600)){using(var g=Graphics.FromImage(blank))g.Clear(Color.White);Require(QrService.ScanAll(blank).Length==0,"blank image accepted");bool cancelled=false;using(var cts=new CancellationTokenSource()){cts.Cancel();try{QrService.ScanAll(blank,cts.Token);}catch(OperationCanceledException){cancelled=true;}}Require(cancelled,"scan ignored cancellation");}
            using(var photo=new Bitmap(1200,1500)){using(var g=Graphics.FromImage(photo))g.Clear(Color.FromArgb(222,198,162));QrRegion artRegion;using(var art=QrArt.Compose(photo,Contact,75,50,60,null,out artRegion,4,CancellationToken.None,true))Require(artRegion.Raw&&QrService.Scan(art)==Contact,"raw pixel material fusion failed");}
            using(var photo=new Bitmap(1000,1200)){using(var g=Graphics.FromImage(photo))g.Clear(Color.Tan);QrRegion region;using(var overlay=QrService.Compose(photo,Contact,1024,75,50,60,null,out region,4,true))using(var cover=BackgroundRevealAuto.CreateCover(overlay,0,-1,CancellationToken.None)){BackgroundRevealAuto.Profile profile;using(var hidden=BackgroundRevealAuto.Compose(cover,overlay,0,false,85,CancellationToken.None,out profile)){var optimized=QrService.Optimize(hidden,region,cover,profile,CancellationToken.None);Require(optimized.Raw,"optimization lost raw metadata");using(var black=DualBlend.Render(hidden,false,CancellationToken.None))Require(QrService.Scan(black)==Contact,"raw hidden QR changed content");}}}
            for(int mask=0;mask<8;mask++){QrRegion region;using(var condition=QrAiCondition.Create(Contact,new Size(736,856),1024,60,50,75,out region,mask,true)){Require(region.Raw&&QrService.Scan(condition)==Contact,"raw AI condition mask failed");Require(QrAiCondition.Resize(region,condition.Size).Raw,"AI resize lost raw state");}}
            File.WriteAllText(Path.Combine(folder,"PASS.md"),"Exact QR payloads, multi-code local screenshots, duplicate removal, unsupported text rejection, cancellation, legacy metadata, pixel fusion, hidden QR optimization, and all eight AI masks passed. No links were opened.");
        }
    }
}
