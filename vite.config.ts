import { defineConfig } from "vite";
import { readFileSync } from "node:fs";
import { viteSingleFile } from "vite-plugin-singlefile";

const siteUrl = "https://g.apps.jasonmc.net/";
const brandingFile = (name: string) => new URL(`./public/branding/${name}`, import.meta.url);
const dataUrl = (type: string, content: Buffer | string) =>
  `data:${type};base64,${Buffer.from(content).toString("base64")}`;

export default defineConfig({
  plugins: [
    {
      name: "inline-app-metadata",
      transformIndexHtml() {
        const manifest = JSON.parse(readFileSync(new URL("./assets/site.webmanifest", import.meta.url), "utf8"));
        // A data URL cannot resolve relative URLs against the manifest's location.
        for (const key of ["id", "start_url", "scope"]) {
          manifest[key] = new URL(manifest[key], siteUrl).href;
        }
        for (const icon of manifest.icons) {
          icon.src = dataUrl(icon.type, readFileSync(brandingFile(icon.src)));
        }
        return [
          { tag: "link", attrs: { rel: "canonical", href: siteUrl } },
          { tag: "meta", attrs: { property: "og:url", content: siteUrl } },
          { tag: "meta", attrs: { property: "og:image", content: new URL("branding/share.png", siteUrl).href } },
          { tag: "link", attrs: { rel: "icon", type: "image/svg+xml", href: dataUrl("image/svg+xml", readFileSync(brandingFile("icon.svg"))) } },
          { tag: "link", attrs: { rel: "icon", type: "image/png", sizes: "32x32", href: dataUrl("image/png", readFileSync(brandingFile("favicon-32.png"))) } },
          { tag: "link", attrs: { rel: "manifest", href: dataUrl("application/manifest+json", JSON.stringify(manifest)) } }
        ];
      },
      generateBundle() {
        // Only these images need external URLs. Keep the full public/branding
        // directory available in development, but emit just these in production.
        for (const name of ["apple-touch-icon.png", "share.png"]) {
          this.emitFile({ type: "asset", fileName: `branding/${name}`, source: readFileSync(brandingFile(name)) });
        }
      }
    },
    viteSingleFile(),
    {
      name: "inter-license",
      transformIndexHtml: {
        order: "post",
        handler: (html) => html.replace(
          "</head>",
          `<!--\n${readFileSync(new URL("./inter-fonts/LICENSE.txt", import.meta.url), "utf8")}\n-->\n</head>`
        )
      }
    }
  ],
  build: {
    copyPublicDir: false,
    target: "es2020",
    assetsInlineLimit: Infinity,
    cssCodeSplit: false
  }
});
