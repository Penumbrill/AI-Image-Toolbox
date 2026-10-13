from pathlib import Path
import random, subprocess, json, shutil, struct, os
root=Path(__file__).resolve().parents[2]
out=root/'build/verification/android-cover-trial';out.mkdir(exist_ok=True)
pkg='com.guangyingruoshui.imagetools';source=root/'android/src'/pkg.replace('.','/')
def run(args):
    p=subprocess.run(list(map(str,args)),capture_output=True,timeout=60)
    assert p.returncode==0,p.stdout.decode(errors='replace')+p.stderr.decode(errors='replace')
for name in ['ScrambleEngine.java','ReferenceColors.java','ReferenceCurve.java','CoverScramble.java']:
    shutil.copyfile(source/name,out/name)
images=(source/'Images.java').read_text('utf-8');curve=images[images.index('    static final class Curve'):images.index('    static Bitmap transform')]
(out/'Images.java').write_text('package '+pkg+';import java.io.*;class Images{'+curve+'}','utf-8')
(out/'Files.java').write_text('package '+pkg+';import java.io.*;import java.nio.charset.*;class Files{static Charset UTF8=StandardCharsets.UTF_8,LATIN=StandardCharsets.ISO_8859_1;static class Job{boolean cancelled;void check()throws InterruptedIOException{if(cancelled)throw new InterruptedIOException();}}static void le(byte[] a,int p,long x,int n){for(int i=0;i<n;i++)a[p+i]=(byte)(x>>>(8*i));}static long le(byte[] a,int p,int n){long x=0;for(int i=0;i<n;i++)x|=(a[p+i]&255L)<<(8*i);return x;}static byte[] read(RandomAccessFile f,int n)throws Exception{byte[] b=new byte[n];f.readFully(b);return b;}}','utf-8')
# Only the Android bitmap and IO wrappers are substituted on the host JVM.
(out/'Bitmap.java').write_text('package android.graphics;public class Bitmap{public int w,h;public int[] p;public Bitmap(int w,int h,int[] p){this.w=w;this.h=h;this.p=p.clone();}public int getWidth(){return w;}public int getHeight(){return h;}public void getPixels(int[] dst,int o,int stride,int x,int y,int a,int b){for(int j=0;j<b;j++)System.arraycopy(p,(y+j)*w+x,dst,o+j*stride,a);}public void setPixels(int[] src,int o,int stride,int x,int y,int a,int b){for(int j=0;j<b;j++)System.arraycopy(src,o+j*stride,p,(y+j)*w+x,a);}}','utf-8')
(out/'Runner.java').write_text('package '+pkg+''';import java.io.*;import java.util.*;import android.graphics.Bitmap;class Runner{public static void main(String[] a)throws Exception{try(DataInputStream in=new DataInputStream(new FileInputStream(a[0]));DataOutputStream out=new DataOutputStream(new FileOutputStream(a[1]))){for(int n=in.readInt();n>0;n--){int w=in.readInt(),h=in.readInt();int[] p=new int[w*h];for(int i=0;i<p.length;i++)p[i]=in.readInt();Bitmap b=new Bitmap(w,h,p);Files.Job j=new Files.Job();j.cancelled=true;boolean failed=false;try{CoverScramble.apply(b,j);}catch(InterruptedIOException ex){failed=true;}if(!failed||!Arrays.equals(p,b.p))throw new Exception("cancel mutated cover");j.cancelled=false;CoverScramble.apply(b,j);if(b.w!=w||b.h!=h)throw new Exception("resized cover");int[] restored=ScrambleEngine.transform(b.p.clone(),w,h,new ScrambleEngine.Options(),true,j);if(!Arrays.equals(p,restored))throw new Exception("inverse mismatch");for(int v:b.p)out.writeInt(v);}}}}''','utf-8')
rng=random.Random(17831);samples=[]
for w,h in [(1,1),(1,97),(99,1),(37,19),(96,128),(311,313)]:
    p=[rng.randrange(-2**31,2**31) for _ in range(w*h)]
    samples.append(dict(Width=w,Height=h,Pixels=p,Options=dict(Strength=1,Rounds=1)))
(out/'vectors.json').write_text(json.dumps(samples),'utf-8')
run([root/'build/windows/AI-Image-Editing-Tools.exe','--scramble-vectors',out/'vectors.json',out/'expected.json'])
with (out/'input.bin').open('wb') as f:
    f.write(struct.pack('>i',len(samples)))
    for s in samples:f.write(struct.pack('>ii',s['Width'],s['Height'])+struct.pack('>'+str(len(s['Pixels']))+'i',*s['Pixels']))
jdk=root/'build/toolchain/jdk17/bin';sdk=root/'build/toolchain/sdk/platforms/android-35/android.jar'
classes=out/'classes';classes.mkdir(exist_ok=True)
run([jdk/'javac.exe','-encoding','UTF-8','-cp',sdk,'-d',classes,*out.glob('*.java')])
run([jdk/'java.exe','-cp',str(classes)+os.pathsep+str(sdk),pkg+'.Runner',out/'input.bin',out/'result.bin'])
actual=(out/'result.bin').read_bytes();expected=json.loads((out/'expected.json').read_text('utf-8-sig'));at=0
for s in expected:
    n=len(s['Pixels']);assert list(struct.unpack_from('>'+str(n)+'i',actual,at))==s['Pixels'];at+=n*4
assert at==len(actual)
(out/'report.json').write_text(json.dumps(dict(Status='PASS',Cases=len(samples),HostBitmapStub=True,PhysicalDevice=False,Cancellation=True,PixelParity=True),indent=2),'utf-8')
print('PASS: six actual Android cover-helper cases match Windows pixels; cancellation and dimensions verified on host JVM')
