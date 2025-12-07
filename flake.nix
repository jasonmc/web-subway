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
        web = pkgs.buildNpmPackage {
          pname = "greenpoint-subway-web";
          version = "0.1.0";
          src = pkgs.lib.cleanSource ./.;
          npmDepsHash = "sha256-VVoBU8lJfAanEXEoCD/ziJM4WteHX0HjKxVlVdE9Os8=";
          npmInstallFlags = [ "--legacy-peer-deps" ];
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
      in {
        packages.default = web;
        devShells.default = pkgs.mkShell {
          packages = [ pkgs.nodejs pkgs.nodePackages.npm pkgs.vite ];
        };
      });
}
