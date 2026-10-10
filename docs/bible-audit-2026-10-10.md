# Acapella for Windows against the Sidgrove Bible, 10/10/2026

Dave, 10/10/2026, of the main window's tab row: "not sure this is very aligned to the SIDGrove Bible
to be honest with you, nor is potentially the whole of the app, which is looking better but not
exactly in line potentially with the SIDGrove Bible".

> **Built the same day.** Dave, shown the options: "what ever you think then finish up and push and
> merge go". So the three recommendations were built into the app: nav A (house section buttons),
> Settings A (folded rows) and status B (the card on Dictations only, a fault as a chip), with the
> key focus halo and the tray's em dash. Findings marked **Done** below are those; everything else
> is as found and still open. What was built and the choices made: `DESIGN.md`, "Three picks from
> the audit, 10 October 2026". Before and after: `docs/screenshots/2026-10-10-bible-build/`.

This is an audit and a set of options. **When it was written, nothing that shipped had changed.** The Bible's rule for a
screen that is not landing is "Options before a rebuild": Bible it, draw three to five different
answers on one contact sheet, each named, scored and owned up to, and build only what he picks.

## How it was done

- **The standard.** All of `Sidgrove Intelligence/docs/BIBLE.md` (Parts 1, 2 and 3), that repo's
  `DESIGN.md` and `docs/UI-CONTRACT.md`, and the source of the web components the Bible names for
  jobs Acapella also has: `components/ui-ext/SegmentedPicker.tsx` (the section buttons),
  `components/ui/PillNav.tsx` (the track and the quiet strip), `components/ui/switch.tsx`,
  `components/ui-ext/IconTile.tsx`, `DisclosureRow.tsx`, `MorePill` and `lib/brand/module-identity.ts`,
  and the focus rule in `app/globals.css`. "Web" below means a value read from those files today.
- **Seen.** Every screen was rendered headlessly by `VisualTourTests` on the sample data and looked
  at as a PNG: the main window at 1080 by 780, 640 by 480 and 1440 by 900, Settings scrolled to
  five places, the empty, listening, paused and fault states, the welcome window's three steps, the
  dictionary editor, About, and the pill's eight states.
- **Not seen.** The tray menu (Windows draws it; only its words were read, in `App.axaml`). Tooltips
  and flyouts (the headless capture does not draw popups). Anything in dark mode. Anything on Dave's
  own screen at 125% or 150% scaling, with his real app icons. No score here has been checked by
  someone who did not make the drawing, and the Bible says a maker cannot pass its own work.

Severity: **High** breaks a rule Dave has given in his own words or the measured web component for
the same job. **Medium** breaks a Part 2 rule. **Low** is a value a few points off, or taste.

## The whole window, scored (Part 1, question 12)

**7 out of 10.** What is already world class: the pill, the day bands that pin, real app marks on
the rows, the serif title with its italic accent. What holds it at 7: the first thing under the
title is the same status card on all three sections, the section switch is the wrong house control
for its job, the Settings page is where "less is more" was not applied, and chips count the rows
directly beneath them. The weakest element on the whole window is the Settings page.

---

## Screen by screen

### 1. Title row and the section tabs (the thing in Dave's screenshot)

