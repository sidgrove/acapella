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
        public const int Bars = 28;
        /// <summary>Width of the coloured wave.</summary>
        public const double Width = 210;
        /// <summary>Height of the coloured wave.</summary>
        public const double Height = 28;
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

        /// <inheritdoc cref="Colors.Card"/>
        public static IBrush Card { get; } = new SolidColorBrush(Colors.Card);

        /// <inheritdoc cref="Colors.OnBrand"/>
        public static IBrush OnBrand { get; } = new SolidColorBrush(Colors.OnBrand);

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

        /// <summary>Primary buttons and body. 14px.</summary>
        public const double Body = 14;

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

        /// <summary>The wordmark, DM Sans bold, tight.</summary>
        public const double Wordmark = 30;

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

        /// <summary>Buttons, badges, nav pills, switches.</summary>
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
        /// <summary>The white margin between the window edge and the rounded wash panel.</summary>
        public const double PanelInset = 20;

        /// <summary>Initial main window width.</summary>
        public const double MainWidth = 1080;

        /// <summary>Maximum main content width, keeping transcript lines readable on large screens.</summary>
        public const double MainContentMaxWidth = 960;

        /// <summary>Initial main window height.</summary>
        public const double MainHeight = 780;

        /// <summary>Smallest main window.</summary>
        public const double MainMinWidth = 640;

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

        /// <summary>The caption strip.</summary>
        public const double CaptionHeight = 52;

        /// <summary>Caption glyph buttons.</summary>
        public const double CaptionButton = 28;

        /// <summary>Primary and ghost pill height: 9px padding around 13px text.</summary>
        public const double ButtonHeight = 34;

        /// <summary>Compact pill height: the kit's <c>h-7</c>.</summary>
        public const double ButtonHeightSmall = 28;

        /// <summary>The hero (record) pill, the site's <c>.button</c> scale.</summary>
        public const double HeroButtonHeight = 44;

        /// <summary>Primary pill horizontal padding. <c>9px 18px</c>.</summary>
        public const double ButtonPadX = 18;

        /// <summary>Compact pill horizontal padding. <c>8px 14px</c>.</summary>
        public const double ButtonPadXSmall = 14;

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

        /// <summary>Overlay pill height.</summary>
        public const double OverlayHeight = 100;
        /// <summary>Maximum additional height for live transcript lines.</summary>
        public const double OverlayTextHeight = 80;
        /// <summary>Visible listening card width, excluding shadow room.</summary>
        public const double OverlayWidth = 300;
        /// <summary>Listening meter height.</summary>
        public const double OverlayBarsHeight = 36;
        /// <summary>Display gain for quiet microphone levels; does not change captured audio.</summary>
        public const double OverlayLevelGain = 4;

        /// <summary>Room around the overlay pill for its shadow.</summary>
        public const double OverlayShadowRoom = 28;

        /// <summary>Distance of the overlay from the bottom of the work area.</summary>
        public const double OverlayBottomMargin = 48;

        /// <summary>Widest the overlay preview text may grow before it is trimmed from the left.</summary>
        public const double OverlayPreviewWidth = 274;

        /// <summary>How many characters of the running transcript the overlay shows.</summary>
        public const int OverlayPreviewChars = 240;

        /// <summary>Height of the multi-line instructions field in Settings.</summary>
        public const double FieldTallHeight = 96;

        /// <summary>Download gauge height.</summary>
        public const double GaugeHeight = 6;

        /// <summary>The logo tile in the caption.</summary>
        public const double LogoTile = 36;

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

        /// <summary>How long the pill says "Nothing heard" before it goes.</summary>
        public static TimeSpan DroppedNotice { get; } = TimeSpan.FromMilliseconds(1200);

        /// <summary>How long after typing stops a settings field is saved.</summary>
        public static TimeSpan SaveDebounce { get; } = TimeSpan.FromMilliseconds(400);

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
    }
}
