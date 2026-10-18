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

## What's different from the official version

- Uses TheTVDB's current (v4) service: shows found by name again, titles and episode names in your language.
- Shows matched from ID files in the show folder: `<id>.tvdb`, or Media Center Master's `tvshow.nfo` /
  `series.xml` (tick box on the Online Data tab, on by default).
- Two different shows with the same name (e.g. *Prime Suspect* 1991 and 1973) are kept apart when their
  folders have ID files.
- Community ratings from Trakt for series, seasons and episodes – the Trakt plugin is not needed.
- Episodes left without a show by an earlier failed import are picked up and matched again.
- Files TheTVDB doesn't list (some specials) no longer come back as "new" in every import.
- Artwork: newly added shows always get pictures; missing pictures are replaced.
- Favourites use TheTVDB's current service (removing a favourite is done on thetvdb.com).
- Removed: TheTVDB personal ratings and the Account ID box (TheTVDB no longer offers them).

## Before you install

1. Close MediaPortal and MP-TVSeries Configuration.
2. Back up `C:\ProgramData\Team MediaPortal\MediaPortal\Database\TVSeriesDatabase4.db3`.
3. The first import after installing refreshes every show's details once, so it takes a while.

Version 4.6 is numbered above the official 4.5.1 releases, so MediaPortal treats it as an upgrade. If the
official team releases an update later, uninstall this version first to install theirs.

## Credits

MP-TVSeries was created by the MP-TVSeries team and is maintained by the MediaPortal Plugin Team
(damienhaynes, andrewjswan, ltfearme and contributors). This fork builds on their work and keeps the original
licence.

This community version for TheTVDB v4 is maintained by Mark Willmer.
