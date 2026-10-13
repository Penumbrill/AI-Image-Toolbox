using System;
using System.IO;
using System.Threading;
using System.Linq;
namespace QQImageSwitch {
 static class ScrambleEngineTests {
  public static void Run(string input,string output) {
   var j=Inspection.Json();j.MaxJsonLength=int.MaxValue;var samples=j.Deserialize<Sample[]>(File.ReadAllText(input));
   foreach(var s in samples){var pixels=(int[])s.Pixels.Clone();var result=ScrambleEngine.Transform(pixels,s.Width,s.Height,s.Options,s.Reverse,CancellationToken.None);var restored=ScrambleEngine.Transform((int[])result.Clone(),s.Width,s.Height,s.Options,true,CancellationToken.None);if(!s.Reverse&&!s.Options.Green&&!s.Options.ReduceGreen)for(int i=0;i<restored.Length;i++)if(restored[i]!=s.Pixels[i])throw new Exception("Pixel round trip failed");s.Pixels=result;}
   File.WriteAllText(output,j.Serialize(samples));
  }
  public sealed class Sample {public bool Reverse{get;set;}public int Width{get;set;}public int Height{get;set;}public int[] Pixels{get;set;}public ScrambleOptions Options{get;set;}}
  public static void RankParameters(string input,string output){var j=Inspection.Json();j.MaxJsonLength=int.MaxValue;var samples=j.Deserialize<Sample[]>(File.ReadAllText(input));File.WriteAllText(output,j.Serialize(samples.Select(s=>ScrambleRounds.RankParameters(s.Pixels,s.Width,s.Height,s.Options.Strength,s.Options.Rounds,CancellationToken.None).Take(3)).ToArray()));}
  public static void Rank(string input,string output){var j=Inspection.Json();j.MaxJsonLength=int.MaxValue;var samples=j.Deserialize<Sample[]>(File.ReadAllText(input));File.WriteAllText(output,j.Serialize(samples.Select(s=>ScrambleRounds.Rank(s.Pixels,s.Width,s.Height,s.Options.Strength,CancellationToken.None)).ToArray()));}
 }
}
