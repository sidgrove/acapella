# Acapella Windows design

The source of visual values is windows/src/Murmur.App/Design/DesignTokens.cs. The current Windows identity uses light surfaces, lavender-blue accents, DM Sans interface text, and Very Vogue headings. Preserve those tokens for this refinement.

Use a restrained background, white cards with hairline edges, compact recording controls and a centred reading column. Keep navigation and list actions visible at 640 by 480 logical pixels. The main header must leave meaningful height for the scrollable history. Wrap transcript text and metadata rather than allowing horizontal overflow.

Use serif type for titles only. Timers and figures use tabular DM Sans. Keep buttons, metadata, settings and other controls in the existing sans family. Motion should communicate interaction or recording state.

## Calm pass — 23 September 2026

Reference: Sidgrove Intelligence's `docs/SIDGROVE_VISUAL_RULES.md`, the 23 September rulings and commit `d03a7c80`. Warmth comes from breathing room, soft neutral surfaces and clear hierarchy, while retaining the Sidgrove type and palette.

- White outer cards with one hairline and no shadow or hover travel. Inset surfaces use `#f7f8fc` without an inner outline.
- A faint grid and restrained periwinkle/peach background blooms, restored after Dave's review: grid at 3% opacity, periwinkle at 7.5%, peach at 6%. Keep gradients behind the opaque cards, never on them. This explicitly requested background treatment supersedes the older no-gradient rule for the Windows page wash.
- 24px default card padding and generous separation between settings and history cards.
- 32px page titles with positive tracking; 14px supporting body text; metadata no smaller than 11px, with stronger contrast.
- Sentence-case labels and quiet neutral navigation pills. Navigation lives below the caption and wraps with the mode action at small widths.
- Preserve recording behaviour, keyboard shortcuts, history actions and settings. Keep the minimum 640 × 480 window usable.

Impeccable 4.3.1 is installed locally at `.agents/skills/impeccable`, copied from the reference project's installed skill. Its Windows context launcher did not run in this environment; the quieter and craft-floor guidance was read directly. No design hook is enabled.

Visual verification: set `ACAPELLA_VISUAL_DIR` to an output folder when running the existing `Long_transcripts_and_actions_fit_the_viewport` tests to capture rendered Avalonia windows at four desktop sizes.

## The Sidgrove Bible, 1 October 2026

Dave: "grab the style bible, design bible, the app bible from Sidgrove Intelligence and apply that to the entire UI and UX of this product ... there's not enough joy". The brief is Sidgrove Intelligence's `docs/BIBLE.md` (App DNA, Style Bible, how we work), with its tokens in that repo's `DESIGN.md`. Where this file and the Bible disagree, the Bible wins; it supersedes the 23 September calm pass above, including the faint grid, which the Bible bans ("No orbs, grid, blur, bokeh or glass").

- **Canvas:** near-white `#f4f5f9` with a periwinkle tint top right and a peach hint bottom left (`Tokens.Canvas`). No grid.
- **Colour through small marks:** the seven accents plus plum (`Tokens.Accent`): a fill, the same hue's edge and its ink. Every settings card, history chip and state has its own hue in an `IconTile` or `Chip`; never a stripe or a loud surface.
- **Real marks, not placeholders:** each dictation wears the real icon of the app it went into (`AppMark`, read from the running process by `Murmur.Platform.Windows.AppIcons` and kept in `%LOCALAPPDATA%\Acapella\app-icons`). A tinted initial is the fallback, slate never.
- **Masthead:** a serif title with an italic accent naming the view ("Your *dictations*"), the section switcher beside it, and one status card whose tile hue is the state (brand ready, crimson listening, amber tidying up, slate paused), with the shortcut drawn as keycaps.
- **Controls:** buttons are soft 10px boxes (36 / 32px), never pills or saturated fills; quiet row actions are icon and word in the brand ink; segmented tracks sit on the `#e6e9f1` bed with the active segment white and lifted; fields are 8px with the brand focus ring, never the Windows blue.
- **Words:** no description sentence under a title and no explainer line under a label; the detail lives in a `Hint` tooltip beside the name.
- **Lists:** the history is grouped under day headings and builds 200 cards at a time; the dictionary is one row per word it writes, with every way it was heard as a chip; switching off and deleting happen in the editor.

Visual check: `ACAPELLA_VISUAL_DIR=<folder>` with the `VisualTourTests` test renders every screen against sample data; set `ACAPELLA_ICON_SEED` to a folder of app icon PNGs to see real marks in the shots.
