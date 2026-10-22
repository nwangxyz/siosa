using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace Siosa
{
    // 보상 아이콘 (data/reward_icons.png + reward_icons.json, Extractor/build_icons.py 가 만든다).
    // 칸 색만으로 어느 퀘스트의 보상 창인지 확정할 수 없을 때(후보가 둘 이상) 보상 칸 그림을 이 아이콘과 비교한다.
    //   - 젬: 결정 위에 스킬마다 다른 금색 문양 → 같은 색끼리도 구분된다
    //   - 장비·플라스크: 아이템 그림 (플라스크는 빈 병)
    public class RewardIcons
    {
        // 게임이 보상 칸 안에 그림을 그리는 위치 (실측 스크린샷으로 맞춘 값)
        //   젬: 67px 칸 안에 62px, 오른쪽 +2, 아래 +4
        //   장비·플라스크: 칸의 94% 크기, 오른쪽 +1, 아래 +2 (1x2 플라스크 127.5px 기준)
        const double GemSize = 62 / 67.0, GemDx = 2 / 67.0, GemDy = 4 / 67.0;
        const double ItemScale = 0.94, ItemDx = 1 / 67.0, ItemDy = 2 / 127.5;
        static readonly Color WindowBack = Color.FromArgb(25, 20, 17);   // 보상 창 안쪽 바탕색 (투명한 유리병 합성용)

        int size;
        readonly Dictionary<string, int[]> icons = new Dictionary<string, int[]>();   // 영문 이름 → ARGB size*size
        readonly HashSet<string> gems = new HashSet<string>();

        public bool Has(string en) => en != null && icons.ContainsKey(en);

        public static RewardIcons Load(string dataDir)
        {
            var g = new RewardIcons();
            if (!GameData.DataExists(dataDir, "reward_icons.json") || !GameData.DataExists(dataDir, "reward_icons.png")) return g;   // 그림이 없으면 색만 본다
            var meta = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(GameData.ReadData(dataDir, "reward_icons.json"));
            g.size = Convert.ToInt32(meta["size"]);
            var names = ((System.Collections.ArrayList)meta["names"]).Cast<string>().ToList();
            var isGem = ((System.Collections.ArrayList)meta["gem"]).Cast<bool>().ToList();
            using (var st = GameData.OpenData(dataDir, "reward_icons.png"))
            using (var atlas = new Bitmap(st))
            {
                var all = Read(atlas);
                for (int i = 0; i < names.Count; i++)
                {
                    var px = new int[g.size * g.size];
                    for (int y = 0; y < g.size; y++)
                        Array.Copy(all, y * atlas.Width + i * g.size, px, y * g.size, g.size);
                    g.icons[names[i]] = px;
                    if (isGem[i]) g.gems.Add(names[i]);
                }
            }
            return g;
        }

        static int[] Read(Bitmap bmp)
        {
            var data = new int[bmp.Width * bmp.Height];
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bd.Scan0, data, 0, data.Length);
            bmp.UnlockBits(bd);
            return data;
        }

        int[] Shrink(Bitmap shot, RectangleF src)
        {
            using (var small = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(shot, new Rectangle(0, 0, size, size), src, GraphicsUnit.Pixel);
                }
                return Read(small);
            }
        }

        // 보상 칸(화면 캡처의 cell 영역)과 아이콘의 차이. 작을수록 비슷하다.
        //   젬: 같으면 대략 8~25, 다르면 25 이상 / 장비·플라스크: 같으면 11~13, 다르면 27 이상 (실측)
        public double Distance(Bitmap shot, Rectangle cell, string en)
        {
            var icon = icons[en];
            long sum = 0; int n = 0;
            if (gems.Contains(en))
            {
                double s = GemSize * cell.Width;
                var px = Shrink(shot, new RectangleF((float)(cell.X + (cell.Width - s) / 2 + GemDx * cell.Width),
                                                    (float)(cell.Y + (cell.Height - s) / 2 + GemDy * cell.Height), (float)s, (float)s));
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        if (y < size / 3 && x >= size * 2 / 3) continue;   // 오른쪽 위 "!"(새 젬) 표시 자리는 제외
                        int a = icon[y * size + x];
                        if (((a >> 24) & 255) <= 160) continue;             // 아이콘의 투명한 부분은 비교하지 않는다
                        sum += Diff(a, px[y * size + x]);
                        n++;
                    }
            }
            else
            {
                // 그림이 있는 부분(불투명도 40 초과)만 비교. 투명한 유리병은 창 바탕색 위에 합성한 색으로 비교한다.
                // (칸 전체를 비교하면 거의 투명한 그림은 어느 칸과도 비슷하게 나온다)
                double w = ItemScale * cell.Width, h = ItemScale * cell.Height;
                var px = Shrink(shot, new RectangleF((float)(cell.X + (cell.Width - w) / 2 + ItemDx * cell.Width),
                                                    (float)(cell.Y + (cell.Height - h) / 2 + ItemDy * cell.Height), (float)w, (float)h));
                for (int i = 0; i < icon.Length; i++)
                {
                    if (((icon[i] >> 24) & 255) <= 40) continue;
                    sum += Diff(Over(icon[i], WindowBack), px[i]);
                    n++;
                }
            }
            return n < 20 ? double.MaxValue : sum / (3.0 * n);
        }

        static int Diff(int a, int b) =>
            Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs((a & 255) - (b & 255));

        static int Over(int argb, Color back)
        {
            int a = (argb >> 24) & 255;
            int r = (((argb >> 16) & 255) * a + back.R * (255 - a)) / 255;
            int g = (((argb >> 8) & 255) * a + back.G * (255 - a)) / 255;
            int b = ((argb & 255) * a + back.B * (255 - a)) / 255;
            return (r << 16) | (g << 8) | b;
        }
    }
}
