using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace QQImageSwitch
{
    static class QrAiIntegrityTests
    {
        static void Require(bool value, string message) { if (!value) throw new Exception("Safetensors integrity: " + message); }
        static int checkedCases;
        static string folder;
        static string FileFor(string name) { return Path.Combine(folder, name + ".safetensors"); }
        static string Tensor(string name, string dtype, string shape, ulong begin, ulong end)
        { return "\"" + name + "\":{\"dtype\":\"" + dtype + "\",\"shape\":" + shape + ",\"data_offsets\":[" + begin + "," + end + "]}"; }
        static string Write(string name, string header, int bytes)
        {
            string path = FileFor(name); byte[] encoded = Encoding.UTF8.GetBytes(header);
            using (var output = new BinaryWriter(File.Create(path))) { output.Write((ulong)encoded.Length); output.Write(encoded); output.Write(new byte[bytes]); }
            return path;
        }
        static void Check(string name, string header, int bytes, bool expected)
        { Require((QrAiIntegrity.Inspect(Write(name, header, bytes)) != null) == expected, name); checkedCases++; }
        public static void Run(string root)
        {
            checkedCases = 0; folder = Path.Combine(root, "safetensors-integrity"); Directory.CreateDirectory(folder);
            string good = Write("good", "{" + Tensor("weight", "F32", "[2]", 0, 8) + "} ", 8);
            var info = QrAiIntegrity.Inspect(good);
            Require(info != null && info.Valid && info.Sha256 == null && info.TensorCount == 1 && info.Role == "unknown", "header-only scan or valid generic tensor"); checkedCases++;
            int callbacks = 0; long last = -1, total = -1;
            var fingerprint = QrAiIntegrity.Inspect(good, true, delegate(long done, long length) { Require(done >= last && done <= length, "hash progress bounds"); last = done; total = length; callbacks++; }, CancellationToken.None);
            using (var hash = SHA256.Create()) Require(fingerprint.Sha256 == BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(good))).Replace("-", "").ToLowerInvariant(), "actual whole-file SHA mismatch");
            Require(callbacks >= 2 && last == total && total == new FileInfo(good).Length, "SHA progress did not include whole file"); checkedCases++;
            byte[] changed = File.ReadAllBytes(good); changed[changed.Length - 1] ^= 1; File.WriteAllBytes(good, changed);
            Require(QrAiIntegrity.Inspect(good, true).Sha256 != fingerprint.Sha256 && QrAiIntegrity.Validate(good), "custom fingerprint should change while structure remains valid"); checkedCases++;
            Check("scalar", "{" + Tensor("scalar", "F32", "[]", 0, 4) + "}", 4, true);
            Check("empty", "{" + Tensor("empty", "F16", "[0,4]", 0, 0) + "}", 0, true);
            Check("zero-adjacent", "{" + Tensor("nonempty", "U8", "[2]", 0, 2) + "," + Tensor("empty", "U8", "[0]", 0, 0) + "}", 2, true);
            Check("metadata-only", "{\"__metadata__\":{\"note\":\"custom fingerprint only\"}}", 0, true);
            Check("metadata-unicode", "{\"__metadata__\":{\"note\":\"\\uD83D\\uDE00\\n\"}," + Tensor("weight", "F32", "[2]", 0, 8) + "}", 8, true);
            Check("unpaired-surrogate", "{\"__metadata__\":{\"note\":\"\\uD800\"}}", 0, false);
            Check("metadata-not-string", "{\"__metadata__\":{\"note\":1}}", 0, false);
            Check("metadata-not-map", "{\"__metadata__\":[\"x\"]}", 0, false);
            Check("metadata-duplicate", "{\"__metadata__\":{\"note\":\"one\",\"note\":\"two\"}}", 0, false);
            Check("duplicates", "{" + Tensor("weight", "F32", "[2]", 0, 8) + "," + Tensor("weight", "F32", "[2]", 8, 16) + "}", 16, false);
            Check("escaped-duplicate", "{" + Tensor("weight", "F32", "[2]", 0, 8) + "," + Tensor("we\\u0069ght", "F32", "[2]", 8, 16) + "}", 16, false);
            Check("duplicate-field", "{\"weight\":{\"dtype\":\"F32\",\"dtype\":\"U8\",\"shape\":[2],\"data_offsets\":[0,2]}}", 2, false);
            Check("leading-hole", "{" + Tensor("weight", "F32", "[2]", 1, 9) + "}", 9, false);
            Check("middle-hole", "{" + Tensor("one", "F32", "[1]", 0, 4) + "," + Tensor("two", "F32", "[1]", 5, 9) + "}", 9, false);
            Check("overlap", "{" + Tensor("one", "F32", "[2]", 0, 8) + "," + Tensor("two", "F32", "[2]", 4, 12) + "}", 12, false);
            Check("trailing-bytes", "{" + Tensor("weight", "F32", "[2]", 0, 8) + "}", 9, false);
            Check("truncated-data", "{" + Tensor("weight", "F32", "[2]", 0, 8) + "}", 7, false);
            Check("offset-out-of-file", "{" + Tensor("weight", "F32", "[2]", 0, 16) + "}", 8, false);
            Check("offset-reversed", "{" + Tensor("weight", "F32", "[1]", 4, 0) + "}", 4, false);
            Check("shape-byte-mismatch", "{" + Tensor("weight", "F32", "[2,2]", 0, 8) + "}", 8, false);
            Check("shape-negative", "{" + Tensor("weight", "F32", "[-2]", 0, 8) + "}", 8, false);
            Check("shape-decimal", "{" + Tensor("weight", "F32", "[2.0]", 0, 8) + "}", 8, false);
            Check("shape-exponent", "{" + Tensor("weight", "F32", "[2e0]", 0, 8) + "}", 8, false);
            Check("shape-overflow", "{" + Tensor("weight", "F32", "[18446744073709551615,2]", 0, 8) + "}", 8, false);
            Check("byte-size-overflow", "{" + Tensor("weight", "F64", "[18446744073709551615]", 0, 8) + "}", 8, false);
            Check("number-overflow", "{" + Tensor("weight", "F32", "[18446744073709551616]", 0, 8) + "}", 8, false);
            Check("offset-negative", "{\"weight\":{\"dtype\":\"F32\",\"shape\":[2],\"data_offsets\":[-1,7]}}", 8, false);
            Check("unknown-dtype", "{" + Tensor("weight", "F123", "[2]", 0, 8) + "}", 8, false);
            Check("missing-shape", "{\"weight\":{\"dtype\":\"F32\",\"data_offsets\":[0,8]}}", 8, false);
            Check("extra-offset", "{\"weight\":{\"dtype\":\"F32\",\"shape\":[2],\"data_offsets\":[0,8,9]}}", 8, false);
            Check("trailing-json", "{" + Tensor("weight", "F32", "[2]", 0, 8) + "}{}", 8, false);
            Check("trailing-comma", "{" + Tensor("weight", "F32", "[2]", 0, 8) + ",}", 8, false);
            Check("first-byte-not-brace", " {" + Tensor("weight", "F32", "[2]", 0, 8) + "}", 8, false);
            string malformed = FileFor("invalid-utf8"); using (var w = new BinaryWriter(File.Create(malformed))) { w.Write(5UL); w.Write(new byte[]{123,34,255,34,125}); }
            Require(QrAiIntegrity.Inspect(malformed) == null, "invalid UTF8 accepted"); checkedCases++;
            foreach (ulong header in new[]{16777217UL, ulong.MaxValue, 100UL})
            { string path = FileFor("bad-header-" + header); using (var w = new BinaryWriter(File.Create(path))) { w.Write(header); w.Write(new byte[]{123,125}); } Require(QrAiIntegrity.Inspect(path) == null, "oversized/truncated header accepted"); checkedCases++; }
            string shortFile=FileFor("short-file");File.WriteAllBytes(shortFile,new byte[7]);Require(QrAiIntegrity.Inspect(shortFile)==null,"truncated length accepted");checkedCases++;
            foreach (var dtype in new Dictionary<string,int>{{"BOOL",8},{"U8",8},{"I8",8},{"F4",4},{"F6_E2M3",6},{"F6_E3M2",6},{"F8_E5M2",8},{"F8_E4M3",8},{"F8_E8M0",8},{"I16",16},{"U16",16},{"F16",16},{"BF16",16},{"I32",32},{"U32",32},{"F32",32},{"I64",64},{"U64",64},{"F64",64}})
                Check("dtype-"+dtype.Key,"{"+Tensor("weight",dtype.Key,"[8]",0,(ulong)dtype.Value)+"}",dtype.Value,true);
            foreach(var dtype in new Dictionary<string,int>{{"F8_E4M3FNUZ",8},{"F8_E5M2FNUZ",8},{"C64",64}})
                Check("dtype-newer-"+dtype.Key,"{"+Tensor("weight",dtype.Key,"[8]",0,(ulong)dtype.Value)+"}",dtype.Value,false);
            Check("subbyte-unaligned", "{" + Tensor("weight", "F4", "[1]", 0, 1) + "}", 1, false);
            string onlyUnet = Model("only-unet",768,false,false,false);var partial=QrAiIntegrity.Inspect(onlyUnet);
            Require(partial != null && partial.Role == "unknown" && partial.Family == "sd15", "standalone UNet accepted as complete base"); checkedCases++;
            var sd15 = QrAiIntegrity.Inspect(Model("base15",768,true,false,false)); Require(sd15.Role=="base" && sd15.Family=="sd15","SD15 base profile"); checkedCases++;
            var xl = QrAiIntegrity.Inspect(Model("basexl",2048,true,true,false)); Require(xl.Role=="base" && xl.Family=="sdxl","SDXL base profile"); checkedCases++;
            foreach (int width in new[]{768,2048}) { var control=QrAiIntegrity.Inspect(Model("control"+width,width,false,false,true));Require(control.Role=="control" && control.Family==(width==768?"sd15":"sdxl"),"control family/profile");checkedCases++; }
            var unsupported=QrAiIntegrity.Inspect(Model("sd2",1024,true,false,false));Require(unsupported.Family==null && unsupported.Role=="unknown","SD2 inferred as SD15");checkedCases++;
            var lora=QrAiIntegrity.Inspect(Write("lora","{"+Tensor("lora_unet_down_blocks_0.lora_down.weight","F32","[1,768]",0,3072)+"}",3072));Require(lora.Role=="lora","LoRA inferred as base/control");checkedCases++;
            var metaAdapter=QrAiIntegrity.Inspect(Write("metadata-lora","{\"__metadata__\":{\"ss_network_module\":\"networks.lora\"},"+Tensor("weight","F32","[1]",0,4)+"}",4));Require(metaAdapter.Role=="lora","LoRA metadata ignored");checkedCases++;
            var harmlessMetadata=QrAiIntegrity.Inspect(Write("metadata-words","{\"__metadata__\":{\"note\":\"text_encoder_2 lora_ appears only in this note\"},"+Tensor("weight","F32","[1]",0,4)+"}",4));Require(harmlessMetadata.Family==null && harmlessMetadata.Role=="unknown","free-form note changed model role/family");checkedCases++;
            using (var cancel=new CancellationTokenSource())
            {cancel.Cancel();bool stopped=false;try{QrAiIntegrity.Inspect(good,false,null,cancel.Token);}catch(OperationCanceledException){stopped=true;}Require(stopped,"pre-cancel ignored");checkedCases++;}
            using (var cancel=new CancellationTokenSource())
            {bool stopped=false;try{QrAiIntegrity.Inspect(good,true,delegate(long done,long length){cancel.Cancel();},cancel.Token);}catch(OperationCanceledException){stopped=true;}Require(stopped,"hash cancellation swallowed");checkedCases++;}
            using(var writer=new FileStream(good,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))Require(QrAiIntegrity.Inspect(good)==null,"file still open for writing accepted");checkedCases++;
            Require(QrAiIntegrity.Inspect(good+":stream.safetensors")==null && QrAiIntegrity.Inspect(@"\\.\NUL.safetensors")==null && QrAiIntegrity.Inspect("https://example.invalid/model.safetensors")==null && QrAiIntegrity.Inspect(folder)==null && QrAiIntegrity.Inspect("\0")==null,"abnormal filesystem path accepted");checkedCases++;
            foreach(string device in new[]{"CON.safetensors","NUL.safetensors","AUX.safetensors","COM1.safetensors","LPT9.safetensors"})Require(QrAiIntegrity.Inspect(Path.Combine(folder,device))==null,"Windows reserved device accepted");checkedCases++;
            File.WriteAllText(Path.Combine(folder,"PASS.md"),"PASS "+checkedCases+" checks: strict UTF8/JSON/duplicates, dtype/shape/offset/file coverage, scalar/empty/subbyte tensors, full-file fingerprint/progress/cancel, complete base/control discovery profiles and LoRA/UNet rejection. Values and author identity are not certified.");
        }

        // Minimal structural fixtures; never real or executable neural-network weights.
        public static string Model(string name,int width,bool completeBase,bool secondText,bool control)
        {
            var parts=new List<string>();ulong offset=0;
            Action<string,string,int> add=delegate(string key,string shape,int bytes){parts.Add(Tensor(key,"F32",shape,offset,offset+(ulong)bytes));offset+=(ulong)bytes;};
            if(control)
            {add("controlnet_cond_embedding.conv_in.weight","[1,3,1,1]",12);add("controlnet_down_blocks.0.weight","[1,1,1,1]",4);add("controlnet_mid_block.weight","[1,1,1,1]",4);add("down_blocks.0.attentions.0.transformer_blocks.0.attn2.to_k.weight","[1,"+width+"]",width*4);}
            else
            {
                add("model.diffusion_model.input_blocks.0.0.weight","[1,4,1,1]",16);add("model.diffusion_model.out.2.weight","[4,1,1,1]",16);
                add("model.diffusion_model.input_blocks.2.1.transformer_blocks.0.attn2.to_k.weight","[1,"+width+"]",width*4);
                if(completeBase){add("first_stage_model.encoder.conv_in.weight","[1,3,1,1]",12);add("first_stage_model.decoder.conv_out.weight","[3,1,1,1]",12);add("cond_stage_model.transformer.text_model.embeddings.token_embedding.weight","[1,768]",3072);}
                if(secondText)add("conditioner.embedders.1.model.token_embedding.weight","[1,1280]",5120);
            }
            return Write(name,"{"+string.Join(",",parts)+"}",(int)offset);
        }
    }

#if INTEGRITY_PROBE
    static class IntegrityProbe
    {
        public static int Main(string[] args)
        { try { QrAiIntegrityTests.Run(args[0]); Console.WriteLine("PASS safetensors integrity checks"); return 0; } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;} }
    }
#endif
}