| # | Miss | The Bible | On screen now (measured) | The Bible or the web has | Severity |
|---|---|---|---|---|---|
| 1.1 | The sections use the toggle's control, not the section control | "Choose the current section: `SegmentedPicker appearance="buttons"` beside the title"; PillNav.tsx: "Page-category choices use the separate-button appearance in SegmentedPicker, not another navigation track" | `NavLink.Track`: one bed `#eef0f7` (`Colors.ToggleBed`), 10px corner, 3px inset, 24px segments (30px in all), a white thumb gliding with a spring. It is the same control as Instant / Polished and "Hold or tap" in Settings | Separate squares, no bed: 32px high, 7px corner, 6px apart, fill `#f6f7fb`, no outline; chosen is white with `0 1px 2px rgba(15,23,42,0.06)`. Dave picked this on the web's joy review the same day: "more colour, the icons and softer" | High |
| 1.2 | The section's mark is a bare glyph, not a tile | "Each thing has its own colour, worn on its icon tile and nowhere else"; marks "22 (11) beside a label, 26 (13) on a tab" | A 13px line glyph in the accent's ink at 50% opacity when not chosen (`Opacity.GlyphResting`), no tile behind it | A 22px tile, fill from `MODULE_HUES` (brand `#e4e6f2` / `#3d4785`, green `#e0ece1` / `#3f7d4d`, purple `#ede7f6` / `#7c5dab`), glyph 11, 80% when not chosen | High |
| 1.3 | The current section is named twice, side by side | Question 5: "Nothing floating, nothing repeated" | "Your *dictations*" and, 24px to its right, the chosen tab "Dictations" | The web names the view in the title's italic accent and uses the buttons for sections whose names the title does not repeat | Medium |
| 1.4 | Keyboard focus is the theme's black box | Lens 13 "Hover, focus and keyboard work"; UI-CONTRACT: focus is `0 0 0 2px rgba(104,116,180,0.28)`, flush | No focus style is set anywhere in the app (`FocusAdorner` is never touched; the app runs `FluentTheme`), so Tab draws Fluent's hard black rectangle round the tab. Seen on the states sheet, top right | The soft brand halo | High |
| 1.5 | Type and weight | Section buttons: 12px, 500 at rest, 600 chosen | 13px (`Fonts.Tab`), 600 always; chosen is told by ink and the thumb only | 12px, weight changes with the choice, over a hidden bold copy so the box never changes width | Low |
| 1.6 | The thumb's shadow is tinted and three layers deep | PillNav.tsx: "ONE keyline + ONE tight contact shadow ... nothing tints or floats"; Dave 05/08: "a bit AI sloppy ... quite pronounced like shadow on these toggle bars" | `Shadow.Thumb`: a 0.5px ring and two brand-tinted layers, the deepest `0 3px 8px rgba(61,71,133,0.08)` | `0 0 0 1px rgba(15,23,42,0.04), 0 1px 2px rgba(15,23,42,0.04)` | Medium |
| 1.7 | Settings sits in the row as a third section | "the '...' closes the row and holds only the rare (hop-outs, downloads, setup, uploads)" | Settings is an equal third tab | Set-up is reached from the end of the title row | Taste |

Complies: the sections sit beside the title (its titleAside), one row only, the italic accent names
the view, there is no description under the title, no icon beside the heading, nothing clips at 640
(the sections that are not current fold to their marks), and the count on Dictionary is a true
count from stored data.

### 2. Status card (and the fault inside it)

| # | Miss | The Bible | On screen now | The Bible or the web has | Severity |
|---|---|---|---|---|---|
| 2.1 | The same card is pinned above all three sections | Lens 4: "Every pixel above the fold earns it"; question 5 "nothing repeated"; "State lives in one chip beside the title, not a banner" | A 62px card and its 16px gap on Dictations, Dictionary and Settings alike. At 640 by 480 the title row and this card take 182 of the 428px under the caption before a single setting or word shows | State is one chip; a card is for the page whose answer it is | High |
| 2.2 | A fault is a full-width coral bar | "A warning is a small chip on the line it is about, never a full-width bar"; "a full-width bar where a small panel does" is on the AI slop list | `BuildFault`: a bar the width of the card, fill `#fde9e3`, a 15px glyph, a 14px sentence and a loose "Dismiss" (`SgButton.Kind.Quiet`: no fill, no edge) | One coral chip (22px), the sentence in its tooltip | High |
| 2.3 | "Dismiss" is floating text | Mechanical check 1: "Never floating text" | Brand-ink words on the coral bar with nothing round them at rest | A real control, or none (a chip clears when the fault does) | Medium |
| 2.4 | The switch is a saturated fill | "Never a saturated or gradient fill with white text"; web `switch.tsx` | 32 by 18, track solid `#3d4785` when on, white thumb; off track `#dfe1ee` | 38 by 22 (or 28 by 16 small); on: track is the on colour at 20% on white, the thumb itself takes the colour and carries a small tick; off: `#e3e5ee`, white thumb with `0 1px 2px rgba(15,23,42,0.18)` | High |
| 2.5 | "hold or tap to talk" is loose text | "No loose text"; "No floating text" | 14px muted words after the keycap, on nothing | Inside the keycap's own chip, or gone (the keycap is the instruction) | Low |
| 2.6 | Card padding | Hub card pad 24, workbench 16 | 12 left and vertical, 24 right | 16 or 24, even | Low |

