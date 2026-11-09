#!/usr/bin/env bash
set -euo pipefail

OSM_IN="${1:-data/osm_corridor.osm.pbf}"
HOODS_IN="${2:-data/nyc_neighborhoods.geojson}"
OUT_SVG="${3:-g_corridor.svg}"
HOODS_LAYER="$(basename "$HOODS_IN" .geojson)"

gdal vector pipeline \
  ! read "$OSM_IN" \
  ! sql --dialect sqlite --sql "$(cat <<SQL
      WITH coasts AS (
        SELECT ST_LineMerge(ST_Collect(geometry)) AS m
        FROM lines WHERE other_tags LIKE '%"natural"=>"coastline"%'
      ),
      labels(lat, lon, text, kind, rot) AS (
        VALUES
          (40.7180, -73.9690, 'EAST RIVER', 'water', -52),     (40.7358, -73.9430, 'NEWTOWN CREEK', 'water', 14),
          (40.6745, -73.9922, 'GOWANUS CANAL', 'water', -68),  (40.6750, -74.0185, 'UPPER BAY', 'water', 0),
          (40.6625, -73.9690, 'PROSPECT PARK', 'park', 0),     (40.6525, -73.9905, 'GREEN-WOOD', 'park', 0),
          (40.7208, -73.9535, 'McCARREN PK', 'park', 0),       (40.7445, -73.9490, 'LONG ISLAND CITY', 'hood', 0),
          (40.7295, -73.9535, 'GREENPOINT', 'hood', 0),        (40.7115, -73.9565, 'WILLIAMSBURG', 'hood', 0),
          (40.6980, -73.9735, 'NAVY YARD', 'hood', 0),         (40.6905, -73.9745, 'FORT GREENE', 'hood', 0),
          (40.6865, -73.9625, 'CLINTON HILL', 'hood', 0),      (40.6870, -73.9410, 'BED-STUY', 'hood', 0),
          (40.6990, -73.9265, 'BUSHWICK', 'hood', 0),          (40.6928, -73.9875, 'DOWNTOWN BK', 'hood', 0),
          (40.6960, -73.9955, 'BK HEIGHTS', 'hood', 0),        (40.6845, -73.9845, 'BOERUM HILL', 'hood', 0),
          (40.6865, -73.9965, 'COBBLE HILL', 'hood', 0),       (40.6785, -73.9965, 'CARROLL GDNS', 'hood', 0),
          (40.6755, -74.0085, 'RED HOOK', 'hood', 0),          (40.6740, -73.9860, 'GOWANUS', 'hood', 0),
          (40.6695, -73.9795, 'PARK SLOPE', 'hood', 0),        (40.6765, -73.9655, 'PROSPECT HTS', 'hood', 0),
          (40.6710, -73.9440, 'CROWN HEIGHTS', 'hood', 0),     (40.6540, -73.9785, 'WINDSOR TERRACE', 'hood', 0),
          (40.6445, -73.9745, 'KENSINGTON', 'hood', 0),        (40.6485, -73.9565, 'FLATBUSH', 'hood', 0),
          (40.6500, -74.0065, 'SUNSET PARK', 'hood', 0),       (40.7280, -73.9840, 'EAST VILLAGE', 'manh', 0),
          (40.7165, -73.9885, 'LOWER EAST SIDE', 'manh', 0),   (40.7090, -74.0060, 'LOWER MANHATTAN', 'manh', 0)
      )
      SELECT 'land' AS layer, 0.0 AS min_area, '' AS text, '' AS kind, 0 AS rot,
        MakePolygon(AddPoint(AddPoint(AddPoint(ST_GeometryN(m, 3), MakePoint(-73.937, 40.795, 4326)), MakePoint(-73.990, 40.795, 4326)), StartPoint(ST_GeometryN(m, 3)))) AS geometry
      FROM coasts
      UNION ALL
      SELECT 'land', 0.0, '', '', 0,
        MakePolygon(AddPoint(AddPoint(AddPoint(AddPoint(ST_GeometryN(m, 1), MakePoint(-74.040, 40.625, 4326)), MakePoint(-73.885, 40.625, 4326)), MakePoint(-73.885, 40.795, 4326)), StartPoint(ST_GeometryN(m, 1))))
      FROM coasts
      UNION ALL
      SELECT 'gov_island', 0.0, '', '', 0, ST_BuildArea(m) FROM coasts
      UNION ALL
      SELECT
        CASE WHEN name IN (
          'Long Island City','Greenpoint','Williamsburg','South Williamsburg','East Williamsburg',
          'Bedford Stuyvesant','Clinton Hill','Fort Greene','Navy Yard','Downtown Brooklyn',
          'Boerum Hill','Cobble Hill','Carroll Gardens','Gowanus','Park Slope','Greenwood Heights',
          'Windsor Terrace','Kensington','Flatbush'
        ) THEN 'hood_corridor' ELSE 'hood_bg' END,
        0.0, '', '', 0, geometry
      FROM "${HOODS_IN}"."${HOODS_LAYER}"
      WHERE ST_Intersects(geometry, BuildMbr(-74.025, 40.63, -73.9, 40.76, 4326))
        AND name NOT IN ('Prospect Park', 'Green-Wood Cemetery', 'Calvary Cemetery')
      UNION ALL
      SELECT 'water_inlet', 6.0, '', '', 0, geometry FROM multipolygons
      WHERE osm_way_id IS NOT NULL AND (natural = 'water' OR other_tags LIKE '%"waterway"=>"dock"%')
        AND (other_tags IS NULL OR (other_tags NOT LIKE '%"water"=>"wastewater"%' AND other_tags NOT LIKE '%"water"=>"fountain"%'))
      UNION ALL
      SELECT 'park', 45.0, '', '', 0, geometry FROM multipolygons WHERE leisure = 'park' OR landuse = 'cemetery'
      UNION ALL
      SELECT 'lake', 12.0, '', '', 0, geometry FROM multipolygons
      WHERE osm_id IS NOT NULL AND natural = 'water' AND (other_tags IS NULL OR other_tags NOT LIKE '%"water"=>"river"%')
      UNION ALL
      SELECT 'track', 0.0, '', '', 0, ST_GeometryN(ST_LineMerge(geometry), 1) FROM multilinestrings WHERE osm_id = '9699111'
      UNION ALL
      SELECT 'label', 0.0, text, kind, rot, MakePoint(lon, lat, 4326) FROM labels
