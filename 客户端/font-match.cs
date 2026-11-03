using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

// 字體模板比對：輸入一行裁圖 + OCR 文字，對每個字元在 Noto 字型候選中找最像的。
// 只用來「校正 Windows OCR 的形近錯字」，不是取代 OCR；低分不覆蓋。
class FontMatch
{
    static PrivateFontCollection PFC;
    static FontFamily FAM;
    static Dictionary<char, Bitmap> Cache = new Dictionary<char, Bitmap>();
    static List<char> Charset = new List<char>();

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2) { Console.Error.WriteLine("usage: font-match.exe <line.png> <ocrText> [font.otf]"); Environment.Exit(2); }
        string img = args[0], ocr = args[1];
        string font = args.Length > 2 ? args[2] : @"D:\Project\HulinBackroomsWiki\客户端\fonts\NotoSansCJKtc-Regular.otf";
        LoadFont(font);
        LoadCharset();
        var res = Match(img, ocr);
        Console.WriteLine(new JavaScriptSerializer().Serialize(res));
    }

    static void LoadFont(string path)
    {
        PFC = new PrivateFontCollection();
        PFC.AddFontFile(path);
        FAM = PFC.Families[0];
    }

    static void LoadCharset()
    {
        string meta = @"D:\Project\HulinBackroomsWiki\客户端\fonts\atlas-32.json";
        if (!File.Exists(meta)) return;
        try
        {
            var s = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(meta));
            var arr = (object[])s["chars"];
            foreach (Dictionary<string, object> it in arr)
            {
                string c = Convert.ToString(it["c"]);
                if (!string.IsNullOrEmpty(c)) Charset.Add(c[0]);
            }
        }
        catch { }
        if (Charset.Count == 0)
        {
            for (char c = '\u4e00'; c <= '\u9fff'; c++) Charset.Add(c);
        }
    }

    static Bitmap Render(char c, int px)
    {
        string key = c + "|" + px;
        if (Cache.ContainsKey(c)) return Cache[c];
        var b = new Bitmap(px + 2, px + 2);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.Black);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var f = new Font(FAM, px, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Color.White))
                g.DrawString(c.ToString(), f, br, 1, 1);
        }
        var bb = Bounds(b);
        if (!bb.IsEmpty)
        {
            var trimmed = new Bitmap(bb.Width, bb.Height);
            using (var g = Graphics.FromImage(trimmed)) g.DrawImage(b, new Rectangle(0, 0, bb.Width, bb.Height), bb, GraphicsUnit.Pixel);
            Cache[c] = trimmed;
            return trimmed;
        }
        Cache[c] = b;
        return b;
    }

    static Bitmap TextMask(string path)
    {
        using (var src = new Bitmap(path))
        {
            var b = new Bitmap(src.Width, src.Height);
            int min = 255, max = 0;
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    var p = src.GetPixel(x, y);
                    int lum = (p.R * 299 + p.G * 587 + p.B * 114) / 1000;
                    if (lum < min) min = lum;
                    if (lum > max) max = lum;
                }
            // 顏色會變：用邊框估計背景亮暗，再相對取文字；不要寫死 145。
            long bs = 0; int bn = 0;
            for (int x = 0; x < src.Width; x++) { var a = src.GetPixel(x, 0); var z = src.GetPixel(x, src.Height - 1); bs += (a.R+a.G+a.B)/3 + (z.R+z.G+z.B)/3; bn += 2; }
            for (int y = 0; y < src.Height; y++) { var a = src.GetPixel(0, y); var z = src.GetPixel(src.Width - 1, y); bs += (a.R+a.G+a.B)/3 + (z.R+z.G+z.B)/3; bn += 2; }
            int bmean = bn > 0 ? (int)(bs / bn) : 0;
            bool darkText = bmean > 128;
            int thr = min + (max - min) * 45 / 100;
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    var p = src.GetPixel(x, y);
                    int lum = (p.R * 299 + p.G * 587 + p.B * 114) / 1000;
                    bool on = darkText ? (lum < thr) : (lum > thr);
                    b.SetPixel(x, y, on ? Color.White : Color.Black);
                }
            return b;
        }
    }

    static Rectangle Bounds(Bitmap b)
    {
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).R > 127)
                {
                    if (x < x0) x0 = x; if (y < y0) y0 = y;
                    if (x > x1) x1 = x; if (y > y1) y1 = y;
                }
        if (x1 < x0) return Rectangle.Empty;
        return new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    static Bitmap Normalize(Bitmap src, int w, int h)
    {
        var b = new Bitmap(w, h);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        return b;
    }

    static double Score(Bitmap a, Bitmap b)
    {
        int hit = 0, union = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                bool pa = a.GetPixel(x, y).R > 127;
                bool pb = b.GetPixel(x, y).R > 127;
                if (pa || pb) union++;
                if (pa && pb) hit++;
            }
        return union == 0 ? 0 : (double)hit / union;
    }

    static List<char> Confusables(char c)
    {
        var map = new Dictionary<char, string> {
            {'夭',"天"}, {'天',"夭"}, {'潦',"流"}, {'流',"潦"}, {'冂',"门"}, {'门',"冂"},
            {'囗',"口"}, {'口',"囗"}, {'麵',"面"}, {'面',"麵"}, {'迴',"回"}, {'回',"迴"},
            {'祂',"他"}, {'牠',"它"}, {'祇',"只"}, {'齣',"出"}, {'證',"证"}, {'証',"证"},
            {'山',"山dD"}  /* d + 」被 OCR 併成山 */
        };
        var list = new List<char> { c };
        if (map.ContainsKey(c)) foreach (var x in map[c]) if (!list.Contains(x)) list.Add(x);
        return list;
    }

    static object Match(string imgPath, string ocr)
    {
        var src = TextMask(imgPath);
        var box = Bounds(src);
        if (box.IsEmpty) return new { text = ocr, error = "blank" };
        var line = new Bitmap(box.Width, box.Height);
        using (var g = Graphics.FromImage(line)) g.DrawImage(src, new Rectangle(0, 0, box.Width, box.Height), box, GraphicsUnit.Pixel);
        // 以 OCR 字數均分；標點/拉丁字元先跳過
        var chars = ocr.Where(c => !char.IsWhiteSpace(c)).ToArray();
        if (chars.Length == 0) return new { text = ocr, error = "empty" };
        int cw = Math.Max(1, line.Width / chars.Length);
        var outp = new StringBuilder();
        var details = new List<object>();
        for (int i = 0; i < chars.Length; i++)
        {
            int x0 = i * cw, w = (i == chars.Length - 1) ? line.Width - x0 : cw;
            w = Math.Min(w, line.Width - x0);
            if (w <= 0) { outp.Append(chars[i]); continue; }
            var cell = new Bitmap(w, line.Height);
            using (var g = Graphics.FromImage(cell)) g.DrawImage(line, new Rectangle(0, 0, w, line.Height), new Rectangle(x0, 0, w, line.Height), GraphicsUnit.Pixel);
            var cb = Bounds(cell);
            if (cb.IsEmpty) { outp.Append(chars[i]); continue; }
            var glyph = new Bitmap(cb.Width, cb.Height);
            using (var g = Graphics.FromImage(glyph)) g.DrawImage(cell, new Rectangle(0, 0, cb.Width, cb.Height), cb, GraphicsUnit.Pixel);
            var ng = Normalize(glyph, 24, 24);
            char best = chars[i]; double bestScore = 0;
            // 只比 OCR 字 + 已知形近字；不做全域猜字，避免把正常字換成罕字。
            var conf = Confusables(chars[i]);
            foreach (char cand in conf)
            {
                var t = Normalize(Render(cand, Math.Max(8, line.Height)), 24, 24);
                double s = Score(ng, t);
                if (s > bestScore) { bestScore = s; best = cand; }
            }
            outp.Append(best);
            details.Add(new { ocr = chars[i], best = best, score = Math.Round(bestScore, 3), x = x0, w = w });
        }
        return new { text = outp.ToString(), details = details };
    }
}
