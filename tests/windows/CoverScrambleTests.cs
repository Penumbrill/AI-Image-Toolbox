using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using QQImageSwitch;

public static class CoverScrambleTests
{
    static CancellationToken token=CancellationToken.None;
    static List<object> rows=new List<object>();
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static Bitmap Art(bool hidden)
    {
        var image=new Bitmap(384,576);
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
            image.SetPixel(x,y,hidden?Color.FromArgb(255,40+x*150/image.Width,100+y*100/image.Height,60+(x+y)%120):Color.FromArgb(255,100+x*120/image.Width,80+y*100/image.Height,140+(x+y)%90));
        using(var g=Graphics.FromImage(image))using(var font=new Font("Arial",24))g.DrawString(hidden?"CONTENT / LOCAL TEST":"COVER / LOCAL TEST",font,Brushes.White,15,250);
        return image;
    }
    static void SamePixels(byte[] actual,Bitmap expected)
    {using(var image=Codec.Decode(actual)){Require(image.Size==expected.Size,"size changed");for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)Require(image.GetPixel(x,y)==expected.GetPixel(x,y),"hidden pixels changed");}}
    static bool CanRestoreDual(string path){try{Codec.Restore(File.ReadAllBytes(path));return true;}catch(InvalidDataException){return false;}}
    static bool CanRestoreFile(string path,string folder){try{PackagingPrototype.Restore(path,folder,token);return true;}catch(InvalidDataException){return false;}}
    static string Reencode(string input,string output){using(var image=Codec.Load(input))Codec.SaveAtomic(output,Codec.StaticPng(image));return output;}
    public static void Run(string output)
    {
        rows.Clear();string dir=Path.GetFullPath(output);Directory.CreateDirectory(dir);
        string source=Path.Combine(dir,"cover.png"),mixed=Path.Combine(dir,"scrambled-cover.png"),payload=Path.Combine(dir,"test-data.zip");
        using(var cover=Art(false))Codec.SaveAtomic(source,Codec.StaticPng(cover));
        using(var hidden=Art(true))Codec.SaveAtomic(Path.Combine(dir,"content.png"),Codec.StaticPng(hidden));
        using(var zip=new ZipArchive(File.Create(payload),ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("LOCAL_TEST.md").Open()))writer.Write("Synthetic local test payload. No private data.\n");
        Obfuscation.WriteReference(source,mixed,false,0,token,new ScrambleOptions());
        foreach(bool mix in new[]{false,true})
        {
            string stem=mix?"scrambled-cover":"normal-cover";
            using(var cover=Codec.Load(source))using(var hidden=Art(true))
            {
                string dual=Path.Combine(dir,"dual-"+stem+".png");Codec.SaveAtomic(dual,Batch.Build(cover,hidden,0,1,false,null,null,token,mix));
                SamePixels(Codec.Restore(File.ReadAllBytes(dual)),hidden);
                using(var expected=Codec.Load(mix?mixed:source))using(var displayed=Codec.Load(dual))Require(displayed.GetPixel(37,71)==expected.GetPixel(37,71),"cover flag not applied");
                bool reencoded=CanRestoreDual(Reencode(dual,Path.Combine(dir,"dual-"+stem+"-reencoded.png")));
                string whole=Path.Combine(dir,"dual-"+stem+"-whole-image-scrambled.png");Obfuscation.WriteReference(dual,whole,false,0,token,new ScrambleOptions());bool wholeSurvives=CanRestoreDual(whole);
                Require(!reencoded&&!wholeSurvives,"static output must not claim APNG restoration");
                rows.Add(new{Tool="dual",MixedCover=mix,ExactOriginalRestoration=true,StaticReencodingRetainsStructure=reencoded,WholeImageScrambleRetainsStructure=wholeSurvives});
                byte[] jpeg=FileBatch.Cover(source,1,false,null,mix,token);
                for(int mode=0;mode<4;mode++)
                {
                    string file=Path.Combine(dir,"file-"+stem+"-mode"+mode+(mode<2?".jpg":".png"));PackagingPrototype.Pack(jpeg,payload,file,mode,token);
                    string restored=PackagingPrototype.Restore(file,Path.Combine(dir,"restored"),token);
                    Require(File.ReadAllBytes(restored).SequenceEqual(File.ReadAllBytes(payload)),"file bytes differ");
                    bool retained=CanRestoreFile(Reencode(file,Path.Combine(dir,"file-"+stem+"-mode"+mode+"-reencoded.png")),Path.Combine(dir,"reencoded-restores"));
                    whole=Path.Combine(dir,"file-"+stem+"-mode"+mode+"-whole-image-scrambled.png");Obfuscation.WriteReference(file,whole,false,0,token,new ScrambleOptions());wholeSurvives=CanRestoreFile(whole,Path.Combine(dir,"whole-restores"));
                    Require(!retained&&!wholeSurvives,"image-only output must not claim packaged-file restoration");
                    rows.Add(new{Tool="file",MixedCover=mix,Mode=mode,ExactOriginalRestoration=true,StaticReencodingRetainsStructure=retained,WholeImageScrambleRetainsStructure=wholeSurvives});
                }
            }
        }
        using(var cancelled=new CancellationTokenSource())using(var cover=Codec.Load(source))using(var hidden=Art(true))
        {
            cancelled.Cancel();bool rejected=false;try{Batch.Build(cover,hidden,0,1,false,null,null,cancelled.Token,true);}catch(OperationCanceledException){rejected=true;}Require(rejected,"cancelled mixed export ran");
        }
        File.WriteAllText(Path.Combine(dir,"report.json"),Inspection.Json().Serialize(new{Status="PASS",PlatformUploadTested=false,SyntheticSamples=true,Results=rows}));
        Console.WriteLine("PASS: two dual-image and eight file-package round trips; scrambled cover preserves original structure, whole-image scrambling and re-encoding remove it. No platform upload performed.");
    }
}
