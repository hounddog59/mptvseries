# MP-TVSeries 4.6 – community fork for TheTVDB's current service

This is a community version of MP-TVSeries for MediaPortal 1. It is **not** the official release from the
MediaPortal Plugin Team. Installing it is entirely your choice, and it changes nothing for anyone who doesn't.

**If you don't like it, simply uninstall it and go back to the original MP-TVSeries you had before.**
The official version is at <https://github.com/Mediaportal-Plugin-Team/mptvseries/releases>.

## Why it exists

TheTVDB switched off the old service that MP-TVSeries was written for. The official plugin now needs a
`.tvdb` file in each show's folder to find new shows. This version talks to TheTVDB's current (v4) service
directly, so shows are found by name again, and it adds a number of fixes.

It includes the official team's work up to 4.5.1.756 (configuration crash fixes, `.tvdb` files, the x265 rule,
skin updates).

## What you need

- **A TheTVDB subscription PIN.** TheTVDB requires every user of an app like this to have their own
  subscription (currently US$11.99 a year) – see <https://support.thetvdb.com/kb/faq.php?id=82>.
  Enter the PIN on the **Online Data** tab of MP-TVSeries Configuration.
- **No API key needed.** TheTVDB also needs a key that identifies the program, and the release has one built
  in, so there is nothing to enter. (If you build MP-TVSeries yourself from this source code, put your own key,
  free from thetvdb.com, in `MP-TVSeries/Online Parsing Classes/APIKey.resx` first.)
- **Trakt (optional):** ratings work without logging in. Without it, everyone using this version shares one
  Trakt allowance, so ratings can download slowly when many people are busy. "Authorize with Trakt.tv" (free
  Trakt account) gives you your own allowance. MP-TVSeries only reads ratings with it.

## Improvements

**TheTVDB (the main reason for this version)**
- Works with TheTVDB's current (v4) service: you enter your own subscriber PIN; the program's key is built in.
- Shows are found by name again. If a search finds nothing, it tries again with a tidied-up name (helps names
  with lots of punctuation, like *R.J. Decker*).
- Show titles, episode names and descriptions in your chosen language, falling back to English – e.g.
  *Shōgun* in English instead of Japanese. After installing, every show's text is refreshed once to fix any
  that came in the wrong language.
- The language list comes from TheTVDB's current service.
- Favourites work again: add them from MediaPortal; remove them on thetvdb.com.

**Finding the right show**
- Reads the show's ID from files in its folder: `<id>.tvdb`, or Media Center Master's `tvshow.nfo`,
  `series.xml` and `.txt` (tick box on the Online Data tab, on by default).
- Two different shows with the same name (e.g. *Prime Suspect* 1973 and 1991) are kept apart when their
  folders have ID files.
- Type in the correct TVDB ID for a wrongly matched show on the Details tab: everything is moved across
  safely, the wrong show's pictures are cleared, and only that show refreshes.
- Correct the season or episode number of a wrongly matched episode on the Details tab.

**Trakt ratings**
- Community ratings for series, seasons and episodes straight from Trakt – the Trakt plugin is not needed.
- Optional "Authorize with Trakt.tv" (sign in with a code; remove it any time), with a "Why authorize?"
  explanation.
- When Trakt asks to slow down, it waits instead of failing.
- Your own ratings in MediaPortal are still passed on to Trakt.

**Artwork**
- Newly added shows always get pictures.
- Missing or damaged pictures are removed and replaced, so the Details tab and MediaPortal show the right
  show's pictures.

**Configuration program**
- "Open File Location" on the right-click menu of the Details tab and the import screens.
- Deleting an episode also deletes its matching subtitle, `.nfo` and thumbnail files.
- The import's right-click menu is a sensible width (full text on hover).
- Log panel: Ctrl+C copies, Ctrl+A selects all.
- On big libraries the scan shows "…found so far" progress, and imports are faster.
- Online Data tab tidied up (no scroll bar); help pop-ups stay up for 30 seconds.
- About tab: a thank-you to Damien Haynes and the team.

## Fixes

- Episodes left without a show by an earlier failed import are found and matched again.
- Files TheTVDB doesn't list (some specials) no longer come back as "new" in every import.
- Very long file paths (over Windows' 260-character limit) no longer stop the import; the log names each
  such folder once.
- The wrong show's pictures no longer stick to a show after it has been corrected.
- Changing a show's ID no longer risks making it vanish from the list.

## Removed (TheTVDB no longer offers these)

- TheTVDB personal ratings and the Account ID box.

## Included from the official team (4.5.1.745 – 4.5.1.756)

- Configuration crash fixes, `.tvdb` file support, the x265 naming rule, Titan skin updates, more reliable
  importing of several shows at once, and the newer build setup.

## How to name your files

I name my episodes like **`Show Name S03E16 Episode Title.mkv`** (season 3, episode 16), each show in its own
folder – that's the most reliable. Out of the box MP-TVSeries also understands these:

| Example | Notes |
|---|---|
| `Show Name S03E16 Episode Title.mkv` | also `Show.Name.s03e16`, `Show_Name_S03E16`, `Show Name 3x16` |
| `Show Name S03E16E17.mkv` | double episodes, also `S03E16-E17` or `3x16x17` |
| `Show Name\Season 3\Show Name S03E16.mkv` | season folders are fine |
| `Show Name\S03E16.mkv` | show name taken from the folder when the file name doesn't have it |
| `Show Name 316.mkv` | three-digit numbers: season 3, episode 16 (also `s3ep16`) |
| `Show Name - [3x16] - Episode Title.mkv` | |
| `Show Name - 3x16 - Episode Title.mkv` | |
| `Show Name\3x16 - Episode Title.mkv` | |
| `Show Name\Season 3\Episode 16 - Episode Title.mkv` | |
| `Show Name 2024-03-15 Episode Title.mkv` | daily shows, matched by air date |

Files with "sample" in the name are skipped. Other layouts can be added on the **Import** tab of MP-TVSeries
Configuration (Parsing Expressions), and there's an option there to take the show name from the folder.

## Installing

1. Close MediaPortal and MP-TVSeries Configuration.
2. Back up `C:\ProgramData\Team MediaPortal\MediaPortal\Database\TVSeriesDatabase4.db3`.
3. Download the `.mpe1` file from the [Releases](https://github.com/hounddog59/mptvseries/releases) page.
   If Windows blocked it: right-click it > Properties > tick **Unblock** > OK.
4. Double-click it. When it asks to remove the older MP-TVSeries first, say **Yes**.
5. Open MP-TVSeries Configuration > **Online Data**, enter your TheTVDB PIN, and run an import. The first
   import refreshes every show's details once, so it takes a while.

To go back: uninstall it in MediaPortal Configuration > Extensions, restore your database backup and install
the official version.

Version 4.6 is numbered above the official 4.5.1 releases, so MediaPortal treats it as an upgrade. If the
official team releases an update later, uninstall this version first to install theirs.

## Planned

- **A free option without a TheTVDB subscription:** show details, episodes and artwork from TMDB (The Movie
  Database, free sign-up), chosen on the Online Data tab.
- **TVmaze as a second backup** (free, no sign-up) for shows the main source doesn't have.

## Credits

MP-TVSeries was created by the MP-TVSeries team and is maintained by the MediaPortal Plugin Team
(damienhaynes, andrewjswan, ltfearme and contributors). This fork builds on their work and keeps the original
licence.

This community version for TheTVDB v4 is maintained by Mark Willmer.
