import { gzipSync } from "node:zlib";
import { readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";

const root = resolve(process.cwd());
const input = resolve(root, "all_stops.csv");
const output = resolve(root, "all_stops.csv.gz");

const data = readFileSync(input);
const gzipped = gzipSync(data, { level: 9 });
writeFileSync(output, gzipped);
console.log(
  `Wrote ${output} (${gzipped.length} bytes) from ${data.length}-byte csv`
);
