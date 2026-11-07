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
        npmDepsHash = "sha256-Xs12fZ3MpJHm2LuOvZ77XJV71eyR5C3LRbSRhpVBFkE=";
        npmInstallFlags = [ "--legacy-peer-deps" ];
        dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
        nugetDeps = map pkgs.dotnetCorePackages.fetchNupkg [
          {
            pname = "Fable.Core";
            version = "4.5.0";
            hash = "sha256-DOUQUo3mQz9GOykafP1NcjjsriFDcYiCSx1LEhR0piU=";
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
        web = pkgs.buildNpmPackage ({
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
        } // fableEnv);
        tests = pkgs.buildNpmPackage ({
          pname = "greenpoint-subway-web-e2e";
          version = "0.1.0";
          inherit src npmDepsHash npmInstallFlags;
          npmBuildScript = "test:e2e";
          installPhase = ''
            runHook preInstall
            mkdir -p $out
            runHook postInstall
          '';
        } // fableEnv);
      in {
        packages.default = web;
        checks.e2e = tests;
        devShells.default = pkgs.mkShell {
          inputsFrom = [ web ];
        };
      });
}
