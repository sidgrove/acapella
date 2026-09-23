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