Complies: the tile's hue is the state and red (crimson) is used for listening only here; the
keycaps; one line; the loader's dots while it works; the mode chip is a door to its setting.

### 3. Dictations (the history)

| # | Miss | The Bible | On screen now | The Bible or the web has | Severity |
|---|---|---|---|---|---|
| 3.1 | Three chips that count the rows under them | Dave, 10/10/2026: "A figure nobody acts on, a count of the rows directly beneath it, a sum of a column in view ... these score low and go"; "Under 6 goes" | Today's band carries "7 dictations" (the rows beneath), "117 words" (their words) and "0.90 s typical wait" (`TranscriptionsView.cs` 228 to 242). Rated for use: 2, 2 and 4 out of 10; nobody decides anything from them | Nothing. Removal is the default and he is told what went | High |
| 3.2 | A scroll bar is drawn | "A list or rail that scrolls draws no bar; its edge fades where there is more (`.sg-soft-edge`)" | A 2px brand-mid line down the right of the list, always visible (`ShellWindow` scroll bar resources) | No bar, a soft fade at the edge | Medium |
| 3.3 | The list scrolls in a box | "The PAGE scrolls, never a box inside it" | Title and status stay; only the list moves | In a desktop window a pinned head is defensible and Dave asked for the band to pin (05/10), so this is noted, not a fault | Taste |
| 3.4 | The time is bare text | "A date or status in a table cell is a subtle pill, never bare text" | 12px bold time at the row's right, on nothing | A slate chip, the same on every row; or keep it bare on purpose and write the reason down | Low |
| 3.5 | Reading size is off the scale | "Six sizes": 11, 12.5, 12.5, 14, 16, 32 | Transcript at 15px (`Fonts.Reading`) | 14 (or accept 15 as the one reading size and say so) | Low |
| 3.6 | Card edge | Hub card: `--sg-panel-border` `#e3e6f0` | `#d8deec` (`Colors.CardBorder`, the workbench shell) on every card | `#e3e6f0` on a hub | Low |

Complies: real app marks on a tinted fallback, one card per day with a tinted band, rows without a
line of buttons (verbs on hover and focus), chips that never wrap, the quiet empty state (one small
tile, 14px words, the key as keycaps), hairlines by weight.

### 4. Dictionary

| # | Miss | The Bible | On screen now | The Bible or the web has | Severity |
|---|---|---|---|---|---|
| 4.1 | Counts of the rows beneath, again | As 3.1 | "7 words" and "10 corrections" in the A to Z band; a "2" chip at the right of "Suggested from your edits" over two rows, and "2" again on the tab | The tab's count is the door and stays; the others go | High |
| 4.2 | "Not this" is floating text beside a boxed "Add" | "Never floating text"; lens 2 "one primary action, the rest quieter" is met, but the quiet one has no box at all | `Kind.Quiet`: ink on nothing | The quiet house button: fill `#eef0f4`, edge `#cbd1de`; or an icon button with a tooltip | Medium |
| 4.3 | Three cards and a status card above the words | Lens 1 "Work queue or status report?" is met (suggestions lead); the chrome budget is not | At 640 by 480 no dictionary word is on screen at all | With 2.1 fixed this mostly cures itself | Medium |
| 4.4 | Struck-through word | Taste | "Hoxon" struck through in muted ink beside the fix | Fine; noted because strike-through at 14px is hard to read | Taste |

Complies: one row per word with its heard forms as chips, a header band, "Add word" as the one
brand primary, search and "..." folded into one held control, the question mark carrying the
explanation instead of a sentence.

