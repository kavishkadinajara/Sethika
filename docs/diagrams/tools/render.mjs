// Renders every docs/diagrams/src/**/*.puml to SVG (PlantUML), then PNG and PDF (Playwright Chromium).
//
//   node docs/diagrams/tools/render.mjs <path-to-plantuml.jar> [name-filter]
//
// PlantUML is not in the repo: download plantuml-1.2024.7.jar from github.com/plantuml/plantuml.
// Playwright comes from web/node_modules (already a dev dependency for the e2e tests).
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
import { mkdirSync, readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "..");
const src = join(root, "src");
const [jar, filter = ""] = process.argv.slice(2);
if (!jar) {
  console.error("usage: node render.mjs <plantuml.jar> [filter]");
  process.exit(1);
}

const require = createRequire(join(root, "..", "..", "web", "package.json"));
const { chromium } = require("playwright");

const walk = (dir) =>
  readdirSync(dir).flatMap((f) => {
    const p = join(dir, f);
    return statSync(p).isDirectory() ? walk(p) : p.endsWith(".puml") ? [p] : [];
  });

const sources = walk(src).filter((p) => p.includes(filter));

// 1. SVG, one PlantUML JVM per folder (low-RAM machine: one at a time).
const byFolder = Object.groupBy(sources, (p) => dirname(p));
for (const [folder, files] of Object.entries(byFolder)) {
  const out = join(root, "svg", relative(src, folder));
  mkdirSync(out, { recursive: true });
  execFileSync(
    "java",
    ["-Xmx768m", "-DPLANTUML_LIMIT_SIZE=32768", "-jar", jar, "-tsvg", "-charset", "UTF-8", "-o", out, ...files],
    { stdio: "inherit" },
  );
}

// 2. PNG (2x) and PDF (vector, page = diagram size) from each SVG.
const browser = await chromium.launch();
const page = await browser.newPage();
for (const file of sources) {
  const rel = relative(src, file).replace(/\.puml$/, "");
  const svgPath = join(root, "svg", `${rel}.svg`);
  const svg = readFileSync(svgPath, "utf8");
  const vb = svg.match(/viewBox="([\d.\s-]+)"/)?.[1].split(/\s+/).map(Number);
  const width = Math.ceil(vb ? vb[2] : 1000);
  const height = Math.ceil(vb ? vb[3] : 1000);

  // Keep a PNG under ~ 16k px on its long side and 120 megapixels.
  const scale = Math.max(1, Math.min(2, 16000 / Math.max(width, height), Math.sqrt(120e6 / (width * height))));

  const html = `<!doctype html><html><head><meta charset="utf-8"><style>
    @page { margin: 0 } html, body { margin: 0; padding: 0; background: #fff }
    svg { display: block }</style></head><body>${svg.replace(/^<\?xml[^>]*>/, "")}</body></html>`;

  await page.setViewportSize({ width, height });
  await page.setContent(html, { waitUntil: "load" });
  for (const kind of ["png", "pdf"]) mkdirSync(dirname(join(root, kind, rel)), { recursive: true });

  const shot = await browser.newPage({ deviceScaleFactor: scale, viewport: { width, height } });
  await shot.setContent(html, { waitUntil: "load" });
  await shot.screenshot({ path: join(root, "png", `${rel}.png`), clip: { x: 0, y: 0, width, height }, timeout: 120000 });
  await shot.close();

  await page.pdf({
    path: join(root, "pdf", `${rel}.pdf`),
    width: `${width}px`,
    height: `${height + 2}px`,
    printBackground: true,
    pageRanges: "1",
  });
  console.log(`${rel}: ${width}x${height} (png x${scale.toFixed(2)})`);
}
await browser.close();
