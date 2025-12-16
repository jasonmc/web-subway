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
const stopsCsv = "stop_id,stop_name\nG26,Greenpoint Av\nG22,Other Stop\n";

const fixedNowMs = Date.parse("2024-01-01T12:00:00Z");
const fixedNowSeconds = Math.floor(fixedNowMs / 1000);

class StubElement {
  constructor(tag) {
    this.tag = tag;
    this.className = "";
    this.textContent = "";
    this.innerHTML = "";
    this.value = "";
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
      "#refresh": new StubElement("button"),
      "#stop-select": new StubElement("select"),
      "#stop-name": new StubElement("span")
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
  let feedFetchCount = 0;
  let stopsFetchCount = 0;
  let timeouts = 0;

  const fetchMock = async (url) => {
    // First fetch is always the stops CSV; subsequent fetches are the feed.
    const isStopsFetch = stopsFetchCount === 0;
    if (isStopsFetch) {
      stopsFetchCount += 1;
      const bytes = new TextEncoder().encode(stopsCsv);
      return new Response(bytes);
    }
    feedFetchCount += 1;
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

  const location = {
    href: "https://example.com/",
    search: "",
    pathname: "/",
    hash: "",
    toString() {
      return this.href;
    }
  };

  const applyUrl = (urlString) => {
    const next = new URL(urlString, location.href);
    location.href = next.href;
    location.search = next.search;
    location.pathname = next.pathname;
    location.hash = next.hash;
  };

  const history = {
    replaceState: (_state, _title, url) => {
      if (url) applyUrl(typeof url === "string" ? url : String(url));
    }
  };

  const context = {};

  Object.assign(context, {
    console,
    fetch: fetchMock,
    Response,
    TextDecoder,
    document,
    location,
    history,
    navigator: { userAgent: "node" },
    setTimeout,
    setInterval,
    clearInterval,
    clearTimeout,
    setInterval: (fn, ms = baseInterval) => {
      const id = intervals.size + 1;
      intervals.set(id, { fn, ms });
      return id;
    },
    clearInterval: (id) => intervals.delete(id),
    requestAnimationFrame: (fn) => {
      const id = ++timeouts;
      setTimeout(() => fn(performance.now()), 0);
      return id;
    },
    URL
  });

  context.window = context;
  context.globalThis = context;
  context.Date = FixedDate;

  return {
    context,
    document,
    feedFetchCount: () => feedFetchCount,
    stopsFetchCount: () => stopsFetchCount
  };
}

async function waitForRender(doc) {
  const lists = doc.get("#lists");
  for (let i = 0; i < 400; i += 1) {
    if (lists.innerHTML.includes("card")) return;
    await new Promise((resolve) => setTimeout(resolve, 20));
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
  const { context, document, feedFetchCount, stopsFetchCount } =
    createSandbox(feedBuffer);

  vm.runInNewContext(script, context, { filename: "bundle.js" });

  await waitForRender(document);

  assert.equal(
    stopsFetchCount(),
    1,
    "stops csv fetched once"
  );

  assert.equal(
    feedFetchCount(),
    1,
    "initial load should request the feed once"
  );

  const statusText = document.get("#status").textContent ?? "";
  assert.ok(statusText.startsWith("Feed age:"), "shows feed age only");

  const listsHtml = document.get("#lists").innerHTML;
  assert.ok(listsHtml.includes("Northbound"), "renders northbound card");
  assert.ok(listsHtml.includes("Southbound"), "renders southbound card");
  assert.ok(listsHtml.includes("in 2 min"), "renders relative time");
  assert.ok(listsHtml.includes("next in 3m"), "renders headway text");

  assert.equal(
    document.get("#stop-select").value,
    "G26",
    "defaults to Greenpoint stop"
  );
  assert.ok(
    document.get("#stop-select").innerHTML.includes("G26"),
    "dropdown limited to current stops"
  );
  assert.equal(
    document.get("#stop-name").textContent,
    "Greenpoint Av",
    "header shows current stop name"
  );
  assert.equal(
    context.location.hash,
    "#G26",
    "url hash synced to selected stop"
  );

  document.get("#refresh").click();
  await waitForFetchCount(feedFetchCount, 2);
  assert.equal(feedFetchCount(), 2, "manual refresh should fetch again");
});
