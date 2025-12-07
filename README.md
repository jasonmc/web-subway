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
Open the printed local URL. Click **Refresh** to refetch the feed.

## Notes

- The endpoint you pasted includes `access-control-allow-origin: *`, so this works directly in the browser as long as the API key is accepted in CORS (`x-api-key` is sent).
- Feed is decoded client-side via `gtfs-realtime-bindings`; data is limited to Greenpoint Av stop IDs and the next 3 arrivals per direction.
