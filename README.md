# G train arrivals

A Vite + F# (Fable/Feliz) page that reads the MTA GTFS-RT G train feed and shows the next arrivals grouped by direction. It selects the nearest G train station using device location, with a station dropdown for manual selection.

## Setup

1. Install deps:
   ```sh
   cd web-subway
   npm install
   ```
2. Optionally override the feed URL in a `.env` file:
   ```
   VITE_MTA_FEED_URL=https://api-endpoint.mta.info/Dataservice/mtagtfsfeeds/nyct%2Fgtfs-g
   ```

The default feed works without an API key. `VITE_` variables are included in the browser bundle, so do not use them for secrets.

## Run

```sh
npm run dev
```
Open the printed local URL. On a fresh visit, allow device location to automatically select the nearest station in the dropdown. Arrivals load while location is pending; if permission is denied, location times out, or geolocation is unavailable, Greenpoint Av / G26 remains selected. Geolocation requires HTTPS or localhost. A station in the URL hash (`#G26`) takes priority, so reloads/bookmarks preserve your selection. Choosing a station manually also takes priority over a pending location request.

Each feed response contains all stations and is cached as a typed snapshot. Station changes select arrivals locally from that snapshot without another request. A response that arrives after a station change is applied to the current selection. Scheduled refreshes continue every five seconds independently of station selection.

