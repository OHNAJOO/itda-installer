using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Itda.Launcher;

/// <summary>
/// 설치 폴더의 Binggrae.ttf를 PrivateFontCollection으로 읽는다. 없으면 맑은 고딕.
/// 개인 글꼴은 GDI+로만 그려지므로 이 글꼴을 쓰는 컨트롤은 UseCompatibleTextRendering = true 여야 한다.
/// </summary>
internal static class UiFonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly FontFamily Family = LoadFamily();

    private static FontFamily LoadFamily()
    {
        try
        {
            if (File.Exists(AppPaths.FontFile))
            {
                Collection.AddFontFile(AppPaths.FontFile);
                if (Collection.Families.Length > 0) return Collection.Families[0];
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or ExternalException)
        {
            Log.Error("글꼴을 읽지 못해 기본 글꼴을 씁니다", ex);
        }
        return new FontFamily("Malgun Gothic");
    }

    public static Font Get(float size, FontStyle style = FontStyle.Regular) => new(Family, size, style, GraphicsUnit.Point);
}
