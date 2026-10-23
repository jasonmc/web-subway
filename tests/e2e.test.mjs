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
const stopsCsv =
  'stop_id,stop_name,stop_lat,stop_lon\n' +
  'G26,"Greenpoint Av, Brooklyn",40.731352,-73.954449\n' +
  'G24,21 St,40.744065,-73.949724\n' +
  'F27,Church Av,40.644041,-73.979678\n' +
  'G22,Other Stop,40.746554,-73.943832\n';

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

function createSandbox(feedBuffer, initialHash = "", geolocation, csv = stopsCsv) {
  const document = new StubDocument();
  const intervals = new Map();
  let feedFetchCount = 0;
  let stopsFetchCount = 0;
  let timeouts = 0;
  let failNextFeed = false;
  let currentNowMs = fixedNowMs;

  const fetchMock = async (url) => {
    // First fetch is always the stops CSV; subsequent fetches are the feed.
    const isStopsFetch = stopsFetchCount === 0;
    if (isStopsFetch) {
      stopsFetchCount += 1;
      const bytes = new TextEncoder().encode(csv);
      return new Response(bytes);
    }
    feedFetchCount += 1;
    if (failNextFeed) {
      failNextFeed = false;
      return new Response("fail", { status: 500, statusText: "fail" });
    }
    return new Response(feedBuffer);
  };

  const baseInterval = 100;
  const OriginalDate = Date;
  class FixedDate extends OriginalDate {
    constructor(value) {
      if (arguments.length === 0) {
        super(currentNowMs);
      } else {
        super(value);
      }
    }

    static now() {
      return currentNowMs;
    }
  }

  const location = {
    href: `https://example.com/${initialHash}`,
    search: "",
    pathname: "/",
    hash: initialHash,
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
    navigator: { userAgent: "node", geolocation },
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
    stopsFetchCount: () => stopsFetchCount,
    failNextFeed: () => {
      failNextFeed = true;
    },
    advanceTime: (ms) => {
      currentNowMs += ms;
    },
    runIntervals: async (times = 1) => {
      for (let i = 0; i < times; i += 1) {
        for (const { fn } of intervals.values()) {
          const result = fn();
          if (result instanceof Promise) {
            await result;
          }
        }
        // Give async callbacks time to finish before next tick.
        await new Promise((resolve) => setTimeout(resolve, baseInterval));
      }
    }
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
  const {
    context,
    document,
    feedFetchCount,
    stopsFetchCount,
    failNextFeed,
    advanceTime,
    runIntervals
  } = createSandbox(feedBuffer);

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

  advanceTime(60_000);
  await runIntervals();

  const updatedListsHtml = document.get("#lists").innerHTML;
  assert.ok(updatedListsHtml.includes("in 1 min"), "countdowns tick in place");

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
    "Greenpoint Av, Brooklyn",
    "header shows current stop name"
  );

  const beforeHtml = document.get("#lists").innerHTML;

  // Simulate a failing fetch; the UI should keep the previous arrivals.
  failNextFeed();
  await runIntervals();
  await waitForFetchCount(feedFetchCount, 2);

  assert.equal(
    document.get("#lists").innerHTML,
    beforeHtml,
    "keeps prior arrivals on fetch failure"
  );
  const statusAfterFail = document.get("#status").textContent ?? "";
  assert.ok(statusAfterFail.length > 0, "status remains populated after fail");
  assert.equal(
    context.location.hash,
    "#G26",
    "url hash synced to selected stop"
  );

  await runIntervals(2);
  await waitForFetchCount(feedFetchCount, 5);
  assert.equal(feedFetchCount(), 5, "scheduled refresh should fetch again");
});

test("preserves selected stop when current feed has no arrivals for it", async () => {
  const html = fs.readFileSync(distIndexPath, "utf8");
  const script = extractInlineModule(html);
  const feedBuffer = buildFeedBuffer(fixedNowSeconds);
  const { context, document } = createSandbox(feedBuffer, "#G22");

  vm.runInNewContext(script, context, { filename: "bundle.js" });

  await waitForRender(document);

  assert.equal(
    document.get("#stop-select").value,
    "G22",
    "keeps the requested stop selected"
  );
  assert.equal(
    document.get("#stop-name").textContent,
    "Other Stop",
    "header keeps the selected stop label"
  );
  assert.ok(
    document.get("#stop-select").innerHTML.includes('value="G22"'),
    "dropdown preserves the selected stop even when it is absent from the feed"
  );
  assert.ok(
    document.get("#lists").innerHTML.includes("No arrivals in the feed."),
    "shows empty state instead of switching stations"
  );
  assert.equal(
    context.location.hash,
    "#G22",
    "url hash remains on the selected stop"
  );
});

