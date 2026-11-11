import { defineConfig } from "vite";
import { readFileSync } from "node:fs";
import { viteSingleFile } from "vite-plugin-singlefile";

export default defineConfig({
  plugins: [
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
    target: "es2020",
    assetsInlineLimit: Infinity,
    cssCodeSplit: false
  }
});
