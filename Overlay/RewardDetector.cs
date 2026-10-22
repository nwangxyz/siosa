using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Siosa
{
    // 보상 창이 열렸는지, 열렸다면 어느 퀘스트의 창인지를 보상 칸 영역의 색으로 판별한다.
    //   - 같은 클래스의 퀘스트 보상은 "칸별 색 배열"(빨강/초록/파랑 젬, 장비)과 개수가 대부분 서로 다르다.
    //   - 보상 칸은 개수에 따라 가운데 정렬되므로, 개수가 다른 후보는 칸 위치 자체가 어긋나 걸러진다.
    //   - 화면 전체가 아니라 보상 칸이 놓일 영역만 캡처한다.
    public class RewardDetector
    {
        public class Candidate
        {
            public Offer Offer;
            public Reward Pick;
            public List<Reward> Items;
            public bool Claimed;    // 이미 받음 처리된 보상 (다시 열리면 '안 받음'으로 되돌리기 위해 후보에 포함)
            public int Priority;    // 작을수록 우선 (색 배열이 같은 후보끼리 비교할 때)
        }

        public class Result
        {
            public Candidate Match;
            public double Score;
            public List<Rectangle> Rects;   // 매치된 후보의 칸 화면 좌표
            public RewardLayout Layout;      // 맞은 배치 (여러 줄 창이면 기억해 둘 값)
            public List<Result> Ties = new List<Result>();   // 색 배열이 같아 구분할 수 없는 다른 퀘스트
            public double[] BaseDist;                         // 판별했을 때 칸별 아이콘 거리 (같은 자리에 다른 창이 떴는지 확인할 기준)
        }

        const double PassScore = 0.75;   // 칸 색 판정 통과 기준

        readonly Settings s;
        public RewardIcons Icons;   // 색 배열로 구분이 안 될 때 아이콘 비교용 (없으면 비교하지 않음)
        public RewardDetector(Settings settings) { s = settings; }

        // 픽셀 하나 분류: 0 배경(어두움) / 1 빨강 / 2 초록 / 3 파랑 / 4 흰색·밝은 회색 / 5 그 밖(금색 장식, 갈색 테두리 등)
        static int PixelClass(int r, int g, int b)
        {
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            int lum = (r + g + b) / 3, sat = max - min;
            if (lum < 45 && sat < 35) return 0;
            if (sat < 35) return lum > 120 ? 4 : 5;
            if (b == max && b > r + 25 && b > g + 10) return 3;
            if (r == max && r > g + 60 && r > b + 60) return 1;   // 금색(R,G 모두 높음)은 빨강이 아님
            if (g == max && g > r + 15 && g > b + 15) return 2;
            return 5;
        }

        // 칸 판정(픽셀 투표): 'r' 'g' 'b' 'w'(흰 젬) 'x'(무언가 있음) '.'(빈칸)
        //   평균 색을 쓰면 젬의 금색 문양이나 플라스크의 투명한 부분 때문에 색이 흐려진다.
        static char Classify(int[] counts)
        {
            int n = counts.Sum(), filled = n - counts[0];
            if (n == 0 || filled < n * 0.12) return '.';
            int best = 1;
            for (int k = 2; k <= 3; k++) if (counts[k] > counts[best]) best = k;
            int second = new[] { 1, 2, 3 }.Where(k => k != best).Max(k => counts[k]);
            if (counts[best] >= filled * 0.15 && counts[best] >= second * 1.5) return "?rgb"[best];
            if (counts[4] >= filled * 0.3) return 'w';
            return 'x';
        }

        List<Rectangle> Place(Rectangle client, Candidate c, RewardLayout layout) =>
            RewardGeometry.Place(client, c.Items, layout);

        // 후보마다 시도할 배치들. 보상이 5개 이하면 어떤 배치든 한 줄이라 하나만 본다.
        public IEnumerable<RewardLayout> LayoutsFor(Candidate c)
        {
            return RewardLayout.Hypotheses(c.Items.Count);
        }

        // shift: 열림 확인 때 맞은 창 높이. 그 높이를 만드는 후보·배치만 비교한다 (null 이면 전부).
        double? onlyShift;

        public Result Detect(IList<Candidate> candidates, double? shift = null)
        {
            if (candidates.Count == 0) return null;
            onlyShift = shift;
            distCache.Clear();
            var client = RewardGeometry.GameClient();

            // 모든 후보·배치의 칸 + 양옆 확인 칸을 덮는 영역만 캡처 (캡처는 한 번, 계산만 여러 번)
            var all = candidates.SelectMany(c => Usable(c).SelectMany(l =>
            {
                var p = Place(client, c, l);
                return p.Concat(RewardGeometry.Neighbors(client, p, l.Sizes));
            })).ToList();
            if (all.Count == 0) return null;
            var area = all.Aggregate(Rectangle.Union);
            area.Intersect(client);
            if (area.Width <= 0 || area.Height <= 0) return null;

            using (var bmp = Capture(area))
                return DetectIn(bmp, area.Location, client, candidates, shift);
        }

        // 화면 아래 가운데에 "퀘스트 완료" 띠가 떠 있는지 (보상을 받으면 뜬다)
        public bool QuestCompleteVisible() => QuestCompleteIn(null, Point.Empty, RewardGeometry.GameClient());

        // 화면 가운데, 그리고 인벤토리가 열려 있을 때의 왼쪽 영역 가운데 — 두 위치를 모두 본다
        public static bool QuestCompleteIn(Bitmap shot, Point origin, Rectangle client) =>
            QuestCompleteAt(shot, origin, client, false) || QuestCompleteAt(shot, origin, client, true);

        static bool QuestCompleteAt(Bitmap shot, Point origin, Rectangle client, bool panelOpen)
        {
            var (left, right, text) = RewardGeometry.QuestCompleteProbes(client, panelOpen);
            var area = Rectangle.Union(Rectangle.Union(left, right), text);
            Bitmap bmp = shot;
            try
            {
                if (bmp == null) { bmp = Capture(area); origin = area.Location; }
                var px = new Pixels(bmp, origin);
                var want = RewardGeometry.QuestCompleteColor;
                foreach (var r in new[] { left, right })
                {
                    var c = px.Average(r);
                    if (Math.Abs(c.R - want.R) > 22 || Math.Abs(c.G - want.G) > 18 || Math.Abs(c.B - want.B) > 15) return false;
                }
                return px.BrightFraction(text) >= 0.08;   // 가운데에 글자가 있어야 한다
            }
            finally { if (shot == null) bmp?.Dispose(); }
        }

        // 판별해 둔 창의 보상 칸이 아직 화면에 있는지 (툴팁이 띠를 가렸을 때 닫힘과 구분용).
        // 툴팁은 보통 아이템 위쪽에 뜨므로 칸 줄 대부분은 보인다 → 기준을 조금 낮춰 0.6.
        // 판별했을 때 칸별 아이콘 거리를 기록해 둔다 (StillThere 의 기준)
        public void SetBaseline(Result r)
        {
            if (Icons == null) return;
            var area = r.Rects.Aggregate(Rectangle.Union);
            area.Inflate(4, 4);
            using (var bmp = Capture(area))
            {
                r.BaseDist = CellDistancesIn(bmp, area.Location, r);
            }
        }

        // 판별해 둔 창이 아직 화면에 있는지 (띠가 안 보일 때 사용).
        //   툴팁은 띠·명판과 일부 칸을 가리지만 나머지 칸의 테두리 선은 보인다. 창을 닫으면 선이 없어진다.
        //   칸 좌우 끝의 밝은 선이 보이는 칸이 30% 이상(최소 1칸)이면 있는 것.
        //   - 아이콘 거리로는 안 됨: 어두운 게임 배경과 어두운 장비 그림이 실제 창만큼 가깝게 나온다
        //   - 판별 때 테두리 띠 픽셀과 비교해도 안 됨: 띠 대부분이 창의 어두운 바탕이라 어두운 마을 바닥과 같게 나온다
        //     (3장 황금 책자 창에서 Esc 후에도 0.6~0.7 이 나와 강조가 남았다)
        public bool FramesThere(Result r)
        {
            var client = RewardGeometry.GameClient();
            var area = r.Rects.Concat(RewardGeometry.Neighbors(client, r.Rects, r.Layout.Sizes)).Aggregate(Rectangle.Union);
            area.Inflate(6, 6);
            area.Intersect(client);
            if (area.Width <= 0 || area.Height <= 0) return false;
            using (var bmp = Capture(area))
                return Held(new Pixels(bmp, area.Location) { Scale = client.Height / 1080.0 }, client, r);
        }

        // 띠가 안 보일 때 판별해 둔 창이 아직 열려 있다고 볼지.
        //   1) 칸 테두리 선이 30% 이상 보이면 있음
        //   2) 젬 보상 창은 툴팁이 칸 대부분을 덮어도(툴팁은 반투명) 칸 색이 판별 때와 그대로 읽힌다.
        //      다른 색으로 읽히는 칸이 하나도 없고, 맞는 칸이 60% 이상(최소 3칸)이면 있음.
        //      툴팁의 불투명한 부분(제목 띠 등)에 덮인 칸은 빈칸으로 읽히므로 감점하지 않는다.
        //      (파멸 툴팁: 테두리 선 3/13칸, 폭풍 연대 툴팁: 보조 젬 제목 띠가 한 칸을 완전히 덮음)
        //      창을 닫으면 칸 자리에 게임 배경이 보여 맞는 칸이 거의 없다.
        bool Held(Pixels px, Rectangle client, Result r)
        {
            if (FramesVisible(px, r.Rects)) return true;
            var items = r.Match.Items;
            if (items.Count < 3 || items.Any(it => it.gem == null)) return false;
            int match = 0, wrong = 0;
            for (int i = 0; i < items.Count; i++)
            {
                char got = Classify(px.Count(Inner(r.Rects[i], 0.15))), want = items[i].gem[0];
                if (got == want || (got == 'x' && want == 'w')) match++;
                else if (got != '.' && got != 'x') wrong++;
            }
            LastStillInfo += $", 칸 색 {match}/{items.Count} 맞음 {wrong} 틀림";
            return wrong == 0 && match >= Math.Max(3, (int)Math.Ceiling(items.Count * 0.6));
        }

        bool FramesVisible(Pixels px, List<Rectangle> rects)
        {
            int framed = rects.Count(px.Framed);
            LastStillInfo = $"테두리 선 {framed}/{rects.Count}칸";
            return framed >= Math.Max(1, (int)Math.Ceiling(rects.Count * 0.3));
        }

        // 개발용: 칸마다 왼쪽·오른쪽 테두리 선 밝기 차 (기준 need)
        public static string FrameInfoIn(Bitmap bmp, Result r)
        {
            var px = new Pixels(bmp, Point.Empty) { Scale = bmp.Height / 1080.0 };
            double need = 18 * Math.Min(1, px.Scale);
            return $"기준 {need:0.0}: " + string.Join("  ", r.Rects.Select(q => $"[{q.X},{q.Y} {q.Width}x{q.Height}] {px.EdgeLine(q.X, q.Y, q.Height):0} / {px.EdgeLine(q.Right - 1, q.Y, q.Height):0}"));
        }

        // 개발용: 스크린샷에서 창이 아직 있다고 볼지
        public bool FramesVisibleIn(Bitmap bmp, Point origin, Result r) => FramesVisible(new Pixels(bmp, origin) { Scale = bmp.Height / 1080.0 }, r.Rects);
        public bool HeldIn(Bitmap bmp, Point origin, Result r) => Held(new Pixels(bmp, origin) { Scale = bmp.Height / 1080.0 }, new Rectangle(0, 0, bmp.Width, bmp.Height), r);

        // 결과의 칸별 아이콘 거리 (아이콘이 없는 보상은 NaN)
        public double[] CellDistancesIn(Bitmap bmp, Point origin, Result r)
        {
            var px = new Pixels(bmp, origin);
            distCache.Clear();
            return r.Match.Items.Select((it, i) => Icons != null && Icons.Has(it.en) ? IconDist(px, r.Rects[i], it.en) : double.NaN).ToArray();
        }

        public string LastStillInfo = "";   // 디버그: 마지막 StillThere 의 칸별 거리

        // 판별해 둔 창이 아직 화면에 있는지.
        //   판별 때와 비슷하게 보이는 칸이 2개 이상(보상이 1개면 1개) 남아 있으면 있는 것.
        //   - 툴팁이 띠나 일부 칸을 가려도 유지되고
        //   - 창을 닫으면 칸 자리에 게임 배경이 보여 아이콘과 달라지므로 바로 사라진다
        //     (장비 칸을 "무언가 있음"으로만 보면 배경도 통과해서 강조가 남았다)
        public bool StillThere(Result r)
        {
            var client = RewardGeometry.GameClient();
            var area = r.Rects.Aggregate(Rectangle.Union);
            area.Intersect(client);
            if (area.Width <= 0 || area.Height <= 0) return false;
            if (Icons != null && r.BaseDist != null)
            {
                using (var bmp = Capture(area))
                {
                    var px = new Pixels(bmp, area.Location);
                    distCache.Clear();
                    int need = Math.Min(2, r.Match.Items.Count), seen = 0;
                    var info = new StringBuilder();
                    for (int i = 0; i < r.Match.Items.Count && seen < need; i++)
                    {
                        if (double.IsNaN(r.BaseDist[i])) continue;
                        double d = IconDist(px, r.Rects[i], r.Match.Items[i].en);
                        info.Append($"{r.BaseDist[i]:0.0}→{d:0.0} ");
                        if (d <= Math.Max(r.BaseDist[i] + 10, 25) && d <= 45) seen++;
                    }
                    LastStillInfo = info.ToString();
                    return seen >= need;
                }
            }
            using (var bmp = Capture(area))
            {
                var px = new Pixels(bmp, area.Location);
                double ok = 0;
                for (int i = 0; i < r.Match.Items.Count; i++)
                {
                    var it = r.Match.Items[i];
                    var cnt = px.Count(Inner(r.Rects[i], 0.15));
                    if (it.gem == null) { int n = cnt.Sum(); ok += n > 0 && n - cnt[0] >= n * 0.08 ? 1 : 0; }
                    else ok += Classify(cnt) == it.gem[0] ? 1 : 0;
                }
                return ok / r.Match.Items.Count >= 0.6;
            }
        }

        // 칸 테두리: 칸 왼쪽·오른쪽 끝에 1px 밝은 선(주변보다 약 30~40 밝음)이 있다.
        // 칸 높이 가운데 60% 구간에서 선(±2px 허용)과 3px 바깥·안쪽 평균의 밝기 차가 18 이상이면 선이 있는 것.
        static double FramedFraction(Pixels px, List<Rectangle> rects)
        {
            int framed = 0;
            foreach (var r in rects)
                if (px.Framed(r)) framed++;
            return rects.Count == 0 ? 0 : (double)framed / rects.Count;
        }

        // 같은 칸·같은 아이콘을 여러 배치 후보에서 반복 비교하므로 한 번 판별하는 동안 결과를 기억해 둔다
        readonly Dictionary<string, double> distCache = new Dictionary<string, double>();

        double IconDist(Pixels px, Rectangle screen, string en)
        {
            string key = $"{screen.X},{screen.Y},{screen.Width},{screen.Height},{en}";
            if (distCache.TryGetValue(key, out var d)) return d;
            d = Icons.Distance(px.Bmp, new Rectangle(screen.X - px.Origin.X, screen.Y - px.Origin.Y, screen.Width, screen.Height), en);
            distCache[key] = d;
            return d;
        }

        static Bitmap Capture(Rectangle area)
        {
            var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            return bmp;
        }

        // 이미지(화면 좌표 origin 에서 캡처한 것)에서 판별. 스크린샷 테스트에서도 쓴다.
        // shift: 열림 확인 때 맞은 창 높이 (null 이면 높이로 거르지 않음)
        public bool SkipFrames;   // 개발용(--detecttest): 툴팁에 가려진 화면에서 칸 자리만 구할 때

        public Result DetectIn(Bitmap bmp, Point origin, Rectangle client, IList<Candidate> candidates, double? shift = null)
        {
            onlyShift = shift;
            var px = new Pixels(bmp, origin) { Scale = client.Height / 1080.0 };
            var all = new List<Result>();
            Result best = null;
            foreach (var c in candidates)
                foreach (var layout in Usable(c))
                {
                    var rects = Place(client, c, layout);
                    var r = new Result { Match = c, Score = Score(px, client, c, rects, layout), Rects = rects, Layout = layout };
                    // 칸 자리에 실제 칸 테두리가 있어야 한다. 배치가 반 칸만 어긋나도(반지 4개 창에 주얼 3개 배치 등)
                    // 색·아이콘은 비슷하게 나올 수 있지만 테두리 선은 맞지 않는다.
                    if (r.Score >= PassScore && !SkipFrames) r.Score = Math.Min(r.Score, FramedFraction(px, rects) >= 0.75 ? 1 : 0.5);
                    all.Add(r);
                    if (best == null || Better(r, best)) best = r;
                }
            // 색 단계는 넉넉하게(칸 하나 정도 틀려도 통과): 어느 퀘스트인지는 아이콘 비교가 최종 결정한다.
            // (삼위일체 보조처럼 문양에 여러 색이 섞인 젬은 칸 색을 잘못 읽을 수 있다)
            // (개발용 SkipFrames: 툴팁에 가려진 화면에서 칸 자리만 구하므로 기준을 낮춘다. 실제 동작은 툴팁이 뜨기 전에 판별해 둔 칸을 쓴다)
            if (best == null || best.Score < (SkipFrames ? 0.5 : PassScore)) return new Result { Score = best?.Score ?? 0 };

            // 칸 색 판정을 통과한 다른 퀘스트가 있으면 색만으로는 확정할 수 없다 → 아이콘 비교 대상.
            //   장비·플라스크 칸은 "무언가 있음"만 보므로, 칸이 많은 장비 보상은 틀린 배치로도 다른 창을 통과할 수 있다.
            // 받음 처리된 퀘스트도 넣는다: 받음 판정이 틀렸을 수 있으므로(안 받고 닫은 경우 등) 아이콘으로 판단하게 한다.
            //   같은 퀘스트라도 배치(줄 나누기)가 여러 개 통과할 수 있으므로 (퀘스트, 배치) 조합을 모두 넘긴다.
            best.Ties = all.Where(r => !ReferenceEquals(r, best) && r.Score >= PassScore).ToList();
            return best;
        }

        IEnumerable<RewardLayout> Usable(Candidate c) =>
            LayoutsFor(c).Where(l => onlyShift == null || Math.Abs(RewardGeometry.Shift(c.Items, l) - onlyShift.Value) < 2);

        // 칸 색으로 확정이 안 되는 후보들(res + res.Ties)을 보상 아이콘으로 가른다.
        // 후보마다 "각 칸이 그 후보의 젬 아이콘과 얼마나 다른지"의 평균을 내고, 가장 작은 쪽이 확실히 앞서면 그 후보로 확정.
        public Result ResolveByIcons(Result res, out string why)
        {
            var all = new[] { res }.Concat(res.Ties).ToList();
            var area = all.SelectMany(r => r.Rects).Aggregate(Rectangle.Union);
            using (var bmp = Capture(area))
                return ResolveByIconsIn(bmp, area.Location, res, out why);
        }

        public Result ResolveByIconsIn(Bitmap shot, Point origin, Result res, out string why)
        {
            why = "아이콘 데이터 없음";
            if (Icons == null) return null;

            // (퀘스트, 배치) 조합마다 "각 칸이 그 보상 아이콘과 얼마나 다른지"의 평균 → 퀘스트마다 가장 잘 맞는 배치만 남긴다
            var perOffer = new Dictionary<string, (Result r, double d)>();
            foreach (var r in new[] { res }.Concat(res.Ties))
            {
                double sum = 0; int n = 0;
                for (int i = 0; i < r.Match.Items.Count; i++)
                {
                    var it = r.Match.Items[i];
                    if (!Icons.Has(it.en)) continue;
                    var cell = r.Rects[i];
                    sum += Icons.Distance(shot, new Rectangle(cell.X - origin.X, cell.Y - origin.Y, cell.Width, cell.Height), it.en);
                    n++;
                }
                if (n == 0) continue;
                double d = sum / n;
                if (!perOffer.TryGetValue(r.Match.Offer.id, out var cur) || d < cur.d) perOffer[r.Match.Offer.id] = (r, d);
            }
            var ranked = perOffer.Values.OrderBy(x => x.d).ToList();
            why = string.Join(", ", ranked.Select(x => $"{x.r.Match.Offer.id}({x.r.Layout}) {x.d:0.0}"));
            if (ranked.Count == 0) return null;

            var win = ranked[0].r;
            // 1등과 차이가 4 미만인 퀘스트는 그림으로도 구분할 수 없다 → 함께 표시.
            // (보상 구성이 완전히 같은 퀘스트도 여기에 해당한다. 예: 정화를 위한 죽음 / 바스티리의 날개의 주얼 3개)
            win.Ties = ranked.Skip(1).Where(x => x.d - ranked[0].d < 4).Select(x => x.r).ToList();
            return win;
        }

        static bool Better(Result a, Result b)
        {
            if (Math.Abs(a.Score - b.Score) > 0.001) return a.Score > b.Score;
            // 같은 점수면 더 많은 칸이 맞은 쪽(근거가 많은 쪽). 가운데 정렬이라 5개 창은 7개 창의 가운데와 겹친다.
            if (a.Match.Items.Count != b.Match.Items.Count) return a.Match.Items.Count > b.Match.Items.Count;
            if (a.Match.Claimed != b.Match.Claimed) return !a.Match.Claimed;   // 안 받은 쪽 우선
            return a.Match.Priority < b.Match.Priority;
        }

        double Score(Pixels px, Rectangle client, Candidate c, List<Rectangle> rects, RewardLayout layout)
        {
            double ok = 0, total = 0;
            for (int i = 0; i < c.Items.Count; i++)
            {
                total += 1;
                var it = c.Items[i];
                if (it.gem == null)
                {
                    // 장비/플라스크: 완전히 빈 칸이면 감점, 아이콘과 비슷하면 통과.
                    // (반지처럼 작은 그림은 칸의 몇 %만 차지해서 "무언가 있음"만으로는 판정할 수 없다)
                    var cnt = px.Count(Inner(rects[i], 0.05));
                    int n = cnt.Sum(), filled = n - cnt[0];
                    if (n == 0 || filled < n * 0.01) { ok -= 1; continue; }
                    if (Icons != null && Icons.Has(it.en)) ok += IconDist(px, rects[i], it.en) <= 40 ? 1 : 0;
                    else ok += filled >= n * 0.03 ? 1 : 0;
                    continue;
                }
                char got = Classify(px.Count(Inner(rects[i], 0.15)));
                char want = it.gem[0];
                if (got == want) ok += 1;
                else if (got == 'x' && want == 'w') ok += 0.5;
                else if (got == '.') ok -= 1;                         // 있어야 할 칸이 비어 있음
            }
            // 첫 줄 양옆은 비어 있어야 한다 (개수가 다른 창과 구분)
            foreach (var n in RewardGeometry.Neighbors(client, rects, layout.Sizes))
            {
                total += 1;
                ok += Classify(px.Count(Inner(n, 0.15))) == '.' ? 1 : -1;
            }
            return total > 0 ? ok / total : 0;
        }

        // 칸 테두리를 피해서 안쪽 영역만 본다
        static Rectangle Inner(Rectangle r, double inset)
        {
            int dx = (int)(r.Width * inset), dy = (int)(r.Height * inset);
            return Rectangle.Inflate(r, -dx, -dy);
        }

        // 후보들로 만들 수 있는 창 높이(위로 올라가는 양) 목록. 젬 한 줄(0)은 항상 포함.
        public IEnumerable<double> Shifts(IEnumerable<Candidate> candidates) =>
            candidates.SelectMany(c => LayoutsFor(c).Select(l => RewardGeometry.Shift(c.Items, l)))
                      .Concat(new[] { 0.0 })
                      .Select(v => Math.Round(v, 1)).Distinct();

        // 보상 창이 열려 있는지: 띠와 명판 4곳의 평균 색이 모두 기준과 비슷하면 열린 것.
        // 창은 내용 높이에 따라 위아래로 움직이므로 가능한 높이마다 확인하고, 맞은 높이를 돌려준다(없으면 null).
        // 캡처 영역이 작아서(1080p 에서 약 415x85 + 높이 차이) 가볍다.
        public double? WindowOpen(IEnumerable<double> shifts) => WindowOpenIn(null, Point.Empty, RewardGeometry.GameClient(), shifts);

        public static double? WindowOpenIn(Bitmap shot, Point origin, Rectangle client, IEnumerable<double> shifts)
        {
            var list = shifts.ToList();
            var all = list.Select(sh => (sh, probes: RewardGeometry.WindowProbes(client, sh).ToList())).ToList();
            var area = all.SelectMany(a => a.probes.Select(p => p.rect)).Aggregate(Rectangle.Union);
            Bitmap bmp = shot;
            try
            {
                if (bmp == null) { bmp = Capture(area); origin = area.Location; }
                var px = new Pixels(bmp, origin);
                // 비슷한 높이(예: 30px 과 44px)는 둘 다 허용 범위에 들 수 있으므로, 색 차이가 가장 작은 높이를 고른다
                double? bestShift = null;
                int bestErr = int.MaxValue;
                foreach (var (sh, probes) in all)
                {
                    int err = 0;
                    foreach (var (rect, want) in probes)
                    {
                        var c = px.Average(rect);
                        int e = Math.Max(Math.Abs(c.R - want.R), Math.Max(Math.Abs(c.G - want.G), Math.Abs(c.B - want.B)));
                        if (e > 25) { err = int.MaxValue; break; }
                        err += e;
                    }
                    if (err < bestErr) { bestErr = err; bestShift = sh; }
                }
                return bestShift;
            }
            finally { if (shot == null) bmp?.Dispose(); }
        }

        // 디버그: 보상 창이 뜨는 영역을 캡처하고 후보별 점수를 파일로 남긴다 (테스트 후 보내주면 튜닝용)
        public string DumpDebug(IList<Candidate> candidates, string dir)
        {
            Directory.CreateDirectory(dir);
            var client = RewardGeometry.GameClient();
            double sc = client.Height / 1080.0;
            var area = client;
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var sb = new StringBuilder();
            sb.AppendLine($"client {client}");
            using (var bmp = Capture(area))
            {
                var px = new Pixels(bmp, area.Location) { Scale = sc };
                foreach (var c in candidates)
                    foreach (var layout in LayoutsFor(c))
                    {
                        var rects = Place(client, c, layout);
                        var cells = string.Join(" ", rects.Select(r => Classify(px.Count(Inner(r, 0.15)))));
                        sb.AppendLine($"{Score(px, client, c, rects, layout):0.00}  {c.Offer.id} [{c.Offer.option}] ({layout})  기대 " +
                                      string.Concat(c.Items.Select(i => i.gem ?? "i")) + $"  실제 {cells}" + (c.Claimed ? "  (받음)" : ""));
                    }
                bmp.Save(Path.Combine(dir, $"reward_{stamp}.png"), ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(dir, $"reward_{stamp}.txt"), sb.ToString(), Encoding.UTF8);
            return Path.Combine(dir, $"reward_{stamp}.png");
        }

        // LockBits 로 한 번에 복사해 두고 영역별로 센다 (GetPixel 반복보다 훨씬 빠름). 좌표는 화면 기준.
        class Pixels
        {
            readonly int w, h;
            readonly Point origin;
            public readonly Bitmap Bmp;
            public Point Origin => origin;
            public double Scale = 1;   // 게임 화면 높이 / 1080 (테두리 선 찾기 폭을 해상도에 맞춘다)
            readonly int[] data;
            public Pixels(Bitmap bmp, Point origin)
            {
                Bmp = bmp;
                this.origin = origin;
                w = bmp.Width; h = bmp.Height;
                data = new int[w * h];
                var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(bd.Scan0, data, 0, data.Length);
                bmp.UnlockBits(bd);
            }

            public Color Average(Rectangle screen)
            {
                var r = new Rectangle(screen.X - origin.X, screen.Y - origin.Y, screen.Width, screen.Height);
                r.Intersect(new Rectangle(0, 0, w, h));
                long R = 0, G = 0, B = 0; int n = 0;
                for (int y = r.Top; y < r.Bottom; y++)
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        int v = data[y * w + x];
                        R += (v >> 16) & 255; G += (v >> 8) & 255; B += v & 255; n++;
                    }
                return n == 0 ? Color.Black : Color.FromArgb((int)(R / n), (int)(G / n), (int)(B / n));
            }

            public double BrightFraction(Rectangle screen)
            {
                var r = new Rectangle(screen.X - origin.X, screen.Y - origin.Y, screen.Width, screen.Height);
                r.Intersect(new Rectangle(0, 0, w, h));
                int n = 0, bright = 0;
                for (int y = r.Top; y < r.Bottom; y++)
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        int v = data[y * w + x];
                        n++;
                        if (((v >> 16) & 255) + ((v >> 8) & 255) + (v & 255) > 500) bright++;
                    }
                return n == 0 ? 0 : (double)bright / n;
            }

            double Lum(int sx, int sy)
            {
                int x = sx - origin.X, y = sy - origin.Y;
                if (x < 0 || y < 0 || x >= w || y >= h) return 0;
                int v = data[y * w + x];
                return (((v >> 16) & 255) + ((v >> 8) & 255) + (v & 255)) / 3.0;
            }

            // 칸 양쪽에 테두리 선이 있는지. 밝기 차 18 이상 (1080p 기준).
            //   해상도가 작으면 1px 선이 주변과 섞여 흐려지므로 기준도 비율만큼 낮춘다 (720p: 12)
            public bool Framed(Rectangle q)
            {
                double need = 18 * Math.Min(1, Scale);
                return EdgeLine(q.X, q.Y, q.Height) >= need && EdgeLine(q.Right - 1, q.Y, q.Height) >= need;
            }

            // 세로선 x(±2px) 의 밝기가 양옆 3px 보다 얼마나 밝은지 (칸 높이 가운데 60%, 2px 간격 평균).
            //   1080p 기준 폭. 해상도가 크면 선이 두꺼워지고 위치 오차도 커지므로 비율만큼 넓힌다 (4K: ±4px, 양옆 6px)
            //   1080 으로 나누어떨어지지 않는 높이(900, 1001 …)에서는 1px 선이 두 픽셀에 나뉘어 그려진다
            //   (900p: 21 → 35·35, 한 픽셀만 보면 +14 로 기준 15 미만). 두 픽셀을 합친 밝기 차도 본다.
            public double EdgeLine(int x, int top, int height)
            {
                double best = double.MinValue;
                int y0 = top + (int)(height * 0.2), y1 = top + (int)(height * 0.8);
                int reach = Math.Max(2, (int)Math.Round(2 * Scale)), side = Math.Max(3, (int)Math.Round(3 * Scale));
                for (int dx = -reach; dx <= reach; dx++)
                {
                    double one = 0, two = 0; int n = 0;
                    for (int y = y0; y < y1; y += 2, n++)
                    {
                        int a = x + dx;
                        one += Lum(a, y) - (Lum(a - side, y) + Lum(a + side, y)) / 2;
                        two += Lum(a, y) + Lum(a + 1, y) - Lum(a - side, y) - Lum(a + 1 + side, y);
                    }
                    if (n > 0) best = Math.Max(best, Math.Max(one, two) / n);
                }
                return best;
            }

            // 영역 안 픽셀(2픽셀 간격)을 PixelClass 별로 센다
            public int[] Count(Rectangle screen)
            {
                var counts = new int[6];
                var r = new Rectangle(screen.X - origin.X, screen.Y - origin.Y, screen.Width, screen.Height);
                r.Intersect(new Rectangle(0, 0, w, h));
                for (int y = r.Top; y < r.Bottom; y += 2)
                    for (int x = r.Left; x < r.Right; x += 2)
                    {
                        int v = data[y * w + x];
                        counts[PixelClass((v >> 16) & 255, (v >> 8) & 255, v & 255)]++;
                    }
                return counts;
            }
        }
    }
}
