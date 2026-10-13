package com.guangyingruoshui.imagetools;
import java.util.*;
final class ScrambleRounds {
 static final class Candidate {int rounds;double strength,score;Candidate(int n,double s){rounds=n;score=s;}}
 static Candidate[] rank(int[] pixels,int w,int h,double strength,Files.Job job)throws Exception{
  ScrambleEngine.Options o=new ScrambleEngine.Options();o.Strength=strength;o.validate(false);int[] map=ReferenceCurve.build(w,h,job),positions=new int[map.length];for(int i=0;i<map.length;i++)positions[map[i]]=i;
  int shift=(int)(Math.floor(strength*(Math.sqrt(5)-1)/2*map.length+.5)%map.length),step=Math.max(1,(int)Math.sqrt((long)w*h/12000.0));ArrayList<Candidate> candidates=new ArrayList<>();
  for(int rounds=1;rounds<=20;rounds++){job.check();int offset=(int)((long)shift*rounds%map.length);double sum=0;int count=0;for(int y=0;y<h;y+=step){job.check();for(int x=0;x<w;x+=step){int i=y*w+x,a=pixels[map[(positions[i]+offset)%map.length]];if(x+1<w){sum+=difference(a,pixels[map[(positions[i+1]+offset)%map.length]]);count++;}if(y+1<h){sum+=difference(a,pixels[map[(positions[i+w]+offset)%map.length]]);count++;}}}candidates.add(new Candidate(rounds,sum/Math.max(1,count)));}
  candidates.sort((a,b)->{int c=Double.compare(a.score,b.score);return c==0?Integer.compare(a.rounds,b.rounds):c;});return candidates.toArray(new Candidate[0]);
 }
 static Candidate[] rankParameters(int[] pixels,int w,int h,double preferredStrength,int preferredRounds,Files.Job job)throws Exception{
  int[] map=ReferenceCurve.build(w,h,job),positions=new int[map.length];for(int i=0;i<map.length;i++)positions[map[i]]=i;int step=Math.max(1,(int)Math.sqrt((long)w*h/12000.0));HashMap<Integer,Candidate> unique=new HashMap<>();ArrayList<Double> strengths=new ArrayList<>();strengths.add(preferredStrength);for(int n=0;n<=20;n++)if(Math.abs(preferredStrength-n/10.0)>1e-9)strengths.add(n/10.0);
  for(double strength:strengths){ScrambleEngine.Options o=new ScrambleEngine.Options();o.Strength=strength;o.validate(false);int shift=(int)(Math.floor(strength*(Math.sqrt(5)-1)/2*map.length+.5)%map.length);for(int q=0;q<=20;q++){int rounds=q==0?preferredRounds:q;if(rounds<1||rounds>20||(q>0&&rounds==preferredRounds))continue;job.check();int offset=(int)((long)shift*rounds%map.length);if(unique.containsKey(offset))continue;double sum=0;int count=0;for(int y=0;y<h;y+=step){job.check();for(int x=0;x<w;x+=step){int i=y*w+x,a=pixels[map[(positions[i]+offset)%map.length]];if(x+1<w){sum+=difference(a,pixels[map[(positions[i+1]+offset)%map.length]]);count++;}if(y+1<h){sum+=difference(a,pixels[map[(positions[i+w]+offset)%map.length]]);count++;}}}Candidate c=new Candidate(rounds,sum/Math.max(1,count));c.strength=strength;unique.put(offset,c);}}
  ArrayList<Candidate> values=new ArrayList<>(unique.values());values.sort((a,b)->{int c=Double.compare(a.score,b.score);if(c==0)c=Double.compare(Math.abs(a.strength-preferredStrength),Math.abs(b.strength-preferredStrength));if(c==0)c=Integer.compare(a.rounds==preferredRounds?0:1,b.rounds==preferredRounds?0:1);if(c==0)c=Integer.compare(a.rounds,b.rounds);return c==0?Double.compare(a.strength,b.strength):c;});return values.toArray(new Candidate[0]);
 }
 static int difference(int a,int b){return Math.abs((a&255)-(b&255))+Math.abs(((a>>8)&255)-((b>>8)&255))+Math.abs(((a>>16)&255)-((b>>16)&255));}
}
