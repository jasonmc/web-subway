{
  description = "Bundled Greenpoint subway arrivals page (no CDN)";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs { inherit system; };
        src = pkgs.lib.cleanSource ./.;
        npmDepsHash = "sha256-dbdbZMD3uiidJ4JLsdH1lPswiXygQdnW0zufh8/uyj4=";
        npmInstallFlags = [ "--legacy-peer-deps" ];
        dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
        nugetDeps = map pkgs.dotnetCorePackages.fetchNupkg [
          {
            pname = "Fable.AST";
            version = "5.0.0-rc.3";
            hash = "sha256-V1ziznJ7ZSgcw+Be4XvJgrC6BTzeLULppanw0LA9bzw=";
          }
          {
            pname = "Fable.Browser.Blob";
            version = "1.1.4";
            hash = "sha256-NH0maz+vS0lLLcl0ttVPtqxTodE0IMGJ4QWUQegcAqs=";
          }
          {
            pname = "Fable.Browser.Dom";
            version = "2.4.4";
            hash = "sha256-414N5oHz1u0gBkJW+EarRR8E4elKAm2s3tVCnZX0KM4=";
          }
          {
            pname = "Fable.Browser.Event";
            version = "1.4.4";
            hash = "sha256-SOA47R1wmWm3CD92w4e57/nHvKF7V7tSRvfPHRahG+k=";
          }
          {
            pname = "Fable.Browser.Gamepad";
            version = "1.0.3";
            hash = "sha256-Sh+bNU9+NdJ9KXv6jn8nH1YWgmVYvk1DxpskHgNGBYM=";
          }
          {
            pname = "Fable.Browser.WebStorage";
            version = "1.0.4";
            hash = "sha256-d5jFgiNAEl2btC63CbC4xgr5J1jgSPqr/pP/NIwZejY=";
          }
          {
            pname = "Fable.Core";
            version = "4.5.0";
            hash = "sha256-DOUQUo3mQz9GOykafP1NcjjsriFDcYiCSx1LEhR0piU=";
          }
          {
            pname = "Fable.Elmish";
            version = "5.0.2";
            hash = "sha256-R6r6AhDnYpS2+u5a4pN+S0+Q0JxtvtjsFUfGt9OG4ok=";
          }
          {
            pname = "Fable.React.Types";
            version = "18.4.0";
            hash = "sha256-n9UNdOl3Lja4IsBbJUacAsycAEIiNz2lTH5u+kK6LKA=";
          }
          {
            pname = "Fable.ReactDom.Types";
            version = "18.2.0";
            hash = "sha256-32EA2PxwDuctsTt3bNfLWwM3AoiEN9CeH+SLUaHlAKY=";
          }
          {
            pname = "Feliz";
            version = "3.3.3";
            hash = "sha256-LJNBOBzD3IxPu8WglzWlrVAEP6dJ6Fwrayn26wzfQQc=";
          }
          {
            pname = "Feliz.Bulma";
            version = "5.0.0";
            hash = "sha256-B/lUpzaYCsULYHmxcx9smerq+plS6Sv047B1efDfq7s=";
          }
          {
            pname = "Feliz.CompilerPlugins";
            version = "3.2.0";
            hash = "sha256-pidFT/BU+Zsn4vcyFjyyYlTsDZVLWmNGYeoNxqPpOLw=";
          }
          {
            pname = "FSharp.Core";
            version = "10.1.401";
            hash = "sha256-zx9OafwtE1Gvn6mSOskO1GYIhzCgCGfL4OsbOyxolj4=";
          }
        ];
        fableEnv = {
          nativeBuildInputs = [
            dotnet-sdk
            pkgs.fable
          ];
          buildInputs = dotnet-sdk.packages ++ nugetDeps;
          DOTNET_CLI_TELEMETRY_OPTOUT = "1";
          DOTNET_NOLOGO = "1";
        };
        nycNeighborhoods = pkgs.fetchurl {
          name = "nyc_neighborhoods.geojson";
          url = "https://raw.githubusercontent.com/chriswhong/nyc-neighborhood-boundaries/a14c924b16ce659d034ab15a8f45229b05a1f1f5/src/data/nyc-neighborhood-boundaries.geojson";
          hash = "sha256-Y+4f3fM1hNnRKyFMcqyYIEE57TnRlKrxUhq+XdqJU9c=";
        };
        gtfsSubwayZip = pkgs.fetchurl {
          name = "gtfs_subway.zip";
          url = "https://web.archive.org/web/20261005200916if_/https://rrgtfsfeeds.s3.amazonaws.com/gtfs_subway.zip";
          hash = "sha256-LJ5puCrFlqL+ZgtWZWUJVwz7o8LkxQvnXq9iTu4bxvg=";
        };
        stopsJson = pkgs.runCommand "stops.json" {
          nativeBuildInputs = [ pkgs.unzip pkgs.sqlite ];
        } ''
          unzip -p ${gtfsSubwayZip} stops.txt | tr -d '\r' > stops.csv
          sqlite3 :memory: \
            ".import --csv stops.csv stops" \
            "SELECT json_group_array(json_object(
               'id', stop_id,
               'name', stop_name,
               'latitude', CAST(stop_lat AS REAL),
               'longitude', CAST(stop_lon AS REAL)
             ))
             FROM (
               SELECT stop_id, stop_name, stop_lat, stop_lon
               FROM stops
               WHERE location_type = '1'
               ORDER BY stop_name COLLATE NOCASE
             );" > $out
        '';
        mapSvg = pkgs.runCommand "g_corridor.svg" {
          nativeBuildInputs = [ pkgs.gdalMinimal ];
        } ''
          bash ${./scripts/build-map-svg.sh} ${./data/osm_corridor.osm.pbf} ${nycNeighborhoods} $out
        '';
        interFonts = pkgs.runCommand "inter-webfonts" {
          nativeBuildInputs = [ pkgs.harfbuzz pkgs.woff2 ];
        } ''
          mkdir -p $out/web
          # Keep Latin and punctuation while preserving both variable-font axes.
          for font in InterVariable InterVariable-Italic; do
            hb-subset ${pkgs.inter}/share/fonts/truetype/$font.ttf \
              --unicodes=0020-00FF,2000-206F \
              --output-file=$out/web/$font.ttf
            woff2_compress $out/web/$font.ttf
            rm $out/web/$font.ttf
          done
          cp ${pkgs.inter.src}/LICENSE.txt $out/LICENSE.txt
        '';
        web = pkgs.buildNpmPackage ({
          pname = "greenpoint-subway-web";
          version = "0.1.0";
          inherit src npmDepsHash npmInstallFlags;
          npmBuildScript = "build";
          preBuild = ''
            cp ${mapSvg} g_corridor.svg
            cp ${stopsJson} stops.json
            ln -s ${interFonts} inter-fonts
          '';
          installPhase = ''
            runHook preInstall
            mkdir -p $out
            cp -r dist/* $out/
            runHook postInstall
          '';
          meta = with pkgs.lib; {
            description = "Static Greenpoint subway arrivals page with bundled JS/CSS";
            license = licenses.mit;
            platforms = platforms.all;
          };
        } // fableEnv);
        tests = pkgs.buildNpmPackage ({
          pname = "greenpoint-subway-web-e2e";
          version = "0.1.0";
          inherit src npmDepsHash npmInstallFlags;
          npmBuildScript = "test:e2e";
          preBuild = ''
            cp ${mapSvg} g_corridor.svg
            cp ${stopsJson} stops.json
            ln -s ${interFonts} inter-fonts
          '';
          installPhase = ''
            runHook preInstall
            mkdir -p $out
            runHook postInstall
          '';
        } // fableEnv);
      in {
        packages.default = web;
        packages.map-svg = mapSvg;
        packages.stops-json = stopsJson;
        packages.inter-fonts = interFonts;
        checks.e2e = tests;
        devShells.default = pkgs.mkShell {
          inputsFrom = [ web ];
          packages = [ pkgs.curl pkgs.gdalMinimal pkgs.osmium-tool ];
          shellHook = ''
            ln -sf ${mapSvg} g_corridor.svg
            ln -sf ${stopsJson} stops.json
            ln -sfn ${interFonts} inter-fonts
          '';
        };
      });
}
