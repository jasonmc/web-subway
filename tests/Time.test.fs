module Tests.TimeTests

open System
open Vitest
open Time
open Tests.Fixtures

Vitest.beforeEach (fun () -> resetBrowser "")

Vitest.test (
    "Unix timestamps become UTC instants",
    fun () ->
        let instant = instantFromSeconds fixedNowSec
        Vitest.expect(instant.Offset).toEqual TimeSpan.Zero
        Vitest.expect(instant.ToUnixTimeMilliseconds()).toEqual (int64 fixedNowMs)
        Vitest.expect(instant).toEqual (DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero))
)

for utc, expected in
    [ "2024-01-01T12:02:00Z", "7:02 AM"
      "2024-07-01T12:02:00Z", "8:02 AM"
      "2024-03-10T06:59:00Z", "1:59 AM"
      "2024-03-10T07:00:00Z", "3:00 AM"
      "2024-11-03T05:30:00Z", "1:30 AM"
      "2024-11-03T06:30:00Z", "1:30 AM" ] do
    Vitest.test (
        $"displays {utc} as {expected} in New York",
        fun () -> Vitest.expect(formatNewYorkTime (DateTimeOffset.Parse utc)).toBe expected
    )

Vitest.test (
    "clock formatting uses the instant rather than its stored offset",
    fun () ->
        let utc = DateTimeOffset.Parse "2024-01-01T12:02:00Z"
        let shifted = utc.ToOffset(TimeSpan.FromHours 9.0)
        Vitest.expect(formatNewYorkTime shifted).toBe "7:02 AM"
)

for before, after in
    [ "2024-03-10T06:59:30Z", "2024-03-10T07:00:30Z"
      "2024-11-03T05:59:30Z", "2024-11-03T06:00:30Z" ] do
    Vitest.test (
        $"countdowns use elapsed time across the DST change at {after}",
        fun () ->
            let start = DateTimeOffset.Parse before
            let finish = DateTimeOffset.Parse after
            Vitest.expect(Feed.formatRelative start finish).toBe "in 1 min"
            Vitest.expect(finish - start).toEqual (TimeSpan.FromMinutes 1.0)
    )
