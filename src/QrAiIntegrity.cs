using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace QQImageSwitch
{
    public sealed class QrAiTensorInfo
    {
        public string Family, Sha256, Role;
        public bool Valid;
        public int TensorCount;
        public long FileLength;
    }

    public static class QrAiIntegrity
    {
        const int MaxHeader = 16 * 1024 * 1024;
        // Format and bit widths match the bundled safetensors 0.6.2 reader:
        // https://github.com/safetensors/safetensors/blob/v0.6.2/safetensors/src/tensor.rs
        // Classification keys: Hugging Face diffusers/loaders/single_file_utils.py.
        // Valid is structural validation, never a claim of an author's trusted hash.
        public static QrAiTensorInfo Inspect(string path, bool contentHash = false)
        { return Inspect(path, contentHash, null, CancellationToken.None); }

        public static QrAiTensorInfo Inspect(string path, bool contentHash, Action<long,long> hashProgress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) return null;
                string full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\.\", StringComparison.Ordinal) || full.StartsWith(@"\\?\", StringComparison.Ordinal)) return null;
                // Do not open Windows devices or alternate data streams as model files.
                int colon = full.IndexOf(':');
                if (colon >= 0 && (colon != 1 || full.IndexOf(':', colon + 1) >= 0)) return null;
                if (!Path.GetExtension(full).Equals(".safetensors", StringComparison.OrdinalIgnoreCase)) return null;
                string stem = Path.GetFileName(full).Split('.')[0].TrimEnd(' ').ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CONIN$" || stem == "CONOUT$" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '1' && stem[3] <= '9')) return null;
                using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
                {
                    long length = stream.Length;
                    if (length < 10) return null;
                    byte[] first = ReadExactly(stream, 8);
                    ulong headerLength = 0;
                    for (int i = 0; i < 8; i++) headerLength |= (ulong)first[i] << (8 * i);
                    if (headerLength < 2 || headerLength > MaxHeader || headerLength > (ulong)(length - 8)) return null;
                    byte[] bytes = ReadExactly(stream, (int)headerLength);
                    if (bytes[0] != (byte)'{') return null;
                    string header = new UTF8Encoding(false, true).GetString(bytes);
                    var parsed = new HeaderParser(header, token);
                    List<Tensor> tensors = parsed.Read();
                    ulong payload = (ulong)(length - 8 - (long)headerLength);
                    tensors.Sort(delegate(Tensor a, Tensor b) { int c = a.Begin.CompareTo(b.Begin); return c != 0 ? c : a.End.CompareTo(b.End); });
                    ulong end = 0;
                    foreach (Tensor tensor in tensors)
                    {
                        token.ThrowIfCancellationRequested();
                        if (tensor.Begin != end || tensor.End < tensor.Begin || tensor.End > payload) return null;
                        ulong elements = 1;
                        foreach (ulong size in tensor.Shape) elements = checked(elements * size);
                        ulong bits = checked(elements * (ulong)DtypeBits(tensor.Dtype));
                        if (bits % 8 != 0 || tensor.End - tensor.Begin != bits / 8) return null;
                        end = tensor.End;
                    }
                    if (end != payload) return null; // No holes, trailing bytes, overlaps or truncation.
                    var result = Classify(tensors, parsed.Metadata);
                    result.Valid = true; result.TensorCount = tensors.Count; result.FileLength = length;
                    if (contentHash)
                    {
                        stream.Position = 0;
                        using (var hash = SHA256.Create())
                        {
                            byte[] buffer = new byte[1024 * 1024]; long done = 0;
                            if (hashProgress != null) hashProgress(done, length);
                            while (true)
                            {
                                token.ThrowIfCancellationRequested();
                                int count = stream.Read(buffer, 0, buffer.Length);
                                if (count == 0) break;
                                hash.TransformBlock(buffer, 0, count, buffer, 0); done += count;
                                if (hashProgress != null) hashProgress(done, length);
                            }
                            token.ThrowIfCancellationRequested();
                            if (done != length || stream.Length != length) return null;
                            hash.TransformFinalBlock(new byte[0], 0, 0);
                            result.Sha256 = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                        }
                    }
                    return result;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (SecurityException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (OverflowException) { return null; }
            catch (InvalidDataException) { return null; }
        }

        public static bool Validate(string path) { return Inspect(path) != null; }

        static byte[] ReadExactly(Stream stream, int count)
        {
            byte[] bytes = new byte[count]; int offset = 0;
            while (offset < count) { int read = stream.Read(bytes, offset, count - offset); if (read <= 0) throw new EndOfStreamException(); offset += read; }
            return bytes;
        }

        static int DtypeBits(string dtype)
        {
            switch (dtype)
            {
                case "F4": return 4;
                case "F6_E2M3": case "F6_E3M2": return 6;
                case "BOOL": case "U8": case "I8": case "F8_E5M2": case "F8_E4M3": case "F8_E8M0": return 8;
                case "I16": case "U16": case "F16": case "BF16": return 16;
                case "I32": case "U32": case "F32": return 32;
                case "I64": case "U64": case "F64": return 64;
                default: throw new InvalidDataException("Unsupported tensor dtype.");
            }
        }

        sealed class Tensor
        {
            public string Name, Dtype;
            public ulong[] Shape;
            public ulong Begin, End;
        }

        static QrAiTensorInfo Classify(List<Tensor> tensors, Dictionary<string,string> metadata)
        {
            bool lora = false, unetIn = false, unetOut = false, vaeEncoder = false, vaeDecoder = false;
            bool text = false, text2 = false, hint = false, controlDown = false, controlMid = false;
            ulong attention = 0; bool conflicting = false;
            foreach (Tensor tensor in tensors)
            {
                string name = tensor.Name.ToLowerInvariant();
                if (name.StartsWith("lora_", StringComparison.Ordinal) || name.Contains(".lora_") || name.Contains(".lora.") || name.Contains(".hada_") || name.Contains(".lokr_")) lora = true;
                if (name == "model.diffusion_model.input_blocks.0.0.weight" || name == "unet.conv_in.weight") unetIn = true;
                if (name == "model.diffusion_model.out.2.weight" || name == "unet.conv_out.weight") unetOut = true;
                if (name.StartsWith("first_stage_model.encoder.", StringComparison.Ordinal) || name.StartsWith("vae.encoder.", StringComparison.Ordinal)) vaeEncoder = true;
                if (name.StartsWith("first_stage_model.decoder.", StringComparison.Ordinal) || name.StartsWith("vae.decoder.", StringComparison.Ordinal)) vaeDecoder = true;
                if (name.StartsWith("cond_stage_model.transformer.", StringComparison.Ordinal) || name.StartsWith("conditioner.embedders.0.transformer.", StringComparison.Ordinal) || name.StartsWith("text_encoder.", StringComparison.Ordinal)) text = true;
                if (name.StartsWith("conditioner.embedders.1.", StringComparison.Ordinal) || name.StartsWith("text_encoder_2.", StringComparison.Ordinal)) text2 = true;
                if (name.StartsWith("controlnet_cond_embedding.", StringComparison.Ordinal) || name.StartsWith("input_hint_block.", StringComparison.Ordinal) || name.StartsWith("control_model.input_hint_block.", StringComparison.Ordinal)) hint = true;
                if (name.StartsWith("controlnet_down_blocks.", StringComparison.Ordinal) || name.StartsWith("zero_convs.", StringComparison.Ordinal) || name.StartsWith("control_model.zero_convs.", StringComparison.Ordinal)) controlDown = true;
                if (name.StartsWith("controlnet_mid_block.", StringComparison.Ordinal) || name.StartsWith("middle_block_out.", StringComparison.Ordinal) || name.StartsWith("control_model.middle_block_out.", StringComparison.Ordinal)) controlMid = true;
                if (name.EndsWith("attn2.to_k.weight", StringComparison.Ordinal) && tensor.Shape.Length == 2)
                { ulong width = tensor.Shape[1]; if (attention != 0 && attention != width) conflicting = true; attention = width; }
            }
            string module;
            if (metadata.TryGetValue("ss_network_module", out module) && (module.IndexOf("lora", StringComparison.OrdinalIgnoreCase) >= 0 || module.IndexOf("lycoris", StringComparison.OrdinalIgnoreCase) >= 0)) lora = true;
            string family = !conflicting && attention == 768 && !text2 ? "sd15" : !conflicting && attention == 2048 ? "sdxl" : null;
            string role = "unknown";
            if (lora) role = "lora";
            else if (family != null)
            {
                // Presence across components is a conservative discovery profile,
                // not proof that all architecture weights or values are meaningful.
                if (hint && controlDown && controlMid && !vaeEncoder && !vaeDecoder && !text && !text2) role = "control";
                else if (unetIn && unetOut && vaeEncoder && vaeDecoder && text && (family == "sd15" || text2) && !hint) role = "base";
            }
            return new QrAiTensorInfo { Family = family, Role = role };
        }

        // This format-specific reader rejects duplicate keys before dictionary insertion.
        // A general .NET JSON deserializer can silently accept duplicate tensor names.
        sealed class HeaderParser
        {
            readonly string value; readonly CancellationToken token; int index;
            public readonly Dictionary<string,string> Metadata = new Dictionary<string,string>(StringComparer.Ordinal);
            public HeaderParser(string value, CancellationToken token) { this.value = value; this.token = token; }
            public List<Tensor> Read()
            {
                var result = new List<Tensor>(); var names = new HashSet<string>(StringComparer.Ordinal);
                Need('{'); Space();
                if (!Take('}')) do
                {
                    token.ThrowIfCancellationRequested(); string name = Text();
                    if (!names.Add(name) || names.Count > 100000) Bad(); Need(':');
                    if (name == "__metadata__") ReadMetadata();
                    else { Tensor tensor = ReadTensor(); tensor.Name = name; result.Add(tensor); }
                    Space(); if (Take('}')) break; Need(',');
                } while (true);
                Space(); if (index != value.Length) Bad(); return result;
            }
            void ReadMetadata()
            {
                Need('{'); if (Take('}')) return;
                do
                {
                    token.ThrowIfCancellationRequested();
                    string key = Text(); if (Metadata.ContainsKey(key) || Metadata.Count >= 10000) Bad(); Need(':'); Metadata.Add(key, Text());
                    if (Take('}')) return; Need(',');
                } while (true);
            }
            Tensor ReadTensor()
            {
                var tensor = new Tensor(); var fields = new HashSet<string>(StringComparer.Ordinal);
                Need('{'); if (Take('}')) Bad();
                do
                {
                    string field = Text(); if (!fields.Add(field)) Bad(); Need(':');
                    if (field == "dtype") { tensor.Dtype = Text(); DtypeBits(tensor.Dtype); }
                    else if (field == "shape") tensor.Shape = Numbers(64);
                    else if (field == "data_offsets") { ulong[] offsets = Numbers(2); if (offsets.Length != 2) Bad(); tensor.Begin = offsets[0]; tensor.End = offsets[1]; }
                    else Bad();
                    if (Take('}')) break; Need(',');
                } while (true);
                if (fields.Count != 3 || tensor.Dtype == null || tensor.Shape == null) Bad(); return tensor;
            }
            ulong[] Numbers(int limit)
            {
                var result = new List<ulong>(); Need('['); if (Take(']')) return result.ToArray();
                do { token.ThrowIfCancellationRequested(); if (result.Count >= limit) Bad(); result.Add(Number()); if (Take(']')) return result.ToArray(); Need(','); } while (true);
            }
            ulong Number()
            {
                Space(); int start = index;
                while (index < value.Length && value[index] >= '0' && value[index] <= '9') index++;
                if (start == index || (index - start > 1 && value[start] == '0')) Bad();
                ulong number; if (!ulong.TryParse(value.Substring(start, index - start), NumberStyles.None, CultureInfo.InvariantCulture, out number)) Bad();
                return number;
            }
            string Text()
            {
                Need('"'); var text = new StringBuilder(); bool closed = false;
                while (index < value.Length)
                {
                    if ((index & 16383) == 0) token.ThrowIfCancellationRequested();
                    char c = value[index++]; if (c == '"') { closed = true; break; }
                    if (c < 32) Bad();
                    if (c == '\\')
                    {
                        if (index == value.Length) Bad(); c = value[index++];
                        switch (c)
                        {
                            case '"': case '\\': case '/': text.Append(c); break;
                            case 'b': text.Append('\b'); break; case 'f': text.Append('\f'); break;
                            case 'n': text.Append('\n'); break; case 'r': text.Append('\r'); break; case 't': text.Append('\t'); break;
                            case 'u':
                                if (index + 4 > value.Length) Bad(); ushort code;
                                if (!ushort.TryParse(value.Substring(index, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) Bad();
                                text.Append((char)code); index += 4; break;
                            default: Bad(); break;
                        }
                    }
                    else text.Append(c);
                    if (text.Length > 1024 * 1024) Bad();
                }
                if (!closed) Bad();
                string result = text.ToString();
                for (int i = 0; i < result.Length; i++) if (char.IsSurrogate(result[i]))
                { if (!char.IsHighSurrogate(result[i]) || ++i >= result.Length || !char.IsLowSurrogate(result[i])) Bad(); }
                return result;
            }
            void Space() { while (index < value.Length && (value[index] == ' ' || value[index] == '\t' || value[index] == '\r' || value[index] == '\n')) index++; }
            bool Take(char c) { Space(); if (index < value.Length && value[index] == c) { index++; return true; } return false; }
            void Need(char c) { if (!Take(c)) Bad(); }
            static void Bad() { throw new InvalidDataException("Invalid safetensors header."); }
        }
    }
}