Station IDs, names, and coordinates are bundled together in `all_stops.csv` (`stop_id`, `stop_name`, `stop_lat`, `stop_lon`). Coordinates come from the [MTA Subway Stations dataset](https://data.ny.gov/Transportation/MTA-Subway-Stations/39hk-dx4f) (retrieved October 23, 2026). Stops with missing coordinates still have names available but are skipped for nearest-station selection. Nearest-station selection runs on the device using straight-line distance among the stations supported by this G train feed; location is not sent to the app's server or MTA.

The stops CSV is embedded directly in the single HTML build. Enable HTTP compression on your web server to reduce transfer size; the browser handles decompression automatically.

## Styling

Inter comes from the locked nixpkgs `inter` package under the SIL Open Font License. Nix uses HarfBuzz's `hb-subset` and `woff2_compress` to subset both upright and italic fonts to Basic Latin, accented Latin, and punctuation. Both retain the original weight and optical-size axes, preserving how the font adapts to different text sizes. Changing labels or weights does not require changing the subset. `nix develop` links these generated files as `inter-fonts`; Nix builds supply the same files automatically. The UI and map labels share this font, and the header train and direction arrows use inline SVGs so their appearance does not depend on the operating system's emoji font. Vite embeds the fonts and their license in the single HTML output; no font service is needed at runtime.

Standard UI components use Feliz.Bulma 5 and bundled Bulma 1 CSS. Use the Feliz.Bulma API in `src/Ui.fs` for cards, form fields, notifications, and columns. `src/style.css` retains the compact feed-age indicator and wider map column, alongside the custom arrival rows and map/animation styles. Other UI styling follows Bulma defaults. `src/bulma.scss` imports only the Bulma modules the UI uses and its system-driven light/dark themes; add new component modules there as needed. This stylesheet is imported before the custom styles in `src/Main.fs`. Dark mode follows `prefers-color-scheme`, including the map palette and native form controls, and responds when the system preference changes. All CSS remains embedded in the single-file build.

At widths of 768px and below, an Arrivals/Map button group shows one view at a time, defaulting to Arrivals. Larger screens show both columns and hide the switch. Switching views preserves station selection and live updates; resizing back to mobile preserves the chosen view for the current page session.

## Tests

- `nix fmt` formats the F# app and tests with Fantomas from locked nixpkgs. `nix fmt -- --check` checks without changing files. `.editorconfig` sets basic whitespace rules; other formatting follows Fantomas defaults. `.fantomasignore` excludes generated build and Fable sources.
- `npm run typecheck`
- `npm test` builds the production page, compiles `tests/Tests.fsproj` with Fable, and runs Vitest. `nix flake check` executes the same workflow and checks F# formatting.
- `npm run test:watch` builds the page once, then watches the F# sources and reruns tests. The standalone-page smoke test uses that initial build; run `npm test` again to verify a fresh production bundle.
- `npm run test:e2e` remains an alias for `npm test`.

Tests follow Feliz's Vitest and React Testing Library setup, using the published F# bindings. `App.test.fs` renders the app view with its actual Elmish initialization/update functions, mocked MTA responses and geolocation, and Vitest's controlled clock. `Feed.test.fs` tests decoding, direction detection, interpolation, time formatting, and generated station coordinates directly.

`Bundle.test.fs` boots the actual inlined script from `dist/index.html` in isolated jsdom pages. It checks production metadata and startup, destination grouping, dropdown and map station selection, URL and late-geolocation precedence, countdowns, failed-refresh recovery, train movement, and the view switch. The pages use Vitest's controlled clock and intervals; async timeouts remain real. These are production-bundle integration tests, without a browser layout engine or live MTA network requests. Test dependencies are excluded from the shipped page.

`src/App.fs` holds the app's initialization and update functions; `src/Main.fs` is the browser entry point. The test project shares the app source files while excluding that entry point, so importing the code does not start a second app.

`App.subscriptions` declares the five-second feed refresh and one-second clock tick with stable Elmish subscription IDs. `Timers.every` returns an `IDisposable` that clears its interval. Both the production program and the test component use those subscriptions, so model updates retain existing timers and component unmounting disposes them.

`src/Time.fs` defines seconds and milliseconds as F# units of measure. Feed timestamps, train interpolation, and timer intervals retain these units internally, with explicit conversions at protobuf and JavaScript boundaries. Arrivals are UTC `DateTimeOffset` instants; countdowns and headways use elapsed `TimeSpan` values. Clock times are formatted with the browser's built-in `Intl.DateTimeFormat` in `America/New_York`, including daylight-saving changes, regardless of the device's timezone. `Time.test.fs` covers UTC construction, New York display, and elapsed time across DST transitions.

`src/Geography.fs` defines distinct latitude/longitude measures in decimal degrees, plus a named coordinate record for device location. Station data, map projections, and estimated trains use these measures so swapped axes fail typechecking. The browser API still supplies raw numbers, which are assigned their coordinate roles at that boundary and checked for finite, valid ranges.

## GitHub Pages

The workflow in `.github/workflows/pages.yml` tests and builds the site with the locked Nix flake, then deploys it to GitHub Pages on every push to `master`. It can also be started manually from the Actions tab. Nix generates the map SVG and supplies Node, .NET, and Fable, so the runner needs no separate npm or .NET setup.

One-time setup:

1. In [the repository's Pages settings](https://github.com/jasonmc/web-subway/settings/pages), select **GitHub Actions** under **Build and deployment → Source**.
2. Commit and push the workflow to `master`.
3. Open **Actions → Deploy to GitHub Pages** and wait for the deployment to finish.

The canonical address is <https://g.apps.jasonmc.net/>. Scripts, styles, station data, the map, favicons, and the manifest with its app icons are embedded in `index.html`. The Apple touch icon and share image remain separate files. Pages provides HTTPS for device location. The public MTA feed is fetched directly by the browser; no API key or repository secret is needed.

## Icons, installation, and sharing

`assets/icon.typ`, `assets/app-icon.typ`, and `assets/share.typ` define the favicon, home-screen icons, and 1200 × 630 link preview in Typst + CeTZ. They share the Inter G badge and palette in `assets/brand.typ`. The favicon is a green circle with a white G; home-screen icons place the badge on a navy background with room for platform masking.

The Nix `branding` package supplies Typst, CeTZ and its dependencies from locked nixpkgs, and the same Inter font used by the app. System fonts are disabled. Typst exports self-contained SVGs with outlined text, then librsvg renders the PNG sizes. Run `nix build .#branding` to generate the artwork. `nix develop` links it into `public/branding`. Vite embeds the favicons and `assets/site.webmanifest` with its PNG icons as data URLs. Only `branding/apple-touch-icon.png` and `branding/share.png` remain alongside `index.html` in the distribution; deploy those too for Apple home-screen icons and link previews.

The manifest provides the app name, icons, and standalone window settings for home-screen installation. Its launch URL, scope, and ID are made absolute using `siteUrl` in `vite.config.ts`, because the embedded manifest has no directory against which to resolve relative URLs. The same setting supplies the canonical link and Open Graph page/image URLs. This adds no service worker or offline cache; arrivals still require a connection. Open Graph metadata provides a static preview for iMessage and other link sharing. Update `siteUrl` if the public address changes. URL fragments such as `#G26` share the same preview. Embedded-manifest installation still needs verification on an actual iPhone; the Apple touch icon remains external for compatibility.

## Nix build (single-file, no CDN)

There is a standalone flake in this folder that builds the static site (`flake.nix`). The Vite build is configured with `vite-plugin-singlefile`, so the output `dist/index.html` contains all JS/CSS inlined with no CDN. Favicons and the manifest with app icons are also embedded. Only the Apple touch icon and share preview are separate files.

1. Ensure you have Node/npm available (needed only to produce `package-lock.json` and the npm dependency hash).
2. From `web-subway/`, run `npm install --package-lock-only` once to generate `package-lock.json`.
3. After updating npm dependencies, replace `npmDepsHash` in `flake.nix` with `pkgs.lib.fakeHash`, then run:
   ```sh
   nix build .#default --print-build-logs
   ```
   Nix will fail with a hash mismatch showing the actual dependency hash. Set `npmDepsHash` in `flake.nix` to that value.
4. Re-run `nix build .#default` to produce a bundled `dist/` and a final `$out` containing `index.html` and the two external images with no CDN references.

The flake exposes:
- `packages.default`: the bundled static site
- `packages.branding`: the generated favicon, home-screen icons, and share preview
- `devShells.default`: .NET + Fable + Node + npm + vite for local dev

## Notes

- The browser fetches the public MTA feed directly using CORS and decodes it with `gtfs-realtime-bindings`.
- The page shows the selected G train station's next 3 boardable arrivals per direction and refreshes every 5 seconds. Trips ending at the selected station are omitted, including short turns. Cards show a direction arrow and the destination from each trip's final non-skipped stop in the MTA feed. Trains with different destinations get separate cards, including short turns and reroutes; station names come from the bundled stops data.

## License

The application code is available under the [MIT License](LICENSE). Station data is sourced from the MTA, as described above.
