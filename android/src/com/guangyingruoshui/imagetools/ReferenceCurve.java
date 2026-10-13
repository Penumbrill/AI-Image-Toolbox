package com.guangyingruoshui.imagetools;
import java.util.ArrayDeque;
// Iterative Gilbert traversal. Flattened coordinates avoid per-pixel objects.
// Gilbert: Copyright (c) 2018 Jakub Cervený, BSD-2-Clause.
final class ReferenceCurve {
 static int[] build(int width,int height,Files.Job job)throws Exception{
  int[] coordinates=new int[Math.multiplyExact(width,height)];int index=0;ArrayDeque<int[]> stack=new ArrayDeque<>();
  stack.push(width>=height?new int[]{0,0,width,0,0,height}:new int[]{0,0,0,height,width,0});
  while(!stack.isEmpty()){job.check();int[] frame=stack.pop();int x=frame[0],y=frame[1],ax=frame[2],ay=frame[3],bx=frame[4],by=frame[5];
   int w=Math.abs(ax+ay),h=Math.abs(bx+by),dax=Integer.signum(ax),day=Integer.signum(ay),dbx=Integer.signum(bx),dby=Integer.signum(by);
   if(h==1){for(int i=0;i<w;i++){coordinates[index++]=x+y*width;x+=dax;y+=day;}continue;}
   if(w==1){for(int i=0;i<h;i++){coordinates[index++]=x+y*width;x+=dbx;y+=dby;}continue;}
   int ax2=ax>>1,ay2=ay>>1,bx2=bx>>1,by2=by>>1,w2=Math.abs(ax2+ay2),h2=Math.abs(bx2+by2);
   if(2*w>3*h){if(w2%2!=0&&w>2){ax2+=dax;ay2+=day;}stack.push(new int[]{x+ax2,y+ay2,ax-ax2,ay-ay2,bx,by});stack.push(new int[]{x,y,ax2,ay2,bx,by});}
   else{if(h2%2!=0&&h>2){bx2+=dbx;by2+=dby;}stack.push(new int[]{x+(ax-dax)+(bx2-dbx),y+(ay-day)+(by2-dby),-bx2,-by2,-(ax-ax2),-(ay-ay2)});stack.push(new int[]{x+bx2,y+by2,ax,ay,bx-bx2,by-by2});stack.push(new int[]{x,y,bx2,by2,ax2,ay2});}
  }
  if(index!=coordinates.length)throw new IllegalArgumentException("Invalid reference curve");return coordinates;
 }
}
