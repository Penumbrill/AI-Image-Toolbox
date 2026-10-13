using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace QQImageSwitch {
 static class ScrambleRounds {
  internal sealed class Candidate {public int Rounds;public double Strength,Score;}
  public static Candidate[] Rank(int[] pixels,int w,int h,double strength,CancellationToken token){
   new ScrambleOptions{Strength=strength}.Validate(false);int[] map=ReferenceCurve.Build(w,h,token),positions=new int[map.Length];for(int i=0;i<map.Length;i++)positions[map[i]]=i;
   int shift=(int)(Math.Floor(strength*(Math.Sqrt(5)-1)/2*map.Length+.5)%map.Length),step=Math.Max(1,(int)Math.Sqrt((long)w*h/12000.0));var candidates=new List<Candidate>();
   for(int rounds=1;rounds<=20;rounds++){token.ThrowIfCancellationRequested();int offset=(int)((long)shift*rounds%map.Length);double sum=0;int count=0;for(int y=0;y<h;y+=step){token.ThrowIfCancellationRequested();for(int x=0;x<w;x+=step){int i=y*w+x,a=pixels[map[(positions[i]+offset)%map.Length]];if(x+1<w){sum+=Difference(a,pixels[map[(positions[i+1]+offset)%map.Length]]);count++;}if(y+1<h){sum+=Difference(a,pixels[map[(positions[i+w]+offset)%map.Length]]);count++;}}}candidates.Add(new Candidate{Rounds=rounds,Score=sum/Math.Max(1,count)});}
   return candidates.OrderBy(c=>c.Score).ThenBy(c=>c.Rounds).ToArray();
  }
  public static Candidate[] RankParameters(int[] pixels,int w,int h,double preferredStrength,int preferredRounds,CancellationToken token){
   int[] map=ReferenceCurve.Build(w,h,token),positions=new int[map.Length];for(int i=0;i<map.Length;i++)positions[map[i]]=i;
   int step=Math.Max(1,(int)Math.Sqrt((long)w*h/12000.0));var unique=new Dictionary<int,Candidate>();
   var strengths=new List<double>{preferredStrength};for(int n=0;n<=20;n++)if(Math.Abs(preferredStrength-n/10.0)>1e-9)strengths.Add(n/10.0);
   foreach(double strength in strengths){new ScrambleOptions{Strength=strength}.Validate(false);int shift=(int)(Math.Floor(strength*(Math.Sqrt(5)-1)/2*map.Length+.5)%map.Length);
    foreach(int rounds in Enumerable.Range(1,20).OrderBy(n=>n==preferredRounds?0:1)){token.ThrowIfCancellationRequested();int offset=(int)((long)shift*rounds%map.Length);if(unique.ContainsKey(offset))continue;
     double sum=0;int count=0;for(int y=0;y<h;y+=step){token.ThrowIfCancellationRequested();for(int x=0;x<w;x+=step){int i=y*w+x,a=pixels[map[(positions[i]+offset)%map.Length]];if(x+1<w){sum+=Difference(a,pixels[map[(positions[i+1]+offset)%map.Length]]);count++;}if(y+1<h){sum+=Difference(a,pixels[map[(positions[i+w]+offset)%map.Length]]);count++;}}}
     unique.Add(offset,new Candidate{Strength=strength,Rounds=rounds,Score=sum/Math.Max(1,count)});
    }
   }return unique.Values.OrderBy(c=>c.Score).ThenBy(c=>Math.Abs(c.Strength-preferredStrength)).ThenBy(c=>c.Rounds==preferredRounds?0:1).ThenBy(c=>c.Rounds).ThenBy(c=>c.Strength).ToArray();
  }
  static int Difference(int a,int b){return Math.Abs((a&255)-(b&255))+Math.Abs(((a>>8)&255)-((b>>8)&255))+Math.Abs(((a>>16)&255)-((b>>16)&255));}
 }
}
