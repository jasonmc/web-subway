import { formatRelative, headway } from "./feed";
import { ARRIVALS_PER_DIRECTION, PREFERRED_STOP_ORDER } from "./config";
import { state, Arrival, ArrivalGroup, FeedMeta } from "./state";
import { stopLabel } from "./stops";

const statusEl = document.querySelector<HTMLElement>("#status");
const listsEl = document.querySelector<HTMLElement>("#lists");
const stopSelect = document.querySelector<HTMLSelectElement>("#stop-select");
const stopNameEl = document.querySelector<HTMLElement>("#stop-name");

if (!statusEl || !listsEl || !stopSelect || !stopNameEl) {
  throw new Error("Missing required UI elements.");
}

function syncStopParam() {
  try {
    const url = new URL(window.location.href);
    url.hash = state.selectedStopPrefix;
    window.history.replaceState({}, "", url);
  } catch {
    // ignore if history/location not available
  }
}

export function setStatusText(text: string, options: { error?: boolean } = {}) {
  statusEl.className = options.error ? "status error" : "status";
  statusEl.textContent = text;
}

export function render(arrivals: ArrivalGroup, _meta: FeedMeta) {
  const cards: Array<{ title: string; icon: string; items: Arrival[] }> = [
    {
      title: "Northbound",
      icon: "⬆️",
      items: arrivals.Northbound.slice(0, ARRIVALS_PER_DIRECTION)
    },
    {
      title: "Southbound",
      icon: "⬇️",
      items: arrivals.Southbound.slice(0, ARRIVALS_PER_DIRECTION)
    },
    {
      title: "Other",
      icon: "⬜",
      items: arrivals.Other.slice(0, ARRIVALS_PER_DIRECTION)
    }
  ];

  listsEl.innerHTML = cards
    .filter((c) => c.items.length > 0)
    .map(
      (card) => `
      <article class="card">
        <h2>${card.icon} ${card.title}</h2>
        ${card.items
          .map((arrival, idx) => {
            const next = card.items[idx + 1];
            return `
              <div class="arrival">
                <div>
                  <span class="route">${arrival.route}</span>
                  <span class="meta"> ${formatRelative(arrival.time)}${headway(
                    arrival,
                    next
                  )}</span>
                </div>
                <span class="time">${arrival.time.toLocaleTimeString([], {
                  hour: "numeric",
                  minute: "2-digit"
                })}</span>
              </div>
            `;
          })
          .join("")}
      </article>
    `
    )
    .join("");

  if (listsEl.innerHTML.trim() === "") {
    listsEl.innerHTML =
      `<article class="card"><p class="meta">No arrivals in the feed.</p></article>`;
  }
}

export function setError(message: string) {
  setStatusText(message, { error: true });
  listsEl.innerHTML = "";
}

export function setSelectedStop(id: string) {
  state.selectedStopPrefix = id;
  stopSelect.value = id;
  stopNameEl.textContent = stopLabel(id);
  syncStopParam();
}

export function updateStopSelect(availableOrder?: string[]) {
  const availableSet =
    availableOrder && availableOrder.length > 0
      ? new Set(availableOrder)
      : undefined;

  const idsToRender = PREFERRED_STOP_ORDER.filter((id) =>
    availableSet ? availableSet.has(id) : true
  );

  const finalIds = idsToRender.length > 0 ? idsToRender : PREFERRED_STOP_ORDER;
  const preservedIds = finalIds.includes(state.selectedStopPrefix)
    ? finalIds
    : [state.selectedStopPrefix, ...finalIds];
  const preservedOptions = preservedIds.map(
    (id) => state.stopLookup.get(id) ?? { id, name: id }
  );

  const nextKey = preservedIds.join(",");
  const needsUpdate = nextKey !== state.lastRenderedStopsKey;

  const nextSelectedId = state.selectedStopPrefix;

  if (needsUpdate) {
    stopSelect.innerHTML = preservedOptions
      .map(
        (stop) =>
          `<option value="${stop.id}" ${
            stop.id === nextSelectedId ? "selected" : ""
          }>${stop.name}</option>`
      )
      .join("");
    state.lastRenderedStopsKey = nextKey;
  }

  setSelectedStop(nextSelectedId);
}

export function initStopSelect(onChange: () => void) {
  updateStopSelect();
  stopSelect.addEventListener("change", () => {
    setSelectedStop(stopSelect.value);
    onChange();
  });
}