SQL
    )" \
  ! reproject -s EPSG:4326 -d EPSG:3857 \
  ! simplify 15 \
  ! sql --dialect sqlite --sql "
      WITH vp(b) AS (
        SELECT ST_Transform(BuildMbr(-74.03, 40.642, -73.909, 40.74835, 4326), 3857)
      ),
      scaled AS (
        SELECT layer, min_area, text, kind, rot,
          ScaleCoords(ShiftCoords(geometry, -MbrMinX(b), -MbrMaxY(b)), 440.0 / (MbrMaxX(b) - MbrMinX(b))) AS px_geom
        FROM \"SELECT\", vp WHERE geometry IS NOT NULL
      )
      SELECT
        layer,
        CASE WHEN layer = 'label' THEN text ELSE AsSVG(px_geom, 0, 1) END AS d,
        CASE WHEN layer = 'label' THEN printf('%.1f', ST_X(px_geom)) ELSE '' END AS x,
        CASE WHEN layer = 'label' THEN printf('%.1f', -ST_Y(px_geom)) ELSE '' END AS y,
        kind,
        rot
      FROM scaled
      WHERE min_area = 0.0 OR ST_Area(px_geom) >= min_area
    " \
  ! write -f CSV /vsistdout/ --lco HEADER=NO --lco SEPARATOR=TAB --lco STRING_QUOTING=IF_NEEDED \
| awk -F'\t' '
  function path(cls, d, ind) {
    return (ind ? ind : "    ") "<path" (cls ? " class=\"" cls "\"" : "") " d=\"" d "\"/>\n"
  }
  $1 == "land"          { clip = clip path("", $2, "      "); land = land path("vec-land-fill", $2); shore = shore path("vec-land", $2) }
  $1 == "gov_island"    { clip = clip path("", $2, "      "); gov  = gov  path("vec-hood-bg", $2);   shore = shore path("vec-land", $2) }
  $1 == "hood_bg"       { hood_bg = hood_bg path("vec-hood-bg", $2) }
  $1 == "hood_corridor" { hood_fg = hood_fg path("vec-hood-corridor", $2) }
  $1 == "water_inlet"   { inlets  = inlets  path("", $2) }
  $1 == "park"          { parks   = parks   path("vec-park", $2) }
  $1 == "lake"          { lakes   = lakes   path("", $2) }
  $1 == "track"         { tracks  = tracks  path("osm-track-casing", $2, "  ") path("osm-track-line", $2, "  ") }
  $1 == "label" {
    rot = ($6 != 0) ? sprintf(" transform=\"rotate(%d %s %s)\" ", $6, $3, $4) : " "
    labels = labels sprintf("    <text x=\"%s\" y=\"%s\"%sclass=\"vec-label vec-label-%s\">%s</text>\n", $3, $4, rot, $5, $2)
  }
  END {
    printf "<svg class=\"osm-svg\" viewBox=\"0 0 440 510\" xmlns=\"http://www.w3.org/2000/svg\" aria-hidden=\"true\">\n" \
           "  <rect class=\"vec-water-bg\" width=\"440\" height=\"510\"/>\n" \
           "  <defs>\n" \
           "    <clipPath id=\"osm-clip-land\">\n%s    </clipPath>\n" \
           "  </defs>\n" \
           "  <g class=\"vec-land-group\">\n%s%s  </g>\n" \
           "  <g class=\"vec-hoods\" clip-path=\"url(#osm-clip-land)\">\n%s%s  </g>\n" \
           "  <g class=\"vec-water-inlets\">\n%s  </g>\n" \
           "  <g class=\"vec-parks\">\n%s  </g>\n" \
           "  <g class=\"vec-lakes\">\n%s  </g>\n" \
           "  <g class=\"vec-shorelines\">\n%s  </g>\n" \
           "%s" \
           "  <g class=\"vec-labels\">\n%s  </g>\n" \
           "</svg>\n",
           clip, land, gov, hood_bg, hood_fg, inlets, parks, lakes, shore, tracks, labels
  }
' > "$OUT_SVG"