### 5. Settings (each card)

The page as a whole first: **eleven open cards, 33 settings and some 39 question marks in one scroll
about 3,500px long, under a pinned status card.** Dave, 06/10/2026: "feature rich, never feature
loud ... everything else waits one click away, in a menu, a fold or a popover"; lens 3: "Lots of the
same button is its own finding". This is the lowest score in the app: **5 out of 10**.

| # | Card | Miss | The Bible | On screen now | Severity |
|---|---|---|---|---|---|
| 5.1 | All | Everything is open at once | "Long lists fold to what needs a person; the routine stays quiet until asked for" | 11 `Panels.SettingsCard` in one column, none folds | High |
| 5.2 | All | A question mark on almost every line | Lens 3; "Count the grey pills, then fold them" | 10 on card heads and about 29 on rows (`Hint.Make`, a 14px glyph in `#686d88`) | High |
| 5.3 | Microphone | The tile is crimson | "Red means recording and nothing else" | `Accent.Crimson` (`#fbe1e6` / `#7a1e36`) on a settings card while nothing records. The welcome window's "Try it" tile does the same | High |
| 5.4 | Sending | The tile is coral | Lens 6: "One colour, one meaning": coral is "blocked, overdue, failed" | `Accent.Coral` on a card that is none of those | Medium |
| 5.5 | Push to talk | Explainer line, and five grey pills | "no explainer line under a label (tooltip it)"; lens 3 | "Click here, then press a key or a combination like Ctrl + Shift + Space" under the keycap; "Or pick one" then five identical white buttons; a "Record a key" chip that looks like a status | Medium |
| 5.6 | Hearing you | A file path as a line of text; a state with no chip | "Say what it is, in the client's words"; "state is one chip" | "Loaded from C:\Users\...\parakeet-v2" in 11px; "Parakeet found, loading" as bold text with a dot | Medium |
| 5.7 | AI clean-up, Jev | Developer words in the fields | "Plain, warm, human and brief" | Placeholders "or leave blank to use GEMINI_API_KEY", "AI_GATEWAY_API_KEY"; labels "Base URL", "Model" twice | Medium |
| 5.8 | Jev, Sync | A paragraph hidden in a tooltip is still a paragraph | "No one reads this" | Head tips of 45 and 40 words | Low |
| 5.9 | Sync | A disabled grey button with a line explaining it | AI slop list: "a disabled grey button with an amber chip explaining it" | "Not signed in", "Sync isn't available here.", then a greyed "Sign in with Sidgrove". This may only be the headless state; check on the real app | Medium |
| 5.10 | Behaviour | Quit is coral text | "Never floating text"; red and its cousins mean something | "Quit Acapella" in coral ink (`#8a2f1c`) with no box, at the foot of a card of switches | Medium |
| 5.11 | All | Switches | As 2.4 | 18 of them | High |
| 5.12 | All | Card titles are bold | `text-lead` is 16px / 600 | 16px / 700 (`Text.Heading`, `SectionHead`) | Low |

Complies: the label in a fixed 240px column with its control beside it, fields only as wide as what
they hold (200 and 380), one tile hue a card, no description under a card title, the toggle is the
house bed with no outline, 8px fields.

### 6. Welcome window

| # | Miss | The Bible | On screen now | Severity |
|---|---|---|---|---|
| 6.1 | The step trail is floating text | "No floating text. A label sits inside the pill with its icon, in one anchored strip" | Three tiles each followed by loose 12.5px words, on the canvas, no bed and no box | High |
| 6.2 | Each step is named twice, 60px apart | Question 5 | "Get the speech model" in the trail and again as the card's head; "Welcome" in the caption and "Welcome to Acapella" under it | Medium |
| 6.3 | "Skip for now" is loose text, centred | "Never floating text"; "never centre ... a strip"; the slop list's "a decline or 'not for me' button nobody needs" (the close button already skips) | `Kind.Quiet`, centred between the edges | Medium |
| 6.4 | "Try it" is crimson before anything records | As 5.3 | `Accent.Crimson` | High |
| 6.5 | No moment of joy at the end | Question 13: "the house confetti pop ... a tick that draws itself in" for a real finish | The first dictation landing in the box ends on a plain "Done" button | Medium |

