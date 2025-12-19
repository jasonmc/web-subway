import { state } from "./state";

export function scheduleRefresh(
  loadFn: (showLoading?: boolean) => void,
  intervalMs = 5000
) {
  if (state.refreshTimer) {
    clearInterval(state.refreshTimer);
  }
  state.refreshTimer = window.setInterval(() => {
    loadFn(false);
  }, intervalMs);
}

export function scheduleAgeTicker(
  updateFn: () => void,
  intervalMs = 1000
) {
  if (state.ageTimer) {
    clearInterval(state.ageTimer);
  }
  state.ageTimer = window.setInterval(() => {
    updateFn();
  }, intervalMs);
}