for (const [latitude, longitude, id, name, hash] of [
  [40.746554, -73.943832, "G22", "Other Stop", ""],
  [40.744, -73.950, "G24", "21 St", ""],
  [40.644, -73.980, "F27", "Church Av", "#unknown"]
]) {
  test(`selects the nearest supported station using device location: ${id}`, async () => {
    const script = extractInlineModule(fs.readFileSync(distIndexPath, "utf8"));
    let locate;
    const { context, document, feedFetchCount } = createSandbox(
      buildFeedBuffer(fixedNowSeconds), hash, {
        getCurrentPosition(success, _error, options) {
          locate = success;
          assert.equal(options.timeout, 8000);
        }
      }
    );
    vm.runInNewContext(script, context, { filename: "bundle.js" });
    await waitForRender(document);
    assert.equal(document.get("#stop-select").value, "G26",
      "arrivals load before location resolves");

    locate({ coords: { latitude, longitude } });
    await waitForFetchCount(feedFetchCount, 2);
    await new Promise((resolve) => setTimeout(resolve, 20));

    assert.equal(document.get("#stop-select").value, id);
    assert.equal(document.get("#stop-name").textContent, name);
    assert.equal(context.location.hash, `#${id}`);
    assert.ok(document.get("#lists").innerHTML.includes("No arrivals in the feed."),
      "nearest station remains selected even without arrivals");
  });
}

test("a station link takes priority over device location", async () => {
  const script = extractInlineModule(fs.readFileSync(distIndexPath, "utf8"));
  let locationRequests = 0;
  const { context, document } = createSandbox(
    buildFeedBuffer(fixedNowSeconds), "#G22", {
      getCurrentPosition() { locationRequests += 1; }
    }
  );
  vm.runInNewContext(script, context, { filename: "bundle.js" });
  await waitForRender(document);
  assert.equal(document.get("#stop-select").value, "G22");
  assert.equal(locationRequests, 0, "saved links do not request location");
});

test("manual station selection takes priority over a pending location request", async () => {
  const script = extractInlineModule(fs.readFileSync(distIndexPath, "utf8"));
  let locate;
  const { context, document, feedFetchCount } = createSandbox(
    buildFeedBuffer(fixedNowSeconds), "", {
      getCurrentPosition(success) { locate = success; }
    }
  );
  vm.runInNewContext(script, context, { filename: "bundle.js" });
  await waitForRender(document);
  const select = document.get("#stop-select");
  select.value = "G26";
  select.handlers.get("change")();
  await waitForFetchCount(feedFetchCount, 2);
  locate({ coords: { latitude: 40.746554, longitude: -73.943832 } });
  await new Promise((resolve) => setTimeout(resolve, 20));
  assert.equal(select.value, "G26");
  assert.equal(context.location.hash, "#G26");
  assert.equal(feedFetchCount(), 2, "late location does not trigger another load");
});

for (const [scenario, geolocation] of [
  ["permission denied", { getCurrentPosition(_success, error) { error({ code: 1 }); } }],
  ["position unavailable", { getCurrentPosition(_success, error) { error({ code: 2 }); } }],
  ["timeout", { getCurrentPosition(_success, error) { error({ code: 3 }); } }],
  ["API throws", { getCurrentPosition() { throw new Error("unavailable"); } }],
  ["invalid coordinates", { getCurrentPosition(success) {
    success({ coords: { latitude: NaN, longitude: -73.94 } });
  } }]
]) {
  test(`keeps the fallback station when geolocation fails: ${scenario}`, async () => {
    const script = extractInlineModule(fs.readFileSync(distIndexPath, "utf8"));
    const { context, document } = createSandbox(
      buildFeedBuffer(fixedNowSeconds), "", geolocation
    );
    vm.runInNewContext(script, context, { filename: "bundle.js" });
    await waitForRender(document);
    assert.equal(document.get("#stop-select").value, "G26");
    assert.ok(document.get("#lists").innerHTML.includes("Northbound"));
  });
}

for (const [scenario, csv, expectedId] of [
  ["bundled station dataset", fs.readFileSync(path.join(projectRoot, "all_stops.csv"), "utf8"), "G22"],
  ["reordered CSV columns", 'stop_lon,stop_name,stop_id,stop_lat\n' +
    '-73.954449,Greenpoint Av,G26,40.731352\n' +
    '-73.943832,Court Sq,G22,40.746554\n', "G22"],
  ["missing coordinates", stopsCsv.replace('40.746554,-73.943832', ','), "G24"],
  ["invalid station coordinates", stopsCsv.replace('40.746554,-73.943832', '999,-73.943832'), "G24"]
]) {
  test(`selects nearest station from CSV: ${scenario}`, async () => {
    const script = extractInlineModule(fs.readFileSync(distIndexPath, "utf8"));
    const { context, document } = createSandbox(
      buildFeedBuffer(fixedNowSeconds), "", {
        getCurrentPosition(success) {
          success({ coords: { latitude: 40.746554, longitude: -73.943832 } });
        }
      }, csv
    );
    vm.runInNewContext(script, context, { filename: "bundle.js" });
    await waitForRender(document);
    assert.equal(document.get("#stop-select").value, expectedId);
    assert.equal(context.location.hash, `#${expectedId}`);
  });
}
