// Iterative Gilbert traversal. Flattened coordinates avoid per-pixel objects.
// Gilbert: Copyright (c) 2018 Jakub Cervený, BSD-2-Clause.
using System;
using System.Collections.Generic;
using System.Threading;
namespace QQImageSwitch {
 static class ReferenceCurve {
  public static int[] Build(int width,int height,CancellationToken token){
   var coordinates=new int[checked(width*height)];int index=0;var stack=new Stack<int[]>();
   stack.Push(width>=height?new[]{0,0,width,0,0,height}:new[]{0,0,0,height,width,0});
   while(stack.Count>0){token.ThrowIfCancellationRequested();var frame=stack.Pop();int x=frame[0],y=frame[1],ax=frame[2],ay=frame[3],bx=frame[4],by=frame[5];
    int w=Math.Abs(ax+ay),h=Math.Abs(bx+by),dax=Math.Sign(ax),day=Math.Sign(ay),dbx=Math.Sign(bx),dby=Math.Sign(by);
    if(h==1){for(int i=0;i<w;i++){coordinates[index++]=x+y*width;x+=dax;y+=day;}continue;}
    if(w==1){for(int i=0;i<h;i++){coordinates[index++]=x+y*width;x+=dbx;y+=dby;}continue;}
    int ax2=ax>>1,ay2=ay>>1,bx2=bx>>1,by2=by>>1,w2=Math.Abs(ax2+ay2),h2=Math.Abs(bx2+by2);
    if(2*w>3*h){if(w2%2!=0&&w>2){ax2+=dax;ay2+=day;}stack.Push(new[]{x+ax2,y+ay2,ax-ax2,ay-ay2,bx,by});stack.Push(new[]{x,y,ax2,ay2,bx,by});}
    else{if(h2%2!=0&&h>2){bx2+=dbx;by2+=dby;}stack.Push(new[]{x+(ax-dax)+(bx2-dbx),y+(ay-day)+(by2-dby),-bx2,-by2,-(ax-ax2),-(ay-ay2)});stack.Push(new[]{x+bx2,y+by2,ax,ay,bx-bx2,by-by2});stack.Push(new[]{x,y,bx2,by2,ax2,ay2});}
   }
   if(index!=coordinates.Length)throw new ArgumentException("Invalid reference curve");return coordinates;
  }
 }
}
