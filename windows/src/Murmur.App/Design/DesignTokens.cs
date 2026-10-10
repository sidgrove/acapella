using Avalonia;
using Avalonia.Media;

namespace Murmur.App.Design;

/// <summary>
/// The Sidgrove design system, as tokens.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is lifted from a shipped Sidgrove surface and the file is named beside
/// it: <c>app/globals.css</c> and <c>components/ui-ext/sidgrove-design.css</c> in Sidgrove
/// Intelligence (the app), <c>components/ui/PillNav.tsx</c> (the nav language), and
/// <c>index.html</c> on sidgrove.com (the site). Where the two disagree the app wins — it is
/// the surface Dave signs off on daily.
/// </para>
/// <para>
/// The rules that came with those values, in Dave's words where they exist:
/// no gradients on components ("that little gradient thing on the card itself is shit");
/// no coloured fills for selection ("I didn't want these AI slop coloured buttons");
/// selection is a quiet neutral fill and weight, never hue; cards are opaque white; one sans for
/// everything ("stick to DM Sans"); serif is for titles and hero numbers only.
/// </para>
/// <para><b>Views must not contain literal values.</b> Add a token instead.</para>
/// </remarks>
public static class Tokens
{
    /// <summary>Brief multicoloured feedback for the user-requested auto-send action.</summary>
    public static class SendFeedback
    {
        /// <summary>How long the send confirmation remains visible.</summary>
        public static TimeSpan Duration { get; } = TimeSpan.FromMilliseconds(1100);
        /// <summary>Number of animated colour bars.</summary>
        public const int Bars = 20;
        /// <summary>Width of the coloured wave: what the pill's row leaves beside the tile and the word.</summary>
        public const double Width = 150;
        /// <summary>Height of the coloured wave, the listening bars' height so the row never moves.</summary>
        public const double Height = 22;
        /// <summary>Gap between bars.</summary>
        public const double Gap = 3;
        /// <summary>Bar corner radius.</summary>
        public const double Radius = 3;
        /// <summary>Minimum height relative to the wave.</summary>
        public const double Floor = 0.2;
        /// <summary>Phase offset between adjacent bars.</summary>
        public const double Phase = 0.45;
        /// <summary>Wave speed in radians per second.</summary>
        public const double Speed = 9;
        /// <summary>Blue, violet, pink, peach and teal wave colours.</summary>
        public static IReadOnlyList<IBrush> Palette { get; } = new IBrush[]
        {
            new SolidColorBrush(Color.Parse("#5D9CEC")),
            new SolidColorBrush(Color.Parse("#9976ED")),
            new SolidColorBrush(Color.Parse("#DD83CD")),
            new SolidColorBrush(Color.Parse("#F1AD85")),
            new SolidColorBrush(Color.Parse("#58BEB5")),
        };
    }

    // ---- The Bible's accents ----

    /// <summary>
    /// One soft hue: a light fill, the same hue's soft edge and its dark ink. Sidgrove
    /// Intelligence <c>components/ui-ext/accents.ts</c>.
    /// </summary>
    /// <remarks>
    /// The Bible (29/09/2026): colour carries meaning through small marks, the icon tile, the
    /// chip, never a stripe or a loud surface. A page is made colourful by giving each section
    /// its own soft tile hue.
    /// </remarks>
    public sealed record Accent(IBrush Fill, IBrush Edge, IBrush Ink)
    {
        private static Accent Make(uint fill, uint edge, uint ink) => new(
            new SolidColorBrush(Rgb(fill)), new SolidColorBrush(Rgb(edge)), new SolidColorBrush(Rgb(ink)));

        /// <summary>Brand periwinkle. <c>ACCENT_BRAND</c>.</summary>
        public static Accent Brand { get; } = Make(0xECEFFA, 0xD4DAEE, 0x3D4785);

        /// <summary>Settled, done, a fix that worked. <c>ACCENT_EMERALD</c>.</summary>
        public static Accent Emerald { get; } = Make(0xDCEFE4, 0xA8D4BA, 0x155A31);

        /// <summary>Caution, in progress. <c>ACCENT_AMBER</c>.</summary>
        public static Accent Amber { get; } = Make(0xFDF3D6, 0xDEC27E, 0x5C4012);

        /// <summary>Recording, live. <c>ACCENT_CRIMSON</c>.</summary>
        public static Accent Crimson { get; } = Make(0xFBE1E6, 0xE6ABB9, 0x7A1E36);

        /// <summary>Neutral, not started, paused. <c>ACCENT_SLATE</c>.</summary>
        public static Accent Slate { get; } = Make(0xEEF0F4, 0xCBD1DE, 0x3A4159);

        /// <summary>Pending, read-only, the cloud. <c>ACCENT_INFO</c>.</summary>
        public static Accent Info { get; } = Make(0xE6EFFF, 0xB8C9EB, 0x1E4A85);

        /// <summary>Blocked, failed. <c>ACCENT_CORAL</c>.</summary>
        public static Accent Coral { get; } = Make(0xFDE9E3, 0xF0B8A8, 0x8A2F1C);

        /// <summary>The plum tile hue the Bible lists for hubs. <c>--purple</c>.</summary>
        public static Accent Plum { get; } = Make(0xF3E3EE, 0xD8B8CC, 0x7A3D6F);

        /// <summary>
        /// A section's own mark on the title row, brand: a fill deep enough to read on the section
        /// button's resting bed. <c>MODULE_HUES.brand</c>, <c>lib/brand/module-identity.ts</c>.
        /// </summary>
        public static Accent MarkBrand { get; } = Make(0xE4E6F2, 0xE4E6F2, 0x3D4785);

        /// <summary>A section's own mark, green. <c>MODULE_HUES.green</c>.</summary>
        public static Accent MarkGreen { get; } = Make(0xE0ECE1, 0xE0ECE1, 0x3F7D4D);

        /// <summary>A section's own mark, purple. <c>MODULE_HUES.purple</c>, its fill a shade deeper so it reads on the bed.</summary>
        public static Accent MarkPurple { get; } = Make(0xE9E0F4, 0xE9E0F4, 0x7C5DAB);

        /// <summary>The tile hues a set of unrelated things cycles through, so each gets its own.</summary>
        public static IReadOnlyList<Accent> Cycle { get; } = [Brand, Emerald, Amber, Info, Plum, Coral, Slate];

