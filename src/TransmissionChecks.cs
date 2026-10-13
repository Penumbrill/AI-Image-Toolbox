using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQImageSwitch
{
    public static class TransmissionChecks
    {
        static readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        public static Bitmap Jpeg(Bitmap image,int quality)
        {
            using(var white=new Bitmap(image.Width,image.Height,PixelFormat.Format24bppRgb))using(var stream=new MemoryStream())
            {
                using(var g=Graphics.FromImage(white)){g.Clear(Color.White);g.DrawImageUnscaled(image,0,0);}
                var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);
                using(var parameters=new EncoderParameters(1)){parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)quality);white.Save(stream,codec,parameters);}
                stream.Position=0;using(var decoded=(Bitmap)Image.FromStream(stream))return decoded.Clone(new Rectangle(0,0,decoded.Width,decoded.Height),PixelFormat.Format32bppArgb);
            }
        }
        public static string Report(string path,string key,CancellationToken token)
        {
            var text=new StringBuilder();ScrambleOptions options=ScrambleParameters.Read(path);if(options!=null)options.Key=key;
            using(var image=Codec.Load(path))
            {
                string[] expected=QrService.ScanAll(image,token);int[] original=ScrambleParameters.Pixels(image);bool alpha=original.Any(p=>(uint)p>>24!=255);
                bool dynamic=false;try{byte[] data=ImageTools.Read(path);if(data.Length>8&&data[0]==137)dynamic=Codec.Parse(data).Any(c=>c.Type=="acTL");}catch(InvalidDataException){}
                text.AppendLine(L.English?"Local simulation only; platform processing can differ.":"仅为本地模拟，平台实际处理可能不同。");
                if(alpha)text.AppendLine(L.English?"Transparency: JPEG replaces alpha with white; background reveal cannot survive this conversion.":"透明度：JPG 将透明处填白，背景显图结构无法通过此转换保留。");
                if(dynamic)text.AppendLine(L.English?"Dynamic image: JPEG keeps only its displayed static frame; dual-image switching is lost.":"动态图片：JPG 仅保留显示的静态帧，双图切换结构丢失。");
                text.AppendLine(L.English?"Appended original files and parameter records do not survive image re-encoding.":"图片重编码不保留附带原文件和混淆参数记录。");
                int[] plain=null;if(options!=null){options.Validate(true);plain=ScrambleEngine.Transform((int[])original.Clone(),image.Width,image.Height,options,true,token);}
                for(int i=0;i<4;i++)
                {
                    token.ThrowIfCancellationRequested();using(var sample=i<2?Jpeg(image,i==0?85:75):Codec.Fit(image,Math.Max(1,(int)Math.Round(image.Width*(i==2?.75:.5))),Math.Max(1,(int)Math.Round(image.Height*(i==2?.75:.5))),Color.Transparent))
                    {
                        string label=i<2?"JPG "+(i==0?85:75)+"%":"Resize "+(i==2?75:50)+"%";text.Append(label+": ");
                        if(expected.Length>0){string[] actual=QrService.ScanAll(sample,token);bool same=expected.OrderBy(x=>x).SequenceEqual(actual.OrderBy(x=>x));text.Append((L.English?"QR exact match ":"二维码内容一致 ")+(same?"PASS":"FAIL"));}
                        else text.Append(L.English?"no source QR detected":"源图未识别到二维码");
                        if(plain!=null){if(i>=2)text.Append(L.English?"; scramble dimensions changed, exact recovery unavailable":"；混淆尺寸改变，不能精确还原");else{int[] decoded=ScrambleEngine.Transform(ScrambleParameters.Pixels(sample),image.Width,image.Height,options,true,token);double error=0;for(int p=0;p<decoded.Length;p++){if((p&65535)==0)token.ThrowIfCancellationRequested();error+=Math.Abs((decoded[p]&255)-(plain[p]&255))+Math.Abs(((decoded[p]>>8)&255)-((plain[p]>>8)&255))+Math.Abs(((decoded[p]>>16)&255)-((plain[p]>>16)&255));}text.Append((L.English?"; restored RGB mean error ":"；还原 RGB 平均误差 ")+(error/(decoded.Length*3.0)).ToString("0.00"));}}
                        text.AppendLine();
                    }
                }
            }
            return text.ToString();
        }
        public static async Task Show(IWin32Window owner)
        {
            using(var picker=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp",Title="选择要检查的导出图片"})
            {
                if(L.Show(picker,owner)!=DialogResult.OK)return;
                string key="";
                try{var saved=ScrambleParameters.Read(picker.FileName);if(saved!=null&&(saved.Algorithm==1||saved.Algorithm==2))using(var prompt=new Form()){Ui.Configure(prompt,L.T("PicEncrypt 密钥"),new Size(420,150));var input=new TextBox{Dock=DockStyle.Top};var accept=Ui.Button("确定",delegate{prompt.DialogResult=DialogResult.OK;});accept.Dock=DockStyle.Bottom;prompt.Controls.Add(input);prompt.Controls.Add(accept);if(L.Show(prompt,owner)!=DialogResult.OK)return;key=input.Text.Trim();}}
                catch(Exception ex){MessageBox.Show(owner,ex.Message,L.T("压缩模拟检查"));return;}
                if(!await gate.WaitAsync(0))return;
                using(var cancellation=new CancellationTokenSource())using(var progress=new Form())
                {
                    Ui.Configure(progress,L.T("压缩模拟检查"),new Size(400,150));var cancel=Ui.Button("停止任务",delegate{cancellation.Cancel();});cancel.Dock=DockStyle.Bottom;progress.Controls.Add(cancel);progress.Controls.Add(Ui.Text("正在后台计算…",12));progress.FormClosing+=delegate{cancellation.Cancel();};progress.Show(owner);
                    try{string report=await Task.Run(()=>Report(picker.FileName,key,cancellation.Token));progress.Close();L.Show(new ResultForm(L.T("压缩模拟检查"),report,Path.GetDirectoryName(picker.FileName)),owner);}
                    catch(OperationCanceledException){}
                    catch(Exception ex){progress.Close();MessageBox.Show(owner,ex.Message,L.T("压缩模拟检查"));}
                    finally{progress.Close();gate.Release();}
                }
            }
        }
    }
}