Complies: one step at a time, one brand primary, ticks that are true (a step shows done only when it
is), the title's italic accent.

### 7. The dictation pill

The strongest screen in the app: **8.5 out of 10.** One row, a tile whose hue is the state, one
moving part a state, the house loader, nothing after the words land.

| # | Miss | The Bible | On screen now | Severity |
|---|---|---|---|---|
| 7.1 | The timer is loose figures | "no floating figures (every number sits in a cell or a plate)" | "00:02" in 12px muted after the bars | Low |
| 7.2 | The pill's corner | Radius scale 8 / 10 / 12 / 16 | About 16 on a 58px pill; on the scale, noted only | Taste |
| 7.3 | "Nothing heard. Is the mic muted?" | "no question for a subtitle" | A question in the state's place | Low |

### 8. Dictionary editor, About, the tray

| # | Where | Miss | The Bible | On screen now | Severity |
|---|---|---|---|---|---|
| 8.1 | Editor | The form stands on the canvas with no card | "Cards ... sit straight on the canvas"; nothing floating | Labels, fields and the switch row straight on the wash | Medium |
| 8.2 | Editor | "Delete" is coral text with no box | "Never floating text" | `Kind.Danger`: coral ink, nothing round it at rest | Medium |
| 8.3 | Editor | Labels above the fields | "the label in a fixed column with its control beside it" | `Panels.Labelled`, above. Defensible at 460px wide | Taste |
| 8.4 | About | Says "DM Sans and Inter" | "Two fonts. DM Sans ... Very Vogue" | Inter is only the test host's font; the words are wrong | Low |
| 8.5 | Tray | An em dash | "no em or en dashes" | Tooltip "Acapella — hold the push-to-talk key to dictate" (`App.axaml`) | Low |
| 8.6 | Tokens | Two fonts too many are declared | "Two fonts"; "No monospace anywhere" | `Fonts.Mono` (JetBrains Mono) and `Fonts.Display` (Perfectly Nineties), with `MonoLabel`, `Badge`, `Coin`, `Headline` and the `Hero` button kind, none used by any view | Low |
| 8.7 | Tokens | A retired edge and grid tokens live on | "The dark `#a8b0d8` edge is retired"; "No orbs, grid" | `Colors.BrandMid` `#a8b0d8` (now only the scroll line and idle bars), `GridLine`, `AmbientPeri`, `AmbientPeach`, `HeroRose` | Low |
| 8.8 | Theme | The app follows the system's dark setting | Not seen | `RequestedThemeVariant="Default"` with light-only tokens: Fluent's own parts (text boxes, tooltips, flyouts) may turn dark on a dark Windows. Needs a look on a real machine | Unknown |

---

## The top ten, ranked

1. **Done (5.1, 5.2).** **Settings is feature loud.** Eleven open cards, 33 settings and some 39
   question marks in one long scroll. Now eleven folded rows, each saying what it is set to; a
   row's question marks show only while it is open.
2. **Done (1.1, 1.2, 1.5).** **The section switch is the toggle, not the house's section control.**
   One bed and a gliding thumb where the web app, as of today, uses separate soft squares each
   wearing a tinted mark. 1.3 (the page named twice) and 1.7 (Settings as a third section) are
   as found.
3. **Done (2.1).** **The status card is repeated on every section**, and takes more than a third of
   the smallest window before any content. It now stands on Dictations only.
4. **Chips that count the rows beneath them**, on Dictations and on Dictionary, the exact thing Dave
   ruled out this morning. Remove first. (3.1, 4.1)
5. **Done (1.4).** **Keyboard focus is a black box.** No focus style existed in the app, so Tab
   showed the theme's. Every button, switch and toggle now wears the brand halo.
6. **The switch is a saturated fill**, 18 times over, where the web's is a pale track with a coloured
   thumb and a tick. (2.4)
