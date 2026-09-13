using System;

using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace DesMeter;

internal sealed class Fonts : IDisposable
{
    private IFontHandle? name;
    private float built;

    internal IFontHandle? Name => this.name;

    internal void Tick(float bodySize)
    {
        var want = MathF.Round(bodySize * 1.3f);

        if (this.name is not null && Math.Abs(want - this.built) < 0.5f) return;

        this.built = want;

        this.name?.Dispose();

        this.name = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(
            e => e.OnPreBuild(tk =>
            {
                var face = tk.AddDalamudDefaultFont(want);

                tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, want), null, face);
            }));
    }

    public void Dispose()
    {
        this.name?.Dispose();
        this.name = null;
    }
}
