namespace Kastom.Launcher.Services;

/// <summary>Аргументи командного рядка лаунчера.</summary>
public sealed record LaunchOptions(string? CatalogDirectory, bool Minimized, string? RenderPagesDirectory, string[] RawArgs)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? catalogDir = null;
        string? renderDir = null;
        var minimized = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--catalog-dir" when i + 1 < args.Length:
                    catalogDir = args[++i];
                    break;
                case "--render-pages" when i + 1 < args.Length:
                    renderDir = args[++i];
                    break;
                case "--minimized":
                    minimized = true;
                    break;
            }
        }

        return new LaunchOptions(catalogDir, minimized, renderDir, args);
    }
}
