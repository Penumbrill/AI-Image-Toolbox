package com.guangyingruoshui.imagetools;

import java.io.IOException;
import java.util.*;
import com.google.zxing.LuminanceSource;

/** Import-only fallback for standard QR data with three concentric round finders.
 * Data cells are sampled unchanged; temporary finder normalization is accepted
 * only when the ordinary QR decoder validates the data and error correction.
 */
final class QrRoundedImporter {
    static final int DISCOVERY_EDGE=1024,MAX_RINGS=48,MAX_GEOMETRIES=8,MAX_ATTEMPTS=64;
    static final class Ring {double x,y,diameter;int left,top,right,bottom,id;Ring(int l,int t,int r,int b,int label){left=l;top=t;right=r;bottom=b;id=label;x=(l+r)/2.;y=(t+b)/2.;diameter=(r-l+b-t+2)/2.;}}

    static List<Ring> rings(LuminanceSource source,Files.Job job)throws IOException {
        int w=source.getWidth(),h=source.getHeight(),count=w*h;byte[] gray=source.getMatrix();
        int[] labels=new int[count],queue=new int[count];List<Ring> possible=new ArrayList<>();int id=0;
        for(int start=0;start<count;start++){
            if((start&4095)==0)QrService.check(job);
            if(labels[start]!=0||(gray[start]&255)>=230)continue;
            id++;int head=0,tail=1,left=start%w,right=left,top=start/w,bottom=top;queue[0]=start;labels[start]=id;
            while(head<tail){
                if((head&4095)==0)QrService.check(job);int p=queue[head++],x=p%w,y=p/w;
                left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);
                for(int yy=Math.max(0,y-1);yy<=Math.min(h-1,y+1);yy++)for(int xx=Math.max(0,x-1);xx<=Math.min(w-1,x+1);xx++){
                    int q=yy*w+xx;if(labels[q]==0&&(gray[q]&255)<230){labels[q]=id;queue[tail++]=q;}
                }
            }
            int rw=right-left+1,rh=bottom-top+1;double aspect=rw/(double)rh,fill=tail/(double)(rw*rh);
            if(Math.min(rw,rh)>=8&&aspect>.85&&aspect<1.15&&fill>.25&&fill<.55){
                Ring ring=new Ring(left,top,right,bottom,id);possible.add(ring);
                if(possible.size()>MAX_RINGS*4){Collections.sort(possible,(a,b)->Double.compare(b.diameter,a.diameter));possible.subList(MAX_RINGS*2,possible.size()).clear();}
            }
        }
        List<Ring> found=new ArrayList<>();
        for(Ring ring:possible){
            QrService.check(job);int inner=labels[(int)Math.round(ring.y)*w+(int)Math.round(ring.x)];
            if(inner==0||inner==ring.id)continue;
            int left=w,right=-1,top=h,bottom=-1,area=0;long sumX=0,sumY=0;
            for(int y=ring.top;y<=ring.bottom;y++){QrService.check(job);for(int x=ring.left;x<=ring.right;x++)if(labels[y*w+x]==inner){left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);sumX+=x;sumY+=y;area++;}}
            int iw=right-left+1,ih=bottom-top+1,rw=ring.right-ring.left+1,rh=ring.bottom-ring.top+1;
            if(area==0||iw/(double)rw<=.32||iw/(double)rw>=.53||ih/(double)rh<=.32||ih/(double)rh>=.53||area/(double)(iw*ih)<.58)continue;
            if(Math.hypot(sumX/(double)area-ring.x,sumY/(double)area-ring.y)>.1*Math.max(rw,rh))continue;
            found.add(ring);
        }
        Collections.sort(found,(a,b)->Double.compare(b.diameter,a.diameter));if(found.size()>MAX_RINGS)found.subList(MAX_RINGS,found.size()).clear();return found;
    }

    static int sample(byte[] values,int w,int h,double x,double y){
        if(x<0||y<0||x>w-1||y>h-1)return 255;int x0=(int)x,y0=(int)y,x1=Math.min(w-1,x0+1),y1=Math.min(h-1,y0+1);double fx=x-x0,fy=y-y0;
        double a=(values[y0*w+x0]&255)*(1-fx)+(values[y0*w+x1]&255)*fx,b=(values[y1*w+x0]&255)*(1-fx)+(values[y1*w+x1]&255)*fx;return (int)Math.round(a*(1-fy)+b*fy);
    }
    static void rectangle(byte[] pixels,int edge,int l,int t,int r,int b,int color){for(int y=Math.max(0,t);y<Math.min(edge,b);y++)Arrays.fill(pixels,y*edge+Math.max(0,l),y*edge+Math.min(edge,r),(byte)color);}
    static LuminanceSource normalized(LuminanceSource source,int n,int m,double ox,double oy,double ux,double uy,double vx,double vy,int threshold,Files.Job job)throws IOException {
        int edge=(n+8)*m;byte[] input=source.getMatrix(),output=new byte[edge*edge];Arrays.fill(output,(byte)255);int w=source.getWidth(),h=source.getHeight();
        for(int y=0;y<n*m;y++){QrService.check(job);for(int x=0;x<n*m;x++){double px=(x+.5)/m,py=(y+.5)/m;output[(y+4*m)*edge+x+4*m]=(byte)(sample(input,w,h,ox+px*ux+py*vx,oy+px*uy+py*vy)<threshold?0:255);}}
        for(int[] finder:new int[][]{{0,0},{n-7,0},{0,n-7}}){int x=(finder[0]+4)*m,y=(finder[1]+4)*m;rectangle(output,edge,x-m,y-m,x+8*m,y+8*m,255);rectangle(output,edge,x,y,x+7*m,y+7*m,0);rectangle(output,edge,x+m,y+m,x+6*m,y+6*m,255);rectangle(output,edge,x+2*m,y+2*m,x+5*m,y+5*m,0);}
        return new QrService.GraySource(edge,edge,output);
    }
    static List<QrService.Match> recover(LuminanceSource source,Files.Job job)throws IOException {
        QrService.check(job);int width=source.getWidth(),height=source.getHeight();
        LuminanceSource discovery=Math.max(width,height)>DISCOVERY_EDGE?QrService.reduced(source,DISCOVERY_EDGE,job):source;
        List<Ring> rings=rings(discovery,job);List<QrService.Match> found=new ArrayList<>();double sx=width/(double)discovery.getWidth(),sy=height/(double)discovery.getHeight();
        for(Ring ring:rings){ring.x=(ring.x+.5)*sx-.5;ring.y=(ring.y+.5)*sy-.5;ring.diameter*=Math.sqrt(sx*sy);}
        int geometries=0,attempts=0;
        for(int a=0;a<rings.size();a++)for(int b=a+1;b<rings.size();b++)for(int c=b+1;c<rings.size();c++){
            QrService.check(job);Ring[] trio={rings.get(a),rings.get(b),rings.get(c)};
            for(int corner=0;corner<3;corner++){
                Ring p=trio[corner],q=trio[(corner+1)%3],r=trio[(corner+2)%3];double ux=q.x-p.x,uy=q.y-p.y,vx=r.x-p.x,vy=r.y-p.y,lu=Math.hypot(ux,uy),lv=Math.hypot(vx,vy),diameter=(p.diameter+q.diameter+r.diameter)/3.;
                if(lu<2*diameter||lv<2*diameter||Math.abs((ux*vx+uy*vy)/(lu*lv))>.12||lu/lv<.85||lu/lv>1.18)continue;
                if(Math.max(p.diameter,Math.max(q.diameter,r.diameter))/Math.min(p.diameter,Math.min(q.diameter,r.diameter))>1.3)continue;
                if(++geometries>MAX_GEOMETRIES)return found;
                if(ux*vy-uy*vx<0){double t=ux;ux=vx;vx=t;t=uy;uy=vy;vy=t;}
                int closest=(int)Math.round((((lu+lv)/2)/(diameter/7)+7-21)/4)*4+21;
                boolean decoded=false;
                for(int n:new int[]{closest,closest-4,closest+4}){
                    if(n<21||n>177)continue;double dx=ux/(n-7),dy=uy/(n-7),ex=vx/(n-7),ey=vy/(n-7),ox=p.x-3.5*dx-3.5*ex,oy=p.y-3.5*dy-3.5*ey;
                    int m=Math.max(4,Math.min(1024/(n+8),(int)Math.round((lu+lv)/2/(n-7))));
                    for(int threshold:new int[]{220,210,230,240}){
                        QrService.check(job);if(++attempts>MAX_ATTEMPTS)return found;
                        String text=QrService.decodeAtScale(normalized(source,n,m,ox,oy,dx,dy,ex,ey,threshold,job),job);if(text==null)continue;
                        double[] xs={ox,ox+n*dx,ox+n*ex,ox+n*(dx+ex)},ys={oy,oy+n*dy,oy+n*ey,oy+n*(dy+ey)};double l=width,t=height,rr=0,bb=0;for(int i=0;i<4;i++){l=Math.min(l,xs[i]);t=Math.min(t,ys[i]);rr=Math.max(rr,xs[i]);bb=Math.max(bb,ys[i]);}
                        QrService.addMatch(found,new QrService.Match(text,Math.max(0,(int)Math.floor(l)),Math.max(0,(int)Math.floor(t)),Math.min(width,(int)Math.ceil(rr)),Math.min(height,(int)Math.ceil(bb)),width,height));decoded=true;break;
                    }
                    if(decoded)break;
                }
            }
        }
        Collections.sort(found,(a,b)->a.top==b.top?Integer.compare(a.left,b.left):Integer.compare(a.top,b.top));return found;
    }
}
