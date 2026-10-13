using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QQImageSwitch
{
    public static class ScrambleParameters
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("AIPTPRM1");
        public static void Append(Stream stream, ScrambleOptions options)
        {
            var saved = options.Copy(); saved.Key = ""; byte[] json = Encoding.UTF8.GetBytes(Inspection.Json().Serialize(saved));
            stream.Write(json, 0, json.Length); byte[] length = BitConverter.GetBytes(json.Length); stream.Write(length, 0, 4); stream.Write(Magic, 0, 8);
        }
        public static ScrambleOptions Read(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long end = stream.Length; var tail = new byte[12];
                if (end < 12) return null; stream.Position = end - 12; ReadExact(stream, tail);
                if (Encoding.ASCII.GetString(tail, 4, 8) == "AIPTSCR1")
                {
                    int size = BitConverter.ToInt32(tail, 0); if (size < 2 || size > 8192 || size > end - 12) throw new InvalidDataException("原文件记录损坏 / Invalid original record");
                    byte[] data = new byte[size]; stream.Position = end - 12 - size; ReadExact(stream, data);
                    var original = Inspection.Json().Deserialize<Obfuscation.Record>(Encoding.UTF8.GetString(data));
                    if (original == null || original.Schema!=1 || original.Length<1 || original.ImageLength < 12 || original.ImageLength > end - 12 - size || original.Length!=end-12-size-original.ImageLength) throw new InvalidDataException("Invalid image length");
                    end = original.ImageLength; stream.Position = end - 12; ReadExact(stream, tail);
                }
                if (Encoding.ASCII.GetString(tail, 4, 8) != "AIPTPRM1") return null;
                int length = BitConverter.ToInt32(tail, 0); if (length < 2 || length > 4096 || length > end - 12) throw new InvalidDataException("混淆参数记录损坏 / Invalid parameter record");
                var json = new byte[length]; stream.Position = end - 12 - length; ReadExact(stream, json);
                var fields=Inspection.Json().Deserialize<System.Collections.Generic.Dictionary<string,object>>(Encoding.UTF8.GetString(json));
                if(fields==null||!fields.ContainsKey("Version")||!fields.ContainsKey("Algorithm")||!fields.ContainsKey("Strength"))throw new InvalidDataException("Missing parameter fields");
                var options = Inspection.Json().Deserialize<ScrambleOptions>(Encoding.UTF8.GetString(json));
                if (options == null || options.Width < 1 || options.Height < 1) throw new InvalidDataException("Invalid parameter dimensions");
                options.Validate(false); return options;
            }
        }
        static void ReadExact(Stream stream, byte[] data) { int p = 0; while (p < data.Length) { int n = stream.Read(data, p, data.Length - p); if (n == 0) throw new InvalidDataException("Truncated record"); p += n; } }
        public static int[] Pixels(Bitmap image)
        {
            var pixels = new int[checked(image.Width * image.Height)]; var rectangle = new Rectangle(0, 0, image.Width, image.Height);
            var data = image.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < image.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * image.Width, image.Width); } finally { image.UnlockBits(data); }
            return pixels;
        }
        public static Bitmap Transform(Bitmap source, ScrambleOptions options, bool reverse, CancellationToken token)
        {
            int[] pixels = ScrambleEngine.Transform(Pixels(source), source.Width, source.Height, options, reverse, token);
            return FromPixels(pixels,source.Width,source.Height);
        }
        public static Bitmap FromPixels(int[] pixels,int width,int height)
        {
            if(pixels==null||pixels.Length!=checked(width*height))throw new ArgumentException("Invalid pixels");
            var output = new Bitmap(width,height,PixelFormat.Format32bppArgb); bool success = false;
            try
            {
                var data = output.LockBits(new Rectangle(0, 0, output.Width, output.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { for (int y = 0; y < output.Height; y++) Marshal.Copy(pixels, y * output.Width, IntPtr.Add(data.Scan0, y * data.Stride), output.Width); } finally { output.UnlockBits(data); }
                success = true; return output;
            }
            finally { if (!success) output.Dispose(); }
        }
    }
}
