# Greenpoint subway arrivals (browser)

Minimal Vite + TypeScript page that pulls the MTA GTFS-RT G train feed, filters Greenpoint Av stop IDs (`G22*`), and shows the next arrivals grouped by direction.

## Setup

1. Install deps:
   ```sh
   cd web-subway
   npm install
   ```
2. Provide your MTA key in a `.env` file (or shell export):
   ```
   VITE_MTA_API_KEY=your-key-here
   # Optional: override feed URL
   # VITE_MTA_FEED_URL=https://api-endpoint.mta.info/Dataservice/mtagtfsfeeds/nyct%2Fgtfs-g
   ```

## Run

```sh
npm run dev
```
Open the printed local URL. Pick a station from the dropdown (defaults to Greenpoint Av / G26). The current station is kept in the URL hash (`#G26`) so reloads/bookmarks preserve your selection. Click **Refresh** to refetch the feed.

Note: the stops list is shipped compressed (gzip) and decompressed client-side; requires a browser with `DecompressionStream` support (modern Chrome/Edge/Safari).

## Tests

- `npm run typecheck`
- `npm run test:e2e` builds the bundle and runs a Node-based harness against the inlined `dist/index.html` output (includes stop selection). `nix flake check` executes this.

## Nix build (single-file, no CDN)

There is a standalone flake in this folder that builds the static site (`flake.nix`). The Vite build is configured with `vite-plugin-singlefile`, so the output `dist/index.html` contains all JS/CSS inlined (no CDN and no additional assets).

1. Ensure you have Node/npm available (needed only to produce `package-lock.json` and the npm dependency hash).
2. From `web-subway/`, run `npm install --package-lock-only` once to generate `package-lock.json`.
3. Compute the npm dependency hash by running:
   ```sh
   nix build .#default --print-build-logs
   ```
   Nix will fail with a message showing the expected `npmDepsHash`. Replace `pkgs.lib.fakeHash` in `web-subway/flake.nix` with that value.
4. Re-run `nix build .#default` to produce a fully bundled `dist/` and a final `$out` containing `index.html` and JS/CSS with no CDN references.

The flake exposes:
- `packages.default`: the bundled static site
- `devShells.default`: Node + npm + vite for local dev

## Notes

- The endpoint you pasted includes `access-control-allow-origin: *`, so this works directly in the browser as long as the API key is accepted in CORS (`x-api-key` is sent).
- Feed is decoded client-side via `gtfs-realtime-bindings`; data is limited to Greenpoint Av stop IDs and the next 3 arrivals per direction.
