package com.guangyingruoshui.imagetools;

import java.io.*;
import java.util.*;
import org.json.JSONObject;

/** UI-independent, deterministic algorithms shared with Windows and the web. */
strictfp final class ScrambleEngine {
    static final class Options {
        int Version=1,Rounds=1,Algorithm,Variant,Block=16,Width,Height;
        double Strength=1;String Seed="",Key="";
        boolean Green,ReduceGreen;
        Options copy(){Options o=new Options();o.Rounds=Rounds;o.Algorithm=Algorithm;o.Variant=Variant;o.Block=Block;o.Width=Width;o.Height=Height;o.Strength=Strength;o.Seed=Seed;o.Key=Key;o.Green=Green;o.ReduceGreen=ReduceGreen;return o;}
        void validate(boolean requireKey)throws IOException{
            if(Rounds<1||Rounds>20||Version!=1||Algorithm<0||Algorithm>3||Variant<0||Variant>1||Double.isNaN(Strength)||Strength<0||Strength>2)throw new IOException("混淆参数无效 / Invalid scramble parameters");
            if(requireKey&&(Algorithm==1||Algorithm==2)){double x;try{x=Double.parseDouble(Key);}catch(RuntimeException e){throw new IOException("请输入原密钥 / Enter the original key");}if(!(x>0&&x<1))throw new IOException("密钥须在 0 与 1 之间 / Key must be between 0 and 1");}
            if(Algorithm==3&&((Block!=8&&Block!=16&&Block!=32)||Seed==null||!Seed.matches("[0-9a-fA-F]{32}")))throw new IOException("分块参数无效 / Invalid block parameters");
        }
        JSONObject json()throws Exception{JSONObject j=new JSONObject();j.put("Version",Version);j.put("Algorithm",Algorithm);j.put("Variant",Variant);j.put("Block",Block);j.put("Width",Width);j.put("Height",Height);j.put("Strength",Strength);j.put("Seed",Seed);j.put("Key","");return j;}
        static Options parse(JSONObject j)throws Exception{Options o=new Options();o.Version=j.getInt("Version");o.Algorithm=j.getInt("Algorithm");o.Variant=j.optInt("Variant");o.Block=j.optInt("Block",16);o.Width=j.getInt("Width");o.Height=j.getInt("Height");o.Strength=j.optDouble("Strength",1);o.Seed=j.optString("Seed","");o.validate(false);if(o.Width<1||o.Height<1)throw new IOException("Invalid dimensions");return o;}
    }
    static String seed(){return UUID.randomUUID().toString().replace("-","");}
    static final class Random32 {
        int state;
        Random32(String seed){for(int i=0;i<32;i+=8)state^=(int)Long.parseLong(seed.substring(i,i+8),16);if(state==0)state=0x6d2b79f5;}
        long next(){state^=state<<13;state^=state>>>17;state^=state<<5;return state&0xffffffffL;}
        int pick(int n){long limit=(0x100000000L/n)*n,x;do{x=next();}while(x>=limit);return (int)(x%n);}
    }
    static int[] transform(int[] pixels,int w,int h,Options o,boolean reverse,Files.Job job)throws Exception{
        o.validate(true);job.check();if(w<1||h<1||(long)w*h!=pixels.length)throw new IOException("Invalid pixel dimensions");
        if(o.Algorithm==0){int[] map=ReferenceCurve.build(w,h,job);int shift=(int)(Math.floor(o.Strength*(Math.sqrt(5)-1)/2*map.length+.5)%map.length);for(int round=0;round<o.Rounds;round++){job.check();if(!reverse&&o.Green)ReferenceColors.adjust(pixels,true,job);int[] result=new int[pixels.length];for(int i=0;i<map.length;i++){if((i&65535)==0)job.check();int oldPos=map[i],newPos=map[(i+shift)%map.length];if(reverse)result[oldPos]=pixels[newPos];else result[newPos]=pixels[oldPos];}if(reverse&&o.ReduceGreen)ReferenceColors.adjust(result,false,job);pixels=result;}return pixels;}
        if(o.Algorithm==3)return blocks(pixels,w,h,o,reverse,job);
        double key=Double.parseDouble(o.Key);
        if(reverse&&o.Algorithm==2)axis(pixels,w,h,key,true,true,true,job);
        axis(pixels,w,h,key,false,reverse,o.Algorithm==2||o.Variant==1,job);
        if(!reverse&&o.Algorithm==2)axis(pixels,w,h,key,true,false,true,job);return pixels;
    }
    static void axis(int[] pixels,int w,int h,double seed,boolean columns,boolean reverse,boolean advance,Files.Job job)throws Exception{
        int length=columns?h:w,count=columns?w:h;double[] values=new double[length];Integer[] order=new Integer[length];int[] row=new int[length];double x=seed;
        for(int line=0;line<count;line++){job.check();if(!advance)x=seed;values[0]=x;order[0]=0;for(int j=1;j<length;j++){x=3.9999999*x*(1-x);values[j]=x;order[j]=j;}
            Arrays.sort(order,(a,b)->{int c=Double.compare(values[a],values[b]);return c==0?Integer.compare(a,b):c;});
            for(int j=0;j<length;j++)row[j]=pixels[columns?j*w+line:line*w+j];
            for(int j=0;j<length;j++){int pos=reverse?order[j]:j;pixels[columns?pos*w+line:line*w+pos]=row[reverse?j:order[j]];}
        }
    }
    static int[] blocks(int[] source,int w,int h,Options o,boolean reverse,Files.Job job)throws Exception{
        List<List<int[]>> groups=new ArrayList<>();List<Integer> sizes=new ArrayList<>();
        for(int y=0;y<h;y+=o.Block)for(int x=0;x<w;x+=o.Block){int tw=Math.min(o.Block,w-x),th=Math.min(o.Block,h-y),size=tw*64+th,g=sizes.indexOf(size);if(g<0){g=sizes.size();sizes.add(size);groups.add(new ArrayList<>());}groups.get(g).add(new int[]{x,y,tw,th});}
        Random32 rng=new Random32(o.Seed);int[] output=new int[source.length];
        for(List<int[]> tiles:groups){int n=tiles.size();int[] permutation=new int[n];for(int i=0;i<n;i++)permutation[i]=i;for(int i=n-1;i>0;i--){int j=rng.pick(i+1),t=permutation[i];permutation[i]=permutation[j];permutation[j]=t;}
            int[] shape=tiles.get(0);int tw=shape[2],th=shape[3];int[] curve=new Images.Curve(tw,th,job).map;int shift=(int)Math.floor(((Math.sqrt(5)-1)/2)*curve.length+.5)%curve.length;
            for(int i=0;i<n;i++){job.check();int[] a=tiles.get(i),b=tiles.get(permutation[i]);int rotation=rng.pick(tw==th?4:2),flip=rng.pick(2);
                for(int j=0;j<curve.length;j++){int p=curve[j],q=curve[(j+shift)%curve.length],xx=q%tw,yy=q/tw;if(flip!=0)xx=tw-1-xx;int rx=xx,ry=yy;
                    if(tw==th){if(rotation==1){rx=tw-1-yy;ry=xx;}else if(rotation==2){rx=tw-1-xx;ry=th-1-yy;}else if(rotation==3){rx=yy;ry=th-1-xx;}}else if(rotation!=0){rx=tw-1-xx;ry=th-1-yy;}
                    int from=(a[1]+p/tw)*w+a[0]+p%tw,to=(b[1]+ry)*w+b[0]+rx;if(reverse)output[from]=source[to];else output[to]=source[from];
                }
            }
        }return output;
    }
    static double continuity(int[] p,int w,int h){double sum=0;int n=0,step=Math.max(1,(int)Math.sqrt((long)w*h/12000.0));for(int y=0;y<h;y+=step)for(int x=0;x<w;x+=step){int i=y*w+x;if(x+1<w){sum+=diff(p[i],p[i+1]);n++;}if(y+1<h){sum+=diff(p[i],p[i+w]);n++;}}return sum/Math.max(1,n);}
    static int diff(int a,int b){return Math.abs((a&255)-(b&255))+Math.abs(((a>>>8)&255)-((b>>>8)&255))+Math.abs(((a>>>16)&255)-((b>>>16)&255));}
    static void append(OutputStream out,Options o)throws Exception{byte[] data=o.json().toString().getBytes(Files.UTF8),length=new byte[4];Files.le(length,0,data.length,4);out.write(data);out.write(length);out.write("AIPTPRM1".getBytes(Files.LATIN));}
    static Options read(File file)throws Exception{
        try(RandomAccessFile in=new RandomAccessFile(file,"r")){long end=in.length();if(end<12)return null;byte[] tail=new byte[12];in.seek(end-12);in.readFully(tail);String magic=new String(tail,4,8,Files.LATIN);
            if(magic.equals("AIPTSCR1")){int n=(int)Files.le(tail,0,4);if(n<2||n>8192||n>end-12)throw new IOException("Invalid original record");byte[] data=new byte[n];in.seek(end-12-n);in.readFully(data);JSONObject original=new JSONObject(new String(data,Files.UTF8));end=original.getLong("ImageLength");if(original.getInt("Schema")!=1||original.getLong("Length")<1||original.getLong("Length")!=in.length()-12-n-end||end<12||end>in.length()-12-n)throw new IOException("Invalid image length");in.seek(end-12);in.readFully(tail);magic=new String(tail,4,8,Files.LATIN);}
            if(!magic.equals("AIPTPRM1"))return null;int n=(int)Files.le(tail,0,4);if(n<2||n>4096||n>end-12)throw new IOException("Invalid parameter record");byte[] data=new byte[n];in.seek(end-12-n);in.readFully(data);return Options.parse(new JSONObject(new String(data,Files.UTF8)));
        }
    }
}