7. **Done (2.2, 2.3).** **A fault is a full-width coral bar with a loose "Dismiss".** It is a chip on
   the status line, and beside the title on the other two sections; pressing it clears it.
8. **Red is used where nothing records**: the Microphone card and the welcome's "Try it" tile are
   crimson; Sending is coral, the hue for failed. (5.3, 5.4, 6.4)
9. **Floating text standing in for buttons**: the welcome trail and "Skip for now", "Not this",
   "Dismiss", "Quit Acapella", "Delete", "hold or tap to talk". (6.1, 6.3, 4.2, 5.10, 8.2, 2.5)
10. **A scroll bar is drawn on every list**, where the house fades the edge and draws none. (3.2)

Then, smaller: developer words in Settings (5.7), the tinted three-layer thumb shadow (1.6), the
title naming the page twice (1.3), no joy at the end of the welcome (6.5), the em dash in the tray
(8.5), dead tokens and two unused fonts (8.6, 8.7).

**Removals that need no options** (the Bible: "Remove first, add back on need, always"), listed and
not done here because this task must not change what ships: the three chips in today's band, the
two in the A to Z band, the "2" beside "Suggested from your edits", the "Or pick one" label, the
model's file path line, the em dash, the unused tokens.

---

## Options

All of these were real Avalonia controls that lived only in the test project
(`windows/tests/Murmur.App.Tests/BibleAudit/`, commit `d948e28`), each stood in the real main window
by finding the shipping chrome at run time. Once the picks were built into the app that folder was
deleted, as planned, so the options that were not picked ship nowhere; the sheets below are the
record, and the fixture can be read back from that commit. (Also done from the smaller list: the
em dash in the tray tooltip, 8.5.)

Each score is the maker's, for the option as drawn, out of 10 against the Part 1 questions; the
columns are the questions it moves most. None has been seen by anyone else or on a real screen.

### Set 1. The section navigation (his screenshot)

- Side by side at 1080 and 640: `docs/screenshots/2026-10-10-bible-audit/sheet-section-nav.png`
- Every state (rest, pointer over, chosen, keyboard focus): `docs/screenshots/2026-10-10-bible-audit/sheet-section-nav-states.png`

| | Name | The idea, in one sentence | Joy (1) | Clear (4) | Clean (5) | Subtract (6) | Colour (7) | Calm (14) | Score | Weakest element, owned up to |
|---|---|---|---|---|---|---|---|---|---|---|
| | Today | The sections on the toggle's bed with a gliding white thumb | 7 | 8 | 6 | 6 | 6 | 8 | 7 | It is the Settings toggles' control doing a different job, and Tab draws a black box |
| A | House section buttons | The web app's own section control: separate soft squares, each wearing a 22px tinted mark, the current one white and lifted | 8.5 | 9 | 8 | 7 | 9 | 8.5 | **8.5** | At rest the two squares that are not chosen are `#f6f7fb` on a `#f4f5f9` canvas, two points apart, so over the pale left of the wash they nearly read as words on nothing; the web has the same values and the same weakness |
| B | Marks only | The words go, because the title already names the page: three marks, named in tooltips | 7.5 | 6.5 | 9 | 9.5 | 8 | 9 | 7.5 | A first-time user has to guess that a book is the dictionary and sliders are settings; the amber count on the book's corner covers part of the glyph |
| C | Two sections and a cog | Settings is set-up, so it leaves the sections and closes the title row as one marked button | 8 | 8.5 | 8 | 8.5 | 8.5 | 8.5 | 8 | The title row's right end now holds two treatments side by side (the held search and "..." on its bed, then a white marked button), and Settings loses its word |
| D | A quiet strip under the title | The Bible's sibling-page pattern: one strip from the left under the title, 26px tiles, every word at any width | 8 | 9 | 7.5 | 5.5 | 9 | 7 | 7.5 | It spends a second header row (46px) in a window where height is the scarce thing, and it overturns Dave's 05/10 ruling that the sections sit beside the title |

