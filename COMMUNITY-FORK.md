# MP-TVSeries 4.7 – community version: free show information, or TheTVDB with your PIN

This is a community version of MP-TVSeries for MediaPortal 1. It is **not** the official release from the
MediaPortal Plugin Team. Installing it is entirely your choice, and it changes nothing for anyone who doesn't.

**If you don't like it, simply uninstall it and go back to the original MP-TVSeries you had before.**
The official version is at <https://github.com/Mediaportal-Plugin-Team/mptvseries/releases>.

## Why it exists

TheTVDB is moving everything to its current (v4) service, which needs each user's own paid subscription PIN.
This version gives you two ways to get your show information:

- **Free, with nothing to sign up for:** from **TMDB** (The Movie Database). This is what you get if you
  don't enter a TheTVDB PIN.
- **TheTVDB**, from its current (v4) service, if you have a subscriber PIN: searching, show details,
  episodes, languages and favourites.

It also adds a number of features and fixes (listed below).

**Which should you use?** The official MP-TVSeries 4.5.1.764 (October 2026) also finds shows by name through
TheTVDB's current service and doesn't need a subscription; it still gets the rest of its data from TheTVDB's
older service. If that works well for you, the official version is the simplest choice. This version works
without a subscription too (using TMDB), and adds everything in the lists below. Choose whichever suits you
best.

**Already have a TheTVDB PIN?** Then this version is well worth a try. One TheTVDB subscription covers every
app that uses TheTVDB this way (Kodi, Plex, Emby and others), so if you already pay for one, your PIN works
here too at no extra cost. Try it, and if it doesn't suit you, uninstall it and go back to the original.

**Already have a TheTVDB PIN?** Then this version is well worth a try. One TheTVDB subscription covers every
app that uses TheTVDB this way (Kodi, Plex, Emby and others), so if you already pay for one, your PIN works
here too at no extra cost. Try it, and if it doesn't suit you, uninstall it and go back to the original.

It includes the official team's work up to 4.5.1.756 (configuration crash fixes, `.tvdb` files, the x265 rule,
skin updates).

## What you need

- **Nothing, for the free option.** Install it and run an import: show information comes from TMDB.
- **Or your TheTVDB subscription PIN**, if you'd rather use TheTVDB. TheTVDB requires every user of an app
  like this to have their own subscription (currently US$11.99 a year) – see
  <https://support.thetvdb.com/kb/faq.php?id=82>. Enter the PIN on the **Online Data** tab of MP-TVSeries
  Configuration, and TheTVDB is used from then on.
- **No API keys needed.** TheTVDB and TMDB each need a key that identifies the program, and the release has
  both built in, so there is nothing to enter. (If you build MP-TVSeries yourself from this source code, put
  your own keys – free from thetvdb.com and themoviedb.org – in
  `MP-TVSeries/Online Parsing Classes/APIKey.resx` first.)
- **Trakt (optional):** ratings work without logging in. Without it, everyone using this version shares one
  Trakt allowance, so ratings can download slowly when many people are busy. "Authorize with Trakt.tv" (free
  Trakt account) gives you your own allowance. MP-TVSeries only reads ratings with it.

## Improvements

**Free show information from TMDB (new in 4.7)**
- No subscription and no sign-up: show details, episodes, cast and pictures come from TMDB when no TheTVDB
  PIN is entered.
- **Source** setting on the Online Data tab: *Automatic* (the default – TheTVDB when you've entered a PIN,
  TMDB when you haven't), *TheTVDB* or *TMDB*. The tab shows which one is in use.
- Your library keeps TheTVDB's show numbers either way (TMDB records them for its shows), so you can switch
  between the two without starting again, and `.tvdb` / `tvshow.nfo` / `series.xml` ID files work with both.
- Shows TMDB has without a TheTVDB number (common for documentaries) are matched by name and year – only when
  exactly one show fits.
- Episode names and descriptions missing in your language are filled in from English.
- Pictures: posters, season posters, episode pictures and cast photos from TMDB; backgrounds at up to Full HD;
  wide banners from fanart.tv.
- Downloads are paced and wait politely if TMDB asks them to slow down.

**TheTVDB**
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
- About tab: a thank-you to Damien Haynes and the team, the TMDB credit and logo, and the correct build date.

## Fixes

- A show that couldn't be found in one import is looked up again in the next (by the TheTVDB number in its
  folder name or ID file).
- A first import no longer downloads every show twice.
- Shows on a new database no longer all show "(needs import)".
- Imports only check for `.tvdb` folder files for new files, not the whole library again.
- Episodes left without a show by an earlier failed import are found and matched again.
- Files TheTVDB doesn't list (some specials) no longer come back as "new" in every import.
- Very long file paths (over Windows' 260-character limit) no longer stop the import; the log names each
  such folder once.
- The wrong show's pictures no longer stick to a show after it has been corrected.
- Changing a show's ID no longer risks making it vanish from the list.

## Removed (TheTVDB no longer offers these)

- TheTVDB personal ratings and the Account ID box.

## Good to know about TMDB

- A few shows aren't on TMDB at all, or TMDB lists them differently – for example some documentaries, and
  series that TMDB puts together as one show (like *Cunk on Britain*, which TMDB has as part of *Cunk on...*).
  These stay unlinked for now; TVmaze is planned as a backup for them (see below).
- For a few shows TMDB numbers the seasons or episodes differently from TheTVDB.
- Favourites are a TheTVDB feature, so they need a TheTVDB PIN.
- This product uses the TMDB API but is not endorsed or certified by TMDB. TMDB is free to use for
  non-commercial use, with credit to TMDB (on the About tab).

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
5. Open MP-TVSeries Configuration and run an import. That uses the free TMDB option. If you have a TheTVDB
   PIN, enter it on the **Online Data** tab first to use TheTVDB instead. The first import refreshes every
   show's details once, so it takes a while.

To go back: uninstall it in MediaPortal Configuration > Extensions, restore your database backup and install
the official version.

Versions 4.6 and 4.7 are numbered above the official 4.5.1 releases, so MediaPortal treats it as an upgrade. If the
official team releases an update later, uninstall this version first to install theirs.

## Planned

- **TVmaze as a second backup** (free, no sign-up) for shows the main source doesn't have.

## Credits

MP-TVSeries was created by the MP-TVSeries team and is maintained by the MediaPortal Plugin Team
(damienhaynes, andrewjswan, ltfearme and contributors). This fork builds on their work and keeps the original
licence.

Show information from TheTVDB.com and TMDB (themoviedb.org); wide banners from fanart.tv.

This community version is maintained by Mark Willmer.
