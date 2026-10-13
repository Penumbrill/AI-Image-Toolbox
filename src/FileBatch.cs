using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace QQImageSwitch
{
    public sealed class FileBatchItem
    {
        public string Path,CoverPath,State="待导出";public long Length;public string Name {get{return System.IO.Path.GetFileName(Path);}}
        public FileBatchItem Copy(){return new FileBatchItem{Path=Path,CoverPath=CoverPath,Length=Length,State=State};}
    }
    public static class FileBatch
    {
        public static byte[] Cover(string path,int number,bool numbered,CoverTextOptions text,bool scrambleCover=false,CancellationToken token=default(CancellationToken))
        {
            bool converted;byte[] raw;if(string.IsNullOrEmpty(path)){using(var b=Codec.DefaultCover(800,500,false))raw=FileDisguise.EncodeCover(b);}else raw=FileDisguise.Cover(path,out converted);if(!scrambleCover&&!numbered&&(text==null||string.IsNullOrWhiteSpace(text.Text)))return raw;
            using(var image=Codec.Decode(raw))using(var b=Batch.MakeCover(image,image.Width,image.Height,number,numbered,Color.White,text,scrambleCover,token))return FileDisguise.EncodeCover(b);
        }
        public static BatchResult Export(IList<FileBatchItem> items,string shared,string folder,bool numbered,CoverTextOptions text,CancellationToken token,IProgress<BatchProgress> progress,IProgress<FileProgress> bytes=null,IList<int> numbers=null,int prototypeMode=1,bool scrambleCover=false)
        {
            var result=new BatchResult();Directory.CreateDirectory(folder);
            for(int i=0;i<items.Count;i++)
            {
                if(token.IsCancellationRequested){result.Cancelled=true;break;}var item=items[i];int number=numbers==null?i+1:numbers[i];
                try{string cover=item.CoverPath??shared;var jpeg=Cover(cover,number,numbered,text,scrambleCover,token);token.ThrowIfCancellationRequested();string suffix=System.IO.Path.GetExtension(item.Path).Equals(".mp4",StringComparison.OrdinalIgnoreCase)?"_v2i":"_a2i";string output=ImageTools.Unique(folder,item.Name,(prototypeMode==0?"_copyb":"")+suffix,prototypeMode>=2?".png":".jpg");PackagingPrototype.Pack(jpeg,item.Path,output,prototypeMode,token,bytes);result.Files.Add(output);item.State="已导出";}
                catch(OperationCanceledException){result.Cancelled=true;break;}catch(Exception ex){item.State="失败";result.Errors.Add(item.Name+"："+ex.Message);}
                if(progress!=null)progress.Report(new BatchProgress{Index=i,Completed=i+1,Total=items.Count,State=item.State});
            }
            return result;
        }
    }
}
