# Vibe Weather branding: design

Date: 2026-10-16 · App: VibeWeather (.NET MAUI Blazor, Android only: `net10.0-android`, min API 24), UI in Czech

## Goal

Replace every .NET MAUI template default the user can see with the Vibe Weather brand: app icon, themed icon, splash, notification icons and colours, display name, template leftovers and the default Blazor error bar.

The brand source is design **1b "Orb"** from Claude Design, file `Vibe Weather Brand.dc.html` (from the user's desktop). Copy it into the repo as `doc/brand/Vibe Weather Brand.dc.html` for reference.

## Out of scope

- The Android package ID stays `com.companyname.vibeweather`. It will be decided when the app is published.
- Project, namespace and assembly names stay `VibeWeather`.
- Notification titles, texts, channels and tap actions don't change.
- iOS, Mac Catalyst and Windows are not built, so they're not touched.

## Brand geometry and colours

All shapes are in a 100×100 box, exactly as in design 1b.

| Part | Shape |
|---|---|
| Sun | `circle cx=60 cy=40 r=29`, vertical linear gradient from top `#F7BC95` (oklch 0.84 0.085 55) to bottom `#EED49F` (oklch 0.88 0.075 85) |
| Cloud halo (cut) | `rect x=15 y=56 w=70 h=32 rx=16` + `circle 40,57 r21` + `circle 63,63 r16`, in the background colour |
| Cloud | `rect x=19 y=60 w=62 h=24 rx=12` + `circle 40,57 r17` + `circle 63,63 r12` |

| Token | Light | Dark |
|---|---|---|
| Background (paper / ink) | `#F1EFEA` | `#0E1013` |
| Cloud | `#FFFFFF` | `#ECEDEF` |
| Text (ink) | `#15171C` | `#ECEDEF` |

The loading bar is a gradient through the cold-to-hot scale: `#A4C6FC` (260), `#81D6CB` (185), `#EBB989` (65), `#F6B0A6` (28), all at oklch L 0.82, C 0.085.

The wordmark is "**Vibe** Weather": Bricolage Grotesque, "Vibe" at weight 600 and "Weather" at 300, letter-spacing −0.03em.

Android resources need hex colours. CSS keeps the oklch values from the design.

**How the art is produced.** All Android artwork is authored as SVG and turned into density PNGs by MAUI's resizetizer:
- the app icon via `MauiIcon`
- the splash via `MauiSplashScreen`
- everything else via `MauiImage` in `Resources/Images`, where each SVG's `width`/`height` is its size in dp

A test build on 2026-10-16 showed the resizetizer (MAUI 10.0.110) renders `<mask>`, `<linearGradient>` and transparent cut-outs correctly. **SVG filters crash it** (MAUIR0001, a missing `SKImageFilter.CreateMatrixConvolution`), so the design's soft cloud shadow is drawn as a copy of the cloud: ink `#15171C` at 6 % opacity, offset 2.5 units down.

## 1. App icon and themed icon

- `Resources/AppIcon/appicon.svg` is the background: solid `#F1EFEA`.
- `Resources/AppIcon/appiconfg.svg` is the foreground: the Orb (light variant) in a 456-unit canvas with `translate(118 113.6) scale(2.2)`. That gives the Orb about the share of the tile it has in the design (×3 looked oversized on the emulator), well inside the safe circle.
- The `MauiIcon` colour changes from `#512BD4` to `#F1EFEA`.
- `ApplicationTitle` becomes `Vibe Weather`.

**Themed icon.** MAUI 10 writes the coloured foreground as the `<monochrome>` layer. The opaque halo would merge the sun and cloud into one blob. To fix that:

- Add `Resources/Images/appicon_monochrome.svg` (108×108 dp): the sun in white, masked by a halo widened by 3 units so the gap is **transparent**, plus the white cloud. It uses the same placement as the foreground, scaled to 108: `translate(27.95 26.9) scale(0.5211)`.
- Add `Platforms/Android/Resources/mipmap-anydpi-v26/appicon_themed.xml` and `appicon_themed_round.xml`. Each is an `<adaptive-icon>` that uses MAUI's generated `@mipmap/appicon_background` and `@mipmap/appicon_foreground`, plus `@drawable/appicon_monochrome`.
- Add `Platforms/Android/Resources/values/` aliases `appicon_themed` and `appicon_themed_round` (type mipmap), pointing to `@mipmap/appicon` and `@mipmap/appicon_round` for API 24–25.
- `AndroidManifest.xml`: set `android:icon="@mipmap/appicon_themed"` and `android:roundIcon="@mipmap/appicon_themed_round"`.

There is one launcher icon, the light tile. Dark mode is handled by the themed icon and the splash.

## 2. Splash (approach A: static system splash, then the in-app animation)

### System splash (before app code runs; follows the **system** night mode)

**The system splash shows only the gradient sun**, with no cloud, so the handoff can't blink. The in-app overlay starts with the sun already in place and animates the rest. (The user chose this on 2026-10-16.)

- `Resources/Splash/splash.svg`: the gradient sun. The `MauiSplashScreen` colour changes from `#512BD4` to `#F1EFEA`. This keeps MAUI's generated resources valid; our own theme is what's actually shown.
- `Resources/Images/vw_splash_sun.svg` (288×288 dp, the size Android 12+ uses for a splash icon without an icon background): the sun at `cx=156 cy=132 r=34.8`. That's the Orb's 100-unit box drawn at 120 dp, centred, which matches the overlay.
- New `Platforms/Android/Resources/drawable/vw_splash.xml`: a layer-list of `@color/vw_splash_bg` plus `@drawable/vw_splash_sun` (288 dp, centred). API 24–30 shows this as the window background.
- New `Platforms/Android/Resources/values/styles.xml` with `VibeWeather.SplashTheme`, whose parent is `Maui.SplashTheme`:
  - `android:windowBackground` = `@drawable/vw_splash`
  - `android:windowSplashScreenBackground` = `@color/vw_splash_bg` (API 31+)
  - `android:windowSplashScreenAnimatedIcon` = `@drawable/vw_splash_sun` (API 31+)
- `values/colors.xml` sets `vw_splash_bg` = `#F1EFEA`, and `values-night/colors.xml` sets it to `#0E1013`. The sun is the same in both themes.
- `MainActivity`: `Theme = "@style/VibeWeather.SplashTheme"`. MAUI still switches to the main theme in `OnCreate`, because `maui_splash` is inherited.

### In-app animated splash (overlay in `wwwroot/index.html`)

- A full-screen `#vw-splash` element, before `#app`, containing:
  - the Orb SVG (120×120 px, centred on the screen)
  - the wordmark (24 px, 14 px below the Orb)
  - the loading bar (56 px wide, 56 px from the bottom)
- The CSS (in `app.css`) is based on 1b's motion. Everything plays **once** and ends in its final state:
  - the sun is static, already in place
  - the cloud slides in from 22 px to the left (0.9 s, `cubic-bezier(.2,.8,.2,1)`, starting at 0.1 s)
  - the wordmark fades up from 8 px (0.7 s, starting at 0.45 s)
  - the bar fills (0.9 s, starting at 0.5 s)
- Orb size and position match the system splash's sun.
  - The page uses `SafeAreaEdges="Container"`, so the WebView sits between the status and navigation bars, and its centre was about 13 dp lower than the screen's on the emulator. This was fixed during verification:
    - `MainActivity`'s insets listener stores `(top bar − bottom bar) / 2` in dp in `Services/SplashGeometry.ShiftDp`.
    - `SplashHandoff` saves it to `localStorage['vw-splash-shift']`, and the head script sets it as `--vw-splash-shift`.
    - The Orb and wordmark are moved up by that amount.
    - Only the very first start after an install can still jump.
  - The WebView is white until `index.html` paints, which blinked the sun out for about 0.3 s. Also fixed during verification:
    - `MainPage` makes the WebView transparent (`BlazorWebViewInitialized`).
    - It keeps the same `vw_splash_sun` image (288 dp, moved up by `ShiftDp`) behind it, so the sun stays on screen through the whole handoff.
- **Theme before the first paint:**
  - A small inline script in `<head>` reads `localStorage['vw-theme']` (`light` | `dark` | `auto`, wrapped in try/catch).
  - On `auto` or a missing value, it uses `matchMedia('(prefers-color-scheme: dark)')`.
  - It sets `data-theme` on `<html>`, and the overlay colours follow it.
- **Writing the theme:** a new `Components/Shared/SplashHandoff.razor`, rendered next to the router in `Routes.razor`, calls `vibeWeather.saveTheme(mode)`. The mode is `ThemeService.Mode` in lower case. The call happens after the first render and on every `Theme.Changed`.
- **Dismissal:** after the first render, `SplashHandoff` also calls `vibeWeather.splashDone()`.
  - The overlay fades out over 250 ms, at the later of that call and 1.4 s after page start (`performance.now()`), so the bar finishes, then removes itself.
  - If the first render takes longer, the overlay waits, with the bar full.
- The overlay appears only on a cold start (page load), never on resume.

**Known limitations:**
- If the system is dark and the app is forced to Light, or the other way round, the system splash briefly shows the system's theme before the overlay. Android gives no way to avoid this.
- Android 16 automatically bundles two or more of the app's notifications into one group. The group header then uses the system accent, not the per-kind colour. Each notification inside keeps its own icon and picture, and a notification on its own shows its colour.

## 3. Notifications

### Small icons (status bar and header): white on transparent, one per type, in 1b shapes

These are SVGs in `Resources/Images` (24×24 dp). They replace the current vector XMLs in `Platforms/Android/Resources/drawable`, which are deleted, and keep the same names:

| Drawable | Type | Shape |
|---|---|---|
| `ic_stat_sun` | Morning summary | The Orb: sun with a transparent halo gap (cut expanded by 3 units so it reads at 24 dp) plus the cloud |
| `ic_stat_precip` | Precipitation alert | The 1b cloud (moved up) plus three slanted round drops |
| `ic_stat_warning` | Severe warning | A rounded triangle with the "!" cut out |
| `ic_stat_place` | Zone arrival | A pin with a round hole |

### Accent per type (`SetColor`): colours from the 1b scale

| Type | Colour |
|---|---|
| Severe warning | clay `#C16048` (oklch 0.6 0.13 35) |
| Precipitation | blue `#507CB5` (oklch 0.58 0.1 255) |
| Morning summary | gold `#CD9A50` (oklch 0.72 0.11 75) |
| Zone arrival | teal `#368985` (oklch 0.58 0.08 190) |

These are defined as `@color/vw_notif_*` in `values/colors.xml`.

### Large icons (`SetLargeIcon`): transparent background, no tile

| Drawable | Shape |
|---|---|
| `ic_large_sun` | The Orb: gradient sun with a transparent halo gap; cloud `#BBC5D1` |
| `ic_large_precip` | Cloud `#BBC5D1` with three drops `#4D97DE` |
| `ic_large_warning` | Rounded triangle with the Orb gradient and an ink `#15171C` "!" |
| `ic_large_place` | Pin with the Orb gradient and a round transparent hole |

These are SVGs in `Resources/Images` (64×64 dp), loaded with `BitmapFactory.DecodeResource`.

The new `Platforms/Android/NotificationBrand.cs` has:
- `enum NotificationKind { MorningSummary, Precipitation, Warning, Arrival }`
- an extension method `Brand(this NotificationCompat.Builder, Context, NotificationKind)` that sets the small icon, the accent colour and the large icon in one place

Each of the four builders replaces its `.SetSmallIcon(...)` with `.Brand(context, NotificationKind.X)`. Nothing else changes in them.

**Preview for verification (Debug builds only).** `Platforms/Android/NotificationPreviewReceiver.cs` (`#if DEBUG`) is an exported receiver with the fixed Java name `vibeweather.debug.NotificationPreview`. On a broadcast it posts all four notifications through the real code paths, or only one with `--es kind summary|precip|warning|arrival`. It uses sample data:
- the morning-summary fallback ("Dobré ráno")
- a precipitation alert for Praha
- a severe warning "Silné bouřky" for Praha
- an arrival at Praha

To make the morning summary reachable, `MorningSummaryReceiver.Show` changes from `static` private to `internal static`.

## 4. Names, leftovers and the error bar

- **Display name "Vibe Weather":**
  - `ApplicationTitle`
  - `Window.Title` in `App.xaml.cs`
  - `<title>` in `index.html`
  - the export title in `ErrorLogPage.razor` (`"Protokol chyb Vibe Weather"`)
- **Delete:**
  - `Resources/Images/dotnet_bot.svg` and its `MauiImage Update` line
  - `Resources/Fonts/OpenSans-Regular.ttf`, the then-empty `Resources/Fonts` folder, the `MauiFont` item in the csproj, and the `ConfigureFonts`/`AddFont` call in `MauiProgram.cs`. The Blazor UI loads its own web fonts from `wwwroot/fonts`.
  - `Resources/Raw/AboutAssets.txt`
  - `wwwroot/lib/bootstrap/`
  - `Components/Pages/Counter.razor`, `Components/Pages/Weather.razor`, `Components/Layout/NavMenu.razor` and `NavMenu.razor.css`
- **Blazor error bar (`#blazor-error-ui`):**
  - Text: "Došlo k neočekávané chybě." with a link "Načíst znovu" and a dismiss ✕.
  - Style: `--surf` background, `--ink` text, `--line` top border, `--font-sans`. Remove `color-scheme: light only` and `lightyellow`.
  - Remove the unused `.blazor-error-boundary` rules.

## Verification

1. `dotnet build VibeWeather -f net10.0-android -c Debug` must report 0 errors.
2. Deploy to emulator `MAUI_Emulator_API_36` with `-t:Run` and `RuntimeIdentifier=android-x64`, then take screenshots of:
   - the launcher icon, with themed icons off and on (Wallpaper & style → Themed icons)
   - the cold-start system splash, then the overlay animation, with the system in light mode and in dark mode
   - all four notifications in the shade and the status bar, posted by `adb shell am broadcast -n com.companyname.vibeweather/vibeweather.debug.NotificationPreview`
3. Before reporting done, `grep` the project for `512BD4`, `dotnet_bot`, `OpenSans` and `bootstrap`; none should remain.

## Revisions after testing on the phone (2026-10-16)

- **Launcher tile.**
  - The paper tile was too light on the phone's home screen, and pure ink was too black.
  - `appicon.svg` is now a dusk-blue vertical gradient: `#3A4E6B` to `#243047` (oklch 0.42 0.055 258 to 0.31 0.045 262).
  - The foreground's halo uses the same gradient, mapped into the Orb group's coordinates.
  - The cloud is `#F4F5F7`, and the `MauiIcon` colour is `#0E1013`.
- **Warning and pin pictures.** These use a deeper warm gradient: `#EF945F` to `#F2BD68` (oklch 0.75 0.13 50 to 0.83 0.12 78).
- **Precipitation pictures follow the kind of precipitation.**
  - `PrecipAlert` has a new `Type` (the spell's `PrecipType`), and `NotificationBrand.PrecipPicture` maps it to a picture.
  - All four share the grey-blue cloud:

    | Picture | Kind |
    |---|---|
    | `ic_large_rain` | blue drops `#4D97DE` (replaces `ic_large_precip`) |
    | `ic_large_icerain` | teal drops on a line of ice, `#00A0A6` (oklch 0.64 0.11 200) |
    | `ic_large_sleet` | two drops around a flake |
    | `ic_large_snow` | three flakes, `#5AAAC9` (oklch 0.70 0.09 225) |

  - The debug preview takes `--es type rain|icerain|sleet|snow`.
