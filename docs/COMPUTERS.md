# Dave's computers

Every computer runs its own copy of Acapella, and since 02/10/2026 the Windows copies share
settings, dictionary, history and keys through sync. So a change made on one computer can
need something on the others: the same build (sync behaviour lives in each copy, and an older
build can undo a fix through what it pulls or pushes), a one-off repair to data a bug has
already damaged, or nothing at all. This file is where that gets remembered, because it lives
in the repo and reaches every computer with the code.

## The computers

| Computer | OS | App | Installed build | Updated |
|---|---|---|---|---|
| Sidgrove_9950x | Windows 11 | Windows app, signed in to sync | e4d2e23 | 23/10/2026 |
| Name not known yet: the other PC signed in to sync, seen pushing changes on 20/10/2026 | Windows | Windows app, signed in to sync | not known | not known |
| Mac | macOS 26 | Mac app, no sync yet | not known | not known |

Fill in a row the first time a computer is updated from this file; the name is what `hostname`
prints there.

## When Dave asks to update the app on a computer

1. **Find what it runs now.** On Windows the installed exe carries the commit it was built
   from, after the `+`:

   ```powershell
   (Get-Item "$env:LOCALAPPDATA\Programs\Acapella\Acapella.exe").VersionInfo.ProductVersion
   ```

   The Mac app does not stamp a commit yet, so go by the table.
2. **Do what is waiting for it.** Every entry below that names this computer gets done as
   part of the update, not after it.
3. **Install.** Windows: `windows\install.ps1`. Mac: `make install`.
4. **Update its row** in the table.
5. **Then think about the others.** Run `git log <build>..HEAD` from the oldest build in the
   table and look for anything that touches sync, settings, keys or a file format. For each
   computer still on an older build, add an entry below saying what it needs and why, and tell
   Dave in the reply which other computers now need updating. An update is not finished while
   the other computers go unmentioned.
6. **Commit this file** with the update. The other computers only see it once it is pushed,
   so ask Dave about pushing.

When a change is made rather than installed, the same rule applies: if it will need anything
on another computer, add the entry in the same commit as the change.

## Waiting on other computers

Remove an entry once every computer it names has had it.

- **9ec772c, sync keeps settings changed on the PC doing the pulling** (once merged).
  Needed on: every Windows computer signed in to sync.
  Why: the fix lives in the copy doing the pulling. A computer on an older build still lets a
  first sync after signing in overwrite settings chosen on it (how cloud hearing went off on
  Sidgrove_9950x on 19/10/2026), and it still logs nothing when a pull changes a setting.
  After installing: check that "Also hear me in the cloud" is set as Dave wants it there, and
  note that computer's name in the table; its log names other computers in lines that start
  `sync: settings from`.
