# Homepage regression checks

From the repository root:

```powershell
dotnet run --project Tools/Horizon.HomeChecks/Horizon.HomeChecks.csproj --configuration Debug -p:Platform=x64
```

The runner uses its own offscreen WPF window, synthetic shortcuts and sun times,
and test-output settings. It does not start the browser or load the user's saved
stack. The browser assembly builds into this tool's ignored `bin/browser` folder.

Coverage: reference moon phases; numerical ranges; city time zones and DST;
sunrise, sunset, midnight and polar events; NOAA parsing and G-scale boundaries;
widget settings and catalog validation; thirty interrupted folder cycles; both
stack folder modes; resize interruptions; homepage bounds at six viewport sizes;
expanded glass registration and the blur setting; compact stack restoration;
and inactivity layout restoration.

Use `-- --weather-only` for the weather regression checks: transparent static
condition icons, wheel events over embedded astronomy cards, passive card clicks,
and stable readable text after light/dark changes, data refreshes and contrast
sweeps. Light/dark card renders are saved under `weather-artifacts` in the runner's
output folder.

PNG renders use a synthetic wallpaper and are written under the runner's output
folder in `layout-artifacts`. Use `-- --layout-only` to run just the layout and
glass checks. Interactive testing on real monitors remains useful for mixed-DPI
movement, visual motion quality, and the user's own wallpaper and bookmark data.