        private static Color Rgb(uint hex) => Color.FromRgb((byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));
    }

    /// <summary>
    /// The page canvas. Sidgrove Intelligence <c>lib/brand/canvas.ts</c>: a near-white base
    /// with two faint static tints, periwinkle top right and peach bottom left, the sign-in
    /// page's hue. No grid, no orbs (the Bible, Part 2 §1).
    /// </summary>
    public static class Canvas
    {
        /// <summary><c>PRACTICE_BASE</c>.</summary>
        public static IBrush Base { get; } = new SolidColorBrush(Color.Parse("#f4f5f9"));

        /// <summary>The periwinkle tint, top right. <c>PRACTICE_PRIMARY</c>.</summary>
        public static IBrush Primary { get; } = new RadialGradientBrush
        {
            Center = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(1, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.Parse("#2e6874b4"), 0), new GradientStop(Color.Parse("#006874b4"), 1)],
        };

        /// <summary>The peach hint, bottom left. <c>PRACTICE_SECONDARY</c>.</summary>
        public static IBrush Secondary { get; } = new RadialGradientBrush
        {
            Center = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0, 1, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.Parse("#3df2b8a4"), 0), new GradientStop(Color.Parse("#00f2b8a4"), 1)],
        };
    }

    // ---- Colour ----

    /// <summary>The palette. Names follow the <c>T</c> object and the CSS variables.</summary>
    public static class Colors
    {
        /// <summary>Brand lavender-blue. <c>--sg-brand</c>.</summary>
        public static Color Brand => Rgb(0x6874B4);

        /// <summary>Brand-strong, the ink of the brand: active labels, primary text on pale pills. <c>--sg-brand-strong</c>.</summary>
        public static Color BrandStrong => Rgb(0x3D4785);

        /// <summary>Brand mid. Hover border on the primary pill. <c>--teal-mid</c>.</summary>
        public static Color BrandMid => Rgb(0xA8B0D8);

        /// <summary>Very light brand tint. <c>--teal-light</c>.</summary>
        public static Color BrandLight => Rgb(0xEEF0FA);

        /// <summary>The primary pill's fill. <c>.sg-btn-brand</c>.</summary>
        public static Color PillFill => Rgb(0xECEFFA);

        /// <summary>The primary pill's fill under the pointer.</summary>
        public static Color PillFillHover => Rgb(0xE3E7F3);

        /// <summary>The primary pill's border.</summary>
        public static Color PillBorder => Rgb(0xD4DAEE);

        /// <summary>Rose: negative, destructive. <c>--coral</c>.</summary>
        public static Color Rose => Rgb(0xB8456B);

        /// <summary>Rose tint. <c>--coral-light</c>.</summary>
        public static Color RoseLight => Rgb(0xFDF2F6);

        /// <summary>Amber text. <c>--amber</c>.</summary>
        public static Color Amber => Rgb(0x7A5E1E);

        /// <summary>Amber mid. <c>--amber-mid</c>.</summary>
        public static Color AmberMid => Rgb(0xC69B2D);

        /// <summary>Amber tint. <c>--amber-light</c>.</summary>
        public static Color AmberLight => Rgb(0xFDF9EE);

        /// <summary>Primary text. The app's ramp: <c>--ink #0f1226</c>.</summary>
        public static Color Ink => Rgb(0x0F1226);

        /// <summary>The site's ink, which the hero button deepens to on hover.</summary>
        public static Color InkSite => Rgb(0x1A1D2E);

        /// <summary>Secondary text and inactive nav. <c>--muted</c>.</summary>
        public static Color Muted => Rgb(0x525672);

        /// <summary>Accessible metadata ink, icons at rest. <c>--sg-faint</c>.</summary>
        public static Color Faint => Rgb(0x686D88);

        /// <summary>Hairline dividers. <c>--sg-line</c>.</summary>
        public static Color Line => Rgb(0xDFE1EE);

        /// <summary>Card shell border. <c>--sg-surface-border</c>.</summary>
        public static Color CardBorder => Rgb(0xD8DEEC);

        /// <summary>Card shell border under the pointer. <c>--sg-surface-border-strong</c>.</summary>
        public static Color CardBorderStrong => Rgb(0xC5CCE0);

        /// <summary>Input and ghost-button chrome. <c>--sg-panel-border</c>.</summary>
        public static Color PanelBorder => Rgb(0xE3E6F0);

        /// <summary>Inner panel fill. <c>--surface</c>.</summary>
        public static Color Surface => Rgb(0xF7F8FC);

        /// <summary>The page wash. <c>--bg</c>.</summary>
        public static Color Wash => Rgb(0xF0F1F8);

        /// <summary>Cards. Opaque white, always.</summary>
        public static Color Card => Rgb(0xFFFFFF);

        /// <summary>
        /// The one toggle's bed: the house rule, "ONE toggle: #eef0f7 bed, no outline". It was the
        /// darker <c>#e6e9f1</c> nav bed, which read as a grey tray (Dave, 05/10/2026: "I don't
        /// really like the toggles").
        /// </summary>
        public static Color ToggleBed => Rgb(0xEEF0F7);

        /// <summary>Column headers and group labels. <c>text-col</c>.</summary>
        public static Color ColHeader => Rgb(0x4A4F6A);

        /// <summary>Text on brand-strong.</summary>
        public static Color OnBrand => Rgb(0xFFFFFF);

        /// <summary>Periwinkle, the site's hero bloom and nav underline. <c>--peri</c>.</summary>
        public static Color Peri => Rgb(0x9297E8);

        /// <summary>The bright hero bloom. <c>rgba(124,135,232)</c>.</summary>
        public static Color PeriBright => Rgb(0x7C87E8);

        /// <summary>Peach, the second hero bloom. <c>--peach</c>.</summary>
        public static Color Peach => Rgb(0xFFC5B2);

        /// <summary>Soft rose, the third orb. <c>--rose-soft</c>.</summary>
        public static Color RoseSoft => Rgb(0xF0B8C8);

        private static Color Rgb(uint hex) => Color.FromRgb(
            (byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));
    }

    /// <summary>Brushes for the colours above, plus the few translucent ones the system uses.</summary>
    public static class Brushes
    {
        /// <inheritdoc cref="Colors.Brand"/>
        public static IBrush Brand { get; } = new SolidColorBrush(Colors.Brand);

        /// <inheritdoc cref="Colors.BrandStrong"/>
        public static IBrush BrandStrong { get; } = new SolidColorBrush(Colors.BrandStrong);

        /// <inheritdoc cref="Colors.BrandMid"/>
        public static IBrush BrandMid { get; } = new SolidColorBrush(Colors.BrandMid);

        /// <inheritdoc cref="Colors.BrandLight"/>
        public static IBrush BrandLight { get; } = new SolidColorBrush(Colors.BrandLight);

        /// <inheritdoc cref="Colors.PillFill"/>
        public static IBrush PillFill { get; } = new SolidColorBrush(Colors.PillFill);

        /// <inheritdoc cref="Colors.PillFillHover"/>
        public static IBrush PillFillHover { get; } = new SolidColorBrush(Colors.PillFillHover);

        /// <inheritdoc cref="Colors.PillBorder"/>
        public static IBrush PillBorder { get; } = new SolidColorBrush(Colors.PillBorder);

        /// <inheritdoc cref="Colors.Rose"/>
        public static IBrush Rose { get; } = new SolidColorBrush(Colors.Rose);

        /// <inheritdoc cref="Colors.RoseLight"/>
        public static IBrush RoseLight { get; } = new SolidColorBrush(Colors.RoseLight);

        /// <summary>Rose at 10%, the destructive button's fill.</summary>
        public static IBrush RoseTint { get; } = new SolidColorBrush(Colors.Rose, Opacity.Destructive);

        /// <inheritdoc cref="Colors.Amber"/>
        public static IBrush Amber { get; } = new SolidColorBrush(Colors.Amber);

        /// <inheritdoc cref="Colors.AmberMid"/>
        public static IBrush AmberMid { get; } = new SolidColorBrush(Colors.AmberMid);

        /// <inheritdoc cref="Colors.AmberLight"/>
        public static IBrush AmberLight { get; } = new SolidColorBrush(Colors.AmberLight);

        /// <inheritdoc cref="Colors.Ink"/>
        public static IBrush Ink { get; } = new SolidColorBrush(Colors.Ink);

        /// <inheritdoc cref="Colors.InkSite"/>
        public static IBrush InkSite { get; } = new SolidColorBrush(Colors.InkSite);

        /// <inheritdoc cref="Colors.Muted"/>
        public static IBrush Muted { get; } = new SolidColorBrush(Colors.Muted);

        /// <inheritdoc cref="Colors.Faint"/>
        public static IBrush Faint { get; } = new SolidColorBrush(Colors.Faint);

        /// <inheritdoc cref="Colors.Line"/>
        public static IBrush Line { get; } = new SolidColorBrush(Colors.Line);

        /// <inheritdoc cref="Colors.CardBorder"/>
        public static IBrush CardBorder { get; } = new SolidColorBrush(Colors.CardBorder);

        /// <inheritdoc cref="Colors.CardBorderStrong"/>
        public static IBrush CardBorderStrong { get; } = new SolidColorBrush(Colors.CardBorderStrong);

        /// <inheritdoc cref="Colors.PanelBorder"/>
        public static IBrush PanelBorder { get; } = new SolidColorBrush(Colors.PanelBorder);

        /// <inheritdoc cref="Colors.Surface"/>
        public static IBrush Surface { get; } = new SolidColorBrush(Colors.Surface);

        /// <inheritdoc cref="Colors.Wash"/>
        public static IBrush Wash { get; } = new SolidColorBrush(Colors.Wash);

        /// <summary>An unchosen segment under the pointer: a whisper of the thumb's white on the bed.</summary>
        public static IBrush SegmentHover { get; } = new SolidColorBrush(Colors.Card, 0.55);

        /// <summary>A list row under the pointer: the faintest brand-tinted white, so the row answers back without a box.</summary>
        public static IBrush RowHover { get; } = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFE));

        /// <inheritdoc cref="Colors.Card"/>
        public static IBrush Card { get; } = new SolidColorBrush(Colors.Card);

        /// <inheritdoc cref="Colors.OnBrand"/>
        public static IBrush OnBrand { get; } = new SolidColorBrush(Colors.OnBrand);

        /// <summary>The segmented control's bed.</summary>
        public static IBrush ToggleBed { get; } = new SolidColorBrush(Colors.ToggleBed);

        /// <summary>
        /// A section button at rest: fill only, no outline. The web's is <c>#f6f7fb</c>, two points off
        /// this app's <c>#f4f5f9</c> canvas, where the unchosen sections read as words on nothing
        /// (audit of 10/10/2026); the house bed <c>#eef0f7</c> holds them, and the chosen one's white
        /// still stands clear of it.
        /// </summary>
        public static IBrush SectionRest { get; } = new SolidColorBrush(Colors.ToggleBed);

        /// <summary>The key focus halo, app-wide. <c>app/globals.css</c>: <c>0 0 0 2px rgba(104,116,180,0.28)</c>.</summary>
        public static IBrush FocusHalo { get; } = new SolidColorBrush(Colors.Brand, Opacity.FocusHalo);

        /// <summary>The hairline between folded rows. <c>DisclosureRow.tsx</c>: <c>#eef0f6</c>.</summary>
        public static IBrush RowLine { get; } = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xF6));

        /// <summary>Column headers and group labels.</summary>
        public static IBrush ColHeader { get; } = new SolidColorBrush(Colors.ColHeader);

        /// <summary>A third-party mark's frame: brand at 12%. <c>ClientAvatar</c>.</summary>
        public static IBrush MarkEdge { get; } = new SolidColorBrush(Colors.Brand, Opacity.Hairline);

        /// <summary>Transparent.</summary>
        public static IBrush None { get; } = Avalonia.Media.Brushes.Transparent;

        /// <summary>The pill nav's bed: brand ink at 16%. <c>PILL_NAV_BED</c>.</summary>
        public static IBrush NavBed { get; } = new SolidColorBrush(Colors.BrandStrong, Opacity.NavBed);

        /// <summary>A pill under the pointer on the bed: white at 70%.</summary>
        public static IBrush NavHover { get; } = new SolidColorBrush(Colors.Card, Opacity.NavHover);

        /// <summary>Hairlines between elements that sit on a card. <c>--line</c>.</summary>
        public static IBrush Hairline { get; } = new SolidColorBrush(Colors.Brand, Opacity.Hairline);

        /// <summary>The focused field's border. <c>.me-search-input:focus</c>.</summary>
        public static IBrush FocusBorder { get; } = new SolidColorBrush(Colors.Brand, Opacity.FocusBorder);

        /// <summary>The focused field's ring.</summary>
        public static IBrush FocusRing { get; } = new SolidColorBrush(Colors.Brand, Opacity.FocusRing);

        /// <summary>Idle listening bars.</summary>
        public static IBrush BarsIdle { get; } = new SolidColorBrush(Colors.BrandMid, Opacity.BarsIdle);

        /// <summary>
        /// A scrolling list's slim bar at rest: the brand's soft mid, not the theme's near-black line,
        /// so a list says it scrolls softly (the Bible, Part 2 §4, "Scrolling lists say so softly").
        /// </summary>
        public static IBrush ScrollThumb { get; } = new SolidColorBrush(Colors.BrandMid);

        /// <summary>The hero grid lines, brand at 6%. <c>.grid-pattern</c>.</summary>
        /// <summary>Sidgrove's upper periwinkle bloom.</summary>
        public static IBrush AmbientPeri { get; } = new RadialGradientBrush
        {
            Center = new RelativePoint(0.8, 0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.8, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.55, RelativeUnit.Relative),
            Opacity = Opacity.HeroPeri,
            GradientStops = [new GradientStop(Colors.PeriBright, 0), new GradientStop(Avalonia.Media.Colors.Transparent, 1)],
        };

        /// <summary>Sidgrove's faint peach bloom.</summary>
        public static IBrush AmbientPeach { get; } = new RadialGradientBrush
        {
            Center = new RelativePoint(0.02, 0.42, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.02, 0.42, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            Opacity = Opacity.HeroPeach,
            GradientStops = [new GradientStop(Colors.Peach, 0), new GradientStop(Avalonia.Media.Colors.Transparent, 1)],
        };
        /// <summary>Fine Sidgrove grid lines.</summary>
        public static IBrush GridLine { get; } = new SolidColorBrush(Colors.Brand, Opacity.Grid);

        /// <summary>The nav underline. Solid peri, not the site's peri-to-peach gradient.</summary>
        public static IBrush NavUnderline { get; } = new SolidColorBrush(Colors.Peri);

        /// <summary>The lit top edge inside the hero button.</summary>
        public static IBrush HeroHighlight { get; } = new SolidColorBrush(Colors.Card, Opacity.HeroInsetHighlight);
    }

    /// <summary>The translucencies the system uses. Every one is named in a source file.</summary>
    public static class Opacity
    {
        /// <summary><c>PILL_NAV_BED</c> — brand ink at 16%.</summary>
        public const double NavBed = 0.16;

        /// <summary>Pill hover on the bed, white at 70%.</summary>
        public const double NavHover = 0.70;

        /// <summary><c>--line</c> — brand at 12%.</summary>
        public const double Hairline = 0.12;

        /// <summary>Focused field border, brand at 40%.</summary>
        public const double FocusBorder = 0.40;

        /// <summary>Focused field ring, brand at 10%.</summary>
        public const double FocusRing = 0.10;

        /// <summary>The key focus halo, brand at 28%.</summary>
        public const double FocusHalo = 0.28;

        /// <summary>A section's mark while its section is not the current one. <c>SectionMark</c>: 80%.</summary>
        public const double MarkResting = 0.8;

        /// <summary>Destructive button fill, rose at 10%.</summary>
        public const double Destructive = 0.10;

        /// <summary>Top wash bloom, brand at 7%. <c>body</c> background.</summary>
        public const double WashTop = 0.07;

        /// <summary>Bottom wash bloom, brand-strong at 5%.</summary>
        public const double WashBottom = 0.05;

        /// <summary>Disabled controls.</summary>
        public const double Disabled = 0.50;

        /// <summary>Idle listening bars.</summary>
        public const double BarsIdle = 0.55;

        /// <summary>An unchosen segment's mark: its own hue, softened, so the colour is there before it is chosen.</summary>
        public const double GlyphResting = 0.5;

        /// <summary>The lit top edge inside the hero button. <c>.button</c> on the site.</summary>
        public const double HeroInsetHighlight = 0.16;

        /// <summary>Hero grid lines. <c>.grid-pattern</c>.</summary>
        public const double Grid = 0.03;

        /// <summary>The periwinkle hero bloom. <c>.hero</c> background.</summary>
        public const double HeroPeri = 0.075;

        /// <summary>The peach hero bloom.</summary>
        public const double HeroPeach = 0.06;

        /// <summary>The rose orb. <c>.orb-rose</c>, softened for a smaller canvas.</summary>
        public const double HeroRose = 0.14;
    }

    // ---- Type ----

    /// <summary>
    /// One font per role, exactly as <c>globals.css</c> declares them: DM Sans for body,
    /// labels, buttons and inputs; Very Vogue Text for titles; Perfectly Nineties for the one
    /// hero number. All three ship inside the app.
    /// </summary>
    public static class Fonts
    {
        /// <summary>DM Sans. Everything that is not a title or the hero number.</summary>
        public static FontFamily Sans { get; } = new("fonts:Acapella#DM Sans");

        /// <summary>Very Vogue Text. Titles only, weight 400.</summary>
        public static FontFamily Serif { get; } = new("fonts:Acapella#Very Vogue");

        /// <summary>Perfectly Nineties. The hero number only, weight 400.</summary>
        public static FontFamily Display { get; } = new("fonts:Acapella#Perfectly Nineties");

        /// <summary>Very Vogue Text Italic. The accent line of a headline. <c>h1 .emphasis</c>.</summary>
        public static FontFamily SerifItalic { get; } = new("fonts:Acapella#Very Vogue");

        /// <summary>JetBrains Mono. Chip labels and micro labels only. <c>--mono</c>.</summary>
        public static FontFamily Mono { get; } = new("fonts:Acapella#JetBrains Mono");

        /// <summary>Tabular figures, the <c>.num</c> class.</summary>
        public static FontFeatureCollection Tabular { get; } = [new FontFeature { Tag = "tnum" }];

        /// <summary>Sentence-case labels, 11px medium weight.</summary>
        public const double Eyebrow = 11;

        /// <summary>Chips and badges. 11px 600, 0.02em.</summary>
        public const double Badge = 11;

        /// <summary>Captions and metadata.</summary>
        public const double Caption = 11;

        /// <summary>Pill nav labels and ghost buttons. 12px.</summary>
        public const double Small = 12;

        /// <summary>Body. 14px.</summary>
        public const double Body = 14;

        /// <summary>A button's label. <c>PillButton</c> md: 12px 600.</summary>
        public const double Button = 12.5;

        /// <summary>A compact button's label. <c>PillButton</c> sm.</summary>
        public const double ButtonSmall = 12;

        /// <summary>A section switcher's labels.</summary>
        public const double Tab = 13;

        /// <summary>Base text, kit inputs. 14px.</summary>
        public const double Base = 14;

        /// <summary>The transcript, the thing you read most. 15px.</summary>
        public const double Reading = 15;

        /// <summary>Section headings, DM Sans 700 tight. 16px.</summary>
        public const double Heading = 16;

        /// <summary>The site's hero button label. 17px.</summary>
        public const double HeroButton = 17;

        /// <summary>A page title in Very Vogue Text. <c>.sg-hero-title</c>: 32px.</summary>
        public const double Title = 32;

        /// <summary>The page title under 640px. <c>--sg-title-size</c> narrow.</summary>
        public const double TitleNarrow = 26;

        /// <summary>Group and column labels. <c>text-col</c>.</summary>
        public const double Label = 12.5;

        /// <summary>The caption-strip title in Very Vogue Text.</summary>
        public const double CaptionTitle = 22;

        /// <summary>The hero number in Perfectly Nineties.</summary>
        public const double Hero = 34;

        /// <summary>The site's mono label. <c>--label</c>: 11px, 0.16em, uppercase.</summary>
        public const double MonoLabel = 11;

        /// <summary>Metadata label tracking: natural spacing.</summary>
        public const double MonoLabelTracking = 0;

        /// <summary>The hero headline in Very Vogue, at app scale. Site h1 is 48–78px.</summary>
        public const double Headline = 46;

        /// <summary>
        /// Headline line height. Very Vogue's ascenders and descenders overshoot its em box,
        /// so anything tighter than <c>1.2</c> crops the glyphs.
        /// </summary>
        public const double HeadlineLineHeight = 56;

        /// <summary>The hero subtitle. Site <c>.subtitle</c> 1.125rem.</summary>
        public const double Subtitle = 16;

        /// <summary>Nav links. <c>.nav-links a</c> 0.9375rem, weight 500.</summary>
        public const double Nav = 15;

        /// <summary>
        /// The name in the title row, Very Vogue Text. 22px, Setlist's size: the two are siblings
        /// and their title rows match (Dave, 04/10/2026).
        /// </summary>
        public const double Wordmark = 22;

        /// <summary>"by Sidgrove Intelligence" after the name, on its line. Small and quiet.</summary>
        public const double Byline = 10.5;

        /// <summary>Sentence-case label tracking: natural spacing.</summary>
        public const double EyebrowTracking = 0;

        /// <summary>Chip tracking, 0.02em at 11px.</summary>
        public const double BadgeTracking = 0.21;

        /// <summary>Title tracking, +0.01em at 32px.</summary>
        public const double TitleTracking = 0.32;

        /// <summary>Heading tracking: natural spacing.</summary>
        public const double HeadingTracking = 0;

        /// <summary>Button tracking, a restrained -0.13px.</summary>
        public const double ButtonTracking = -0.13;
    }

    // ---- Geometry ----

    /// <summary>The 8px rhythm the site declares, with the small steps the app uses.</summary>
    public static class Space
    {
        /// <summary>2</summary>
        public const double Hair = 2;

        /// <summary>3 — the pill-nav track inset. <c>PILL_NAV_TRACK_INSET</c>.</summary>
        public const double TrackInset = 3;

        /// <summary>4</summary>
        public const double Tight = 4;

        /// <summary>6</summary>
        public const double Chip = 6;

        /// <summary>8</summary>
        public const double Snug = 8;

        /// <summary>12</summary>
        public const double Base = 12;

        /// <summary>14 — the stat-strip gap.</summary>
        public const double Grid = 14;

        /// <summary>16</summary>
        public const double Roomy = 16;

        /// <summary>24 — calm hub card padding.</summary>
        public const double Card = 24;

        /// <summary>24</summary>
        public const double Wide = 24;

        /// <summary>32</summary>
        public const double Section = 32;

        /// <summary>56 — empty-state vertical padding.</summary>
        public const double Empty = 56;
    }

    /// <summary>The app's radius scale: 8 / 10 / 12, and the pill.</summary>
    public static class Radius
    {
        /// <summary>Chips and small controls. <c>--radius-sm</c>.</summary>
        public const double Control = 8;

        /// <summary>Inner panels. <c>--radius-md</c>.</summary>
        public const double Inner = 10;

        /// <summary>Cards and inputs in the app. <c>--radius-lg</c>.</summary>
        public const double Card = 12;

        /// <summary>Top-level cards and panels on the site. <c>--r-card</c>.</summary>
        public const double CardLarge = 20;

        /// <summary>Buttons, chips, tiles and the segmented track. <c>--radius-md</c>.</summary>
        public const double Button = 10;

        /// <summary>The active segment inside the track.</summary>
        public const double Segment = 7;

        /// <summary>The window's inner frame, a little looser than the cards so the curves nest. <c>--radius-xl</c>.</summary>
        public const double Frame = 16;

        /// <summary>Badges and switches.</summary>
        public const double Pill = 999;

        /// <summary>Listening bars.</summary>
        public const double Bar = 2;
    }

    /// <summary>Line weights.</summary>
    public static class Border
    {
        /// <summary>Every border in the system.</summary>
        public const double Hairline = 1;

        /// <summary>Focus ring width. <c>0 0 0 3px</c>.</summary>
        public const double Ring = 3;

        /// <summary>The key focus halo's width.</summary>
        public const double FocusHalo = 2;
    }

    /// <summary>The shadows, each copied from its CSS declaration.</summary>
    public static class Shadow
    {
        /// <summary><c>--shadow-soft</c> on the site: chips and floating panels.</summary>
        public static BoxShadows Soft => new(
            Layer(1, 2, 0x000000, 0.03),
            [Layer(4, 16, 0x000000, 0.04), Layer(12, 32, 0x000000, 0.03)]);

        /// <summary><c>--sg-surface-shadow</c>: calm cards have no shadow.</summary>
        public static BoxShadows Card => default;

        /// <summary><c>.sg-card:hover</c>.</summary>
        public static BoxShadows CardHover => default;

        /// <summary><c>--shadow-lift</c>: floating surfaces, the overlay.</summary>
        public static BoxShadows Lift => new(
            Layer(2, 8, 0x6874B4, 0.06),
            [Layer(8, 24, 0x6874B4, 0.08), Layer(20, 48, 0x32326E, 0.06)]);

        /// <summary><c>.sg-empty</c>.</summary>
        public static BoxShadows Empty => default;

        /// <summary><c>.sg-btn-brand</c>, <c>.me-btn-ghost</c>: one hairline shadow.</summary>
        public static BoxShadows Button => new(Layer(1, 2, 0x0F172A, 0.06));

        /// <summary><c>PILL_NAV_ACTIVE_LIFT</c>: keyline plus a whisper of shadow.</summary>
        public static BoxShadows NavActive => new(
            new BoxShadow { Spread = 1, Color = Color.FromArgb((byte)(0.04 * 255), 0x0F, 0x17, 0x2A) },
            [Layer(1, 2, 0x0F172A, 0.04)]);

        /// <summary>The site's <c>.button</c> ramp at rest.</summary>
        public static BoxShadows Hero => new(
            Layer(1, 2, 0x3D4785, 0.22),
            [Layer(4, 10, 0x3D4785, 0.18), Layer(12, 28, 0x3D4785, 0.18)]);

        /// <summary>The site's <c>.button:hover</c> ramp, on ink.</summary>
        public static BoxShadows HeroHover => new(
            Layer(1, 2, 0x1A1D2E, 0.24),
            [Layer(6, 14, 0x1A1D2E, 0.20), Layer(18, 40, 0x1A1D2E, 0.20)]);

        /// <summary>The site's <c>.button:active</c>, collapsed.</summary>
        public static BoxShadows HeroPressed => new(
            Layer(1, 2, 0x1A1D2E, 0.22),
            [Layer(2, 5, 0x1A1D2E, 0.16)]);

        /// <summary>
        /// The toggle's thumb: a hairline ring and a soft lift, both tinted with the brand ink rather
        /// than grey, so the white reads warm and held.
        /// </summary>
        public static BoxShadows Thumb => new(
            new BoxShadow { Spread = 0.5, Color = Color.FromArgb(0x14, 0x3D, 0x47, 0x85) },
            [Layer(1, 2, 0x3D4785, 0.10), Layer(3, 8, 0x3D4785, 0.08)]);

        /// <summary>The chosen section button. <c>SECTION_BUTTON_ON</c>: <c>0 1px 2px rgba(15,23,42,0.06)</c>.</summary>
        public static BoxShadows Section => new(Layer(1, 2, 0x0F172A, 0.06));

        /// <summary>No shadow.</summary>
        public static BoxShadows None => default;

        private static BoxShadow Layer(double offsetY, double blur, uint rgb, double opacity) => new()
        {
            OffsetY = offsetY,
            Blur = blur,
            Color = Color.FromArgb((byte)(opacity * 255), (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF)),
        };
    }

    // ---- Layout ----

    /// <summary>Window and control dimensions.</summary>
    public static class Layout
    {
        /// <summary>The white margin between the window edge and the rounded wash panel. Setlist's.</summary>
        public const double PanelInset = 12;

        /// <summary>Initial main window width.</summary>
        public const double MainWidth = 1080;

        /// <summary>Maximum main content width, keeping transcript lines readable on large screens.</summary>
        public const double MainContentMaxWidth = 960;

        /// <summary>Initial main window height.</summary>
        public const double MainHeight = 780;

        /// <summary>Smallest main window.</summary>
        public const double MainMinWidth = 640;

        /// <summary>The status card's words: the state, then the shortcut's keycaps.</summary>
        public const double StatusWordsHeight = 48;

        /// <summary>Below this width the page title drops to its narrow size.</summary>
        public const double NarrowTitleBelow = 640;

        /// <summary>Smallest main window height.</summary>
        public const double MainMinHeight = 480;

        /// <summary>Settings sheet width.</summary>
        public const double SettingsWidth = 880;
        /// <summary>Maximum visible microphone list height before scrolling.</summary>
        public const double DeviceDropdownHeight = 260;

        /// <summary>Editor and About width.</summary>
        public const double DialogWidth = 460;

        /// <summary>Reading column width, in the spirit of <c>--sg-page-read</c> at desktop scale.</summary>
        public const double ContentMaxWidth = 760;

        /// <summary>
        /// The caption strip: one line for the mark, the name and its byline. 40, Setlist's height
        /// (Dave, 04/10/2026: "too much of the vertical space at the top is taken by random stuff ...
        /// more space for the interface").
        /// </summary>
        public const double CaptionHeight = 40;

        /// <summary>From the window's left edge to the mark.</summary>
        public const double CaptionPadLeft = 18;

        /// <summary>Where the name's baseline sits, down from the top of the caption strip.</summary>
        public const double CaptionBaseline = 27;

        /// <summary>Between the name and its byline.</summary>
        public const double BylineGap = 9;

        /// <summary>How far the byline's baseline sits above the name's.</summary>
        public const double BylineRaise = 1;

        /// <summary>Narrower than this, the byline drops out rather than trimming to nothing.</summary>
        public const double BylineMinWidth = 60;

        /// <summary>Caption glyph buttons.</summary>
        public const double CaptionButton = 28;

        /// <summary>A button. <c>PillButton</c> md: <c>h-9</c>.</summary>
        public const double ButtonHeight = 36;

        /// <summary>A compact button. <c>PillButton</c> sm and <c>HeaderAction</c>: <c>h-8</c>.</summary>
        public const double ButtonHeightSmall = 32;

        /// <summary>A segment inside the track, the track being this plus its inset: 30 in all, slim enough for the title row.</summary>
        public const double SegmentHeight = 24;

        /// <summary>From a section button's left edge to its mark. <c>SECTION_BUTTON</c>: <c>pl-[5px]</c>.</summary>
        public const double SectionMarkInset = 5;

        /// <summary>The fault chip at its widest: the start of the sentence, the rest in its tooltip.</summary>
        public const double FaultChipWidth = 280;

        /// <summary>With less room than this beside the title, the fault chip is its mark alone.</summary>
        public const double FaultChipMinWidth = 150;

        /// <summary>The chevron at the end of a folded row. <c>DisclosureRow.tsx</c>: <c>size-4</c>.</summary>
        public const double Chevron = 16;

        /// <summary>A folded row's air above and below its line. <c>DisclosureRow.tsx</c>: <c>py-3</c>, less the tile's own height.</summary>
        public const double DisclosurePadY = 10;

        /// <summary>A section tab in the masthead: words on the page, the current one underlined.</summary>
        public const double TabHeight = 34;

        /// <summary>Between one section tab's words and the next.</summary>
        public const double TabGap = 22;

        /// <summary>Narrower than this, the status card leaves out its resting waveform.</summary>
        public const double StatusBarsBelow = 640;

        /// <summary>Narrower than this, the masthead's tools drop their words and keep their icons.</summary>
        public const double CompactToolsBelow = 700;

        /// <summary>A day's band at the top of its card, the same with or without today's chips in it.</summary>
        public const double DayBandHeight = 38;

        /// <summary>The underline under the current section, and its rounded ends.</summary>
        public const double TabUnderline = 2;

        /// <summary>The count chip beside a section's name.</summary>
        public const double CountChipHeight = 18;

        /// <summary>The hero (record) pill, the site's <c>.button</c> scale.</summary>
        public const double HeroButtonHeight = 44;

        /// <summary>Button horizontal padding. <c>px-3.5</c>.</summary>
        public const double ButtonPadX = 14;

        /// <summary>Compact button horizontal padding. <c>px-3</c>.</summary>
        public const double ButtonPadXSmall = 12;

        /// <summary>Hero pill horizontal padding.</summary>
        public const double HeroPadX = 24;

        /// <summary>Kit input height. <c>h-8</c>.</summary>
        public const double FieldHeight = 34;

        /// <summary>Kit input horizontal padding. <c>px-2.5</c>.</summary>
        public const double FieldPadX = 10;

        /// <summary>Nav pill height. <c>h-8</c>.</summary>
        public const double NavPillHeight = 30;

        /// <summary>Nav pill horizontal padding. <c>px-3.5</c>.</summary>
        public const double NavPillPadX = 14;

        /// <summary>Switch width. Kit default 32.</summary>
        public const double SwitchWidth = 32;

        /// <summary>Switch height. Kit default 18.4.</summary>
        public const double SwitchHeight = 18;

        /// <summary>Status dot diameter.</summary>
        public const double Dot = 8;

        /// <summary>Listening bars on the main card.</summary>
        public const int BarsCount = 32;

        /// <summary>Listening bars on the overlay.</summary>
        public const int BarsCountSmall = 14;

        /// <summary>One bar's width.</summary>
        public const double BarWidth = 3;

        /// <summary>Gap between bars.</summary>
        public const double BarGap = 3;

        /// <summary>Bar field height on the main card.</summary>
        public const double BarsHeight = 80;

        /// <summary>Display gain for the main-window microphone animation.</summary>
        public const double MainLevelGain = 4;

        /// <summary>Bar field height on the overlay.</summary>
        public const double BarsHeightSmall = 22;

        /// <summary>Minimum bar height as a fraction of the field.</summary>
        public const double BarMinFraction = 0.10;

        /// <summary>
        /// The overlay pill at rest: one row, a 32px tile with 12px of air and the hairline. It was a
        /// 100px box that stood mostly empty (Dave, 10/10/2026: "more joy" in the popup).
        /// </summary>
        public const double OverlayHeight = 58;
        /// <summary>The most the live transcript adds beneath the row: four whole lines, never a cut one.</summary>
        public const double OverlayTextHeight = 80;
        /// <summary>One line of the live transcript.</summary>
        public const double OverlayLineHeight = 20;
        /// <summary>Visible pill width, excluding shadow room.</summary>
        public const double OverlayWidth = 320;
        /// <summary>Listening meter height in the pill's row.</summary>
        public const double OverlayBarsHeight = 22;
        /// <summary>Listening bars in the pill's row.</summary>
        public const int OverlayBars = 18;
        /// <summary>How far the pill rises as it arrives and sinks as it goes.</summary>
        public const double OverlayTravel = 8;

        /// <summary>
        /// One dot of the house loader ("three small bouncing dots", the Bible, Part 2 §4). The Bible's
        /// 8px is for a card; beside 3px bars in a 58px pill that shouts, so the pill's are 6.
        /// </summary>
        public const double LoaderDot = 6;
        /// <summary>Between the loader's dots.</summary>
        public const double LoaderGap = 5;
        /// <summary>How high a loader dot hops.</summary>
        public const double LoaderBounce = 5;
        /// <summary>Display gain for quiet microphone levels; does not change captured audio.</summary>
        public const double OverlayLevelGain = 4;

        /// <summary>The icon tile ramp: 22 inline, 26 a row mark, 32 a section or app mark, 36 a lead. <c>IconTile</c>.</summary>
        public const double TileSmall = 22;

        /// <inheritdoc cref="TileSmall"/>
        public const double TileRow = 26;

        /// <inheritdoc cref="TileSmall"/>
        public const double Tile = 32;

        /// <inheritdoc cref="TileSmall"/>
        public const double TileLead = 36;

        /// <summary>Chips: a declared height, never padding-derived. <c>CHIP_BASE</c>.</summary>
        public const double ChipHeight = 22;

        /// <summary>Chip horizontal padding.</summary>
        public const double ChipPadX = 8;

        /// <summary>A keycap in a shortcut.</summary>
        public const double KeyCapHeight = 24;

        /// <summary>The label column of a settings form.</summary>
        public const double LabelColumn = 240;

        /// <summary>The dictionary's word column, before the ways it was heard.</summary>
        public const double WordColumn = 180;

        /// <summary>A field holding a key or a URL: as wide as what it holds, never the space it sits in.</summary>
        public const double FieldWide = 380;

        /// <summary>A field holding a word or two.</summary>
        public const double FieldShort = 200;

        /// <summary>A field holding a list of words.</summary>
        public const double FieldList = 380;

        /// <summary>A field holding a paragraph of the user's own words.</summary>
        public const double FieldTallWidth = 460;

        /// <summary>The history and dictionary search.</summary>
        public const double SearchWidth = 260;

        /// <summary>The search in a narrow window.</summary>
        public const double SearchWidthNarrow = 170;

        /// <summary>Below this view width the search narrows.</summary>
        public const double NarrowSearchBelow = 760;

        /// <summary>Room around the overlay pill for its shadow.</summary>
        public const double OverlayShadowRoom = 28;

        /// <summary>Distance of the overlay from the bottom of the work area.</summary>
        public const double OverlayBottomMargin = 48;

        /// <summary>
        /// The live transcript's column in the pill: it hangs under the state word, the tile standing
        /// clear to its left as an app's mark does on a history row.
        /// </summary>
        public const double OverlayPreviewWidth = 250;

        /// <summary>How many characters of the running transcript the overlay shows.</summary>
        public const int OverlayPreviewChars = 240;

        /// <summary>Height of the multi-line instructions field in Settings.</summary>
        public const double FieldTallHeight = 96;

        /// <summary>Download gauge height.</summary>
        public const double GaugeHeight = 6;

        /// <summary>The logo tile in the caption. Setlist's.</summary>
        public const double LogoTile = 26;

        /// <summary>Hero grid pitch. <c>.grid-pattern</c> 56px.</summary>
        public const double GridPitch = 56;

        /// <summary>The white coin inside the hero button. <c>.button-coin</c> 40px.</summary>
        public const double Coin = 32;

        /// <summary>Hero pill left padding, where the label carries the weight. <c>2.25rem</c>.</summary>
        public const double HeroPadLeft = 30;

        /// <summary>Hero pill right padding, hugging the coin. <c>0.5rem</c>.</summary>
        public const double HeroPadRight = 6;

        /// <summary>Badge padding. <c>0.5625rem 1.375rem</c>.</summary>
        public const double BadgePadX = 18;

        /// <summary>Badge height.</summary>
        public const double BadgeHeight = 22;

        /// <summary>Nav underline thickness.</summary>
        public const double NavUnderline = 2;

        /// <summary>Horizontal room a scrolling list keeps for its cards' shadows.</summary>
        public const double ScrollGutter = 8;
    }

    // ---- Motion ----

    /// <summary>Two easings, as the site declares: expo-out for travel, and quick fades.</summary>
    public static class Motion
    {
        /// <summary>Hover fades. <c>0.15s</c>.</summary>
        public static TimeSpan Quick { get; } = TimeSpan.FromMilliseconds(150);

        /// <summary>The toggle's thumb gliding to the new choice, settling with a small spring.</summary>
        public static TimeSpan Glide { get; } = TimeSpan.FromMilliseconds(340);

        /// <summary>How far a segment gives under the pointer as it is pressed.</summary>
        public const double SegmentPress = 0.96;

        /// <summary>
        /// Whether things glide. Off in the headless tests, whose clock never runs, so a capture
        /// shows where a thumb ends up rather than where it set off from.
        /// </summary>
        public static bool Animate { get; set; } = true;

        /// <summary>Card lift. <c>0.24s</c>.</summary>
        public static TimeSpan Lift { get; } = TimeSpan.FromMilliseconds(240);

        /// <summary>The site button's travel. <c>0.4s</c>.</summary>
        public static TimeSpan Travel { get; } = TimeSpan.FromMilliseconds(400);

        /// <summary>The site button's press. <c>0.08s</c>.</summary>
        public static TimeSpan Press { get; } = TimeSpan.FromMilliseconds(80);

        /// <summary>How often the panel polls the engine.</summary>
        public static TimeSpan PanelPoll { get; } = TimeSpan.FromMilliseconds(50);

        /// <summary>One animation frame.</summary>
        public static TimeSpan Frame { get; } = TimeSpan.FromMilliseconds(16);

        /// <summary>How long "Copied" stays on a button.</summary>
        public static TimeSpan Confirmation { get; } = TimeSpan.FromMilliseconds(1400);

        /// <summary>The pill arriving: a short rise and fade, easing out.</summary>
        public static TimeSpan OverlayIn { get; } = TimeSpan.FromMilliseconds(180);

        /// <summary>The pill leaving, quicker than it came.</summary>
        public static TimeSpan OverlayOut { get; } = TimeSpan.FromMilliseconds(140);

        /// <summary>How long the pill says "Done" before it goes: once, small (the Bible, Part 1, question 13).</summary>
        public static TimeSpan DoneNotice { get; } = TimeSpan.FromMilliseconds(700);

        /// <summary>The done tick drawing itself in.</summary>
        public static TimeSpan TickDraw { get; } = TimeSpan.FromMilliseconds(280);

        /// <summary>One round of the loader's three dots.</summary>
        public static TimeSpan LoaderRound { get; } = TimeSpan.FromMilliseconds(1100);

        /// <summary>How far round behind its neighbour each loader dot hops, as a fraction of a round.</summary>
        public const double LoaderStagger = 0.16;

        /// <summary>Where a state tile starts from as it changes state, springing back to full size.</summary>
        public const double TilePop = 0.86;

        /// <summary>How long the pill says "Nothing heard" before it goes.</summary>
        public static TimeSpan DroppedNotice { get; } = TimeSpan.FromMilliseconds(1200);

        /// <summary>How long after typing stops a settings field is saved.</summary>
        public static TimeSpan SaveDebounce { get; } = TimeSpan.FromMilliseconds(400);

        /// <summary>How often "last synced 3 minutes ago" is brought up to date.</summary>
        public static TimeSpan RelativeTimeTick { get; } = TimeSpan.FromSeconds(30);

        /// <summary>How long a destructive button stays armed waiting for its second click.</summary>
        public static TimeSpan ConfirmWindow { get; } = TimeSpan.FromSeconds(4);

        /// <summary>Cards stay still under the pointer.</summary>
        public const double CardLift = 0;

        /// <summary>Hover lift of the hero button. <c>translateY(-2px)</c>.</summary>
        public const double HeroLift = 2;

        /// <summary>Press travel of any button. <c>translate-y-px</c>.</summary>
        public const double PressTravel = 1;

        /// <summary>How fast a bar rises toward the level, per frame.</summary>
        public const double BarAttack = 0.45;

        /// <summary>How fast a bar falls, per frame.</summary>
        public const double BarRelease = 0.12;

        /// <summary>Idle breath of the bars as a fraction of the field.</summary>
        public const double BarIdleBreath = 0.05;

        /// <summary>
        /// The resting waveform's height as a fraction of the field: a low, calm voice shape
        /// rather than a row of dots, so the card shows what it listens for before anyone speaks.
        /// </summary>
        public const double BarIdleShape = 0.55;
    }
}
