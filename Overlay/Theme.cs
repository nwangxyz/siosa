using System.Drawing;

namespace Siosa
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(30, 31, 34);
        public static readonly Color Panel = Color.FromArgb(43, 45, 49);
        public static readonly Color Text = Color.FromArgb(220, 221, 222);
        public static readonly Color Dim = Color.FromArgb(140, 142, 148);
        public static readonly Color Accent = Color.FromArgb(232, 190, 92);   // 선택 테두리 / 강조 (PoE 금색 계열)

        public static readonly Font Body = new Font("Malgun Gothic", 9f);
        public static readonly Font Bold = new Font("Malgun Gothic", 9f, FontStyle.Bold);
        public static readonly Font Header = new Font("Malgun Gothic", 12f, FontStyle.Bold);

        // 젬 색상 (어두운 바탕에서 읽히도록 채도를 낮춘 값)
        public static Color GemBack(string gem, bool selected)
        {
            Color c;
            switch (gem)
            {
                case "r": c = Color.FromArgb(120, 40, 40); break;
                case "g": c = Color.FromArgb(40, 100, 50); break;
                case "b": c = Color.FromArgb(40, 70, 130); break;
                case "w": c = Color.FromArgb(90, 90, 90); break;
                default: c = Color.FromArgb(85, 70, 45); break;   // 장비 아이템
            }
            return selected ? Lighten(c, 0.35f) : c;
        }

        public static Color GemText(string gem)
        {
            switch (gem)
            {
                case "r": return Color.FromArgb(240, 120, 110);
                case "g": return Color.FromArgb(120, 220, 120);
                case "b": return Color.FromArgb(120, 170, 255);
                case "w": return Color.FromArgb(230, 230, 230);
                default: return Color.FromArgb(220, 190, 130);
            }
        }

        static Color Lighten(Color c, float t) => Color.FromArgb(
            (int)(c.R + (255 - c.R) * t), (int)(c.G + (255 - c.G) * t), (int)(c.B + (255 - c.B) * t));
    }
}
