using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QQImageSwitch
{
    public sealed class QrAiStorageDrive
    {
        public string Root;public long AvailableBytes;public bool Ready=true,Fixed=true;
    }
    public sealed class QrAiDeletePlan
    {
        internal string RootPath;internal readonly List<string> Files=new List<string>(),References=new List<string>();
        internal bool All;internal string Selected;public string Root {get{return RootPath;}}public int FileCount {get{return Files.Count;}}
        public long Bytes {get;internal set;}public int ExternalReferences {get{return References.Count;}}
        public string Description {get;internal set;}
    }
    public static class QrAiStorage
    {
        public const long DefaultMinimumBytes=20L*1024*1024*1024;
        const string Marker=".qr-ai-owned.json";
        public static string LegacyDefaultRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIImageTools","ai");}}
        public static string DefaultRoot()
        {
            var drives=new List<QrAiStorageDrive>();foreach(var drive in DriveInfo.GetDrives())try{if(drive.DriveType!=DriveType.Fixed)continue;drives.Add(new QrAiStorageDrive{Root=drive.Name,Ready=drive.IsReady,Fixed=drive.DriveType==DriveType.Fixed,AvailableBytes=drive.IsReady?drive.AvailableFreeSpace:0});}catch(IOException){}catch(UnauthorizedAccessException){}
            return DefaultRoot(drives);
        }
        public static string DefaultRoot(IEnumerable<QrAiStorageDrive> drives,long requiredBytes=DefaultMinimumBytes)
        {
            var eligible=(drives??Enumerable.Empty<QrAiStorageDrive>()).Where(d=>d!=null&&d.Ready&&d.Fixed&&!string.IsNullOrEmpty(d.Root)&&d.Root.Length>=2&&d.Root[1]==':'&&char.ToUpperInvariant(d.Root[0])>='D'&&char.ToUpperInvariant(d.Root[0])<='Z'&&d.AvailableBytes>=Math.Max(0,requiredBytes)).OrderBy(d=>char.ToUpperInvariant(d.Root[0])).FirstOrDefault();
            // With no suitable data disk, retain a visible D suggestion; never silently
            // reserve a system-drive cache. The user can choose any explicit directory.
            return eligible==null?@"D:\AI-Models":Path.Combine(eligible.Root,"AI-Models");
        }
        public static bool IsLegacyDefault(string path){try{return string.Equals(Path.GetFullPath(path),Path.GetFullPath(LegacyDefaultRoot),StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}}
        public static bool IsAutomaticRoot(string path){try{string p=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);return p.Length==12&&p[1]==':'&&char.ToUpperInvariant(p[0])>='D'&&char.ToUpperInvariant(p[0])<='Z'&&p.Substring(2).Equals(@"\AI-Models",StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}}
        public static void UseDirectory(QrAiOptions options,string path){options.Root=Path.GetFullPath(path);options.AutoRoot=false;}
        static long OwnedRuntimeBytes(string root,bool owned,bool runtime,long maximum,string environment="cuda")
        {
            if(!owned||maximum<=0)return 0;var record=Object(Bound(root,Marker));object value;var saved=record.TryGetValue("RuntimeFolders",out value)?value as System.Collections.IEnumerable:null;
            var folders=saved==null?new HashSet<string>():new HashSet<string>(saved.Cast<object>().OfType<string>());var queue=new Queue<Tuple<string,int>>();
            string prefix=environment=="cpu"?"runtime/cpu/":"runtime/";
            foreach(string child in new[]{"python","pip-cache","hf-cache","torch-cache","temp"}){string folder=prefix+child;if(runtime||folders.Contains(folder)||environment=="cpu"&&folders.Contains("runtime/cpu"))queue.Enqueue(Tuple.Create(Bound(root,folder),0));}
            long bytes=0;int entries=0;
            if(runtime||folders.Contains(prefix+"python")||environment=="cpu"&&folders.Contains("runtime/cpu"))foreach(string name in new[]{prefix+"python.zip",prefix+"python.zip.part",prefix+"pip.whl",prefix+"pip.whl.part"})
            {string file=Bound(root,name);try{NoLinks(root,file);if(File.Exists(file)&&(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)bytes+=Math.Min(maximum-bytes,new FileInfo(file).Length);}catch(IOException){}catch(UnauthorizedAccessException){}if(bytes>=maximum)return maximum;}
            while(queue.Count>0&&entries<200000)
            {
                var next=queue.Dequeue();try
                {
                    NoLinks(root,next.Item1);
                    if(!Directory.Exists(next.Item1)||(File.GetAttributes(next.Item1)&FileAttributes.ReparsePoint)!=0)continue;
                    foreach(string path in Directory.EnumerateFileSystemEntries(next.Item1))
                    {
                        if(++entries>200000)return bytes;var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)continue;
                        if((attributes&FileAttributes.Directory)!=0){if(next.Item2<24)queue.Enqueue(Tuple.Create(path,next.Item2+1));}
                        else {bytes+=Math.Min(maximum-bytes,new FileInfo(path).Length);if(bytes>=maximum)return maximum;}
                    }
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
            return bytes;
        }
        public static long EstimateSpace(QrAiOptions options,bool models=true)
        {
            string root=Normal(options.Root);NoLinks(root,root);bool runtime;bool owned=Owned(root,out runtime);bool existing=owned;
            string runtimePath=QrAiService.RuntimeFolder(options.Root,options.Environment);bool ready=File.Exists(Path.Combine(runtimePath,"python","python.exe"))&&Directory.Exists(Path.Combine(runtimePath,"python","Lib","site-packages","torch"));
            long environment=(options.Environment=="cpu"?3L:6L)*1024*1024*1024;long bytes=(ready?0:Math.Max(0,environment-OwnedRuntimeBytes(root,owned,runtime,environment,options.Environment)))+1024L*1024*1024;var basis=QrAiService.ReadModel(options.Base,false);var control=QrAiService.ReadModel(options.Control,true);string family=basis!=null?basis.Family:control!=null?control.Family:options.Family;
            if(models)foreach(var package in Packages().Where(p=>Text(p,"family")==family||Text(p,"family")=="shared"))foreach(var file in ((System.Collections.IEnumerable)package["files"]).Cast<object>().OfType<Dictionary<string,object>>())
            {
                string local=Text(file,"local"),target=Text(package,"target");bool weight=local.EndsWith(".safetensors")||local.EndsWith(".bin");if(weight&&(target.StartsWith("base/")&&basis!=null||target.StartsWith("controlnet/")&&control!=null))continue;
                long size=Convert.ToInt64(file["size"]);string path=Path.Combine(QrAiService.ResolveModelPackage(options.Root,target),local.Replace('/',Path.DirectorySeparatorChar));try{if(File.Exists(path)&&new FileInfo(path).Length==size){existing=true;continue;}}catch(IOException){}catch(UnauthorizedAccessException){}
                string partial=Bound(root,target+"/"+local+".part");try{if(owned&&File.Exists(partial)){NoLinks(root,partial);long downloaded=new FileInfo(partial).Length;if(downloaded>0&&downloaded<=size){existing=true;size-=downloaded;}}}catch(IOException){}catch(UnauthorizedAccessException){}bytes+=size;
            }
            if(options.AutoRoot&&!existing&&!ready&&basis==null&&control==null)bytes=Math.Max(bytes,DefaultMinimumBytes);return bytes;
        }
        public static void EnsureStorage(QrAiOptions options,bool models=true)
        {
            var drives=new List<QrAiStorageDrive>();foreach(var drive in DriveInfo.GetDrives())try{if(drive.DriveType!=DriveType.Fixed)continue;drives.Add(new QrAiStorageDrive{Root=drive.Name,Ready=drive.IsReady,Fixed=drive.DriveType==DriveType.Fixed,AvailableBytes=drive.IsReady?drive.AvailableFreeSpace:0});}catch(IOException){}catch(UnauthorizedAccessException){}
            EnsureStorage(options,drives,EstimateSpace(options,models));
        }
        public static void EnsureStorage(QrAiOptions options,IEnumerable<QrAiStorageDrive> drives,long requiredBytes)
        {
            string volume=Path.GetPathRoot(Path.GetFullPath(options.Root));var all=drives.ToArray();var chosen=all.FirstOrDefault(d=>string.Equals(d.Root,volume,StringComparison.OrdinalIgnoreCase));
            if(chosen!=null&&chosen.Ready&&chosen.AvailableBytes>=requiredBytes)return;
            if(options.AutoRoot&&IsAutomaticRoot(options.Root))
            {
                string replacement=DefaultRoot(all,requiredBytes);string next=Path.GetPathRoot(replacement);chosen=all.FirstOrDefault(d=>string.Equals(d.Root,next,StringComparison.OrdinalIgnoreCase));
                if(chosen!=null&&chosen.Ready&&chosen.AvailableBytes>=requiredBytes){options.Root=replacement;return;}
            }
            throw new IOException("AI 存储目录不可用或空间不足，请选择其他目录。预计还需 "+Math.Ceiling(requiredBytes/(1024d*1024*1024)).ToString("0")+" GB 可用空间。");
        }
        static Dictionary<string,object> Object(string file){try{return Inspection.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(file,Encoding.UTF8))??new Dictionary<string,object>();}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}return new Dictionary<string,object>();}
        static string Text(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&value is string?(string)value:"";}
        static bool Flag(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&value is bool&&(bool)value;}
        static string Normal(string path){string full=Path.GetFullPath(path),volume=Path.GetPathRoot(full);if(full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)==volume.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))throw new InvalidDataException("请选择具体的 AI 存储文件夹。");return full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}
        static string Bound(string root,string relative)
        {
            if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Split('/','\\').Any(p=>p==".."||p=="."))throw new InvalidDataException("AI 清理路径无效。");
            string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("AI 清理路径超出存储目录。");return full;
        }
        static void NoLinks(string root,string path)
        {
            string current=path;while(!string.IsNullOrEmpty(current))
            {
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("AI 存储目录包含外部链接，清理已停止。");
                if(string.Equals(current,root,StringComparison.OrdinalIgnoreCase))break;string parent=Path.GetDirectoryName(current);if(parent==current)break;current=parent;
            }
            // Also reject a linked ancestor of the chosen root.
            current=Path.GetDirectoryName(root);while(!string.IsNullOrEmpty(current)){if(Directory.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("AI 存储目录包含外部链接，清理已停止。");current=Path.GetDirectoryName(current);}
        }
        static List<Dictionary<string,object>> Packages()
        {using(var stream=typeof(QrAiStorage).Assembly.GetManifestResourceStream("AiQrModels"))using(var reader=new StreamReader(stream)){var manifest=Inspection.Json().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());return ((System.Collections.IEnumerable)manifest["packages"]).Cast<object>().OfType<Dictionary<string,object>>().ToList();}}
        static bool LegacyOwned(string root)
        {
            string local=Bound(root,"engine/models.json");if(!File.Exists(local))return false;
            using(var expected=typeof(QrAiStorage).Assembly.GetManifestResourceStream("AiQrModels"))using(var actual=File.OpenRead(local))using(var hash=SHA256.Create())return hash.ComputeHash(expected).SequenceEqual(hash.ComputeHash(actual));
        }
        static bool RecognizedRuntime(string root)
        {
            string zip=Bound(root,"runtime/python.zip"),pth=Bound(root,"runtime/python/python311._pth");try{NoLinks(root,zip);NoLinks(root,pth);using(var hash=SHA256.Create())using(var input=File.OpenRead(zip))return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant()=="009d6bf7e3b2ddca3d784fa09f90fe54336d5b60f0e0f305c37f400bf83cfd3b"&&File.ReadAllText(pth)=="python311.zip\n.\nLib/site-packages\nimport site\n";}catch(IOException){}catch(UnauthorizedAccessException){}return false;
        }
        public static void Register(string root)
        {
            root=Normal(root);NoLinks(root,root);Directory.CreateDirectory(root);string marker=Bound(root,Marker);var record=Object(marker);
            if(Text(record,"Root")==root&&Flag(record,"Owned"))return;
            bool legacy=LegacyOwned(root);bool ownRuntime=!Directory.Exists(Bound(root,"runtime"));
            if(!ownRuntime&&legacy)ownRuntime=RecognizedRuntime(root);
            var runtimeFolders=new[]{"runtime/python","runtime/pip-cache","runtime/hf-cache","runtime/torch-cache","runtime/temp"}.Where(folder=>ownRuntime||!Directory.Exists(Bound(root,folder))).ToArray();
            Codec.SaveAtomic(marker,Encoding.UTF8.GetBytes(Inspection.Json().Serialize(new{Root=root,Owned=true,Runtime=ownRuntime,RuntimeFolders=runtimeFolders,Version=1})));
        }
        static bool Owned(string root,out bool runtime)
        {var record=Object(Bound(root,Marker));bool legacy=LegacyOwned(root);runtime=Flag(record,"Runtime")||legacy&&RecognizedRuntime(root);return Text(record,"Root")==root&&Flag(record,"Owned")||legacy;}
        public static void RegisterEnvironment(string root,string environment)
        {
            QrAiService.RuntimeFolder(root,environment);Register(root);if(environment!="cpu")return;root=Normal(root);string marker=Bound(root,Marker);var record=Object(marker);string folder=Bound(root,"runtime/cpu");NoLinks(root,folder);
            object existing;var saved=record.TryGetValue("RuntimeFolders",out existing)?existing as System.Collections.IEnumerable:null;var folders=saved==null?new List<string>():saved.Cast<object>().OfType<string>().ToList();
            if(!Directory.Exists(folder)&&!folders.Contains("runtime/cpu")){folders.Add("runtime/cpu");record["RuntimeFolders"]=folders;Codec.SaveAtomic(marker,Encoding.UTF8.GetBytes(Inspection.Json().Serialize(record)));}
        }
        static string[] SelectedFiles(string root,string path)
        {
            string selected=Normal(path);if(!selected.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return new string[0];NoLinks(root,selected);
            if(File.Exists(selected))return selected.EndsWith(".safetensors",StringComparison.OrdinalIgnoreCase)?new[]{selected.Substring(root.Length+1)}:new string[0];
            var model=QrAiService.ReadModel(selected);if(model==null)return new string[0];var files=new List<string>();
            foreach(string component in model.Control?new[]{""}:model.Family=="sdxl"?new[]{"unet","vae","text_encoder","text_encoder_2"}:new[]{"unet","vae","text_encoder"})
            {string folder=Path.Combine(selected,component);files.AddRange(Directory.EnumerateFiles(folder,"*.safetensors").Take(256));string config=Path.Combine(folder,"config.json");if(File.Exists(config))files.Add(config);}
            foreach(string relative in new[]{"model_index.json","scheduler/scheduler_config.json","tokenizer/tokenizer_config.json","tokenizer/vocab.json","tokenizer/merges.txt","tokenizer/special_tokens_map.json","tokenizer_2/tokenizer_config.json","tokenizer_2/vocab.json","tokenizer_2/merges.txt","tokenizer_2/special_tokens_map.json","feature_extractor/preprocessor_config.json"}){string file=Path.Combine(selected,relative.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(file))files.Add(file);}
            foreach(string file in files)NoLinks(root,file);return files.Select(f=>f.Substring(root.Length+1)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static void RegisterSelected(QrAiOptions options)
        {
            string root=Normal(options.Root);if(!File.Exists(Bound(root,Marker)))return;var record=Object(Bound(root,Marker));if(Text(record,"Root")!=root||!Flag(record,"Owned"))return;object stored;var models=record.TryGetValue("Models",out stored)?stored as Dictionary<string,object>??new Dictionary<string,object>():new Dictionary<string,object>();
            foreach(string selected in new[]{options.Base,options.Control})if(!string.IsNullOrWhiteSpace(selected)){var files=SelectedFiles(root,selected);if(files.Length>0)models[Normal(selected)]=files;}record["Models"]=models;Codec.SaveAtomic(Bound(root,Marker),Encoding.UTF8.GetBytes(Inspection.Json().Serialize(record)));
        }
        static void AddRegistered(QrAiDeletePlan plan,string selected=null)
        {
            var record=Object(Bound(plan.RootPath,Marker));object stored;var models=record.TryGetValue("Models",out stored)?stored as Dictionary<string,object>:null;if(models==null)return;
            foreach(var model in models){if(selected!=null&&!string.Equals(model.Key,selected,StringComparison.OrdinalIgnoreCase))continue;if(!model.Key.StartsWith(plan.RootPath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;var files=model.Value as System.Collections.IEnumerable;if(files==null||model.Value is string)continue;foreach(var file in files)if(file is string){string full=Bound(plan.RootPath,(string)file);if(full==model.Key||full.StartsWith(model.Key+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Add(plan,(string)file);}}
        }
        static void Add(QrAiDeletePlan plan,string relative)
        {
            string path=Bound(plan.RootPath,relative);NoLinks(plan.RootPath,path);if(File.Exists(path)&&!plan.Files.Contains(path,StringComparer.OrdinalIgnoreCase)){plan.Files.Add(path);plan.Bytes+=new FileInfo(path).Length;}
        }
        static void AddTree(QrAiDeletePlan plan,string relative)
        {
            string start=Bound(plan.RootPath,relative);if(!Directory.Exists(start))return;var queue=new Queue<string>();queue.Enqueue(start);int count=0;
            while(queue.Count>0){string dir=queue.Dequeue();NoLinks(plan.RootPath,dir);foreach(string file in Directory.EnumerateFiles(dir)){if(++count>200000)throw new IOException("运行环境文件过多，请检查所选存储目录。");Add(plan,file.Substring(plan.RootPath.Length+1));}foreach(string child in Directory.EnumerateDirectories(dir)){NoLinks(plan.RootPath,child);queue.Enqueue(child);}}
        }
        static void AddPackage(QrAiDeletePlan plan,Dictionary<string,object> package)
        {
            string target=Text(package,"target");object files;if(!package.TryGetValue("files",out files)||!(files is System.Collections.IEnumerable))return;
            foreach(var file in ((System.Collections.IEnumerable)files).Cast<object>().OfType<Dictionary<string,object>>()){string local=Text(file,"local");if(string.IsNullOrEmpty(local))continue;Add(plan,target+"/"+local);Add(plan,target+"/"+local+".part");}
        }
        static Dictionary<string,object> References(string root)
        {var data=Object(Bound(root,"model-reuse.json"));object packages;return data.TryGetValue("packages",out packages)?packages as Dictionary<string,object>??new Dictionary<string,object>():new Dictionary<string,object>();}
        public static QrAiDeletePlan PlanAll(string root)
        {
            root=Normal(root);NoLinks(root,root);var plan=new QrAiDeletePlan{RootPath=root,All=true};bool runtime;
            if(Owned(root,out runtime))
            {
                foreach(var package in Packages())AddPackage(plan,package);
                AddRegistered(plan);
                // Configuration-only packages use the same locked non-weight names.
                foreach(var package in Packages().Where(p=>Text(p,"target").StartsWith("base/")||Text(p,"target").StartsWith("controlnet/")))
                {
                    var copy=new Dictionary<string,object>(package);copy["target"]="configs/"+(Text(package,"target").StartsWith("base/")?Text(package,"family"):"controlnet-"+Text(package,"family"));copy["files"]=((System.Collections.IEnumerable)package["files"]).Cast<object>().OfType<Dictionary<string,object>>().Where(f=>!Text(f,"local").EndsWith(".safetensors")&&!Text(f,"local").EndsWith(".bin")).ToArray();AddPackage(plan,copy);
                }
                foreach(string file in new[]{"engine/local_qr.py","engine/models.json","model-hashes.json","custom-model-hashes.json",Marker})Add(plan,file);
                var marker=Object(Bound(root,Marker));object folderValue;var folderList=marker.TryGetValue("RuntimeFolders",out folderValue)?folderValue as System.Collections.IEnumerable:null;var managedFolders=folderList==null?new HashSet<string>():new HashSet<string>(folderList.Cast<object>().OfType<string>());
                foreach(string folder in new[]{"runtime/python","runtime/pip-cache","runtime/hf-cache","runtime/torch-cache","runtime/temp"})if(runtime||managedFolders.Contains(folder))AddTree(plan,folder);
                if(runtime||managedFolders.Contains("runtime/cpu"))AddTree(plan,"runtime/cpu");
                if(runtime||managedFolders.Contains("runtime/python"))foreach(string file in new[]{"runtime/python.zip","runtime/python.zip.part","runtime/pip.whl","runtime/pip.whl.part"})Add(plan,file);
            }
            foreach(var reference in References(root))plan.References.Add(reference.Key);
            plan.Description=L.T("将删除应用管理的模型和运行环境文件；外部模型只移除引用，其他文件保留。");return plan;
        }
        public static QrAiDeletePlan PlanSelected(string root,string modelPath)
        {
            root=Normal(root);NoLinks(root,root);var plan=new QrAiDeletePlan{RootPath=root};string selected=Normal(modelPath);plan.Selected=selected;bool runtime;
            if(Owned(root,out runtime)){foreach(var package in Packages())if(string.Equals(Bound(root,Text(package,"target")),selected,StringComparison.OrdinalIgnoreCase))AddPackage(plan,package);AddRegistered(plan,selected);foreach(string file in SelectedFiles(root,selected))Add(plan,file);}
            foreach(var reference in References(root)){var record=reference.Value as Dictionary<string,object>;if(record!=null&&!string.IsNullOrEmpty(Text(record,"path"))&&string.Equals(Normal(Text(record,"path")),selected,StringComparison.OrdinalIgnoreCase))plan.References.Add(reference.Key);}
            plan.Description=L.T("将删除所选的应用管理模型；外部或非托管模型只取消选择和引用。");return plan;
        }
        public static Task Execute(QrAiDeletePlan plan,Action<string> progress,CancellationToken token)
        {
            if(plan==null)throw new ArgumentNullException("plan");return Task.Run(delegate
            {
                string root=Normal(plan.RootPath);NoLinks(root,root);
                // Rebuild the allow-list immediately before mutation; a plan is never
                // permission to traverse a subsequently replaced junction.
                var fresh=plan.All?PlanAll(root):PlanSelected(root,plan.Selected);var allowed=new HashSet<string>(fresh.Files,StringComparer.OrdinalIgnoreCase);
                foreach(string file in plan.Files){token.ThrowIfCancellationRequested();NoLinks(root,file);if(!allowed.Contains(file))throw new IOException("AI 清理清单已变化，请重新确认。");}
                string marker=Bound(root,Marker);int completed=0;foreach(string file in plan.Files.Where(f=>!string.Equals(f,marker,StringComparison.OrdinalIgnoreCase))){token.ThrowIfCancellationRequested();NoLinks(root,file);if(File.Exists(file))File.Delete(file);if(progress!=null)progress(L.T("正在清理 AI 文件：")+(++completed)+" / "+plan.FileCount);}
                string ledger=Bound(root,"model-reuse.json");var refs=References(root);foreach(string key in plan.References)refs.Remove(key);
                if(plan.References.Count>0){token.ThrowIfCancellationRequested();NoLinks(root,ledger);Codec.SaveAtomic(ledger,Encoding.UTF8.GetBytes(Inspection.Json().Serialize(new{version=1,packages=refs})));}
                if(plan.All&&refs.Count==0&&plan.Files.Contains(marker,StringComparer.OrdinalIgnoreCase)&&File.Exists(ledger)){token.ThrowIfCancellationRequested();NoLinks(root,ledger);File.Delete(ledger);}
                if(plan.Files.Contains(marker,StringComparer.OrdinalIgnoreCase)){token.ThrowIfCancellationRequested();NoLinks(root,marker);if(File.Exists(marker))File.Delete(marker);}
                else if(plan.Selected!=null&&File.Exists(marker)){var record=Object(marker);object stored;var models=record.TryGetValue("Models",out stored)?stored as Dictionary<string,object>:null;if(models!=null&&models.Remove(plan.Selected))Codec.SaveAtomic(marker,Encoding.UTF8.GetBytes(Inspection.Json().Serialize(record)));}
                // Only empty parents of removed files can go; never recursively delete.
                foreach(string parent in plan.Files.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p=>p.Length))
                {string dir=parent;while(dir!=root&&dir.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){token.ThrowIfCancellationRequested();NoLinks(root,dir);if(!Directory.Exists(dir)||Directory.EnumerateFileSystemEntries(dir).Any())break;Directory.Delete(dir);dir=Path.GetDirectoryName(dir);}}
            },token);
        }
    }
}
