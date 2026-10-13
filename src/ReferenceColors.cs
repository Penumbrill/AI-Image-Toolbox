using System;
using System.Threading;
namespace QQImageSwitch
{
    // Optional HSL-based green adjustment. This is lossy.
    static class ReferenceColors
    {
        public static void Adjust(int[] pixels,bool enhance,CancellationToken token)
        {
            for(int i=0;i<pixels.Length;i++)
            {
                if((i&65535)==0)token.ThrowIfCancellationRequested();int p=pixels[i],r=(p>>16)&255,g=(p>>8)&255,b=p&255;
                double red=r/255.0,green=g/255.0,blue=b/255.0,max=Math.Max(red,Math.Max(green,blue)),min=Math.Min(red,Math.Min(green,blue)),l=(max+min)/2,h=0,s=0;
                if(max!=min){double d=max-min;s=l>.5?d/(2-max-min):d/(max+min);h=max==red?(green-blue)/d+(green<blue?6:0):max==green?(blue-red)/d+2:(red-green)/d+4;h/=6;h*=360;}
                s*=100;l*=100;if(h<75||h>165||s<=8||l<=10||l>=90)continue;
                double gf=enhance?1+Math.Min(s,70)/100*.6:Math.Max(.5,1-Math.Min(s,80)/100*.5),rb=enhance?Math.Max(.7,1-Math.Min(s,70)/100*.3):Math.Min(1.3,1+Math.Min(s,80)/100*.3);
                r=Math.Min(255,(int)Math.Floor(r*rb+.5));g=Math.Min(255,(int)Math.Floor(g*gf+.5));b=Math.Min(255,(int)Math.Floor(b*rb+.5));pixels[i]=(p&unchecked((int)0xff000000))|(r<<16)|(g<<8)|b;
            }
        }
    }
}
