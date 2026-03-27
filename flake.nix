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
        npmDepsHash = "sha256-aCCe2qNEJ561IHZcnTrL6DRoxH4QRYQqzII59O2/ZeA=";
        npmInstallFlags = [ "--legacy-peer-deps" ];
        web = pkgs.buildNpmPackage {
          pname = "greenpoint-subway-web";
          version = "0.1.0";
          inherit src npmDepsHash npmInstallFlags;
          npmBuildScript = "build";
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
        };
        tests = pkgs.buildNpmPackage {
          pname = "greenpoint-subway-web-e2e";
          version = "0.1.0";
          inherit src npmDepsHash npmInstallFlags;
          npmBuildScript = "test:e2e";
          installPhase = ''
            runHook preInstall
            mkdir -p $out
            runHook postInstall
          '';
        };
      in {
        packages.default = web;
        checks.e2e = tests;
        devShells.default = pkgs.mkShell { inputsFrom = [ web ]; };
      });
}
