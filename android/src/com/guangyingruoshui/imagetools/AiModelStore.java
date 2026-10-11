package com.guangyingruoshui.imagetools;

import android.content.Context;
import android.content.SharedPreferences;
import org.json.*;
import java.io.*;
import java.net.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;

/** Pinned, resumable model preparation. No network access is required for valid caches. */
final class AiModelStore {
    static final String OFFICIAL="https://huggingface.co",MIRROR="https://hf-mirror.com";
    static class Resource {
        String target,repo,revision,path,local,sha;long size;
        String relative(){return target+"/"+local;}
        String suffix(){return "/"+repo+"/resolve/"+revision+"/"+path;}
    }
    static class Snapshot {
        final long bytes,total;final int ready,count,percent;final String details,phase;
        Snapshot(long b,long t,int r,int c,String d,String p){bytes=b;total=t;ready=r;count=c;details=d;phase=p;percent=t<=0?0:(int)Math.min(c>0&&r<c?99:100,Math.max(0,b*100.0/t));}
    }
    interface Reporter {void update(Snapshot progress);}
    final Context context;final SharedPreferences prefs;final ArrayList<Resource> resources=new ArrayList<>();
    final ArrayList<File> roots=new ArrayList<>();File root;JSONObject verified;
    AiModelStore(Context c)throws Exception {
        context=c;prefs=c.getSharedPreferences("image-tools",Context.MODE_PRIVATE);
        try(InputStream in=c.getAssets().open("android-ai-models.json")){load(new JSONObject(readText(in)));}
        for(File external:c.getExternalFilesDirs(null))if(external!=null)roots.add(new File(external,"AI-Models"));
        roots.add(new File(c.getFilesDir(),"AI-Models"));String saved=prefs.getString("androidAiRoot","");
        for(File option:roots)if(option.getCanonicalPath().equals(saved)){root=option;break;}
        if(root==null)for(File option:roots){File parent=option.getParentFile();if(parent!=null&&parent.isDirectory()&&parent.getUsableSpace()>6L*1024*1024*1024){root=option;break;}}
        if(root==null)root=roots.get(roots.size()-1);
        if(!root.isDirectory()&&!root.mkdirs())throw new IOException("无法创建模型目录 / Cannot create model folder");
        prefs.edit().putString("androidAiRoot",root.getCanonicalPath()).apply();verified=readRecord(root);
    }
    // Host regression constructor uses the identical downloader, integrity and cleanup code.
    AiModelStore(File directory,JSONObject manifest)throws Exception {context=null;prefs=null;root=directory;roots.add(root);if(!root.isDirectory()&&!root.mkdirs())throw new IOException("Cannot create model folder");load(manifest);verified=readRecord(root);}
    void load(JSONObject manifest)throws Exception {
        JSONArray packages=manifest.getJSONArray("packages");HashSet<String> destinations=new HashSet<>();
        for(int n=0;n<packages.length();n++){JSONObject pack=packages.getJSONObject(n);JSONArray files=pack.getJSONArray("files");
            for(int i=0;i<files.length();i++){JSONObject item=files.getJSONObject(i);Resource r=new Resource();r.repo=pack.getString("repo");r.revision=pack.getString("revision");r.target=pack.getString("target");r.path=item.getString("path");r.local=item.getString("local");r.size=item.getLong("size");r.sha=item.getString("sha256");
                if(!r.repo.matches("[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+")||!r.revision.matches("[a-f0-9]{40}")||!r.sha.matches("[a-f0-9]{64}")||r.size<1||r.size>64L*1024*1024*1024)throw new IOException("Invalid pinned model manifest");
                validateRelative(r.target);validateRelative(r.local);validateRelative(r.path);
                if(!r.path.matches("[A-Za-z0-9_./-]+")||!destinations.add(r.relative())||r.local.endsWith(".part"))throw new IOException("Invalid or duplicate resource path");
                safe(root==null?new File("."):root,r.relative());resources.add(r);}
        }
    }
    static void validateRelative(String relative)throws IOException {
        if(relative==null||relative.isEmpty()||relative.startsWith("/")||relative.contains("\\")||relative.contains(":"))throw new IOException("Invalid model path");
        for(String part:relative.split("/",-1))if(part.isEmpty()||part.equals(".")||part.equals(".."))throw new IOException("Invalid model path component");
    }
    static File safe(File directory,String relative)throws IOException {
        validateRelative(relative);File f=new File(directory,relative);String base=directory.getCanonicalPath();if(!base.endsWith(File.separator))base+=File.separator;
        if(!f.getCanonicalPath().startsWith(base))throw new IOException("Model path escapes its folder");return f;
    }
    static String readText(InputStream in)throws IOException {ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] b=new byte[8192];int n;while((n=in.read(b))!=-1){if(out.size()+n>8*1024*1024)throw new IOException("Oversized model metadata");out.write(b,0,n);}return new String(out.toByteArray(),Files.UTF8);}
    static JSONObject readRecord(File dir){try(InputStream in=new FileInputStream(safe(dir,"verified-models.json"))){return new JSONObject(readText(in));}catch(Exception ignored){return new JSONObject();}}
    synchronized void saveRecord()throws Exception {
        writeRecord(root,verified);
    }
    static void writeRecord(File directory,JSONObject values)throws Exception {
        File temp=safe(directory,"verified-models.json.tmp"),out=safe(directory,"verified-models.json");
        try(FileOutputStream stream=new FileOutputStream(temp)){stream.write(values.toString().getBytes(Files.UTF8));stream.getFD().sync();}
        androidCommit(temp,out);
    }
    synchronized void forget(File f)throws Exception {
        String canonical=f.getCanonicalPath();verified.remove(canonical);
        for(File directory:roots)if(!directory.getCanonicalFile().equals(root.getCanonicalFile())){JSONObject other=readRecord(directory);if(other.remove(canonical)!=null)writeRecord(directory,other);}
    }
    synchronized void record(File f,String hash)throws Exception {
        JSONObject item=new JSONObject();item.put("bytes",f.length());item.put("modified",f.lastModified());item.put("sha256",hash);item.put("header",headerHash(f));verified.put(f.getCanonicalPath(),item);saveRecord();
    }
    static void androidCommit(File temp,File out)throws IOException {
        // rename is atomic on app-owned storage; no delete-before-rename for records.
        if(!temp.renameTo(out))throw new IOException("Cannot commit model record");
    }
    static String headerHash(File f)throws Exception{try(InputStream in=new FileInputStream(f)){byte[] b=new byte[4096];int n=in.read(b);return Files.hex(MessageDigest.getInstance("SHA-256").digest(Arrays.copyOf(b,Math.max(0,n))));}}
    synchronized boolean recordMatches(File f,Resource r)throws Exception {
        if(!f.isFile()||f.length()!=r.size)return false;JSONObject item=verified.optJSONObject(f.getCanonicalPath());
        return item!=null&&item.optLong("bytes",-1)==f.length()&&item.optLong("modified",-1)==f.lastModified()&&r.sha.equals(item.optString("sha256"))&&headerHash(f).equals(item.optString("header"));
    }
    synchronized boolean cached(File f,Resource r)throws Exception {
        // A size/mtime/header match cannot establish integrity of the rest of a model.
        return recordMatches(f,r)&&r.sha.equals(Files.hash(f,new Files.Job()));
    }
    File path(String target,String local)throws IOException {return safe(root,target+"/"+local);}
    Resource byRelative(String relative){for(Resource r:resources)if(r.relative().equals(relative))return r;return null;}
    void verifySelected(File file,Files.Job job)throws Exception {
        String canonical=file.getCanonicalPath();
        for(File directory:roots)for(Resource r:resources)if(safe(directory,r.relative()).getCanonicalPath().equals(canonical)){if(!r.sha.equals(Files.hash(file,job)))throw new IOException("所选模型校验失败，请修复 / Selected model checksum failed; repair the model");return;}
        if(!registered(file))throw new IOException("所选模型未登记或已改变，请重新导入 / Selected model is unregistered or changed; import it again");
        JSONObject item=verified.optJSONObject(canonical);if(item==null)for(File directory:roots){item=readRecord(directory).optJSONObject(canonical);if(item!=null)break;}
        if(item==null||!item.optString("sha256").equals(Files.hash(file,job)))throw new IOException("所选模型校验失败，请重新导入 / Selected model checksum failed; import it again");
        validateCheckpoint(file,file.getName().endsWith(".gguf"));
    }
    ArrayList<Resource> select(Set<String> targets){ArrayList<Resource> list=new ArrayList<>();for(Resource r:resources)if(targets.contains(r.target))list.add(r);return list;}
    synchronized File locate(Resource r,boolean force,Files.Job job)throws Exception {
        ArrayList<File> ordered=new ArrayList<>();ordered.add(root);for(File d:roots)if(!d.equals(root))ordered.add(d);
        for(File dir:ordered){File f=safe(dir,r.relative());if(!f.isFile()||f.length()!=r.size)continue;
            job.check();if(r.sha.equals(Files.hash(f,job))){if(force||!recordMatches(f,r))record(f,r.sha);job.check();return f;}}
        return null;
    }
    static class ProgressState {
        final ArrayList<Resource> list;final long[] bytes,speed;final String[] phase;final boolean[] ready;final Reporter reporter;long last;
        ProgressState(ArrayList<Resource> l,Reporter r){list=l;reporter=r;bytes=new long[l.size()];speed=new long[l.size()];phase=new String[l.size()];ready=new boolean[l.size()];Arrays.fill(phase,"等待 / Waiting");}
        synchronized void set(int i,long value,long rate,String state,boolean complete,boolean force){bytes[i]=Math.min(list.get(i).size,Math.max(0,value));speed[i]=Math.max(0,rate);phase[i]=state;ready[i]=complete;
            if(reporter==null)return;long now=System.nanoTime();if(!force&&now-last<150000000)return;last=now;long done=0,total=0;int n=0;StringBuilder d=new StringBuilder();
            for(int k=0;k<list.size();k++){Resource item=list.get(k);done+=bytes[k];total+=item.size;if(ready[k])n++;if(!ready[k]||list.size()<6)d.append(item.relative()).append(" · ").append(format(bytes[k])).append(" / ").append(format(item.size)).append(" · ").append(format(speed[k])).append("/s · ").append(phase[k]).append('\n');}
            reporter.update(new Snapshot(done,total,n,list.size(),d.toString(),state));}
    }
    static String format(long n){return n>=1073741824?String.format(Locale.ROOT,"%.2f GiB",n/1073741824.0):n>=1048576?String.format(Locale.ROOT,"%.1f MiB",n/1048576.0):n>=1024?String.format(Locale.ROOT,"%.1f KiB",n/1024.0):n+" B";}
    void prepare(Set<String> targets,String source,boolean force,Files.Job job,Reporter reporter)throws Exception {
        ArrayList<Resource> needed=select(targets);if(needed.isEmpty())throw new IOException("No compatible resources selected");ProgressState state=new ProgressState(needed,reporter);ArrayList<Integer> missing=new ArrayList<>();long remaining=0;
        for(int i=0;i<needed.size();i++){job.check();Resource r=needed.get(i);state.set(i,0,0,"校验 / Verifying",false,true);File available=locate(r,force,job);if(available!=null){state.set(i,r.size,0,"已校验 / Verified",true,true);continue;}File part=safe(root,r.relative()+".part");long done=part.isFile()&&part.length()<=r.size?part.length():0;state.set(i,done,0,"待下载 / Queued",false,true);remaining+=r.size-done;missing.add(i);}
        if(missing.isEmpty())return;
        long fullMissing=256L*1024*1024;for(int i:missing)fullMissing+=needed.get(i).size;
        if(root.getUsableSpace()<remaining+256L*1024*1024&&context!=null){for(File directory:roots){File parent=directory.getParentFile();if(parent!=null&&parent.getUsableSpace()>=fullMissing&&(directory.isDirectory()||directory.mkdirs())){saveRecord();root=directory;verified=readRecord(root);prefs.edit().putString("androidAiRoot",root.getCanonicalPath()).apply();remaining=fullMissing-256L*1024*1024;for(int i:missing)state.set(i,0,0,"更换存储位置 / Changed storage",false,true);break;}}}
        Files.space(root,remaining+256L*1024*1024);List<String> domains=chooseSources(source,needed.get(missing.get(0)).suffix(),job);
        ExecutorService pool=Executors.newFixedThreadPool(Math.min(3,missing.size()));ArrayList<Future<?>> tasks=new ArrayList<>();Files.Job downloads=new Files.Job();
        try{for(int index:missing)tasks.add(pool.submit(()->{try{download(needed.get(index),index,state,domains,job,downloads);}catch(Exception e){throw new CompletionException(e);}}));
            for(Future<?> task:tasks){while(true){job.check();try{task.get(200,TimeUnit.MILLISECONDS);break;}catch(TimeoutException wait){}catch(ExecutionException error){Throwable cause=error.getCause();if(cause instanceof CompletionException)cause=cause.getCause();if(cause instanceof Exception)throw (Exception)cause;throw new IOException("Model download failed",cause);}}}
            for(int i=0;i<needed.size();i++){job.check();if(locate(needed.get(i),false,job)==null)throw new IOException("Required model is incomplete: "+needed.get(i).relative());state.set(i,needed.get(i).size,0,"已校验 / Verified",true,true);}
        }finally{downloads.cancelled=true;pool.shutdownNow();pool.awaitTermination(35,TimeUnit.SECONDS);}
    }
    List<String> chooseSources(String policy,String suffix,Files.Job job)throws Exception {
        job.check();
        if("official".equals(policy))return Collections.singletonList(OFFICIAL);if("mirror".equals(policy))return Collections.singletonList(MIRROR);
        boolean china=Locale.getDefault().getCountry().equalsIgnoreCase("CN");if(context!=null)try{android.telephony.TelephonyManager t=(android.telephony.TelephonyManager)context.getSystemService(Context.TELEPHONY_SERVICE);china|="cn".equalsIgnoreCase(t.getNetworkCountryIso());}catch(Exception ignored){}
        String first=china?MIRROR:OFFICIAL,second=china?OFFICIAL:MIRROR;long a=probe(first+suffix,job),b=probe(second+suffix,job);
        job.check();
        if(a==Long.MAX_VALUE&&b==Long.MAX_VALUE)throw new IOException("官方与镜像均不可达 / Both model sources are unavailable");
        return (china&&a!=Long.MAX_VALUE||a<=b)?Arrays.asList(first,second):Arrays.asList(second,first);
    }
    static long probe(String url,Files.Job job){long start=System.nanoTime();try{job.check();HttpURLConnection c=open(url,0,"HEAD");try{int status=c.getResponseCode();return status>=200&&status<300?System.nanoTime()-start:Long.MAX_VALUE;}finally{c.disconnect();}}catch(Exception ignored){return Long.MAX_VALUE;}}
    static HttpURLConnection open(String address,long start,String method)throws IOException {
        for(int n=0;n<8;n++){
            URL u=new URL(address);if(!u.getProtocol().equals("https")||u.getHost().isEmpty()||u.getUserInfo()!=null)throw new IOException("Model downloads require HTTPS");
            HttpURLConnection c=(HttpURLConnection)u.openConnection();
            try{
                c.setConnectTimeout(9000);c.setReadTimeout(15000);c.setInstanceFollowRedirects(false);c.setRequestMethod(method);c.setRequestProperty("Accept-Encoding","identity");c.setRequestProperty("User-Agent","AI-Image-Toolbox-Android/2");if(start>0)c.setRequestProperty("Range","bytes="+start+"-");
                int status=c.getResponseCode();if(status==301||status==302||status==303||status==307||status==308){String next=c.getHeaderField("Location");c.disconnect();if(next==null)throw new IOException("Missing redirect");address=new URL(u,next).toString();continue;}return c;
            }catch(IOException|RuntimeException failure){c.disconnect();throw failure;}
        }
        throw new IOException("Too many model redirects");
    }
    void download(Resource r,int index,ProgressState state,List<String> sources,Files.Job job,Files.Job downloads)throws Exception {
        File out=safe(root,r.relative()),part=safe(root,r.relative()+".part");if(!out.getParentFile().isDirectory()&&!out.getParentFile().mkdirs())throw new IOException("Cannot create model package");Exception last=null;
        for(String domain:sources)for(int retry=0;retry<2;retry++){job.check();downloads.check();try{
            if(part.length()>r.size&&!part.delete())throw new IOException("Oversized download fragment");long start=part.isFile()?part.length():0;
            if(start<r.size){HttpURLConnection c=open(domain+r.suffix(),start,"GET");try{int status=c.getResponseCode();if(status!=200&&status!=206)throw new IOException("模型下载 HTTP / Model HTTP "+status);
                if(status==206){String range=c.getHeaderField("Content-Range");if(range==null||!range.matches("bytes "+start+"-[0-9]+/"+r.size))throw new IOException("Invalid resumed Content-Range");}else start=0;
                long count=start,mark=start,time=System.nanoTime();state.set(index,count,0,domain.equals(MIRROR)?"镜像下载 / Mirror":"官方下载 / Official",false,true);
                try(InputStream in=new BufferedInputStream(c.getInputStream());FileOutputStream stream=new FileOutputStream(part,start>0)){byte[] buffer=new byte[1024*256];int n;while((n=in.read(buffer))!=-1){job.check();downloads.check();if(count+n>r.size)throw new IOException("Model exceeds pinned size");stream.write(buffer,0,n);count+=n;long now=System.nanoTime();if(now-time>=200000000){state.set(index,count,(long)((count-mark)*1e9/(now-time)),"下载 / Downloading",false,false);mark=count;time=now;}}stream.getFD().sync();}
                if(count!=r.size)throw new EOFException("模型未下载完整 / Model download incomplete");
            }finally{c.disconnect();}}
            job.check();downloads.check();state.set(index,r.size,0,"校验 SHA-256 / Verifying SHA-256",false,true);Files.Job hashJob=new Files.Job();hashJob.progress=(d,t)->{hashJob.cancelled=job.cancelled||downloads.cancelled;state.set(index,r.size,0,"SHA-256 "+d*100/t+"%",false,false);};String actual=Files.hash(part,hashJob);job.check();downloads.check();
            if(!r.sha.equals(actual)){part.delete();throw new IOException("模型校验失败 / Model SHA-256 mismatch: "+r.relative());}
            if(out.exists()&&!out.delete())throw new IOException("Cannot replace invalid model");Files.commit(part,out,job);record(out,r.sha);state.set(index,r.size,0,"已校验 / Verified",true,true);return;
        }catch(InterruptedIOException cancel){throw cancel;}catch(Exception failure){last=failure;state.set(index,part.isFile()?part.length():0,0,"重试 / Retrying",false,true);}}
        throw last==null?new IOException("Download failed"):last;
    }
    synchronized File importModel(InputStream input,String destination,Files.Job job,Reporter reporter)throws Exception {
        job.check();File out=safe(root,"custom/"+destination),part=safe(root,"custom/"+destination+".part");if(!out.getParentFile().isDirectory()&&!out.getParentFile().mkdirs())throw new IOException("Cannot create custom model folder");
        MessageDigest md=MessageDigest.getInstance("SHA-256");long count=0,start=System.nanoTime(),last=start;
        try{
            try(FileOutputStream stream=new FileOutputStream(part)){byte[] b=new byte[256*1024];int n;
                while((n=input.read(b))!=-1){job.check();Files.space(root,n);stream.write(b,0,n);md.update(b,0,n);count+=n;long now=System.nanoTime();if(reporter!=null&&now-last>200000000){reporter.update(new Snapshot(count,0,0,1,destination+" · "+format(count)+" · "+format((long)(count*1e9/(now-start)))+"/s","导入 / Importing"));last=now;}}stream.getFD().sync();}
            job.check();String sha=Files.hex(md.digest());validateCheckpoint(part,destination.endsWith(".gguf"));
            File existing=null;
            for(Resource r:resources)if(r.size==count&&r.sha.equals(sha)){existing=locate(r,false,job);if(existing!=null)break;}
            if(existing==null)for(File f:installed("")){job.check();if(f.length()==count&&Files.hash(f,job).equals(sha)){existing=f;break;}}
            if(existing!=null){if(!part.delete())throw new IOException("Cannot remove duplicate import");if(prefs!=null)prefs.edit().putString(destination.startsWith("base-")?"androidAiCustomBase":"androidAiCustomControl",existing.getCanonicalPath()).apply();if(reporter!=null)reporter.update(new Snapshot(count,count,1,1,"已复用 / Reused: "+existing.getName(),"已校验 / Verified"));return existing;}
            if(out.isFile()&&out.length()==count&&Files.hash(out,job).equals(sha)){record(out,sha);if(reporter!=null)reporter.update(new Snapshot(count,count,1,1,destination,"已校验 / Verified"));return out;}
            if(out.exists()&&!out.delete())throw new IOException("Cannot replace imported model");job.check();Files.commit(part,out,job);record(out,sha);if(reporter!=null)reporter.update(new Snapshot(count,count,1,1,destination,"已校验 / Verified"));return out;
        }finally{part.delete();}
    }
    static void validateCheckpoint(File f,boolean gguf)throws Exception {
        try(RandomAccessFile r=new RandomAccessFile(f,"r")){
            if(gguf){byte[] header=Files.read(r,24);long version=Files.le(header,4,4),tensors=Files.le(header,8,8),metadata=Files.le(header,16,8);
                if(!new String(header,0,4,Files.LATIN).equals("GGUF")||(version!=2&&version!=3)||tensors<10||tensors>(f.length()-24)/24||metadata<1||metadata>(f.length()-24)/12)throw new IOException("Invalid or incomplete GGUF model");return;}
            long length=Files.le(Files.read(r,8),0,8);if(length<2||length>16*1024*1024||length>f.length()-8)throw new IOException("Invalid safetensors header");
            long dataLength=f.length()-8-length;JSONObject tensors=new JSONObject(new String(Files.read(r,(int)length),Files.UTF8));Iterator<String> names=tensors.keys();ArrayList<long[]> intervals=new ArrayList<>();int count=0;
            while(names.hasNext()){String name=names.next();if(name.equals("__metadata__"))continue;JSONObject tensor=tensors.getJSONObject(name);JSONArray offsets=tensor.getJSONArray("data_offsets"),shape=tensor.getJSONArray("shape");if(offsets.length()!=2)throw new IOException("Invalid tensor offsets");long start=offsets.getLong(0),end=offsets.getLong(1);Files.range(start,end-start,dataLength);
                String dtype=tensor.getString("dtype");int width=dtype.equals("F64")||dtype.equals("I64")||dtype.equals("U64")?8:dtype.equals("F32")||dtype.equals("I32")||dtype.equals("U32")?4:dtype.equals("F16")||dtype.equals("BF16")||dtype.equals("I16")||dtype.equals("U16")?2:dtype.equals("BOOL")||dtype.equals("I8")||dtype.equals("U8")||dtype.equals("F8_E4M3")||dtype.equals("F8_E5M2")?1:0;if(width==0)throw new IOException("Unknown tensor dtype");
                long elements=1;for(int dimension=0;dimension<shape.length();dimension++){long size=shape.getLong(dimension);if(size<0||(elements>0&&size>Long.MAX_VALUE/elements))throw new IOException("Invalid tensor shape");elements*=size;}if(elements>Long.MAX_VALUE/width||elements*width!=end-start)throw new IOException("Tensor data is incomplete");if(end>start)intervals.add(new long[]{start,end});count++;}
            if(count<10||dataLength<1)throw new IOException("Incomplete model tensor set");Collections.sort(intervals,(a,b)->Long.compare(a[0],b[0]));long end=0;for(long[] interval:intervals){if(interval[0]!=end)throw new IOException("Invalid or overlapping tensor data");end=interval[1];}if(end!=dataLength)throw new IOException("Unexpected trailing model data");
        }
    }
    boolean registered(File f)throws IOException {
        String canonical=f.getCanonicalPath();JSONObject item=verified.optJSONObject(canonical);
        if(item==null)for(File dir:roots){item=readRecord(dir).optJSONObject(canonical);if(item!=null)break;}
        return item!=null&&f.isFile()&&item.optLong("bytes",-1)==f.length()&&item.optLong("modified",-1)==f.lastModified()&&item.optString("sha256").matches("[a-f0-9]{64}");
    }
    ArrayList<File> installed(String prefix)throws IOException {ArrayList<File> result=new ArrayList<>();HashSet<String> seen=new HashSet<>();for(File dir:roots){File custom=safe(dir,"custom");File[] files=custom.listFiles();if(files!=null)for(File f:files){File candidate=safe(dir,"custom/"+f.getName());if(candidate.isFile()&&registered(candidate)&&candidate.getName().startsWith(prefix)&&(candidate.getName().endsWith(".safetensors")||candidate.getName().endsWith(".gguf"))&&seen.add(candidate.getCanonicalPath()))result.add(candidate);}}return result;}
    long managedBytes(String target)throws IOException {long total=0;for(File dir:roots)for(Resource r:resources)if(target==null||target.equals(r.target)){total+=safe(dir,r.relative()).length();total+=safe(dir,r.relative()+".part").length();}if(target==null)for(File f:installed(""))total+=f.length();return total;}
    synchronized void deleteCustom(File selected,Files.Job job)throws Exception {
        boolean owned=false;for(File directory:roots){File custom=safe(directory,"custom");if(selected.getCanonicalPath().startsWith(custom.getCanonicalPath()+File.separator))owned=true;}
        if(!owned||!selected.getName().matches("(base-|control-).+\\.(safetensors|gguf)")||!registered(selected))throw new IOException("未登记的文件保留 / Unregistered file is retained");
        job.check();if(!selected.delete())throw new IOException("Cannot delete imported model");forget(selected);saveRecord();
    }
    synchronized void deleteManaged(String target,Files.Job job)throws Exception {
        try{
            for(File dir:roots){for(Resource r:resources)if(target==null||target.equals(r.target)){job.check();File f=safe(dir,r.relative()),part=safe(dir,r.relative()+".part");if(f.exists()&&!f.delete()||part.exists()&&!part.delete())throw new IOException("Cannot delete model: "+r.relative());forget(f);}
                if(target==null){File custom=safe(dir,"custom");File[] list=custom.listFiles();if(list!=null)for(File f:list){job.check();File candidate=safe(dir,"custom/"+f.getName());if(!candidate.isFile()||!registered(candidate))continue;if(candidate.getName().matches("(base-|control-).+\\.(safetensors|gguf)")){if(!candidate.delete())throw new IOException("Cannot delete imported model");forget(candidate);}}}
            }
        }finally{saveRecord();}
        if(prefs!=null&&target==null)prefs.edit().remove("androidAiCustomBase").remove("androidAiCustomControl").remove("androidAiResultPath").apply();
    }

}