Drawn and dropped before the sheet: a left-hand rail (it contradicts the same 05/10 ruling and
takes 64px of a 640px window), and today's track redrawn with the web's track values (one layout
pared back is not a second option).

**The pick: A.** It is the control the Bible names for this exact job, it is what Dave chose on the
web app the same day, it puts colour on the mark and nowhere else, and it fixes the black focus box
and the twice-used toggle in one move. Worth mixing in: C's idea of moving Settings to the end of
the row is independent of the look and could follow later if he wants fewer sections; B's fold to
marks is already how A behaves when the window is narrow.

### Set 2. The Settings page

- `docs/screenshots/2026-10-10-bible-audit/sheet-settings.png`

| | Name | The idea, in one sentence | Clear (4) | Subtract (6) | Fighting for its life (9) | Calm (14) | Score | Weakest element, owned up to |
|---|---|---|---|---|---|---|---|---|
| | Today | Eleven open cards in one scroll | 6 | 3 | 4 | 4 | 5 | The whole of it is on screen at once |
| A | Folded rows | Each section is one line that says what it is set to in plain words and opens in place | 9 | 8.5 | 9 | 9 | **8.5** | Eleven summaries have to be written and kept true from stored settings; a summary that is wrong once is worse than none. Opening a row still shows its question marks until those are thinned too |
| B | Everyday first | One card of the four things changed most, and one soft pill for the other 29 | 8.5 | 9.5 | 8 | 9.5 | 8 | Which four is a guess until it is measured, and 29 settings behind one pill is a junk drawer the first time something has to be found |
| C | Four groups on one strip | Today's cards sorted into four groups, the house toggle choosing the group | 7.5 | 5 | 6 | 6.5 | 6.5 | It adds a control row and removes nothing: each group is still open cards with every question mark, and the group names are ours, not his |

**The pick: A.** It is the web app's `DisclosureRow` ("why can't I click the row to drop down",
Dave, 03/10), it makes the page fit one screen, and the summaries turn a form into something that
can be read. B is the calmer picture and its top card could become A's first rows opened by
default.

### Set 3. The status card and the fault

- `docs/screenshots/2026-10-10-bible-audit/sheet-status.png`

| | Name | The idea, in one sentence | Clear (4) | Clean (5) | Subtract (6) | Calm (14) | Score | Weakest element, owned up to |
|---|---|---|---|---|---|---|---|---|
| | Today | The card above every section, a fault as a coral bar | 8 | 5 | 5 | 6 | 6.5 | The same card three times, and the bar |
| A | Quiet until it matters | The card goes; the window says nothing at rest and shows one chip beside the title when it is paused or something failed | 7 | 9 | 9.5 | 9.5 | 8 | The pause switch loses its one-click home (it would live in the tray and in Settings), and someone new no longer sees which key to hold once the history is not empty |
| B | Only on its own page | The card stays on Dictations, where it is the answer, and leaves the other two; a fault is a chip on its line | 9 | 8.5 | 8 | 8.5 | **8.5** | The page's top changes height between sections, and a fault raised while he is on Settings is not on screen until he goes back (the pill and the chip on return cover it, but it is a gap) |
| C | In the window's own strip | State, key and pause move into the empty caption strip, on every section, taking no height | 8 | 7.5 | 7 | 8 | 7.5 | The title bar is not where a Windows user looks for an app's state, it leaves less bar to drag the window by, and at 640 with a fault showing the strip is full |

**The pick: B.** It keeps everything the card does where it earns its place, gives Dictionary and
Settings back 78px each, and turns the fault into the chip the Bible asks for, with nothing a
person relies on taken away. A is the braver subtraction and the right second step if he finds he
never looks at the card.

---

## What this did not do

- No shipping view, token or behaviour changed. Two helpers in `VisualTourTests` went from private
  to internal so the audit's sheets could use the same sample data.
- The options were not put through a second scorer, and were not seen at Dave's width on his
  screen. Before any is built, the chosen one needs that look.
- The fold to marks, the keyboard order and the tooltips are real in the option controls; the
  tooltips themselves could not be captured.
