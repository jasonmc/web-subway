import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import gtfs from "gtfs-realtime-bindings";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(__dirname, "..");
const distIndexPath = path.join(projectRoot, "dist", "index.html");

const fixedNowMs = Date.parse("2024-01-01T12:00:00Z");
const fixedNowSeconds = Math.floor(fixedNowMs / 1000);

class StubElement {
  constructor(tag) {
    this.tag = tag;
    this.className = "";
    this.textContent = "";
    this.innerHTML = "";
    this.handlers = new Map();
  }

  addEventListener(type, handler) {
    this.handlers.set(type, handler);
  }

  click() {
    this.handlers.get("click")?.();
  }
}

class StubDocument {
  constructor() {
    this.nodes = {
      "#status": new StubElement("div"),
      "#lists": new StubElement("div"),
      "#refresh": new StubElement("button")
    };
  }

  querySelector(selector) {
    return this.nodes[selector] ?? null;
  }

  querySelectorAll(_selector) {
    return [];
  }

  createElement(tag) {
    const el = new StubElement(tag);
    if (tag === "link") {
      el.relList = {
        supports: (value) => value === "modulepreload"
      };
    }
    return el;
  }

  get(selector) {
    const el = this.querySelector(selector);
    if (!el) throw new Error(`Missing element ${selector}`);
    return el;
  }
}

function extractInlineModule(html) {
  const match = html.match(/<script[^>]*type="module"[^>]*>([\s\S]*?)<\/script>/);
  if (!match) {
    throw new Error("dist/index.html missing inline module script");
  }
  return match[1];
}

const { transit_realtime } = gtfs;

function buildFeedBuffer(nowSeconds) {
  const message = transit_realtime.FeedMessage.create({
    header: { gtfsRealtimeVersion: "2.0", timestamp: nowSeconds },
    entity: [
      {
        id: "north-1",
        tripUpdate: {
          trip: { routeId: "G", directionId: 0 },
          stopTimeUpdate: [
            { stopId: "G26N", arrival: { time: nowSeconds + 120 } },
            { stopId: "G26N", arrival: { time: nowSeconds + 300 } }
          ]
        }
      },
      {
        id: "south-1",
        tripUpdate: {
          trip: { routeId: "G", directionId: 1 },
          stopTimeUpdate: [{ stopId: "G26S", arrival: { time: nowSeconds + 420 } }]
        }
      },
      {
        id: "other-stop",
        tripUpdate: {
          trip: { routeId: "G", directionId: 0 },
          stopTimeUpdate: [{ stopId: "ZZZ", arrival: { time: nowSeconds + 60 } }]
        }
      }
    ]
  });

  const bytes = transit_realtime.FeedMessage.encode(message).finish();
  return bytes.buffer.slice(
    bytes.byteOffset,
    bytes.byteOffset + bytes.byteLength
  );
}

function createSandbox(feedBuffer) {
  const document = new StubDocument();
  const intervals = new Map();
  let fetchCount = 0;

  const fetchMock = async () => {
    fetchCount += 1;
    return new Response(feedBuffer);
  };

  const baseInterval = 100;
  const OriginalDate = Date;
  class FixedDate extends OriginalDate {
    constructor(value) {
      if (arguments.length === 0) {
        super(fixedNowMs);
      } else {
        super(value);
      }
    }

    static now() {
      return fixedNowMs;
    }
  }

  const context = {
    console,
    fetch: fetchMock,
    Response,
    document,
    navigator: { userAgent: "node" },
    setTimeout,
    clearTimeout,
    setInterval: (fn, ms = baseInterval) => {
      const id = intervals.size + 1;
      intervals.set(id, { fn, ms });
      return id;
    },
    clearInterval: (id) => intervals.delete(id),
    URL
  };

  context.window = context;
  context.globalThis = context;
  context.Date = FixedDate;

  return {
    context,
    document,
    fetchCount: () => fetchCount
  };
}

async function waitForRender(doc) {
  const lists = doc.get("#lists");
  for (let i = 0; i < 50; i += 1) {
    if (lists.innerHTML.includes("card")) return;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error("render did not finish in time");
}

async function waitForFetchCount(getCount, expected) {
  for (let i = 0; i < 50; i += 1) {
    if (getCount() >= expected) return;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error(`fetch count did not reach ${expected}`);
}

test("renders arrivals from bundled app and refreshes", async () => {
  const html = fs.readFileSync(distIndexPath, "utf8");
  const script = extractInlineModule(html);
  const feedBuffer = buildFeedBuffer(fixedNowSeconds);
  const { context, document, fetchCount } = createSandbox(feedBuffer);

  vm.runInNewContext(script, context, { filename: "bundle.js" });

  await waitForRender(document);

  assert.equal(
    fetchCount(),
    1,
    "initial load should request the feed once"
  );

  const statusText = document.get("#status").textContent ?? "";
  assert.ok(statusText.includes("Feed age: just now"), "shows feed age");
  assert.ok(
    statusText.includes("Showing 3 arrivals"),
    "shows arrival count"
  );

  const listsHtml = document.get("#lists").innerHTML;
  assert.ok(listsHtml.includes("Northbound"), "renders northbound card");
  assert.ok(listsHtml.includes("Southbound"), "renders southbound card");
  assert.ok(listsHtml.includes("in 2 min"), "renders relative time");
  assert.ok(listsHtml.includes("next in 3m"), "renders headway text");

  document.get("#refresh").click();
  await waitForFetchCount(fetchCount, 2);
  assert.equal(fetchCount(), 2, "manual refresh should fetch again");
});
