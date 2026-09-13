using System;

using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace DesMeter;

internal sealed class Fonts : IDisposable
{
    private IFontHandle? name;
    private IFontHandle? icons;
    private float built;

    internal IFontHandle? Name => this.name;

    internal IFontHandle? Icons => this.icons;

    internal void Tick(float bodySize)
    {
        var body = MathF.Round(bodySize);

        if (this.name is not null && Math.Abs(body - this.built) < 0.5f) return;

        this.built = body;
        this.Drop();

        var named = MathF.Round(body * 1.3f);
        var glyphs = MathF.Round(body * 0.7f);

        this.name = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(
            e => e.OnPreBuild(tk =>
            {
                var face = tk.AddDalamudDefaultFont(named);

                tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, named), null, face);
            }));

        this.icons = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(
            e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = glyphs })));
    }

    private void Drop()
    {
        this.name?.Dispose();
        this.icons?.Dispose();
        this.name = null;
        this.icons = null;
    }

    public void Dispose() => this.Drop();
}
