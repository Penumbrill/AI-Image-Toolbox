using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using ZXing;
using ZXing.Common;

namespace QQImageSwitch
{
    // Import-only recovery for standard QR data with circular finder artwork.
    // Geometry is inferred from the screenshot; only a real QR decoder may
    // accept a candidate. Output verification never uses this recovery path.
    static class QrImportRecovery
    {
        sealed class Part{public int X,Y,W,H,Area;public long SumX,SumY;}
        sealed class Ring{public double X,Y,D;}
        public static string[] Recover(Bitmap source,CancellationToken token)
        {
            double ratio=Math.Min(1,Math.Min(3200.0/Math.Max(source.Width,source.Height),Math.Sqrt(4000000.0/((double)source.Width*source.Height))));
            using(var image=Codec.Fit(source,Math.Max(1,(int)(source.Width*ratio)),Math.Max(1,(int)(source.Height*ratio)),Color.White))
            {
                int width=image.Width,height=image.Height;byte[] gray=Gray(image,token);var rings=FindRings(gray,width,height,token);if(rings.Count<3)return new string[0];
                var found=new HashSet<string>(StringComparer.Ordinal);var reader=new BarcodeReader{AutoRotate=true,Options=new DecodingOptions{TryHarder=true,TryInverted=true,PossibleFormats=new[]{BarcodeFormat.QR_CODE}}};int attempts=0;
                for(int a=0;a<rings.Count-2;a++)for(int b=a+1;b<rings.Count-1;b++)for(int c=b+1;c<rings.Count;c++)
                {
                    var trio=new[]{rings[a],rings[b],rings[c]};
                    for(int corner=0;corner<3;corner++)
                    {
                        token.ThrowIfCancellationRequested();Ring p=trio[corner],q=trio[(corner+1)%3],r=trio[(corner+2)%3];double ux=q.X-p.X,uy=q.Y-p.Y,vx=r.X-p.X,vy=r.Y-p.Y;
                        double lu=Math.Sqrt(ux*ux+uy*uy),lv=Math.Sqrt(vx*vx+vy*vy),mean=(p.D+q.D+r.D)/3;
                        if(lu<1.8*mean||lv<1.8*mean||Math.Abs((ux*vx+uy*vy)/(lu*lv))>.12||lu/lv<.85||lu/lv>1.18||trio.Max(t=>t.D)/trio.Min(t=>t.D)>1.25)continue;
                        if(ux*vy-uy*vx<0){double swap=ux;ux=vx;vx=swap;swap=uy;uy=vy;vy=swap;}
                        double estimate=(lu+lv)/2/(mean/7)+7;int nearest=(int)Math.Round((estimate-21)/4)*4+21;
                        bool accepted=false;
                        foreach(int n in new[]{nearest,nearest-4,nearest+4})
                        {
                            if(n<21||n>177)continue;double ax=ux/(n-7),ay=uy/(n-7),bx=vx/(n-7),by=vy/(n-7);
                            double ox=p.X-3.5*(ax+bx),oy=p.Y-3.5*(ay+by);int module=Math.Max(4,Math.Min(32,(int)Math.Round((lu+lv)/2/(n-7))));
                            foreach(int level in new[]{210,220,230,240})
                            {
                                if(++attempts>64)return found.ToArray();token.ThrowIfCancellationRequested();
                                using(var candidate=Normalize(gray,width,height,n,module,ox,oy,ax,ay,bx,by,level,token))
                                {
                                    var results=reader.DecodeMultiple(candidate);token.ThrowIfCancellationRequested();
                                    if(results==null){var one=reader.Decode(candidate);token.ThrowIfCancellationRequested();if(one!=null)results=new[]{one};}
                                    if(results!=null)foreach(var result in results)if(result.Text!=null){found.Add(result.Text);accepted=true;}
                                }
                                if(accepted)break;
                            }
                            if(accepted)break;
                        }
                    }
                }
                return found.ToArray();
            }
        }
        static byte[] Gray(Bitmap image,CancellationToken token)
        {
            int width=image.Width,height=image.Height;var gray=new byte[width*height];var row=new int[width];
            var bits=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{for(int y=0;y<height;y++){token.ThrowIfCancellationRequested();Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),row,0,width);for(int x=0;x<width;x++){int v=row[x];gray[y*width+x]=(byte)((((v>>16)&255)+2*((v>>8)&255)+(v&255))/4);}}}
            finally{image.UnlockBits(bits);}return gray;
        }
        static List<Ring> FindRings(byte[] gray,int width,int height,CancellationToken token)
        {
            var labels=new int[gray.Length];var queue=new int[gray.Length];var parts=new List<Part>{null};
            for(int start=0;start<gray.Length;start++)
            {
                if((start&65535)==0)token.ThrowIfCancellationRequested();if(gray[start]>=230||labels[start]!=0)continue;
                int id=parts.Count,head=0,tail=1,minX=width,minY=height,maxX=0,maxY=0;long sumX=0,sumY=0;queue[0]=start;labels[start]=id;
                while(head<tail)
                {
                    if((head&65535)==0)token.ThrowIfCancellationRequested();int at=queue[head++],y=at/width,x=at-y*width;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);sumX+=x;sumY+=y;
                    for(int dy=-1;dy<=1;dy++){int ny=y+dy;if(ny<0||ny>=height)continue;for(int dx=-1;dx<=1;dx++){int nx=x+dx;if(nx<0||nx>=width)continue;int next=ny*width+nx;if(labels[next]==0&&gray[next]<230){labels[next]=id;queue[tail++]=next;}}}
                }
                parts.Add(new Part{X=minX,Y=minY,W=maxX-minX+1,H=maxY-minY+1,Area=tail,SumX=sumX,SumY=sumY});
            }
            var rings=new List<Ring>();
            for(int index=1;index<parts.Count;index++)
            {
                Part outer=parts[index];double aspect=outer.W/(double)outer.H,fill=outer.Area/((double)outer.W*outer.H);
                if(Math.Min(outer.W,outer.H)<20||aspect<=.85||aspect>=1.15||fill<=.25||fill>=.55)continue;
                double cx=outer.X+(outer.W-1)/2.0,cy=outer.Y+(outer.H-1)/2.0;int inner=labels[(int)Math.Round(cy)*width+(int)Math.Round(cx)];if(inner==0||inner==index)continue;Part core=parts[inner];
                double rx=core.W/(double)outer.W,ry=core.H/(double)outer.H,dx=core.SumX/(double)core.Area-cx,dy=core.SumY/(double)core.Area-cy;
                if(rx<=.32||rx>=.53||ry<=.32||ry>=.53||core.Area/((double)core.W*core.H)<.58||Math.Sqrt(dx*dx+dy*dy)>.1*Math.Max(outer.W,outer.H))continue;
                rings.Add(new Ring{X=cx,Y=cy,D=(outer.W+outer.H)/2.0});
            }
            return rings.OrderByDescending(r=>r.D).Take(24).ToList();
        }
        static Bitmap Normalize(byte[] gray,int width,int height,int n,int module,double ox,double oy,double ax,double ay,double bx,double by,int level,CancellationToken token)
        {
            int side=(n+8)*module,face=n*module;var image=new Bitmap(side,side);BitmapData bits=null;
            try
            {
                bits=image.LockBits(new Rectangle(0,0,side,side),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);var row=new int[side];
                for(int y=0;y<side;y++)
                {
                    token.ThrowIfCancellationRequested();for(int x=0;x<side;x++)row[x]=unchecked((int)0xffffffff);
                    int fy=y-4*module;if(fy>=0&&fy<face)for(int fx=0;fx<face;fx++)
                    {
                        double sx=ox+(fx+.5)*ax/module+(fy+.5)*bx/module-.5,sy=oy+(fx+.5)*ay/module+(fy+.5)*by/module-.5;
                        if(sx<0||sy<0||sx>width-1||sy>height-1)continue;
                        int ix=(int)sx,iy=(int)sy,jx=Math.Min(width-1,ix+1),jy=Math.Min(height-1,iy+1);double dx=sx-ix,dy=sy-iy;
                        double value=(gray[iy*width+ix]*(1-dx)+gray[iy*width+jx]*dx)*(1-dy)+(gray[jy*width+ix]*(1-dx)+gray[jy*width+jx]*dx)*dy;
                        if(value<level)row[fx+4*module]=unchecked((int)0xff000000);
                    }
                    Marshal.Copy(row,0,IntPtr.Add(bits.Scan0,y*bits.Stride),side);
                }
                image.UnlockBits(bits);bits=null;
                using(var g=Graphics.FromImage(image))foreach(var p in new[]{new Point(0,0),new Point(n-7,0),new Point(0,n-7)})
                {
                    int x=(p.X+4)*module,y=(p.Y+4)*module;g.FillRectangle(Brushes.White,x-module,y-module,9*module,9*module);
                    for(int inset=0;inset<3;inset++)g.FillRectangle(inset==1?Brushes.White:Brushes.Black,x+inset*module,y+inset*module,(7-2*inset)*module,(7-2*inset)*module);
                }
                return image;
            }
            catch{if(bits!=null)image.UnlockBits(bits);image.Dispose();throw;}
        }
    }
}
