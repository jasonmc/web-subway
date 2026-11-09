#!/usr/bin/env bash
set -euo pipefail

OSM_OUT="${1:-data/osm_corridor.osm.pbf}"
PBF_URL="${2:-https://download.bbbike.org/osm/bbbike/NewYork/NewYork.osm.pbf}"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

mkdir -p "$(dirname "$OSM_OUT")"

echo "Downloading $PBF_URL ..." >&2
curl -sSfL -A "web-subway/0.1" "$PBF_URL" -o "$TMP_DIR/region.osm.pbf"

osmium extract -b -74.028,40.635,-73.905,40.755 "$TMP_DIR/region.osm.pbf" -o "$TMP_DIR/bbox.osm.pbf" --overwrite
osmium tags-filter "$TMP_DIR/bbox.osm.pbf" \
  w/natural=coastline \
  wr/natural=water \
  w/waterway=dock \
  wr/leisure=park \
  wr/landuse=cemetery \
  r/ref=G \
  -f pbf,add_metadata=false -o "$OSM_OUT" --overwrite

echo "Wrote $OSM_OUT" >&2
