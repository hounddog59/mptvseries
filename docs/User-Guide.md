# MP-TVSeries community version – User Guide

This guide covers the community version of MP-TVSeries for MediaPortal 1 (4.8.2.6). It explains what the plugin
does and where to find each feature. Each section is short, so you can jump straight to the part you need.

- [Getting started](#getting-started)
- [Where your show information comes from](#where-your-show-information-comes-from)
- [Naming your files](#naming-your-files)
- [Using MP-TVSeries in MediaPortal](#using-mp-tvseries-in-mediaportal)
- [Views](#views)
- [The Info menu](#the-info-menu)
- [MP-TVSeries Configuration, tab by tab](#mp-tvseries-configuration-tab-by-tab)
- [Fixing a wrongly matched show or episode](#fixing-a-wrongly-matched-show-or-episode)
- [Renaming your files](#renaming-your-files)
- ["Needs your help" – shows a scan couldn't match](#needs-your-help--shows-a-scan-couldnt-match)
- [Deleting several at once](#deleting-several-at-once)
- [Trakt](#trakt)
- [Updates](#updates)
- [Torrent Search (with MyTorrents)](#torrent-search-with-mytorrents)
- [Troubleshooting](#troubleshooting)
- [Going back to the official version](#going-back-to-the-official-version)

---

## Getting started

1. Close MediaPortal **and** MP-TVSeries Configuration.
2. Back up your database:
   `C:\ProgramData\Team MediaPortal\MediaPortal\Database\TVSeriesDatabase4.db3`.
3. Download the `.mpe1` file from the [Releases](https://github.com/hounddog59/mptvseries/releases) page and
   double-click it. If Windows blocked it: right-click > **Properties** > tick **Unblock** > **OK**.
   When it asks to remove the older MP-TVSeries first, say **Yes**.
4. Open **MP-TVSeries Configuration** (Start menu) and go to the **Import** tab.
5. Add the folders your TV shows are in (**Import Paths**), then click **Start Import Wizard**.
6. When the import has finished, start MediaPortal and open **TV Series** from the home screen.

The first import downloads information and pictures for every show, so on a big library it takes a while.
After that, only new files are looked at.

## Where your show information comes from

You can choose this on the **Online Data** tab, under **Source**:

| Source | What you need | Notes |
|---|---|---|
| **Automatic** (default) | nothing | Uses TheTVDB if you've entered a PIN, TMDB if you haven't. |
| **TMDB** | nothing | Free, with no sign-up. A few shows (mostly documentaries) aren't on TMDB. |
| **TheTVDB** | your TheTVDB subscriber PIN | Enter it with **Pin Code...** on the Online Data tab. |

Your library keeps TheTVDB's show numbers either way, so you can switch sources at any time without starting
again. The API keys the program needs are built in, so you don't need to enter any.

## Naming your files

The most reliable layout is one folder per show, with files named like
`Show Name S03E16 Episode Title.mkv`. Other common layouts work too (`3x16`, `S03E16E17` for double
episodes, season folders, air dates for daily shows). The full table is on the
[community version page](../COMMUNITY-FORK.md#how-to-name-your-files).

**Tip:** if a show keeps matching the wrong series, put a file named `<TVDB id>.tvdb` (for example
`81189.tvdb`) in the show's folder. MP-TVSeries also reads `tvshow.nfo`, `series.xml` and `.txt` ID files. This
also keeps apart two shows with the same name, such as *Prime Suspect* (1973) and *Prime Suspect* (1991).

## Using MP-TVSeries in MediaPortal

- Open **TV Series** from the MediaPortal home screen.
- You move from **Series** > **Season** > **Episode**. Press **OK/Enter** to open, **Back/Esc** to go back.
- Press **Info** (or **F9** on a keyboard) on any series, season or episode to open the **Info menu**.
- Watched episodes are marked when you finish them. You can also mark them yourself from the Info menu.
- If enabled, a rating window appears after you watch an episode, so you can rate it.

## Views

Views are the different lists you can switch between, such as *All*, *Unwatched* or *Recently Added*. To
change view in MediaPortal: **Info** > **Change View**.

Choose which views are offered on the **Views/Filters** tab of MP-TVSeries Configuration. To add one of the
ready-made views, click **Templates...**. To change one, click **Edit**.

| View | What it shows |
|---|---|
| All | Every series in your library. |
| Favourites | Series you've marked as favourites. |
| Unwatched | Series that still have episodes you haven't watched. |
| **Continue Watching** | Shows you've watched lately (the last 60 days – change it on the **General** tab) that still have unwatched episodes, the one you watched last at the top. |
| **Recently Added** | The series that have had episodes added in the last 7 days. Pick one to see just its new episodes. |
| Recently Added Episodes | The old style: one list of every episode added in the last 7 days. |
| Latest | Episodes that have aired in the last 30 days. |
| Continuing / Ended | Shows still being made, or shows that have finished. |
| Channels, Genres, Actors, Content Rating | Your series grouped by network, genre, actor or rating. |
| Kids Series / Adult Series | Filtered by content rating. |
| High Definition / Standard Definition | Episodes 720p and above, or below. |
| Top 10 (online / yours) | The highest-rated series. |
| View Tags | Your own lists. Add a series to one with **Info** > **Add Series to View**. |

You can protect a view with a PIN (tick **Prompt for Pin Code when entering view**) and hide views you don't
use (untick **View is available for selection**).

## The Info menu

Press **Info** on a series, season or episode. What's offered depends on where you are. The main items:

| Item | What it does |
|---|---|
| **Torrent Search ...** | Searches for the series or episode with MyTorrents (only when MyTorrents is installed and enabled). It's at the top of the menu. |
| Toggle Watched | Marks an episode watched or unwatched. |
| Mark all as Watched / Unwatched | For a whole series or season. |
| Rate this Series / Episode | Your own rating (also sent to Trakt if you use it). |
| Add to Playlist | Adds episodes to the MediaPortal playlist. |
| Fanart / Cycle Poster / Cycle Banner | Choose the pictures shown for the series or season. |
| Actors | The cast, with photos. |
| Force New Online Lookup For Series | Searches for the series again if it's matched to the wrong show. |
| Add Series to View / Remove Series from View | Puts the series in one of your own lists. |
| Change View / Change Layout | Switches list or layout (list, banners, posters, etc.). |
| Actions | Delete, Update, Update Media Info, play a random episode, subtitles. |
| Filters / Options | Show only unwatched, hide spoilers, show hidden items, and more. |

## MP-TVSeries Configuration, tab by tab

Start it from the Start menu (**MP-TVSeries Configuration**). Close MediaPortal before changing settings, because
MediaPortal saves its own copy when it closes.

| Tab | What's there |
|---|---|
| **Details** | Your library. Pick a show to see and edit its information and pictures. Here you can type the correct TVDB ID for a wrongly matched show, or correct an episode's season and episode number. Right-click > **Open File Location**. |
| **Import** | Import paths (where your shows are), parsing expressions (how file names are read) and **Start Import Wizard**. |
| **Online Data** | Source (Automatic / TheTVDB / TMDB), TheTVDB **Pin Code...**, language, automatic updates, the ID-file option, artwork download limits and Trakt. |
| **Views/Filters** | Which views are offered, **Templates...** to add ready-made ones, and **Edit**. |
| **General** | MediaPortal behaviour, for example what Play does on a series, skipping the season list when there's only one season, the rating window, and hiding spoilers. |
| **Layout** | How series, seasons and episodes are shown, and the text in each field. |
| **Logos** | Rules for the little logos (HD, audio channels and so on). |
| About | Version, build date and credits. |

The log panel at the bottom shows what's happening. **Ctrl+A** selects all of it and **Ctrl+C** copies it, which
is handy when you're asking for help.

## Fixing a wrongly matched show or episode

- **Wrong show:** on the **Details** tab, pick the show and type the correct TVDB ID. Everything moves across,
  the wrong pictures are cleared, and only that show is refreshed. Or, in MediaPortal: **Info** >
  **Force New Online Lookup For Series**.
- **Wrong episode number:** on the **Details** tab, pick the episode and correct its season or episode number.
- **To stop it happening again:** put a `<id>.tvdb` file in the show's folder (see
  [Naming your files](#naming-your-files)).

## Renaming your files

In MP-TVSeries Configuration, right-click a show or season on the **Details** tab > **Rename files...** (or rename
all your shows one by one). The show folder comes first, then the season folders and episode files, each with a
tick box. Titles come from the online data; subtitles, `.nfo` files and thumbnails go with their episode, and the
database is updated straight away – no rescan. **Undo the last rename** puts things back. Files MP-TVSeries hasn't
matched are left alone. If you use MyTorrents, its naming styles are used, so both programs name things the same.

## "Needs your help" – shows a scan couldn't match

When an import can't match a show, it goes into the **Needs your help** list instead of a pop-up for each one.
For each show: **Pick** one of the suggestions, **Search** with your own words, or **Skip** it for now (skipped
files aren't offered again until you bring them back). Scans that run in MediaPortal save these for
Configuration, so nothing pops up on the TV.

## Deleting several at once

On the **Details** tab, click **Tick several**, tick the shows, seasons or episodes, then **Delete ticked...**.
**Delete several...** can tick the ones whose files are gone for you. Unmatched shows with no files left are
removed automatically.

## Trakt

- **Ratings (built in):** community ratings for series, seasons and episodes come from Trakt. You don't need the
  Trakt plugin or a Trakt account. If ratings download slowly (everyone shares one allowance), use
  **Authorize with Trakt.tv (optional)...** on the Online Data tab to get your own. You can remove it at any time.
- **Syncing watched flags (plugin):** to keep watched flags and ratings in step with Trakt (and across several
  HTPCs), install **Trakt for MediaPortal** – the MP-TVSeries installer offers it as **Trakt Plugin**. Set it up
  in MediaPortal: **Trakt** > **Settings** > **Account Settings** > **Authorize Application**, enter the code on
  the Trakt website, then tick **MP-TVSeries** under the plugin's **Plugins** settings.

## Updates

From 4.8.2.6, MediaPortal's extension manager (**MediaPortal Configuration > Extensions**, or MPEInstaller) tells
you when a newer version of MP-TVSeries is out, and can install it.

## Torrent Search (with MyTorrents)

If you have the MyTorrents plugin installed and enabled, **Torrent Search ...** is the first item in the Info
menu. On a series it searches for the series, and on an episode it searches for that episode. MyTorrents has
its own settings and guide.

## Troubleshooting

| Problem | What to try |
|---|---|
| A show isn't found | Check the folder and file name, and try a `.tvdb` ID file. With TMDB, some documentaries aren't listed – enter a TheTVDB PIN for those. If it's in **Needs your help**, use **Search** there. |
| An episode keeps coming back as "new" | Usually a special TheTVDB doesn't list. Correct its number on the Details tab. |
| Pictures are wrong or missing | **Info** > **Fanart** or **Cycle Poster** in MediaPortal, or the pictures on the Details tab. Run an import to replace missing ones. |
| Settings don't stick | Close MediaPortal before changing settings in Configuration. |
| Something else | Look at `C:\ProgramData\Team MediaPortal\MediaPortal\log\MP-TVSeries.log`, then ask on [Issues](https://github.com/hounddog59/mptvseries/issues) or the [MP-TVSeries forum](https://forum.team-mediaportal.com/forums/my-tvseries.162/). |

## Going back to the official version

Uninstall MP-TVSeries in **MediaPortal Configuration > Extensions**, restore your database backup and install
the [official version](https://github.com/Mediaportal-Plugin-Team/mptvseries/releases).
