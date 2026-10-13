using System;
using System.IO;
using System.Text;
using System.Threading;
namespace QQImageSwitch {
 // Fixed-length metadata replacement keeps MP4 sample offsets and EBML cue offsets valid.
 public static class VideoMetadata {
  public static string Clean(string input,string folder,CancellationToken token){
   string ext=Path.GetExtension(input).ToLowerInvariant();if(!Supported(ext))throw new InvalidDataException("视频支持 MP4 / MOV / M4V / WebM / MKV。");
   string output=ImageTools.Unique(folder,input,"_已清信息",ext),temp=output+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var source=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read))using(var target=new FileStream(temp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){
    byte[] buffer=new byte[1024*1024];int n;while((n=source.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();target.Write(buffer,0,n);}target.Flush();target.Position=0;
    if(ext==".webm"||ext==".mkv")CleanEbml(target,token);else{if(target.Length<16||Type(target,4)!="ftyp")throw new InvalidDataException("无效 MP4/MOV 文件头。");bool media=false,movie=false;Boxes(target,0,target.Length,0,token,ref media,ref movie);if(!media||!movie)throw new InvalidDataException("视频缺少媒体数据或轨道结构。");}target.Flush();
   }token.ThrowIfCancellationRequested();File.Move(temp,output);return output;}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public static bool Supported(string ext){return ext==".mp4"||ext==".mov"||ext==".m4v"||ext==".webm"||ext==".mkv";}
  static ulong UInt(Stream f,long p,int n){f.Position=p;ulong v=0;for(int i=0;i<n;i++){int b=f.ReadByte();if(b<0)throw new EndOfStreamException();v=v<<8|(byte)b;}return v;}
  static string Type(Stream f,long p){f.Position=p;var a=new byte[4];if(f.Read(a,0,4)!=4)throw new EndOfStreamException();return Encoding.GetEncoding(28591).GetString(a);}
  static void Zero(Stream f,long p,long length,CancellationToken token){byte[] b=new byte[65536];f.Position=p;while(length>0){token.ThrowIfCancellationRequested();int n=(int)Math.Min(length,b.Length);f.Write(b,0,n);length-=n;}}
  static void Boxes(Stream f,long start,long end,int depth,CancellationToken token,ref bool media,ref bool movie){
   if(depth>16)throw new InvalidDataException("视频容器嵌套过深。");for(long p=start;p<end;){token.ThrowIfCancellationRequested();if(end-p<8)throw new InvalidDataException("视频数据块被截断。");ulong size=UInt(f,p,4);string type=Type(f,p+4);int head=8;if(size==1){if(end-p<16)throw new InvalidDataException("视频扩展长度被截断。");size=UInt(f,p+8,8);head=16;}if(size==0)size=(ulong)(end-p);if(size<(ulong)head||size>(ulong)(end-p))throw new InvalidDataException("视频数据块长度无效。");long next=p+(long)size;
    bool erase=type=="udta"||type=="meta";if(type=="uuid"&&size>=(ulong)(head+16)){f.Position=p+head;byte[] id=new byte[16];f.Read(id,0,16);erase=BitConverter.ToString(id)=="BE-7A-CF-CB-97-A9-42-E8-9C-71-99-94-91-E3-AF-AC";}
    if(erase){f.Position=p+4;byte[] free=Encoding.ASCII.GetBytes("free");f.Write(free,0,4);Zero(f,p+head,(long)size-head,token);}
    else if(type=="moov"||type=="trak"||type=="mdia"||type=="minf"||type=="stbl"||type=="edts"||type=="mvex"||type=="moof"||type=="traf") {if(type=="moov")movie=true;Boxes(f,p+head,next,depth+1,token,ref media,ref movie);}
    else if(type=="mvhd"||type=="tkhd"||type=="mdhd"){int v=(int)UInt(f,p+head,1);int dates=v==0?8:v==1?16:0;if(dates==0||size<(ulong)(head+4+dates))throw new InvalidDataException("视频时间信息无效。");Zero(f,p+head+4,dates,token);}
    if(type=="pssh")throw new InvalidDataException("不支持加密视频容器。");
    if(type=="stsd"){if(size<(ulong)(head+8))throw new InvalidDataException("视频样本描述无效。");long entry=p+head+8;ulong count=UInt(f,p+head+4,4);if(count>(ulong)((next-entry)/8))throw new InvalidDataException("视频样本描述无效。");for(ulong n=0;n<count;n++){ulong length=UInt(f,entry,4);if(length<8||length>(ulong)(next-entry))throw new InvalidDataException("视频样本描述无效。");string format=Type(f,entry+4);if(format=="encv"||format=="enca")throw new InvalidDataException("不支持加密视频容器。");entry+=(long)length;}}
    if(type=="mdat")media=true;p=next;
   }
  }
  sealed class Element {public long Start,Data,End;public ulong Id;}
  static ulong Vint(Stream f,ref long p,int max,bool id,out bool unknown){int first=(int)UInt(f,p++,1),mask=128,n=1;while((first&mask)==0&&n<=max){mask>>=1;n++;}if(n>max||mask==0)throw new InvalidDataException("无效 EBML 长度。");ulong value=(ulong)(id?first:first&(mask-1));for(int i=1;i<n;i++)value=value<<8|UInt(f,p++,1);unknown=!id&&value==((1UL<<(7*n))-1);return value;}
  static Element ReadElement(Stream f,long p,long end,bool segment){long start=p;bool unknown;ulong id=Vint(f,ref p,4,true,out unknown),size=Vint(f,ref p,8,false,out unknown);if(p>end||(!unknown&&size>(ulong)(end-p))||unknown&&!(segment||id==0x18538067))throw new InvalidDataException("视频 EBML 数据块无效。");return new Element{Start=start,Data=p,End=unknown?end:p+(long)size,Id=id};}
  static void Void(Stream f,Element e,CancellationToken token){long total=e.End-e.Start;int width=1;while(width<8&&(ulong)(total-1-width)>=((1UL<<(7*width))-1))width++;long payload=total-1-width;if(payload<0)throw new InvalidDataException("元数据块过短。");byte[] size=new byte[width];ulong v=(ulong)payload;for(int i=width-1;i>=0;i--){size[i]=(byte)v;v>>=8;}size[0]|=(byte)(1<<(8-width));f.Position=e.Start;f.WriteByte(0xec);f.Write(size,0,width);Zero(f,e.Start+1+width,payload,token);}
  static void CleanEbml(Stream f,CancellationToken token){if(f.Length<8||UInt(f,0,4)!=0x1a45dfa3)throw new InvalidDataException("无效 WebM/MKV 文件头。");bool found=false;for(long p=0;p<f.Length;){token.ThrowIfCancellationRequested();var e=ReadElement(f,p,f.Length,false);if(e.Id==0x18538067){found=true;for(long q=e.Data;q<e.End;){token.ThrowIfCancellationRequested();var child=ReadElement(f,q,e.End,false);if(child.Id==0x1254c367||child.Id==0x1941a469)Void(f,child,token);else if(child.Id==0x1549a966){for(long a=child.Data;a<child.End;){var info=ReadElement(f,a,child.End,false);if(info.Id==0x7ba9||info.Id==0x4d80||info.Id==0x5741||info.Id==0x4461)Void(f,info,token);a=info.End;}}q=child.End;}}p=e.End;}if(!found)throw new InvalidDataException("视频缺少 EBML Segment。");}
 }
}
