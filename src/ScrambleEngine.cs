using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace QQImageSwitch
{
    public sealed class ScrambleOptions
    {
        public int Version = 1, Rounds = 1, Algorithm, Variant, Block = 16, Width, Height;
        public double Strength = 1;
        public bool Green, ReduceGreen;
        public string Seed = "", Key = "";
        public ScrambleOptions Copy() { return (ScrambleOptions)MemberwiseClone(); }
        public void Validate(bool requireKey)
        {
            if (Rounds < 1 || Rounds > 20 || Version != 1 || Algorithm < 0 || Algorithm > 3 || Variant < 0 || Variant > 1 || double.IsNaN(Strength) || Strength < 0 || Strength > 2)
                throw new ArgumentException("混淆参数无效 / Invalid scramble parameters");
            double key;
            if (requireKey && (Algorithm == 1 || Algorithm == 2) && (!double.TryParse(Key, NumberStyles.Float, CultureInfo.InvariantCulture, out key) || !(key > 0 && key < 1)))
                throw new ArgumentException("PicEncrypt 密钥须为 0 与 1 之间的小数 / Key must be between 0 and 1");
            if (Algorithm == 3 && (Block != 8 && Block != 16 && Block != 32 || Seed == null || !System.Text.RegularExpressions.Regex.IsMatch(Seed, "^[0-9a-fA-F]{32}$")))
                throw new ArgumentException("分块种子或尺寸无效 / Invalid block seed or size");
        }
    }

    // Independent adapters. Gilbert traversal remains in Obfuscation (BSD-2-Clause).
    public static class ScrambleEngine
    {
        sealed class Random32
        {
            uint state;
            public Random32(string seed) { for (int i = 0; i < 32; i += 8) state ^= uint.Parse(seed.Substring(i, 8), NumberStyles.HexNumber); if (state == 0) state = 0x6d2b79f5; }
            uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
            public int Pick(int n) { ulong limit = (0x100000000UL / (uint)n) * (uint)n; uint x; do { x = Next(); } while (x >= limit); return (int)(x % (uint)n); }
        }
        struct Tile { public int X, Y, W, H; }
        public static int[] Transform(int[] pixels, int width, int height, ScrambleOptions options, bool reverse, CancellationToken token)
        {
            options.Validate(true); token.ThrowIfCancellationRequested();
            if (width < 1 || height < 1 || (long)width * height != pixels.Length) throw new ArgumentException("Invalid pixel dimensions");
            if (options.Algorithm == 0)
            {
                int[] map = ReferenceCurve.Build(width, height, token);
                int offset = (int)(Math.Floor(options.Strength * (Math.Sqrt(5) - 1) / 2 * map.Length + .5) % map.Length);
                for(int round=0;round<options.Rounds;round++){
                    token.ThrowIfCancellationRequested();if(!reverse&&options.Green)ReferenceColors.Adjust(pixels,true,token);
                    var result=new int[pixels.Length];for(int i=0;i<map.Length;i++){if((i&65535)==0)token.ThrowIfCancellationRequested();int oldPos=map[i],newPos=map[(i+offset)%map.Length];if(reverse)result[oldPos]=pixels[newPos];else result[newPos]=pixels[oldPos];}
                    if(reverse&&options.ReduceGreen)ReferenceColors.Adjust(result,false,token);pixels=result;
                }
                return pixels;
            }
            if (options.Algorithm == 3) return Blocks(pixels, width, height, options, reverse, token);
            double key = double.Parse(options.Key, CultureInfo.InvariantCulture);
            if (reverse && options.Algorithm == 2) Axis(pixels, width, height, key, true, true, true, token);
            Axis(pixels, width, height, key, false, reverse, options.Algorithm == 2 || options.Variant == 1, token);
            if (!reverse && options.Algorithm == 2) Axis(pixels, width, height, key, true, false, true, token);
            return pixels;
        }
        static void Axis(int[] pixels, int w, int h, double seed, bool columns, bool reverse, bool advance, CancellationToken token)
        {
            int length = columns ? h : w, count = columns ? w : h;
            var values = new double[length]; var order = new int[length]; var row = new int[length]; double x = seed;
            for (int line = 0; line < count; line++)
            {
                token.ThrowIfCancellationRequested(); if (!advance) x = seed;
                values[0] = x; order[0] = 0;
                for (int j = 1; j < length; j++) { x = 3.9999999 * x * (1 - x); values[j] = x; order[j] = j; }
                Array.Sort(order, (a, b) => { int c = values[a].CompareTo(values[b]); return c == 0 ? a.CompareTo(b) : c; });
                for (int j = 0; j < length; j++) row[j] = pixels[columns ? j * w + line : line * w + j];
                for (int j = 0; j < length; j++) { int pos = reverse ? order[j] : j; pixels[columns ? pos * w + line : line * w + pos] = row[reverse ? j : order[j]]; }
            }
        }
        static int[] Blocks(int[] source, int w, int h, ScrambleOptions o, bool reverse, CancellationToken token)
        {
            var groups = new List<List<Tile>>(); var sizes = new List<int>();
            for (int y = 0; y < h; y += o.Block) for (int x = 0; x < w; x += o.Block)
            {
                int tw = Math.Min(o.Block, w - x), th = Math.Min(o.Block, h - y), size = tw * 64 + th, group = sizes.IndexOf(size);
                if (group < 0) { group = sizes.Count; sizes.Add(size); groups.Add(new List<Tile>()); }
                groups[group].Add(new Tile { X = x, Y = y, W = tw, H = th });
            }
            var rng = new Random32(o.Seed); var output = new int[source.Length];
            foreach (var tiles in groups)
            {
                int n = tiles.Count; var permutation = new int[n]; for (int i = 0; i < n; i++) permutation[i] = i;
                for (int i = n - 1; i > 0; i--) { int j = rng.Pick(i + 1), t = permutation[i]; permutation[i] = permutation[j]; permutation[j] = t; }
                Tile shape = tiles[0]; int[] curve = Obfuscation.Curve(shape.W, shape.H, token); int shift = (int)Math.Floor(((Math.Sqrt(5) - 1) / 2) * curve.Length + .5) % curve.Length;
                for (int i = 0; i < n; i++)
                {
                    token.ThrowIfCancellationRequested(); Tile a = tiles[i], b = tiles[permutation[i]]; int rotation = rng.Pick(shape.W == shape.H ? 4 : 2), flip = rng.Pick(2);
                    for (int j = 0; j < curve.Length; j++)
                    {
                        int p = curve[j], q = curve[(j + shift) % curve.Length], xx = q % shape.W, yy = q / shape.W;
                        if (flip != 0) xx = shape.W - 1 - xx;
                        int rx = xx, ry = yy;
                        if (shape.W == shape.H) { if (rotation == 1) { rx = shape.W - 1 - yy; ry = xx; } else if (rotation == 2) { rx = shape.W - 1 - xx; ry = shape.H - 1 - yy; } else if (rotation == 3) { rx = yy; ry = shape.H - 1 - xx; } }
                        else if (rotation != 0) { rx = shape.W - 1 - xx; ry = shape.H - 1 - yy; }
                        int from = (a.Y + p / shape.W) * w + a.X + p % shape.W, to = (b.Y + ry) * w + b.X + rx;
                        if (reverse) output[from] = source[to]; else output[to] = source[from];
                    }
                }
            }
            return output;
        }
        public static double Continuity(int[] pixels, int w, int h)
        {
            double sum = 0; int count = 0, stride = Math.Max(1, (int)Math.Sqrt((long)w * h / 12000.0));
            for (int y = 0; y < h; y += stride) for (int x = 0; x < w; x += stride) { int p = y * w + x; if (x + 1 < w) { sum += Difference(pixels[p], pixels[p + 1]); count++; } if (y + 1 < h) { sum += Difference(pixels[p], pixels[p + w]); count++; } }
            return sum / Math.Max(1, count);
        }
        static int Difference(int a, int b) { return Math.Abs((a & 255) - (b & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)); }
    }
}
