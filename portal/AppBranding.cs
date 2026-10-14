using System.Reflection;
using System.Runtime.InteropServices;

namespace CS2ServerPortal;

internal static class AppBranding
{
    public const string AppUserModelId = "Gold.CS2Serververwaltung";
    public static readonly Icon WindowIcon = LoadIcon();
    public static readonly Bitmap Logo = LoadLogo();

    public static void InitializeTaskbarIdentity() =>
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));

    private static Icon LoadIcon()
    {
        using var stream = OpenAsset("cs2-server.ico");
        using var icon = new Icon(stream, 256, 256);
        return (Icon)icon.Clone();
    }

    private static Bitmap LoadLogo()
    {
        using var stream = OpenAsset("cs2-server-logo.png");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Stream OpenAsset(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("CS2ServerPortal.Assets." + name)
        ?? throw new InvalidOperationException("Das Programmsymbol konnte nicht geladen werden.");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
