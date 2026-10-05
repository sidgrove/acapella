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

## The toggles and the history, 5 October 2026

Dave: "I don't really like the toggles; they are a bit shit if I'm honest with you. Can we have more joy there?"

- **One toggle, the house rule:** `#eef0f7` bed, no outline, a white thumb that glides to the choice (`Segmented`, `GlidePanel`). The old bed was the darker `#e6e9f1` nav tray, which read as a grey box; the mode toggle and the section tabs were the same tray twice. The mode toggle wears a bolt (Instant, amber) and sparkles (Polished, plum), coloured only while chosen.
- **Section tabs are navigation, not a toggle:** words on the page with a brand underline that glides under the current one (`NavLink.Track`). "Transcriptions" is now "Dictations", the word the title uses.
- **Status card is one line,** with the voice bars always there as a calm resting waveform; the "On" label went (the switch says it).
- **History is one card per day:** the day is the card's tinted top band, rows hang beneath it with an inset hairline, the time sits on the right and gives way to the row's verbs (copy first) under the pointer or keyboard. No column of identical Copy buttons; the bin is quiet until armed.
- **Empty history** shows the key to press as keycaps.

Before and after: `docs/screenshots/2026-10-05-bible/`.

## Round two, 5 October 2026 (Dave's corrections)

- **Tabs are held again:** an underline under bare words read as floating text (the Bible: words sit on a control or in a held shape). The sections sit on the house bed with the white thumb gliding, beside the title (its titleAside), not at the far right.
- **The title row is only the mark and the window controls.** Instant / Polished moved to Settings (AI clean-up, "Writing mode", the house toggle with its bolt and sparkles). A small chip on the status line says which mode is in use and opens Settings.
- **No toolbar strip:** search is a magnifier on the title row that opens into a field (the bed's white thumb), and the rare and destructive actions (clear the history, edit dictionary.txt) sit behind "..." beside it. Today's figures ride in today's band; the dictionary's counts ride in its "A to Z" band.
- **Rows are the words:** no timing line; when, where, how long and which models are on the time's tooltip. The row's verbs float over its top right corner on hover, so the words keep the full width.
- **Motion was looked at:** `The_glides_are_rendered_frame_by_frame` (with `ACAPELLA_VISUAL_DIR`) writes the glide frame by frame in real time; `round-2/glide.gif` shows the thumb settling in about a quarter of a second.

## Round three, 5 October 2026 (Dave: "almost there" on the toggles; freeze the header)

- **The toggles, warmer:** each tab leads with its own mark (mic in brand, book in emerald, sliders in plum), in its hue even at rest, softened until chosen or hovered. The white thumb sits on a brand-tinted ring and lift instead of grey, glides with a small spring past its mark and home (`Thumb.Spring`, 340 ms, clamped inside the bed), an unchosen segment shows a whisper of white under the pointer, and a segment gives a little (96%) as it is pressed.
- **The day's band is the list's header and pins:** it sits above the scrolling rows as the card's rounded top, outside the scroll, so the margin above and the rounded corners never move. Each later day's band runs edge to edge between the rows and takes over the pinned header as it reaches the top. The days are one continuous card now; only the last row rounds it off.
